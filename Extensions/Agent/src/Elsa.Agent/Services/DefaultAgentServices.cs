using System.Collections.Concurrent;
using System.Text.Json;
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
    IAgentSessionRecordStore? recordStore = null, InMemoryAgentProposalStore? proposalStore = null, IAgentTurnRegistry? turns = null)
    : IAgentSessionService, IAgentSessionPolicyParticipant, IDisposable, IAsyncDisposable
{
    /// <summary>How often leases of sessions with a running turn or a running executor are renewed in the background.</summary>
    internal static readonly TimeSpan RenewalInterval = TimeSpan.FromSeconds(10);
    private const long InitialPolicyRevision = 1;
    private const string PauseKey = "pause";
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly IAgentSessionRecordStore _store = recordStore ?? new InMemoryAgentSessionRecordStore(clock);
    private readonly string _ownerToken = NewId();
    private readonly ConcurrentDictionary<string, SessionRow> _rows = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _gates = new(StringComparer.Ordinal);
    private ITimer? _renewal;

    InMemoryAgentSessionService? IAgentSessionPolicyParticipant.PolicyOwner => this;

    /// <summary>The record store this owner writes through.</summary>
    internal IAgentSessionRecordStore RecordStore => _store;

    public async Task<AgentSession> CreateAsync(AgentSessionCreateRequest request, CancellationToken cancellationToken = default)
    {
        // Refused before any audit owner is associated or anything recorded, whatever the record store.
        if (request.Workspace is { Length: > AgentSessionCreateRequest.MaxWorkspaceLength })
            throw new ArgumentException($"The workspace exceeds {AgentSessionCreateRequest.MaxWorkspaceLength} characters.", nameof(request));
        if (request.ContinuedFrom is { Length: > AgentSessionCreateRequest.MaxContinuedFromLength })
            throw new ArgumentException($"The continued session identity exceeds {AgentSessionCreateRequest.MaxContinuedFromLength} characters.", nameof(request));
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
        await ReleaseIfShutDownAsync(session.Id, created.Fence);

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

    /// <summary>
    /// The owner of a loaded row, or else the record store's plain owner columns. Nothing is hydrated, no ownership is
    /// taken and nothing is written, so a caller authorized on it cannot cause settlement or a takeover.
    /// </summary>
    public async Task<AgentSessionOwner?> FindOwnerAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        if (_rows.TryGetValue(sessionId, out var row))
            lock (row)
                return new(row.Session.ActorId, row.Session.TenantId);
        return await _store.FindOwnerAsync(sessionId, cancellationToken);
    }

    /// <summary>The session that records a proposal, read from the record store without hydrating it.</summary>
    internal Task<string?> FindProposalSessionAsync(string proposalId, CancellationToken cancellationToken = default)
        => _store.FindProposalSessionAsync(proposalId, cancellationToken);

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
        // Canonical records are computed before the row monitor is taken; only snapshots the session has not recorded are written.
        var snapshots = message.DisclosureHistory?.Select(AgentDisclosureHistory.Record).ToArray() ?? [];

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
            var attributed = row.Intern(message with { AcceptedPolicy = row.State });
            var added = row.Unrecorded(snapshots);
            return new((attributed, true), new AgentSessionRecordWrite(sessionId, row.Fence) { Messages = [ToRecord(attributed)], Snapshots = added }, () =>
            {
                if (request.Role == AgentRole.User && request.Status == AgentMessageStatus.Pending)
                    row.Bind(row.NewClaim(AgentSessionClaimKind.Prompt, null), PromptKey(attributed.Id));
                row.Recorded(attributed.DisclosureHistory);
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
    /// Withdraws a prompt that never started. Under the session write gate, the prompt must still be Pending and no turn may
    /// be registered for the session: a registered turn may be about to start it, and a start's own compare-and-set from
    /// Pending cannot interleave with this one. The prompt becomes Cancelled with reason <c>withdrawn</c> and its Prompt
    /// claim is released.
    /// </summary>
    public Task<AgentResult<AgentMessage>> WithdrawMessageAsync(string sessionId, string messageId, CancellationToken cancellationToken = default)
        => CommitAsync<AgentResult<AgentMessage>>(sessionId,
            () => AgentResult<AgentMessage>.Failure("agent.session.not_found", $"Agent session '{sessionId}' was not found.", 404), row =>
            {
                var message = row.Messages.FirstOrDefault(x => string.Equals(x.Id, messageId, StringComparison.Ordinal));
                if (message is not { Role: AgentRole.User })
                    return new(AgentResult<AgentMessage>.Failure("agent.message.not_found", $"Agent message '{messageId}' was not found in session '{sessionId}'.", 404));
                if (message.Status != AgentMessageStatus.Pending || turns?.IsActive(row.Session.Id) == true)
                    return new(AgentResult<AgentMessage>.Failure("agent.message.not_pending", "The prompt is not waiting to start, so it cannot be withdrawn.", 409));
                var planned = PlanMessageStatus(row, messageId, AgentMessageStatus.Pending, AgentMessageStatus.Cancelled, AgentMessageStatusReasons.Withdrawn);
                // A prompt has no disclosure history, so its record holds the complete message.
                return new(AgentResult<AgentMessage>.Success(planned.Write!.Messages[0].Message), planned.Write, planned.Apply);
            }, cancellationToken);

    /// <summary>The record store's owner-scoped list; it filters by owner before it orders and limits, and decodes no payload.</summary>
    public async Task<AgentSessionListResult> ListOwnedAsync(AgentSessionOwnerQuery query, CancellationToken cancellationToken = default)
        => new(AgentSessionQueryOutcome.Succeeded, await _store.ListOwnedAsync(query, cancellationToken));

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

    /// <summary>
    /// Records the documented session limit a turn was refused at, so the session summary and detail report it after a
    /// reload. Nothing else changes: no provenance is erased, no budget reset and nothing replayed. Repeating the same limit
    /// writes nothing.
    /// </summary>
    internal Task RecordLimitAsync(string sessionId, string limit, CancellationToken cancellationToken = default)
        => CommitAsync<bool>(sessionId, () => false, row =>
        {
            if (string.Equals(row.Session.LimitReached, limit, StringComparison.Ordinal)) return new(false);
            var limited = row.Session with { LimitReached = limit, UpdatedAt = _clock.GetUtcNow() };
            return new(true, new AgentSessionRecordWrite(row.Session.Id, row.Fence) { Session = limited }, () => row.Session = limited);
        }, cancellationToken);

    /// <summary>Acquires an unkeyed claim under the session write gate; null when the session is not recorded.</summary>
    internal Task<AgentSessionClaim?> AcquireAsync(string sessionId, AgentSessionClaimKind kind, CancellationToken cancellationToken = default)
    {
        if (kind is AgentSessionClaimKind.Prompt or AgentSessionClaimKind.Proposal or AgentSessionClaimKind.Pause)
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "Keyed claims are acquired through their owning transition.");
        if (kind == AgentSessionClaimKind.Foreground) EnsureRenewal();
        return CommitAsync<AgentSessionClaim?>(sessionId, () => null, row => new(row.NewClaim(kind, null)), cancellationToken);
    }

    /// <summary>Acquires a provisional proposal claim before store insertion; it records the prepared deadline.</summary>
    internal Task<AgentSessionClaim?> AcquireProposalAsync(string sessionId, DateTimeOffset? deadline, CancellationToken cancellationToken = default)
        => CommitAsync<AgentSessionClaim?>(sessionId, () => null, row => new(row.NewClaim(AgentSessionClaimKind.Proposal, deadline)), cancellationToken);

    /// <summary>
    /// Gives a recorded, unsettled proposal its claim on the session's current row, as hydration rebuilds it, when the
    /// provisional claim could not be bound because its row was replaced between acquisition and insertion. An existing
    /// claim (for example, one a newer hydration rebuilt from the record) is kept.
    /// </summary>
    internal Task EnsureProposalClaimAsync(string sessionId, string proposalId, DateTimeOffset? deadline, CancellationToken cancellationToken = default)
        => CommitAsync<bool>(sessionId, () => false, row =>
        {
            if (row.Find(ProposalKey(proposalId)) is null)
                row.Bind(row.NewClaim(AgentSessionClaimKind.Proposal, deadline), ProposalKey(proposalId));
            return new(true);
        }, cancellationToken);

    /// <summary>Binds a provisional proposal claim to its inserted proposal identity.</summary>
    internal bool BindProposal(AgentSessionClaim claim, string proposalId)
    {
        if (claim.Kind != AgentSessionClaimKind.Proposal || !_rows.TryGetValue(claim.SessionId, out var row)) return false;
        lock (row) return row.Bind(claim, ProposalKey(proposalId));
    }

    /// <summary>
    /// Checks that the proposal's claim may still authorize dispatch; false refuses a pre-dispatch claim past its
    /// deadline, and a session that is not recorded, which can never authorize dispatch. Nothing is marked.
    /// </summary>
    internal Task<bool> MarkProposalDispatchedAsync(string sessionId, string proposalId, CancellationToken cancellationToken = default)
        => DispatchProposalAsync(sessionId, proposalId, null, cancellationToken);

    /// <summary>
    /// Under the session write gate: checks that the proposal's claim may still authorize dispatch, awaits the
    /// <paramref name="executing"/> transition's write (the recorded Executing status), applies it, and only then marks the
    /// claim dispatched with a live executor. A refused or failed write leaves the claim exactly as it was, so a dispatch
    /// that never happened can never keep the session busy or its lease renewed. False refuses a pre-dispatch claim past
    /// its deadline, and a session that is not recorded.
    /// </summary>
    internal async Task<bool> DispatchProposalAsync(string sessionId, string proposalId, Func<AgentSessionFence, AgentSessionTransition<bool>>? executing,
        CancellationToken cancellationToken = default)
    {
        var gate = Gate(sessionId);
        await gate.WaitAsync(cancellationToken);
        var notRecorded = false;
        try
        {
            var row = await HydrateAsync(sessionId, cancellationToken);
            notRecorded = row is null;
            if (row is null) return false;
            AgentSessionClaim? claim;
            AgentSessionFence fence;
            lock (row)
            {
                // Every unsettled proposal of a loaded row holds a claim, so a missing one means this process does not hold the
                // proposal's current state (for example, another process settled it); it can never authorize dispatch.
                claim = row.Find(ProposalKey(proposalId));
                // An expired pre-dispatch claim no longer counts as busy, so it cannot authorize dispatch.
                if (claim is null || !claim.Dispatched && claim.Deadline is { } deadline && deadline <= _clock.GetUtcNow()) return false;
                fence = row.Fence;
            }
            if (executing is null) return true;
            // The plan runs and its change applies outside the row monitor, as every collaborator transition does.
            var planned = executing(fence);
            if (planned.Write is { } write) await WriteThroughAsync(sessionId, row, write);
            planned.Apply?.Invoke();
            lock (row)
            {
                claim.Dispatched = true;
                claim.Executing = true;
            }
            EnsureRenewal();
            return true;
        }
        finally
        {
            gate.Release();
            if (notRecorded) ForgetGate(sessionId, gate);
        }
    }

    /// <summary>The proposal's executor returned (or failed): its claim no longer needs its lease renewed for it.</summary>
    internal void EndExecution(string sessionId, string proposalId)
    {
        if (!_rows.TryGetValue(sessionId, out var row)) return;
        lock (row)
            if (row.Find(ProposalKey(proposalId)) is { } claim)
                claim.Executing = false;
    }

    /// <summary>One proposal's current record, read from the record store without hydrating or changing anything; null when it is not recorded.</summary>
    internal async Task<AgentProposalRecord?> LoadProposalRecordAsync(string sessionId, string proposalId, CancellationToken cancellationToken = default)
        => (await _store.LoadAsync(sessionId, cancellationToken))?.Proposals.FirstOrDefault(x => string.Equals(x.Proposal.Id, proposalId, StringComparison.Ordinal));

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

    /// <summary>
    /// Records the paused turn on its session's record and, in the same transition, the session's Pause claim, so the
    /// recorded pause and the claim that keeps its policy never come apart.
    /// </summary>
    internal Task SavePausedTurnAsync(AgentPausedTurn state, CancellationToken cancellationToken = default)
    {
        var snapshots = state.DisclosureHistory?.Select(AgentDisclosureHistory.Record).ToArray() ?? [];
        return CommitAsync<bool>(state.SessionId, () => throw new InvalidOperationException($"Agent session '{state.SessionId}' was not found."), row =>
        {
            var interned = row.Intern(state);
            var added = row.Unrecorded(snapshots);
            return new(true, new AgentSessionRecordWrite(row.Session.Id, row.Fence) { PausedTurn = ToRecord(interned), Snapshots = added }, () =>
            {
                row.Recorded(interned.DisclosureHistory);
                row.PausedTurn = interned;
                EnsurePause(row);
            });
        }, cancellationToken);
    }

    /// <summary>The session's recorded paused turn, after hydrating the session; null when there is none.</summary>
    internal async Task<AgentPausedTurn?> FindPausedTurnAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        if (await RowAsync(sessionId, cancellationToken) is not { } row)
            return null;
        lock (row)
            return row.PausedTurn;
    }

    /// <summary>Removes the session's recorded paused turn. Its Pause claim is released by the continuation that observed it.</summary>
    internal Task RemovePausedTurnAsync(string sessionId, CancellationToken cancellationToken = default)
        => CommitAsync<bool>(sessionId, () => false, row => row.PausedTurn is null
            ? new(false)
            : new(true, new AgentSessionRecordWrite(row.Session.Id, row.Fence) { RemovePausedTurn = true }, () => row.PausedTurn = null), cancellationToken);

    /// <summary>Releases exactly this token. A released or superseded token is a no-op.</summary>
    internal void Release(AgentSessionClaim claim)
    {
        if (!_rows.TryGetValue(claim.SessionId, out var row)) return;
        lock (row) row.Release(claim);
    }

    /// <summary>The session's current policy revision after hydrating it; null when the session is not recorded.</summary>
    internal async Task<long?> CurrentRevisionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        if (await RowAsync(sessionId, cancellationToken) is not { } row)
            return null;
        lock (row)
            return row.Revision;
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
        var notRecorded = false;
        try
        {
            row = await HydrateAsync(sessionId, cancellationToken);
            notRecorded = row is null;
            AgentSessionFence? fence = null;
            if (row is not null)
                lock (row) fence = row.Fence;
            var planned = plan(fence);
            if (row is not null && planned.Write is { } write) await WriteThroughAsync(sessionId, row, write);
            planned.Apply?.Invoke();
            return planned.Result;
        }
        finally
        {
            gate.Release();
            if (notRecorded) ForgetGate(sessionId, gate);
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
        return new(true, new AgentSessionRecordWrite(row.Session.Id, row.Fence) { Messages = [ToRecord(updated)] }, () =>
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
        var notRecorded = false;
        try
        {
            row = await HydrateAsync(sessionId, cancellationToken);
            notRecorded = row is null;
            return row;
        }
        finally
        {
            gate.Release();
            if (notRecorded) ForgetGate(sessionId, gate);
        }
    }

    /// <summary>
    /// The decode failures a record store reports for recorded state it cannot read, and hydration's own verification
    /// refusals. A store that maps its decode failures itself (as the EF store does) already throws
    /// <see cref="AgentSessionUnavailableException"/>; an infrastructure failure is never in this set.
    /// </summary>
    private static bool IsUnreadable(Exception failure)
        => failure is AgentSessionUnavailableException or JsonException or NotSupportedException or ArgumentException
            or InvalidDataException or FormatException or InvalidCastException or OverflowException;

    /// <summary>Under the session write gate: single-flight hydration. It never takes a proposal gate.</summary>
    private async Task<SessionRow?> HydrateAsync(string sessionId, CancellationToken cancellationToken)
    {
        if (_rows.TryGetValue(sessionId, out var loaded))
            return loaded;
        // A process that began shutting down takes no new ownership: its sessions go to the next process at once.
        ThrowIfShutDown(sessionId);
        var ownership = await _store.AcquireOwnershipAsync(sessionId, _ownerToken, cancellationToken);
        if (ownership.Outcome == AgentSessionRecordOutcome.NotFound)
            return null;
        if (!ownership.Succeeded || ownership.Fence is null)
            throw new AgentSessionOwnershipException(sessionId, AgentSessionOwnershipException.OwnedElsewhere,
                "Another host process currently owns this session's work. Retry after its lease lapses.", ownership.LeaseExpiresAt);
        if (await ReleaseIfShutDownAsync(sessionId, ownership.Fence))
            ThrowIfShutDown(sessionId);
        AgentSessionRecord? record;
        HydratedHistory history;
        try
        {
            if ((record = await _store.LoadAsync(sessionId, cancellationToken)) is null)
                return null;
            // Every snapshot, history and fingerprint is verified before anything is settled or acts on the row (FR-018).
            history = HydratedHistory.Rebuild(record);
        }
        // An ownership refusal, a disposed store or a cancellation keeps its own outcome and its lease handling.
        catch (Exception failure) when (failure is not (OperationCanceledException or ObjectDisposedException or AgentSessionOwnershipException))
        {
            // Nothing was settled; the lease this hydration took is given back so the failure leaves no trace.
            try { await _store.ReleaseOwnershipAsync(sessionId, ownership.Fence, CancellationToken.None); }
            catch (Exception) { }
            // Only verification or a decode failure shows the recorded state itself to be unreadable (FR-018). A store
            // that cannot answer right now (a pool timeout, exhausted retries, a read that kept changing, which the EF
            // store reports as AgentContributionException) keeps its own, retryable outcome.
            if (IsUnreadable(failure))
                throw failure as AgentSessionUnavailableException ?? new AgentSessionUnavailableException(sessionId, "unreadable", failure);
            throw;
        }

        // Recovery settles what the recorded facts already decide, in one write, before anything acts on the row. The audit
        // records a settlement owes are written with it (with any an earlier hydration recorded but did not get to emit),
        // and cleared only once emitted, so a process that stops in between never loses them.
        var recovery = Recover(record, history.Messages, ownership.Fence, _clock.GetUtcNow());
        var audits = record.PendingAudits.Concat(recovery.Audits).ToArray();
        try
        {
            if (recovery.Write is { } write)
                await WriteThroughAsync(sessionId, null, write with { PendingAudits = audits });
            // Owed records are emitted and cleared before the row is published: if either fails, nothing is loaded, so the
            // session's next access (in this process or another) hydrates again and emits them then.
            if (audits.Length > 0)
            {
                foreach (var settled in audits)
                    await auditSink.EmitAsync(settled, CancellationToken.None);
                await WriteThroughAsync(sessionId, null, new AgentSessionRecordWrite(sessionId, ownership.Fence) { PendingAudits = [] });
            }
        }
        // A lost ownership is no longer this process's lease to give back; any other refusal or failure leaves the recorded
        // facts as they are, so the lease this hydration took is released, as a failed load releases it.
        catch (Exception failure) when (failure is not AgentSessionOwnershipException)
        {
            try { await _store.ReleaseOwnershipAsync(sessionId, ownership.Fence, CancellationToken.None); }
            catch (Exception) { }
            throw;
        }
        var row = new SessionRow(recovery.Record.Session, ownership.Fence, recovery.Messages, recovery.Record.Context, history.PausedTurn, history.Snapshots);
        lock (row) RebuildClaims(row, recovery.Messages, history.PausedTurn, recovery.Record.Proposals);
        LoadProposals(recovery.Record);
        row = _rows.GetOrAdd(sessionId, row);
        bool renew;
        lock (row) renew = row.NeedsRenewal;
        if (renew) EnsureRenewal();
        return row;
    }

    /// <summary>
    /// The zero-effect outcomes the recorded facts decide at hydration (research R4): an expired session is closed, a
    /// prompt that was streaming without a pause is interrupted, and overdue or in-flight proposals settle. Settlement
    /// takes no proposal gate; it applies only to the recorded status it read.
    /// </summary>
    private (AgentSessionRecord Record, IReadOnlyList<AgentMessage> Messages, AgentSessionRecordWrite? Write, IReadOnlyList<AgentAuditEvent> Audits) Recover(
        AgentSessionRecord record, IReadOnlyList<AgentMessage> hydrated, AgentSessionFence fence, DateTimeOffset now)
    {
        AgentSession? closed = null;
        if (record.Session.Status == AgentSessionStatus.Active && record.Session.ExpiresAt is { } expires && expires <= now)
            closed = record.Session with { Status = AgentSessionStatus.Expired, UpdatedAt = now };
        var interrupted = hydrated
            .Where(x => x.Status == AgentMessageStatus.Streaming && x.Id != record.PausedTurn?.Turn.SourceMessageId)
            .Select(x => x with { Status = AgentMessageStatus.Cancelled, CompletedAt = now, StatusReason = AgentMessageStatusReasons.Interrupted })
            .ToArray();
        // Each settlement writes from the version it read, so a concurrent change refuses it instead of being overwritten.
        var settlements = record.Proposals.Select(x => AgentProposalRecovery.Settle(x, now)).OfType<AgentProposalRecovery.Settlement>().ToArray();
        if (closed is null && interrupted.Length == 0 && settlements.Length == 0)
            return (record, hydrated, null, []);

        var messages = hydrated.Select(x => interrupted.FirstOrDefault(y => y.Id == x.Id) ?? x).ToArray();
        var proposals = record.Proposals.Select(x => settlements.FirstOrDefault(y => y.Record.Proposal.Id == x.Proposal.Id) is { } settled
            ? settled.Record with { Version = settled.Record.Version + 1 } : x).ToArray();
        var recovered = record with { Session = closed ?? record.Session, Proposals = proposals };
        var audits = settlements.Select(x => AgentProposalRecovery.Audit(x, now)).ToArray();
        return (recovered, messages, new AgentSessionRecordWrite(record.Session.Id, fence)
        {
            Session = closed, Messages = interrupted.Select(ToRecord).ToArray(), Proposals = settlements.Select(x => x.Record).ToArray()
        }, audits);
    }

    /// <summary>
    /// Rebuilds the claims the recorded facts imply (research R4): a Prompt claim per pending prompt, the Pause claim of
    /// a paused turn, and a Proposal claim per unsettled prepared proposal with its deadline, dispatched when its request
    /// already left. Foreground and Preparation claims never survive a process.
    /// </summary>
    private static void RebuildClaims(SessionRow row, IReadOnlyList<AgentMessage> messages, AgentPausedTurn? paused, IReadOnlyList<AgentProposalRecord> proposals)
    {
        foreach (var prompt in messages.Where(x => x.Role == AgentRole.User && x.Status == AgentMessageStatus.Pending))
            row.Bind(row.NewClaim(AgentSessionClaimKind.Prompt, null), PromptKey(prompt.Id));
        if (paused is not null)
            EnsurePause(row);
        foreach (var proposal in proposals.Select(x => x.Proposal))
        {
            if (proposal.PreparedInvocation is not { } prepared || InMemoryAgentProposalService.Settled(proposal)) continue;
            var claim = row.NewClaim(AgentSessionClaimKind.Proposal, prepared.ExpiresAt);
            claim.Dispatched = proposal.Status is AgentActionProposalStatus.Executing or AgentActionProposalStatus.AwaitingClient
                or AgentActionProposalStatus.Uncertain;
            row.Bind(claim, ProposalKey(proposal.Id));
        }
    }

    /// <summary>Loads the session's recorded proposals, outcomes and duplicate-detection keys into the process-local proposal store.</summary>
    private void LoadProposals(AgentSessionRecord record)
    {
        if (proposalStore is null) return;
        lock (proposalStore.Mutation)
        {
            foreach (var recorded in record.Proposals)
            {
                var proposal = recorded.Proposal;
                proposalStore.Proposals[proposal.Id] = proposal;
                proposalStore.Versions[proposal.Id] = recorded.Version;
                if (recorded.Execution is { } execution) proposalStore.Executions[proposal.Id] = execution;
                if (recorded.Result is { } result) proposalStore.Results[proposal.Id] = result;
                if (recorded.AcknowledgementDigest is { } digest && proposal.PreparedInvocation is { } prepared)
                    proposalStore.Acknowledgements[prepared.InvocationId] = digest;
            }
            foreach (var key in record.Keys)
            {
                var index = key.Kind switch
                {
                    AgentSessionKeyKinds.Call => proposalStore.CallProposals,
                    AgentSessionKeyKinds.Invocation => proposalStore.InvocationProposals,
                    AgentSessionKeyKinds.TurnMutation => proposalStore.TurnMutations,
                    AgentSessionKeyKinds.CallInput => proposalStore.CallInputs,
                    _ => null
                };
                index?[key.Key] = key.Value;
            }
        }
    }

    /// <summary>Hydrates the session that records a proposal this owner has not loaded; false when no recorded session holds it.</summary>
    internal async Task<bool> HydrateProposalAsync(string proposalId, CancellationToken cancellationToken = default)
        => await _store.FindProposalSessionAsync(proposalId, cancellationToken) is { } sessionId && await RowAsync(sessionId, cancellationToken) is not null;

    /// <summary>Hydrates a session this owner has not loaded; false when it is not recorded.</summary>
    internal async Task<bool> HydrateSessionAsync(string sessionId, CancellationToken cancellationToken = default)
        => await RowAsync(sessionId, cancellationToken) is not null;

    /// <summary>Awaits one record write; an owner or fence that is no longer current evicts the row and applies nothing.</summary>
    private async Task WriteThroughAsync(string sessionId, SessionRow? row, AgentSessionRecordWrite write)
    {
        // A planned write always completes, so the store and the applied change never diverge on cancellation.
        var written = await _store.WriteAsync(write, CancellationToken.None);
        if (written.Succeeded)
        {
            // A write extends the lease; one that completes after shutdown released it must not leave a full-length lease.
            await ReleaseIfShutDownAsync(sessionId, write.Fence);
            return;
        }
        if (written.Outcome is AgentSessionRecordOutcome.OwnershipLost or AgentSessionRecordOutcome.NotFound)
        {
            if (row is not null) LoseOwnership(sessionId, row);
            throw new AgentSessionOwnershipException(sessionId, AgentSessionOwnershipException.OwnershipLost,
                "This host process lost the session's ownership to another process; nothing was applied.");
        }
        // A proposal decided on a version that moved meanwhile; its caller refuses the decision with a conflict.
        if (written.Outcome == AgentSessionRecordOutcome.VersionConflict)
            throw new AgentProposalConflictException(write.SessionId);
        throw new AgentContributionException($"Agent session '{write.SessionId}' could not record the transition ({written.Outcome}); nothing was applied.");
    }

    /// <summary>Under the session write gate: hydrate, plan under the row monitor, await the write, apply under the monitor.</summary>
    private async Task<T> CommitAsync<T>(string sessionId, Func<T> missing, Func<SessionRow, AgentSessionTransition<T>> plan, CancellationToken cancellationToken)
    {
        var gate = Gate(sessionId);
        await gate.WaitAsync(cancellationToken);
        SessionRow? row = null;
        var notRecorded = false;
        try
        {
            row = await HydrateAsync(sessionId, cancellationToken);
            notRecorded = row is null;
            if (row is null)
                return missing();
            AgentSessionTransition<T> planned;
            lock (row) planned = plan(row);
            if (planned.Write is { } write) await WriteThroughAsync(sessionId, row, write);
            if (planned.Apply is { } apply)
                lock (row) apply();
            return planned.Result;
        }
        finally
        {
            gate.Release();
            if (notRecorded) ForgetGate(sessionId, gate);
        }
    }

    /// <summary>
    /// Renews the lease of every loaded session that has a running turn or a running executor, as the background
    /// timer does. A session whose ownership moved is lost: its row is evicted and its running turn cancelled. After a
    /// graceful shutdown began, nothing is renewed, and a renewal that was already in flight releases the lease it
    /// extended again, so it can never undo the shutdown's release.
    /// </summary>
    internal async Task RenewLeasesAsync(CancellationToken cancellationToken = default)
    {
        foreach (var (sessionId, row) in _rows.ToArray())
        {
            if (Volatile.Read(ref _disposed) == 1) return;
            AgentSessionFence fence;
            lock (row)
            {
                if (!row.NeedsRenewal) continue;
                fence = row.Fence;
            }
            AgentSessionRecordResult renewed;
            // One session's failing read must not stop the renewal of the others; the next guarded write detects a lost lease.
            try { renewed = await _store.AcquireOwnershipAsync(sessionId, _ownerToken, cancellationToken); }
            catch (Exception) when (!cancellationToken.IsCancellationRequested) { continue; }
            if (!renewed.Succeeded || renewed.Fence != fence)
                LoseOwnership(sessionId, row);
            // Shutdown marks itself disposed before it releases, so a renewal landing after that release sees the mark here.
            else if (Volatile.Read(ref _disposed) == 1)
            {
                try { await _store.ReleaseOwnershipAsync(sessionId, renewed.Fence!, CancellationToken.None); }
                catch (Exception) { }
            }
        }
    }

    /// <summary>Whether the background lease renewal is running.</summary>
    internal bool RenewalActive => Volatile.Read(ref _renewal) is not null;

    /// <summary>How long disposal waits for the record store to release this process's leases.</summary>
    private static readonly TimeSpan ReleaseTimeout = TimeSpan.FromSeconds(5);
    private int _disposed;

    /// <summary>
    /// A graceful shutdown stops lease renewal and releases the lease of every loaded session, so a restarted process
    /// takes its sessions over at once instead of waiting for the leases to lapse. Release is best effort: a release that
    /// fails or does not finish in time leaves its lease to lapse, and fences still refuse any later write from here.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
        Interlocked.Exchange(ref _renewal, null)?.Dispose();
        try { ReleaseLeasesAsync().Wait(ReleaseTimeout); }
        catch (Exception) { }
    }

    /// <inheritdoc cref="Dispose"/>
    public ValueTask DisposeAsync() => new(ShutDownAsync());

    /// <summary>
    /// The graceful shutdown of <see cref="Dispose"/>, run by <see cref="ReleaseAgentSessionLeasesTerminator"/> while the
    /// shell's container is still usable: a store that opens a scope per operation (the EF store) can no longer do so once
    /// the container is being disposed. Idempotent; the container's later disposal finds the work already done.
    /// </summary>
    internal async Task ShutDownAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
        Interlocked.Exchange(ref _renewal, null)?.Dispose();
        try { await ReleaseLeasesAsync().WaitAsync(ReleaseTimeout); }
        catch (Exception) { }
    }

    private void ThrowIfShutDown(string sessionId)
    {
        if (Volatile.Read(ref _disposed) == 1)
            throw new ObjectDisposedException(nameof(InMemoryAgentSessionService), $"This host process is shutting down; agent session '{sessionId}' was not loaded.");
    }

    /// <summary>After shutdown began, gives back the lease a completed store operation just took or extended; true when it did.</summary>
    private async Task<bool> ReleaseIfShutDownAsync(string sessionId, AgentSessionFence fence)
    {
        if (Volatile.Read(ref _disposed) == 0) return false;
        try { await _store.ReleaseOwnershipAsync(sessionId, fence, CancellationToken.None); }
        catch (Exception) { }
        return true;
    }

    private Task ReleaseLeasesAsync() => Task.WhenAll(_rows.ToArray().Select(async loaded =>
    {
        AgentSessionFence fence;
        lock (loaded.Value) fence = loaded.Value.Fence;
        try { await _store.ReleaseOwnershipAsync(loaded.Key, fence, CancellationToken.None); }
        catch (Exception) { }
    }));

    private void EnsureRenewal()
    {
        if (Volatile.Read(ref _renewal) is not null || Volatile.Read(ref _disposed) == 1) return;
        var timer = _clock.CreateTimer(_ => _ = RenewInBackgroundAsync(), null, RenewalInterval, RenewalInterval);
        if (Interlocked.CompareExchange(ref _renewal, timer, null) is not null) timer.Dispose();
        // A shutdown that began meanwhile already stopped renewal; this late timer must not outlive it.
        else if (Volatile.Read(ref _disposed) == 1) Interlocked.Exchange(ref _renewal, null)?.Dispose();
    }

    private async Task RenewInBackgroundAsync()
    {
        // Renewal is best effort: a lease it fails to extend is detected by the next guarded write instead.
        try { await RenewLeasesAsync(); }
        catch (Exception) { }
    }

    /// <summary>
    /// Evicts a row whose ownership moved: its claims become no-ops, everything loaded for its proposals is forgotten,
    /// and the session's running turn is cancelled. Only the current row is lost: a stale row read before an eviction and
    /// a newer hydration never ends the newer row's work.
    /// </summary>
    private void LoseOwnership(string sessionId, SessionRow row)
    {
        if (!_rows.TryRemove(new KeyValuePair<string, SessionRow>(sessionId, row))) return;
        if (proposalStore is not null)
            lock (proposalStore.Mutation) proposalStore.Forget(sessionId);
        // Only the default registry can cancel by session; a replaced registry's turn fails at its next guarded write.
        if (turns is AgentTurnRegistry registry) registry.CancelSession(sessionId);
    }

    private SemaphoreSlim Gate(string sessionId) => _gates.GetOrAdd(sessionId, _ => new(1, 1));

    // Probes for sessions that are not recorded must not accumulate gates. Only a hydration that completed and found no
    // record forgets the gate; after a failure or cancellation other callers may still be queued on it.
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

    /// <summary>A message as the record store holds it: its disclosure history becomes ordered digests plus their fingerprint.</summary>
    private static AgentMessageRecord ToRecord(AgentMessage message) => message.DisclosureHistory is not { } history
        ? new(message)
        : new(message with { DisclosureHistory = null })
        {
            DisclosureDigests = history.Select(AgentDisclosureHistory.Identity).ToArray(),
            DisclosureHistoryFingerprint = AgentInvocationIntegrity.Digest(history)
        };

    /// <summary>The paused turn as the record store holds it; its pause fingerprint is kept verbatim.</summary>
    private static AgentPausedTurnRecord ToRecord(AgentPausedTurn turn) => turn.DisclosureHistory is not { } history
        ? new(turn)
        : new(turn with { DisclosureHistory = null }) { DisclosureDigests = history.Select(AgentDisclosureHistory.Identity).ToArray() };

    /// <summary>
    /// A session's recorded histories rebuilt from their digest references (research R3): every snapshot's text still
    /// digests to its recorded digest and reads back to the same snapshot; every message history matches its recorded
    /// fingerprint; every assistant message and the paused turn still match their delivery and pause fingerprints. Any
    /// mismatch makes the session unavailable (FR-018).
    /// </summary>
    private sealed record HydratedHistory(IReadOnlyList<AgentMessage> Messages, AgentPausedTurn? PausedTurn,
        IReadOnlyDictionary<string, AgentDisclosureSnapshot> Snapshots)
    {
        public static HydratedHistory Rebuild(AgentSessionRecord record)
        {
            var sessionId = record.Session.Id;
            var snapshots = new Dictionary<string, AgentDisclosureSnapshot>(StringComparer.Ordinal);
            foreach (var recorded in record.Snapshots)
                snapshots[recorded.Digest] = AgentDisclosureHistory.Rebuild(recorded)
                    ?? throw new AgentSessionUnavailableException(sessionId, "snapshot_digest_mismatch");

            IReadOnlyList<AgentDisclosureSnapshot>? History(IReadOnlyList<string>? digests)
                => digests is null ? null : Array.AsReadOnly(digests.Select(digest => snapshots.GetValueOrDefault(digest)
                    ?? throw new AgentSessionUnavailableException(sessionId, "snapshot_missing")).ToArray());

            var messages = record.Messages.Select(recorded =>
            {
                var message = recorded.Message;
                // A recorded history fingerprint without the history it fingerprints is missing state, never an empty history.
                if (recorded.DisclosureDigests is null && recorded.DisclosureHistoryFingerprint is not null)
                    throw new AgentSessionUnavailableException(sessionId, "history_missing");
                if (History(recorded.DisclosureDigests) is { } history)
                {
                    if (recorded.DisclosureHistoryFingerprint is not { } fingerprint || AgentInvocationIntegrity.Digest(history) != fingerprint)
                        throw new AgentSessionUnavailableException(sessionId, "history_fingerprint_mismatch");
                    message = message with { DisclosureHistory = history };
                }
                if (message.Role == AgentRole.Assistant && message.DisclosureHistory is not null && !AgentDisclosureHistory.AssistantHistoryMatches(message))
                    throw new AgentSessionUnavailableException(sessionId, "assistant_history_mismatch");
                return message;
            }).ToArray();
            // A pause fingerprint is always recorded with the history it covers.
            if (record.PausedTurn is { DisclosureDigests: null } missing && missing.Turn.DisclosureHistoryFingerprint is not null)
                throw new AgentSessionUnavailableException(sessionId, "history_missing");
            var paused = record.PausedTurn is { } pause && History(pause.DisclosureDigests) is { } pausedHistory
                ? pause.Turn with { DisclosureHistory = pausedHistory }
                : record.PausedTurn?.Turn;
            if (paused?.DisclosureHistory is not null && !AgentDisclosureHistory.PauseHistoryMatches(paused))
                throw new AgentSessionUnavailableException(sessionId, "pause_history_mismatch");
            return new(messages, paused, snapshots);
        }
    }

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
        public SessionRow(AgentSession session, AgentSessionFence fence, IEnumerable<AgentMessage> messages, IEnumerable<AgentContextAttachment> context,
            AgentPausedTurn? pausedTurn, IReadOnlyDictionary<string, AgentDisclosureSnapshot> snapshots) : this(session, fence)
        {
            Messages.AddRange(messages);
            Context.AddRange(context);
            PausedTurn = pausedTurn;
            foreach (var (digest, snapshot) in snapshots) _snapshots.Add(digest, snapshot);
        }

        // The session's distinct recorded snapshots by digest. Every recorded history refers to these instances.
        private readonly Dictionary<string, AgentDisclosureSnapshot> _snapshots = new(StringComparer.Ordinal);

        public AgentSession Session { get; set; }
        public AgentSessionFence Fence { get; }
        public long Revision { get; set; }
        public List<AgentMessage> Messages { get; } = [];
        public List<AgentContextAttachment> Context { get; } = [];
        public AgentPausedTurn? PausedTurn { get; set; }

        /// <summary>The records among <paramref name="snapshots"/> whose digest the session has not recorded yet, once each.</summary>
        public AgentDisclosureSnapshotRecord[] Unrecorded(IEnumerable<AgentDisclosureSnapshotRecord> snapshots)
            => snapshots.Where(x => !_snapshots.ContainsKey(x.Digest)).DistinctBy(x => x.Digest).ToArray();

        /// <summary>After its write: interns the snapshots of an interned history that the session had not recorded yet.</summary>
        public void Recorded(IReadOnlyList<AgentDisclosureSnapshot>? history)
        {
            foreach (var snapshot in history ?? [])
                _snapshots.TryAdd(AgentDisclosureHistory.Identity(snapshot), snapshot);
        }

        /// <summary>The message with each recorded snapshot of its history replaced by the session's interned instance.</summary>
        public AgentMessage Intern(AgentMessage message)
            => message.DisclosureHistory is { } history ? message with { DisclosureHistory = Intern(history) } : message;

        /// <inheritdoc cref="Intern(AgentMessage)"/>
        public AgentPausedTurn Intern(AgentPausedTurn turn)
            => turn.DisclosureHistory is { } history ? turn with { DisclosureHistory = Intern(history) } : turn;

        private IReadOnlyList<AgentDisclosureSnapshot> Intern(IReadOnlyList<AgentDisclosureSnapshot> history)
            => Array.AsReadOnly(history.Select(x => _snapshots.GetValueOrDefault(AgentDisclosureHistory.Identity(x)) ?? x).ToArray());
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

        /// <summary>
        /// A running turn, or an execution whose executor is still running here, needs its lease kept alive while it
        /// works. A dispatched operation that waits on a client or on its owner keeps the session busy, but nothing is in
        /// flight in this process, so it must not keep another process from taking the session over.
        /// </summary>
        public bool NeedsRenewal => _claims.Any(claim => claim.Kind == AgentSessionClaimKind.Foreground
            || claim.Kind == AgentSessionClaimKind.Proposal && claim.Executing);

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

/// <summary>Why a cancelled message stopped (<see cref="AgentMessage.StatusReason"/>).</summary>
internal static class AgentMessageStatusReasons
{
    /// <summary>The process stopped while the prompt's turn was streaming.</summary>
    public const string Interrupted = "interrupted";
    /// <summary>The owner withdrew a prompt that never started.</summary>
    public const string Withdrawn = "withdrawn";
    /// <summary>The owner stopped the running turn.</summary>
    public const string UserStop = "user-stop";
}
