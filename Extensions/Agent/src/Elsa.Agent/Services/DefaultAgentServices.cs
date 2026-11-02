using System.Collections.Concurrent;
using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;
using Elsa.Primitives.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using static Elsa.Agent.Services.AgentIds;

namespace Elsa.Agent.Services;

/// <summary>
/// The default session owner. Each session row's monitor orders message insertion, the session's policy and
/// policy revision, and the claims of work admitted under that policy.
/// </summary>
/// <remarks>
/// <para>
/// Every change is written through <see cref="IAgentSessionRecordStore"/>: take the session's async write gate,
/// await the record write, then apply the change in memory under the row monitor. A monitor is never held across an
/// await. A session this owner has not loaded is hydrated from the store, once, under the same gate, and every
/// operation hydrates before it acts. Claim acquisition and policy changes serialize on the gate, so a claim
/// acquired while a policy write is in flight waits and then reads the new revision. Lock order: call key, proposal
/// gate, session write gate, row monitor; hydration never takes a proposal gate. Claims are process-local tokens:
/// releasing one needs no hydration, because a row that is not loaded holds none.
/// </para>
/// <para>
/// When an audit ownership association is installed, each new session is associated with its actor and tenant
/// before the row exists or any record for it is emitted. The association has no dependency on this service.
/// </para>
/// </remarks>
internal sealed class InMemoryAgentSessionService(IAgentAuditSink auditSink, TimeProvider? clock = null, IAgentAuditOwnership? auditOwnership = null,
    IAgentSessionRecordStore? recordStore = null)
    : IAgentSessionService, IAgentSessionPolicyParticipant
{
    private const long InitialPolicyRevision = 1;
    private const string PauseKey = "pause";
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly IAgentSessionRecordStore _store = recordStore ?? new InMemoryAgentSessionRecordStore(clock);
    private readonly string _ownerToken = NewId();
    private readonly ConcurrentDictionary<string, SessionRow> _rows = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _gates = new(StringComparer.Ordinal);

    InMemoryAgentSessionService? IAgentSessionPolicyParticipant.PolicyOwner => this;

    /// <summary>The record store this owner writes through.</summary>
    internal IAgentSessionRecordStore RecordStore => _store;

    public async Task<AgentSession> CreateAsync(AgentSessionCreateRequest request, CancellationToken cancellationToken = default)
    {
        var now = _clock.GetUtcNow();
        var session = new AgentSession(
            NewId(),
            request.Title,
            request.TenantId,
            request.ActorId,
            request.ConversationId,
            request.ProviderId,
            request.Mode,
            request.Policy ?? AgentPolicy.Default,
            AgentSessionStatus.Active,
            now,
            now,
            now.AddHours(8),
            request.Metadata)
        { PolicyRevision = InitialPolicyRevision, Workspace = request.Workspace, ContinuedFrom = request.ContinuedFrom };

        // A blank owner cannot be associated; such a session stays ineligible for owner-scoped audit queries.
        if (auditOwnership is not null && !string.IsNullOrWhiteSpace(session.ActorId) && !string.IsNullOrWhiteSpace(session.TenantId)
            && !await auditOwnership.AssociateAsync(session.Id, session.ActorId, session.TenantId, cancellationToken))
            throw new AgentContributionException("The new session identity is already associated with another audit owner.");
        var created = await _store.CreateAsync(new AgentSessionRecord(session), _ownerToken, cancellationToken);
        if (!created.Succeeded || created.Fence is null)
            throw new AgentContributionException("The new session identity is already recorded.");
        _rows[session.Id] = new SessionRow(session, created.Fence);

        await auditSink.EmitAsync(new(
            NewId(),
            AgentAuditEventKind.SessionCreated,
            session.Id,
            session.ActorId,
            "Agent session created.",
            now,
            new Dictionary<string, string> { ["providerId"] = session.ProviderId }), cancellationToken);

        return session;
    }

    public async Task<AgentSession?> FindAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        if (await RowAsync(sessionId, cancellationToken) is not { } row)
            return null;
        lock (row)
            return row.Session;
    }

    public async Task<AgentMessage> AddMessageAsync(string sessionId, AgentMessageCreateRequest request, CancellationToken cancellationToken = default)
    {
        if (await RowAsync(sessionId, cancellationToken) is not { } loaded)
            throw new InvalidOperationException($"Agent session '{sessionId}' was not found.");
        sessionId = loaded.Session.Id;

        var message = new AgentMessage(
            NewId(),
            sessionId,
            request.Role,
            request.Content,
            request.Status,
            request.CapabilityId,
            DateTimeOffset.UtcNow,
            null,
            null,
            request.ContextAttachmentIds,
            [],
            request.Context)
        {
            RequestId = request.RequestId,
            Source = request.Source,
            Client = request.Client,
            PromptMode = request.PromptMode,
            DeliveryFingerprint = request.DeliveryFingerprint,
            ToolResults = Array.AsReadOnly(request.ToolResults.ToArray()),
            HasDisclosureProvenance = request.HasDisclosureProvenance || request.Role == AgentRole.User,
            DisclosureHistory = request.DisclosureHistory is null ? null : AgentDisclosureHistory.Freeze(request.DisclosureHistory)
        };

        var (accepted, inserted) = await CommitAsync<(AgentMessage Message, bool Inserted)>(sessionId, () => throw new InvalidOperationException($"Agent session '{sessionId}' was not found."), row =>
        {
            var messages = row.Messages;
            if (request.Role == AgentRole.Tool && !string.IsNullOrEmpty(request.RequestId))
            {
                var existing = messages.FirstOrDefault(x => x.Role == AgentRole.Tool && x.RequestId == request.RequestId);
                if (existing is not null)
                {
                    if (!string.IsNullOrEmpty(request.DeliveryFingerprint) && existing.DeliveryFingerprint == request.DeliveryFingerprint)
                        return new((existing, false));
                    throw new AgentContributionException("An observed tool outcome cannot change its original invocation identity or evidence.");
                }
            }
            if (request.Role == AgentRole.User && !string.IsNullOrWhiteSpace(request.RequestId))
            {
                var existing = messages.FirstOrDefault(x => x.Role == AgentRole.User && x.RequestId == request.RequestId);
                if (existing is not null)
                {
                    if (!string.IsNullOrEmpty(request.DeliveryFingerprint) && existing.DeliveryFingerprint == request.DeliveryFingerprint)
                        return new((existing, false));
                    throw new AgentContributionException("A prompt delivery identity was reused with different input.");
                }
            }
            // Replaceable evaluators/collectors authorized this delivery under the whole policy they read.
            if (request.ExpectedPolicyRevision is { } expected && expected != row.Revision)
                throw new AgentSessionPolicyChangedException(sessionId, expected, row.Revision);
            if (request.Role == AgentRole.User && messages.Any(x => x.Role == AgentRole.User && x.Status is AgentMessageStatus.Pending or AgentMessageStatus.Streaming))
                throw new AgentContributionException("Another prompt remains pending in this session.");
            // Attribution is the policy current at this atomic insertion; a later change never relabels it.
            var attributed = message with { AcceptedPolicy = row.State };
            return new((attributed, true), new AgentSessionRecordWrite(sessionId, row.Fence) { Messages = [attributed] }, () =>
            {
                if (request.Role == AgentRole.User && request.Status == AgentMessageStatus.Pending)
                    row.Bind(row.NewClaim(AgentSessionClaimKind.Prompt, null), PromptKey(attributed.Id));
                messages.Add(attributed);
            });
        }, cancellationToken);
        if (!inserted)
            return accepted;

        await auditSink.EmitAsync(new(
            NewId(),
            AgentAuditEventKind.MessageAccepted,
            sessionId,
            null,
            "Agent message accepted.",
            accepted.CreatedAt,
            new Dictionary<string, string> { ["messageId"] = accepted.Id, ["role"] = accepted.Role.ToString() }), cancellationToken);

        return accepted;
    }

    public async Task<AgentMessage?> FindLatestMessageAsync(string sessionId, AgentRole? role = null, CancellationToken cancellationToken = default)
    {
        if (await RowAsync(sessionId, cancellationToken) is not { } row)
            return null;

        lock (row)
        {
            return row.Messages.LastOrDefault(x => role is null || x.Role == role);
        }
    }

    public async Task SetMessageStatusAsync(string sessionId, string messageId, AgentMessageStatus status, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await CommitAsync(sessionId, () => false, row => PlanMessageStatus(row, messageId, null, status, null), cancellationToken);
    }

    /// <summary>
    /// Compare-and-set of one message's status: changes it only while it still has <paramref name="expected"/>.
    /// False when the message is missing or another transition moved it first.
    /// </summary>
    internal Task<bool> TrySetMessageStatusAsync(string sessionId, string messageId, AgentMessageStatus expected, AgentMessageStatus status,
        string? reason = null, CancellationToken cancellationToken = default)
        => CommitAsync(sessionId, () => false, row => PlanMessageStatus(row, messageId, expected, status, reason), cancellationToken);

    public async Task<IReadOnlyCollection<AgentMessage>> ListMessagesAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        if (await RowAsync(sessionId, cancellationToken) is not { } row)
            return [];

        lock (row)
            return row.Messages.ToList();
    }

    public async Task<AgentMessage?> FindMessageAsync(string sessionId, string messageId, CancellationToken cancellationToken = default)
    {
        if (await RowAsync(sessionId, cancellationToken) is not { } row)
            return null;

        lock (row)
            return row.Messages.FirstOrDefault(x => string.Equals(x.Id, messageId, StringComparison.Ordinal));
    }

    public async Task AddContextAsync(string sessionId, IReadOnlyCollection<AgentContextAttachment> attachments, CancellationToken cancellationToken = default)
    {
        if (attachments.Count == 0)
            return;

        await CommitAsync<bool>(sessionId, () => throw new InvalidOperationException($"Agent session '{sessionId}' was not found."), row =>
        {
            var existingIds = row.Context.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
            var added = attachments.Where(x => existingIds.Add(x.Id)).ToArray();
            return added.Length == 0
                ? new(true)
                : new(true, new AgentSessionRecordWrite(row.Session.Id, row.Fence) { Context = added }, () => row.Context.AddRange(added));
        }, cancellationToken);
    }

    public async Task<IReadOnlyCollection<AgentContextAttachment>> ListContextAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        if (await RowAsync(sessionId, cancellationToken) is not { } row)
            return [];

        lock (row)
            return row.Context.ToList();
    }

    /// <summary>Acquires an unkeyed claim under the session write gate; null when the session is not recorded.</summary>
    internal Task<AgentSessionClaim?> AcquireAsync(string sessionId, AgentSessionClaimKind kind, CancellationToken cancellationToken = default)
    {
        if (kind is AgentSessionClaimKind.Prompt or AgentSessionClaimKind.Proposal or AgentSessionClaimKind.Pause)
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "Keyed claims are acquired through their owning transition.");
        return CommitAsync<AgentSessionClaim?>(sessionId, () => null, row => new(row.NewClaim(kind, null)), cancellationToken);
    }

    /// <summary>Acquires a provisional proposal claim before store insertion; it records the prepared deadline.</summary>
    internal Task<AgentSessionClaim?> AcquireProposalAsync(string sessionId, DateTimeOffset? deadline, CancellationToken cancellationToken = default)
        => CommitAsync<AgentSessionClaim?>(sessionId, () => null, row => new(row.NewClaim(AgentSessionClaimKind.Proposal, deadline)), cancellationToken);

    /// <summary>Binds a provisional proposal claim to its inserted proposal identity.</summary>
    internal bool BindProposal(AgentSessionClaim claim, string proposalId)
    {
        if (claim.Kind != AgentSessionClaimKind.Proposal || !_rows.TryGetValue(claim.SessionId, out var row)) return false;
        lock (row) return row.Bind(claim, ProposalKey(proposalId));
    }

    /// <summary>
    /// Marks the proposal's claim dispatched after hydrating its session; false refuses a pre-dispatch claim past its
    /// deadline, and a session that is not recorded, which can never authorize dispatch.
    /// </summary>
    internal Task<bool> MarkProposalDispatchedAsync(string sessionId, string proposalId, CancellationToken cancellationToken = default)
        => CommitAsync<bool>(sessionId, () => false, row =>
        {
            if (row.Find(ProposalKey(proposalId)) is not { } claim) return new(true);
            if (claim.Dispatched) return new(true);
            // An expired pre-dispatch claim no longer counts as busy, so it cannot authorize dispatch.
            if (claim.Deadline is { } deadline && deadline <= _clock.GetUtcNow()) return new(false);
            claim.Dispatched = true;
            return new(true);
        }, cancellationToken);

    /// <summary>Releases the claim bound to this proposal identity, if any.</summary>
    internal void ReleaseProposal(string sessionId, string proposalId)
    {
        if (!_rows.TryGetValue(sessionId, out var row)) return;
        lock (row)
            if (row.Find(ProposalKey(proposalId)) is { } claim)
                row.Release(claim);
    }

    /// <summary>Returns the retained pause claim, acquiring it under the session write gate when absent.</summary>
    internal Task<AgentSessionClaim?> EnsurePauseAsync(string sessionId, CancellationToken cancellationToken = default)
        => CommitAsync<AgentSessionClaim?>(sessionId, () => null, row => new(EnsurePause(row)), cancellationToken);

    /// <summary>The currently retained pause claim, if any.</summary>
    internal AgentSessionClaim? FindPause(string sessionId)
    {
        if (!_rows.TryGetValue(sessionId, out var row)) return null;
        lock (row) return row.Find(PauseKey);
    }

    /// <summary>Releases exactly this token. A released or superseded token is a no-op.</summary>
    internal void Release(AgentSessionClaim claim)
    {
        if (!_rows.TryGetValue(claim.SessionId, out var row)) return;
        lock (row) row.Release(claim);
    }

    /// <summary>Reads the session's current policy state for its owning actor and tenant.</summary>
    internal async Task<AgentSessionPolicyResult> GetPolicyAsync(string sessionId, string actorId, string tenantId, CancellationToken cancellationToken = default)
    {
        if (await RowAsync(sessionId, cancellationToken) is not { } row)
            return new(AgentSessionPolicyOutcome.NotFound, null);
        lock (row)
            return Owned(row.Session, actorId, tenantId)
                ? new(AgentSessionPolicyOutcome.Succeeded, row.State)
                : new(AgentSessionPolicyOutcome.NotFound, null);
    }

    /// <summary>
    /// Applies one conditional autonomy change. The busy check, the awaited revision write and its application are one
    /// step under the session write gate, so no claim can be admitted between them.
    /// </summary>
    internal Task<AgentSessionPolicyResult> ChangeAutonomyAsync(AgentSessionAutonomyChangeRequest request, CancellationToken cancellationToken = default)
    {
        // 1. An undefined mode is refused before any session state is consulted.
        if (!Enum.IsDefined(request.RequestedMode))
            return Task.FromResult(new AgentSessionPolicyResult(AgentSessionPolicyOutcome.InvalidMode, null));
        return CommitAsync<AgentSessionPolicyResult>(request.SessionId, () => new AgentSessionPolicyResult(AgentSessionPolicyOutcome.NotFound, null), row =>
        {
            var session = row.Session;
            if (!Owned(session, request.ActorId, request.TenantId))
                return new(new(AgentSessionPolicyOutcome.NotFound, null));
            var now = _clock.GetUtcNow();
            // 2. Closed or expired.
            if (session.Status != AgentSessionStatus.Active || session.ExpiresAt is { } expires && expires <= now)
                return new(new(AgentSessionPolicyOutcome.SessionClosed, null));
            // 3. Stale before any no-op comparison, so an old request cannot be acknowledged as current.
            if (request.ExpectedRevision != row.Revision)
                return new(new(AgentSessionPolicyOutcome.StaleRevision, row.State));
            // 4. Admitted work owns the current policy.
            if (row.IsBusy(now))
                return new(new(AgentSessionPolicyOutcome.Busy, row.State));
            // 5. Clamp to the current ceiling; 6. replace only the autonomy mode.
            var effective = session.Policy.Clamp(request.RequestedMode);
            if (effective == session.Policy.AutonomyMode)
                return new(new(AgentSessionPolicyOutcome.Succeeded, row.State));
            // 7. A real change increments the revision; it is recorded before it governs anything.
            var revision = row.Revision + 1;
            var changed = session with
            {
                Policy = session.Policy with { AutonomyMode = effective },
                PolicyRevision = revision,
                UpdatedAt = now
            };
            return new(new(AgentSessionPolicyOutcome.Succeeded, new(Freeze(changed.Policy), revision)),
                new AgentSessionRecordWrite(session.Id, row.Fence) { Session = changed }, () =>
                {
                    row.Revision = revision;
                    row.Session = changed;
                });
        }, cancellationToken);
    }

    /// <summary>
    /// One collaborator transition on a session aggregate, such as a proposal change: under the session write gate,
    /// <paramref name="plan"/> runs (outside the row monitor), its write is awaited, then its change is applied. A
    /// session that is not recorded receives a null fence; its plan's change is then applied in memory only.
    /// </summary>
    internal async Task<T> CommitAsync<T>(string sessionId, Func<AgentSessionFence?, AgentSessionTransition<T>> plan, CancellationToken cancellationToken = default)
    {
        var gate = Gate(sessionId);
        await gate.WaitAsync(cancellationToken);
        SessionRow? row = null;
        try
        {
            row = await HydrateAsync(sessionId, cancellationToken);
            AgentSessionFence? fence = null;
            if (row is not null)
                lock (row) fence = row.Fence;
            var planned = plan(fence);
            if (row is not null && planned.Write is { } write) await WriteThroughAsync(row, write);
            planned.Apply?.Invoke();
            return planned.Result;
        }
        finally
        {
            gate.Release();
            if (row is null) ForgetGate(sessionId, gate);
        }
    }

    private static AgentSessionClaim EnsurePause(SessionRow row)
    {
        if (row.Find(PauseKey) is { } existing) return existing;
        var claim = row.NewClaim(AgentSessionClaimKind.Pause, null);
        row.Bind(claim, PauseKey);
        return claim;
    }

    private static AgentSessionTransition<bool> PlanMessageStatus(SessionRow row, string messageId, AgentMessageStatus? expected,
        AgentMessageStatus status, string? reason)
    {
        var messages = row.Messages;
        var index = messages.FindIndex(x => x.Id == messageId);
        if (index < 0 || expected is { } required && messages[index].Status != required)
            return new(false);
        var terminal = status is AgentMessageStatus.Completed or AgentMessageStatus.Failed or AgentMessageStatus.Cancelled;
        var updated = messages[index] with
        {
            Status = status, CompletedAt = terminal ? DateTimeOffset.UtcNow : null,
            StatusReason = reason ?? (status == AgentMessageStatus.Cancelled ? messages[index].StatusReason : null)
        };
        return new(true, new AgentSessionRecordWrite(row.Session.Id, row.Fence) { Messages = [updated] }, () =>
        {
            messages[index] = updated;
            // Releases only this exact prompt's claim, including a never-started Pending prompt.
            if (terminal && row.Find(PromptKey(messageId)) is { } prompt)
                row.Release(prompt);
        });
    }

    /// <summary>The loaded row, hydrating it once under the session write gate; null when the session is not recorded.</summary>
    private async ValueTask<SessionRow?> RowAsync(string sessionId, CancellationToken cancellationToken)
    {
        if (_rows.TryGetValue(sessionId, out var row))
            return row;
        var gate = Gate(sessionId);
        await gate.WaitAsync(cancellationToken);
        try
        {
            row = await HydrateAsync(sessionId, cancellationToken);
            return row;
        }
        finally
        {
            gate.Release();
            if (row is null) ForgetGate(sessionId, gate);
        }
    }

    /// <summary>Under the session write gate: single-flight hydration. It never takes a proposal gate.</summary>
    private async Task<SessionRow?> HydrateAsync(string sessionId, CancellationToken cancellationToken)
    {
        if (_rows.TryGetValue(sessionId, out var loaded))
            return loaded;
        var ownership = await _store.AcquireOwnershipAsync(sessionId, _ownerToken, cancellationToken);
        if (ownership.Outcome == AgentSessionRecordOutcome.NotFound)
            return null;
        if (!ownership.Succeeded || ownership.Fence is null)
            throw new AgentContributionException($"Agent session '{sessionId}' is owned by another process.");
        if (await _store.LoadAsync(sessionId, cancellationToken) is not { } record)
            return null;
        return _rows.GetOrAdd(sessionId, new SessionRow(record, ownership.Fence));
    }

    /// <summary>Awaits one record write; an owner or fence that is no longer current evicts the row and applies nothing.</summary>
    private async Task WriteThroughAsync(SessionRow row, AgentSessionRecordWrite write)
    {
        // A planned write always completes, so the store and the applied change never diverge on cancellation.
        var written = await _store.WriteAsync(write, CancellationToken.None);
        if (written.Succeeded) return;
        if (written.Outcome is AgentSessionRecordOutcome.OwnershipLost or AgentSessionRecordOutcome.NotFound)
        {
            _rows.TryRemove(new KeyValuePair<string, SessionRow>(write.SessionId, row));
            throw new AgentContributionException($"Agent session '{write.SessionId}' is no longer owned by this process; nothing was applied.");
        }
        throw new AgentContributionException($"Agent session '{write.SessionId}' could not record the transition ({written.Outcome}); nothing was applied.");
    }

    /// <summary>Under the session write gate: hydrate, plan under the row monitor, await the write, apply under the monitor.</summary>
    private async Task<T> CommitAsync<T>(string sessionId, Func<T> missing, Func<SessionRow, AgentSessionTransition<T>> plan, CancellationToken cancellationToken)
    {
        var gate = Gate(sessionId);
        await gate.WaitAsync(cancellationToken);
        SessionRow? row = null;
        try
        {
            row = await HydrateAsync(sessionId, cancellationToken);
            if (row is null)
                return missing();
            AgentSessionTransition<T> planned;
            lock (row) planned = plan(row);
            if (planned.Write is { } write) await WriteThroughAsync(row, write);
            if (planned.Apply is { } apply)
                lock (row) apply();
            return planned.Result;
        }
        finally
        {
            gate.Release();
            if (row is null) ForgetGate(sessionId, gate);
        }
    }

    private SemaphoreSlim Gate(string sessionId) => _gates.GetOrAdd(sessionId, _ => new(1, 1));

    // Probes for sessions that are not recorded must not accumulate gates; hydration itself is idempotent.
    private void ForgetGate(string sessionId, SemaphoreSlim gate)
    {
        if (!_rows.ContainsKey(sessionId))
            _gates.TryRemove(new KeyValuePair<string, SemaphoreSlim>(sessionId, gate));
    }

    private static bool Owned(AgentSession session, string actorId, string tenantId)
        => string.Equals(session.ActorId, actorId, StringComparison.Ordinal) && string.Equals(session.TenantId, tenantId, StringComparison.Ordinal);

    /// <summary>Retained snapshots must not alias caller-owned mutable collections.</summary>
    private static AgentPolicy Freeze(AgentPolicy policy) => policy with
    {
        AllowedContextKinds = Array.AsReadOnly(policy.AllowedContextKinds.ToArray()),
        DeniedCapabilityIds = Array.AsReadOnly(policy.DeniedCapabilityIds.ToArray())
    };

    private static string PromptKey(string messageId) => "prompt:" + messageId;
    private static string ProposalKey(string proposalId) => "proposal:" + proposalId;

    /// <summary>One loaded session's state. Every member is used only under <c>lock (row)</c>.</summary>
    private sealed class SessionRow
    {
        private readonly HashSet<AgentSessionClaim> _claims = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<string, AgentSessionClaim> _keyed = new(StringComparer.Ordinal);

        public SessionRow(AgentSession session, AgentSessionFence fence)
        {
            Session = session;
            Fence = fence;
            Revision = session.PolicyRevision ?? InitialPolicyRevision;
        }

        /// <summary>Hydrates the recorded facts. Claims are never recorded; nothing is rebuilt from them here.</summary>
        public SessionRow(AgentSessionRecord record, AgentSessionFence fence) : this(record.Session, fence)
        {
            Messages.AddRange(record.Messages);
            Context.AddRange(record.Context);
            PausedTurn = record.PausedTurn;
        }

        public AgentSession Session { get; set; }
        public AgentSessionFence Fence { get; }
        public long Revision { get; set; }
        public List<AgentMessage> Messages { get; } = [];
        public List<AgentContextAttachment> Context { get; } = [];
        public AgentPausedTurn? PausedTurn { get; set; }
        // The live policy is the host's; claims and accepted messages retain a frozen snapshot of it.
        public AgentSessionPolicyState State => new(Freeze(Session.Policy), Revision);

        public AgentSessionClaim NewClaim(AgentSessionClaimKind kind, DateTimeOffset? deadline)
        {
            var claim = new AgentSessionClaim(Session.Id, kind, State, deadline);
            _claims.Add(claim);
            return claim;
        }

        public bool Bind(AgentSessionClaim claim, string key)
        {
            if (!_claims.Contains(claim) || claim.Key is not null || !_keyed.TryAdd(key, claim)) return false;
            claim.Key = key;
            return true;
        }

        public AgentSessionClaim? Find(string key) => _keyed.GetValueOrDefault(key);

        public void Release(AgentSessionClaim claim)
        {
            if (!_claims.Remove(claim)) return;
            if (claim.Key is { } key && _keyed.TryGetValue(key, out var bound) && ReferenceEquals(bound, claim))
                _keyed.Remove(key);
        }

        // A pre-dispatch proposal past its prepared deadline can never be dispatched, so it is not busy.
        public bool IsBusy(DateTimeOffset now) => _claims.Any(claim => claim.Kind != AgentSessionClaimKind.Proposal
            || claim.Dispatched || claim.Deadline is not { } deadline || deadline > now);
    }
}

/// <summary>
/// One planned transition of a session aggregate: its result, the record write that makes it durable (none for a
/// refusal or a no-op), and the in-memory change applied only after that write succeeded.
/// </summary>
internal readonly record struct AgentSessionTransition<T>(T Result, AgentSessionRecordWrite? Write = null, Action? Apply = null);

internal sealed class InMemoryAgentFeedbackService(IAgentAuditSink auditSink) : IAgentFeedbackService
{
    private readonly ConcurrentDictionary<string, AgentFeedback> _feedback = new(StringComparer.Ordinal);

    public async Task<AgentFeedback> AddAsync(AgentFeedback feedback, CancellationToken cancellationToken = default)
    {
        _feedback[feedback.Id] = feedback;
        await auditSink.EmitAsync(new(
            NewId(),
            AgentAuditEventKind.FeedbackReceived,
            feedback.SessionId,
            feedback.ActorId,
            "Agent feedback received.",
            feedback.CreatedAt,
            new Dictionary<string, string> { ["feedbackId"] = feedback.Id, ["messageId"] = feedback.MessageId ?? string.Empty, ["rating"] = feedback.Rating }), cancellationToken);

        return feedback;
    }
}

/// <summary>
/// Process-local audit records and their session ownership association. One monitor orders append,
/// association and query, so an owner query observes a consistent snapshot of both.
/// </summary>
internal sealed class InMemoryAgentAuditStore : IAgentAuditSink, IAgentAuditReader, IAgentAuditOwnership
{
    private readonly List<AgentAuditEvent> _events = [];
    private readonly Dictionary<string, (string ActorId, string TenantId)> _owners = new(StringComparer.Ordinal);

    public Task EmitAsync(AgentAuditEvent auditEvent, CancellationToken cancellationToken = default)
    {
        lock (_events)
            _events.Add(auditEvent);

        return Task.CompletedTask;
    }

    public Task<IReadOnlyCollection<AgentAuditEvent>> ListAsync(string? sessionId = null, int? take = null, CancellationToken cancellationToken = default)
    {
        lock (_events)
        {
            IEnumerable<AgentAuditEvent> query = _events
                .Where(x => sessionId is null || string.Equals(x.SessionId, sessionId, StringComparison.Ordinal))
                .OrderByDescending(x => x.CreatedAt);

            if (take is > 0)
                query = query.Take(take.Value);

            return Task.FromResult<IReadOnlyCollection<AgentAuditEvent>>(query.ToList());
        }
    }

    public Task<AgentAuditQueryResult> ListOwnedAsync(AgentAuditOwnerQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentException.ThrowIfNullOrWhiteSpace(query.ActorId, nameof(query));
        ArgumentException.ThrowIfNullOrWhiteSpace(query.TenantId, nameof(query));
        ArgumentOutOfRangeException.ThrowIfLessThan(query.Take, 1, nameof(query));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(query.Take, AgentAuditOwnerQuery.MaxTake, nameof(query));
        cancellationToken.ThrowIfCancellationRequested();
        lock (_events)
        {
            // Ownership and the session filter apply before ordering and the limit; ties keep the latest append first.
            var events = _events
                .Select((item, sequence) => (Item: item, Sequence: sequence))
                .Where(x => x.Item.SessionId is { } sessionId
                    && (query.SessionId is null || string.Equals(sessionId, query.SessionId, StringComparison.Ordinal))
                    && _owners.TryGetValue(sessionId, out var owner)
                    && string.Equals(owner.ActorId, query.ActorId, StringComparison.Ordinal)
                    && string.Equals(owner.TenantId, query.TenantId, StringComparison.Ordinal))
                .OrderByDescending(x => x.Item.CreatedAt)
                .ThenByDescending(x => x.Sequence)
                .Take(query.Take)
                .Select(x => x.Item)
                .ToList();
            return Task.FromResult(new AgentAuditQueryResult(AgentAuditQueryOutcome.Succeeded, events.AsReadOnly()));
        }
    }

    public Task<bool> AssociateAsync(string sessionId, string actorId, string tenantId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(actorId);
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_events)
        {
            if (_owners.TryGetValue(sessionId, out var owner))
                return Task.FromResult(string.Equals(owner.ActorId, actorId, StringComparison.Ordinal)
                    && string.Equals(owner.TenantId, tenantId, StringComparison.Ordinal));
            _owners[sessionId] = (actorId, tenantId);
            return Task.FromResult(true);
        }
    }
}

internal sealed class DefaultAgentProviderRegistry : IAgentProviderRegistry
{
    public DefaultAgentProviderRegistry(IEnumerable<IAgentProvider> providers)
    {
        var registered = providers.ToList();
        if (registered.Count > 1)
            throw new AgentHarnessConflictException(registered.Select(x => x.ProviderId).ToList());

        Active = registered.SingleOrDefault();
    }

    public IAgentProvider? Active { get; }
}

internal static class AgentIds
{
    public static string NewId() => ShortIdentityGenerator.Generate(DateTimeOffset.UtcNow);
}
