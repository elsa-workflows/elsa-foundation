using System.Collections.Concurrent;
using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;
using Elsa.Primitives.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using static Elsa.Agent.Services.AgentIds;

namespace Elsa.Agent.Services;

/// <summary>
/// Process-lifetime session rows. Each row's monitor orders message insertion, the session's policy and
/// policy revision, and the claims of work admitted under that policy.
/// </summary>
/// <remarks>
/// When an audit ownership association is installed, each new session is associated with its actor and tenant
/// before the row exists or any record for it is emitted. The association has no dependency on this service.
/// </remarks>
internal sealed class InMemoryAgentSessionService(IAgentAuditSink auditSink, TimeProvider? clock = null, IAgentAuditOwnership? auditOwnership = null)
    : IAgentSessionService, IAgentSessionPolicyParticipant
{
    private const long InitialPolicyRevision = 1;
    private const string PauseKey = "pause";
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly ConcurrentDictionary<string, SessionRow> _rows = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, List<AgentContextAttachment>> _contexts = new(StringComparer.Ordinal);

    InMemoryAgentSessionService? IAgentSessionPolicyParticipant.PolicyOwner => this;

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
        { PolicyRevision = InitialPolicyRevision };

        // A blank owner cannot be associated; such a session stays ineligible for owner-scoped audit queries.
        if (auditOwnership is not null && !string.IsNullOrWhiteSpace(session.ActorId) && !string.IsNullOrWhiteSpace(session.TenantId)
            && !await auditOwnership.AssociateAsync(session.Id, session.ActorId, session.TenantId, cancellationToken))
            throw new AgentContributionException("The new session identity is already associated with another audit owner.");
        _rows[session.Id] = new SessionRow(session);
        _contexts[session.Id] = [];

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

    public Task<AgentSession?> FindAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        if (!_rows.TryGetValue(sessionId, out var row))
            return Task.FromResult<AgentSession?>(null);
        lock (row)
            return Task.FromResult<AgentSession?>(row.Session);
    }

    public async Task<AgentMessage> AddMessageAsync(string sessionId, AgentMessageCreateRequest request, CancellationToken cancellationToken = default)
    {
        if (!_rows.TryGetValue(sessionId, out var row))
            throw new InvalidOperationException($"Agent session '{sessionId}' was not found.");
        sessionId = row.Session.Id;

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
        var messages = row.Messages;

        lock (row)
        {
            if (request.Role == AgentRole.Tool && !string.IsNullOrEmpty(request.RequestId))
            {
                var existing = messages.FirstOrDefault(x => x.Role == AgentRole.Tool && x.RequestId == request.RequestId);
                if (existing is not null)
                {
                    if (!string.IsNullOrEmpty(request.DeliveryFingerprint) && existing.DeliveryFingerprint == request.DeliveryFingerprint)
                        return existing;
                    throw new AgentContributionException("An observed tool outcome cannot change its original invocation identity or evidence.");
                }
            }
            if (request.Role == AgentRole.User && !string.IsNullOrWhiteSpace(request.RequestId))
            {
                var existing = messages.FirstOrDefault(x => x.Role == AgentRole.User && x.RequestId == request.RequestId);
                if (existing is not null)
                {
                    if (!string.IsNullOrEmpty(request.DeliveryFingerprint) && existing.DeliveryFingerprint == request.DeliveryFingerprint)
                        return existing;
                    throw new AgentContributionException("A prompt delivery identity was reused with different input.");
                }
            }
            // Replaceable evaluators/collectors authorized this delivery under the whole policy they read.
            if (request.ExpectedPolicyRevision is { } expected && expected != row.Revision)
                throw new AgentSessionPolicyChangedException(sessionId, expected, row.Revision);
            if (request.Role == AgentRole.User && messages.Any(x => x.Role == AgentRole.User && x.Status is AgentMessageStatus.Pending or AgentMessageStatus.Streaming))
                throw new AgentContributionException("Another prompt remains pending in this session.");
            // Attribution is the policy current at this atomic insertion; a later change never relabels it.
            message = message with { AcceptedPolicy = row.State };
            if (request.Role == AgentRole.User && request.Status == AgentMessageStatus.Pending)
                row.Bind(row.NewClaim(AgentSessionClaimKind.Prompt, null), PromptKey(message.Id));
            messages.Add(message);
        }

        await auditSink.EmitAsync(new(
            NewId(),
            AgentAuditEventKind.MessageAccepted,
            sessionId,
            null,
            "Agent message accepted.",
            message.CreatedAt,
            new Dictionary<string, string> { ["messageId"] = message.Id, ["role"] = message.Role.ToString() }), cancellationToken);

        return message;
    }

    public Task<AgentMessage?> FindLatestMessageAsync(string sessionId, AgentRole? role = null, CancellationToken cancellationToken = default)
    {
        if (!_rows.TryGetValue(sessionId, out var row))
            return Task.FromResult<AgentMessage?>(null);

        lock (row)
        {
            return Task.FromResult(row.Messages.LastOrDefault(x => role is null || x.Role == role));
        }
    }

    public Task SetMessageStatusAsync(string sessionId, string messageId, AgentMessageStatus status, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_rows.TryGetValue(sessionId, out var row))
            lock (row)
            {
                var messages = row.Messages;
                var index = messages.FindIndex(x => x.Id == messageId);
                if (index >= 0)
                {
                    var terminal = status is AgentMessageStatus.Completed or AgentMessageStatus.Failed or AgentMessageStatus.Cancelled;
                    messages[index] = messages[index] with { Status = status, CompletedAt = terminal ? DateTimeOffset.UtcNow : null };
                    // Releases only this exact prompt's claim, including a never-started Pending prompt.
                    if (terminal && row.Find(PromptKey(messageId)) is { } prompt)
                        row.Release(prompt);
                }
            }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyCollection<AgentMessage>> ListMessagesAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        if (!_rows.TryGetValue(sessionId, out var row))
            return Task.FromResult<IReadOnlyCollection<AgentMessage>>([]);

        lock (row)
            return Task.FromResult<IReadOnlyCollection<AgentMessage>>(row.Messages.ToList());
    }

    public Task<AgentMessage?> FindMessageAsync(string sessionId, string messageId, CancellationToken cancellationToken = default)
    {
        if (!_rows.TryGetValue(sessionId, out var row))
            return Task.FromResult<AgentMessage?>(null);

        lock (row)
            return Task.FromResult(row.Messages.FirstOrDefault(x => string.Equals(x.Id, messageId, StringComparison.Ordinal)));
    }

    public Task AddContextAsync(string sessionId, IReadOnlyCollection<AgentContextAttachment> attachments, CancellationToken cancellationToken = default)
    {
        if (attachments.Count == 0)
            return Task.CompletedTask;

        if (!_rows.ContainsKey(sessionId))
            throw new InvalidOperationException($"Agent session '{sessionId}' was not found.");

        var contexts = _contexts.GetOrAdd(sessionId, _ => []);
        lock (contexts)
        {
            var existingIds = contexts.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
            foreach (var attachment in attachments.Where(x => existingIds.Add(x.Id)))
                contexts.Add(attachment);
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyCollection<AgentContextAttachment>> ListContextAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        if (!_contexts.TryGetValue(sessionId, out var contexts))
            return Task.FromResult<IReadOnlyCollection<AgentContextAttachment>>([]);

        lock (contexts)
            return Task.FromResult<IReadOnlyCollection<AgentContextAttachment>>(contexts.ToList());
    }

    /// <summary>Acquires an unkeyed claim; null when this owner has no row for the session.</summary>
    internal AgentSessionClaim? Acquire(string sessionId, AgentSessionClaimKind kind)
    {
        if (kind is AgentSessionClaimKind.Prompt or AgentSessionClaimKind.Proposal or AgentSessionClaimKind.Pause)
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "Keyed claims are acquired through their owning transition.");
        if (!_rows.TryGetValue(sessionId, out var row)) return null;
        lock (row) return row.NewClaim(kind, null);
    }

    /// <summary>Acquires a provisional proposal claim before store insertion; it records the prepared deadline.</summary>
    internal AgentSessionClaim? AcquireProposal(string sessionId, DateTimeOffset? deadline)
    {
        if (!_rows.TryGetValue(sessionId, out var row)) return null;
        lock (row) return row.NewClaim(AgentSessionClaimKind.Proposal, deadline);
    }

    /// <summary>Binds a provisional proposal claim to its inserted proposal identity.</summary>
    internal bool BindProposal(AgentSessionClaim claim, string proposalId)
    {
        if (claim.Kind != AgentSessionClaimKind.Proposal || !_rows.TryGetValue(claim.SessionId, out var row)) return false;
        lock (row) return row.Bind(claim, ProposalKey(proposalId));
    }

    /// <summary>Marks the proposal's claim dispatched; false refuses a pre-dispatch claim past its deadline.</summary>
    internal bool MarkProposalDispatched(string sessionId, string proposalId)
    {
        if (!_rows.TryGetValue(sessionId, out var row)) return true;
        lock (row)
        {
            if (row.Find(ProposalKey(proposalId)) is not { } claim) return true;
            if (claim.Dispatched) return true;
            // An expired pre-dispatch claim no longer counts as busy, so it cannot authorize dispatch.
            if (claim.Deadline is { } deadline && deadline <= _clock.GetUtcNow()) return false;
            claim.Dispatched = true;
            return true;
        }
    }

    /// <summary>Releases the claim bound to this proposal identity, if any.</summary>
    internal void ReleaseProposal(string sessionId, string proposalId)
    {
        if (!_rows.TryGetValue(sessionId, out var row)) return;
        lock (row)
            if (row.Find(ProposalKey(proposalId)) is { } claim)
                row.Release(claim);
    }

    /// <summary>Returns the retained pause claim, acquiring it when absent.</summary>
    internal AgentSessionClaim? EnsurePause(string sessionId)
    {
        if (!_rows.TryGetValue(sessionId, out var row)) return null;
        lock (row)
        {
            if (row.Find(PauseKey) is { } existing) return existing;
            var claim = row.NewClaim(AgentSessionClaimKind.Pause, null);
            row.Bind(claim, PauseKey);
            return claim;
        }
    }

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
    internal AgentSessionPolicyResult GetPolicy(string sessionId, string actorId, string tenantId)
    {
        if (!_rows.TryGetValue(sessionId, out var row))
            return new(AgentSessionPolicyOutcome.NotFound, null);
        lock (row)
            return Owned(row.Session, actorId, tenantId)
                ? new(AgentSessionPolicyOutcome.Succeeded, row.State)
                : new(AgentSessionPolicyOutcome.NotFound, null);
    }

    /// <summary>Applies one conditional autonomy change under the session row monitor.</summary>
    internal AgentSessionPolicyResult ChangeAutonomy(AgentSessionAutonomyChangeRequest request)
    {
        // 1. An undefined mode is refused before any session state is consulted.
        if (!Enum.IsDefined(request.RequestedMode))
            return new(AgentSessionPolicyOutcome.InvalidMode, null);
        if (!_rows.TryGetValue(request.SessionId, out var row))
            return new(AgentSessionPolicyOutcome.NotFound, null);
        lock (row)
        {
            var session = row.Session;
            if (!Owned(session, request.ActorId, request.TenantId))
                return new(AgentSessionPolicyOutcome.NotFound, null);
            var now = _clock.GetUtcNow();
            // 2. Closed or expired.
            if (session.Status != AgentSessionStatus.Active || session.ExpiresAt is { } expires && expires <= now)
                return new(AgentSessionPolicyOutcome.SessionClosed, null);
            // 3. Stale before any no-op comparison, so an old request cannot be acknowledged as current.
            if (request.ExpectedRevision != row.Revision)
                return new(AgentSessionPolicyOutcome.StaleRevision, row.State);
            // 4. Admitted work owns the current policy.
            if (row.IsBusy(now))
                return new(AgentSessionPolicyOutcome.Busy, row.State);
            // 5. Clamp to the current ceiling; 6. replace only the autonomy mode.
            var effective = session.Policy.Clamp(request.RequestedMode);
            if (effective == session.Policy.AutonomyMode)
                return new(AgentSessionPolicyOutcome.Succeeded, row.State);
            // 7. A real change increments the revision.
            row.Revision++;
            row.Session = session with
            {
                Policy = session.Policy with { AutonomyMode = effective },
                PolicyRevision = row.Revision,
                UpdatedAt = now
            };
            return new(AgentSessionPolicyOutcome.Succeeded, row.State);
        }
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

    /// <summary>One session's state. Every member is used only under <c>lock (row)</c>.</summary>
    private sealed class SessionRow(AgentSession session)
    {
        private readonly HashSet<AgentSessionClaim> _claims = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<string, AgentSessionClaim> _keyed = new(StringComparer.Ordinal);

        public AgentSession Session { get; set; } = session;
        public long Revision { get; set; } = session.PolicyRevision ?? InitialPolicyRevision;
        public List<AgentMessage> Messages { get; } = [];
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
