using Elsa.Weaver.Core.Models;

namespace Elsa.Weaver.Core.Contracts;

public interface IAgentSessionService
{
    Task<AgentSession> CreateAsync(AgentSessionCreateRequest request, CancellationToken cancellationToken = default);

    Task<AgentSession?> FindAsync(string sessionId, CancellationToken cancellationToken = default);

    Task<AgentMessage> AddMessageAsync(string sessionId, AgentMessageCreateRequest request, CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<AgentMessage>> ListMessagesAsync(string sessionId, CancellationToken cancellationToken = default);

    Task<AgentMessage?> FindMessageAsync(string sessionId, string messageId, CancellationToken cancellationToken = default);

    Task<AgentMessage?> FindLatestMessageAsync(string sessionId, AgentRole? role = null, CancellationToken cancellationToken = default);

    Task SetMessageStatusAsync(string sessionId, string messageId, AgentMessageStatus status, CancellationToken cancellationToken = default);

    Task AddContextAsync(string sessionId, IReadOnlyCollection<AgentContextAttachment> attachments, CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<AgentContextAttachment>> ListContextAsync(string sessionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Preview: the session's owner, or null when it is not found. Callers authorize on it before anything hydrates the
    /// session, takes its ownership or settles its recorded work, so a foreign caller causes none of those. The default
    /// reads <see cref="FindAsync"/>; the default session owner reads the record store's plain owner columns instead.
    /// </summary>
    async Task<AgentSessionOwner?> FindOwnerAsync(string sessionId, CancellationToken cancellationToken = default)
        => await FindAsync(sessionId, cancellationToken) is { } session ? new(session.ActorId, session.TenantId) : null;

    /// <summary>
    /// Preview: the sessions of the query's actor and tenant, narrowed to its workspace, newest first, at most
    /// <see cref="AgentSessionOwnerQuery.Take"/>, older than its cursor. The owning store filters by owner before it orders
    /// and limits; there is no global read. The default refuses with <see cref="AgentSessionQueryOutcome.Unsupported"/>,
    /// so a replacement that predates this query never falls back to scanning sessions. Implementations throw
    /// <see cref="ArgumentException"/> for a blank actor or tenant, a limit outside 1..<see cref="AgentSessionOwnerQuery.MaxTake"/>
    /// and a cursor they did not issue.
    /// </summary>
    Task<AgentSessionListResult> ListOwnedAsync(AgentSessionOwnerQuery query, CancellationToken cancellationToken = default)
        => Task.FromResult(AgentSessionListResult.Unsupported);

    /// <summary>
    /// Preview: withdraws an accepted prompt that never started: a compare-and-set from Pending to Cancelled with reason
    /// <c>withdrawn</c>, refused with <c>agent.message.not_pending</c> when the prompt is not Pending or a turn is
    /// registered for the session, so a withdraw cannot lose to a concurrent start. A missing prompt is
    /// <c>agent.message.not_found</c>. The default refuses with <c>agent.message.withdraw_unsupported</c>.
    /// </summary>
    Task<AgentResult<AgentMessage>> WithdrawMessageAsync(string sessionId, string messageId, CancellationToken cancellationToken = default)
        => Task.FromResult(AgentResult<AgentMessage>.Failure("weaver.message.withdraw_unsupported",
            "The installed agent session service cannot withdraw prompts.", 409));
}

/// <summary>
/// Preview: reads and conditionally changes a session's acknowledged autonomy policy.
/// </summary>
/// <remarks>
/// The update is supported only when the installed session store owns the policy revision and the lifecycle
/// services that admit work under that policy participate with the same owner. Otherwise every call returns
/// <see cref="AgentSessionPolicyOutcome.Unsupported"/> and no shadow policy state is created. A change refuses an
/// undefined mode, then a closed or expired session, then a stale expected revision, then a busy session; it then
/// clamps the requested mode to the current ceiling, replaces only the autonomy mode and increments the revision
/// only on a real change.
/// </remarks>
public interface IAgentSessionPolicyService
{
    Task<AgentSessionPolicyResult> GetAsync(string sessionId, string actorId, string tenantId, CancellationToken cancellationToken = default);

    Task<AgentSessionPolicyResult> ChangeAutonomyAsync(AgentSessionAutonomyChangeRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// Preview replacement contract: the record of every session aggregate behind the default session owner. At most
/// one implementation may be registered; select a replacement with
/// <c>services.SelectAgentSessionRecordStore(...)</c> (<c>Elsa.Weaver.Extensions.AgentPersistenceSelection</c>), which
/// replaces only the process-local default and refuses a second selection.
/// </summary>
/// <remarks>
/// The default session owner writes through this store before it applies a change in memory, and hydrates a
/// session from it before acting on a session it has not loaded. One aggregate covers the session (policy, revision
/// and ownership), its messages and context, the paused turn, its proposals with their outcomes, and the
/// duplicate-detection keys. Each write is atomic and is guarded on the writer's owner token and fence; the same
/// write extends the owner's lease, or re-acquires a lapsed lease that no other owner took over. Implementations
/// return copies: a caller never shares mutable state with the store.
/// </remarks>
public interface IAgentSessionRecordStore
{
    /// <summary>
    /// Whether every write is guarded on the current owner and fence. A store that cannot fence cannot back durable
    /// recovery, because a stale process could then continue a turn: selecting one fails shell activation
    /// (the Agent durable-recovery validator).
    /// </summary>
    bool SupportsFencing { get; }

    /// <summary>Records a new aggregate owned by <paramref name="ownerToken"/>; refuses an identity that is already recorded.</summary>
    Task<AgentSessionRecordResult> CreateAsync(AgentSessionRecord record, string ownerToken, CancellationToken cancellationToken = default);

    /// <summary>Reads one aggregate for hydration, or null when it is not recorded.</summary>
    Task<AgentSessionRecord?> LoadAsync(string sessionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Takes ownership when the aggregate has no owner, its lease has lapsed, or <paramref name="ownerToken"/> already
    /// owns it. A takeover increments the fence. A live lease held by another owner returns
    /// <see cref="AgentSessionRecordOutcome.OwnedElsewhere"/>.
    /// </summary>
    Task<AgentSessionRecordResult> AcquireOwnershipAsync(string sessionId, string ownerToken, CancellationToken cancellationToken = default);

    /// <summary>Releases the lease when <paramref name="fence"/> is still current; otherwise a no-op.</summary>
    Task ReleaseOwnershipAsync(string sessionId, AgentSessionFence fence, CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes one transition atomically when its fence is current, inserting its keys all or nothing, and extends
    /// the lease. A stale fence returns <see cref="AgentSessionRecordOutcome.OwnershipLost"/>; a key recorded with a
    /// different value returns <see cref="AgentSessionRecordOutcome.KeyConflict"/>. Neither changes anything.
    /// </summary>
    Task<AgentSessionRecordResult> WriteAsync(AgentSessionRecordWrite write, CancellationToken cancellationToken = default);

    /// <summary>The session that owns a proposal, or null when the proposal is not recorded.</summary>
    Task<string?> FindProposalSessionAsync(string proposalId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The recorded sessions of the query's actor and tenant, filtered before ordering and the limit. Implementations
    /// throw <see cref="ArgumentException"/> for a blank actor or tenant and a limit outside 1..<see cref="AgentSessionOwnerQuery.MaxTake"/>.
    /// </summary>
    Task<AgentSessionSummaryPage> ListOwnedAsync(AgentSessionOwnerQuery query, CancellationToken cancellationToken = default);

    /// <summary>
    /// The plain owner columns of one recorded session, or null when it is not recorded. This read takes no ownership,
    /// extends no lease and writes nothing, so authorization can run on it before any hydration. The default reads
    /// <see cref="LoadAsync"/>; a store that protects its payloads overrides it to read the owner columns only.
    /// </summary>
    async Task<AgentSessionOwner?> FindOwnerAsync(string sessionId, CancellationToken cancellationToken = default)
        => await LoadAsync(sessionId, cancellationToken) is { } record ? new(record.Session.ActorId, record.Session.TenantId) : null;
}

public interface IAgentPolicyEvaluator
{
    ValueTask<AgentPolicyDecision> EvaluateAvailabilityAsync(AgentPolicy policy, CancellationToken cancellationToken = default);

    ValueTask<AgentPolicyDecision> EvaluateContextAsync(AgentPolicy policy, IReadOnlyCollection<AgentContextAttachment> attachments, CancellationToken cancellationToken = default);

    ValueTask<AgentPolicyDecision> EvaluateCapabilityAsync(AgentPolicy policy, string capabilityId, CancellationToken cancellationToken = default);
}

public interface IAgentContextSanitizer
{
    ValueTask<IReadOnlyCollection<AgentContextAttachment>> SanitizeAsync(IReadOnlyCollection<AgentContextAttachment> attachments, CancellationToken cancellationToken = default);
}

public interface IAgentContextProvider
{
    AgentContextProviderDescriptor Descriptor { get; }

    string ScopeKind => Descriptor.ScopeKind;

    ValueTask<AgentContextContribution> CollectAsync(AgentContextRequest request, CancellationToken cancellationToken = default);

    /// <summary>Re-admits this source's retained evidence under current native authority; no effects may be executed.</summary>
    /// <remarks>The default refuses disclosure. Success authorizes supplied historical evidence/results unchanged;
    /// the returned attachments may omit obsolete current context. Implementations must not downgrade authority failures to optional limitations.</remarks>
    ValueTask<AgentContextContribution> AdmitDisclosureAsync(AgentContextDisclosureRequest request, CancellationToken cancellationToken = default)
        => ValueTask.FromResult(new AgentContextContribution([], [new(Descriptor.Id, "weaver.context.disclosure_unavailable",
            "The installed context source cannot establish current disclosure authority. Start a fresh request after updating the module.", true)]));
}

public interface IAgentContextCollector
{
    Task<AgentResult<AgentContextContribution>> CollectAsync(AgentPolicy policy, AgentContextRequest request, CancellationToken cancellationToken = default);
}

public interface IAgentCapabilityProvider
{
    ValueTask<IReadOnlyCollection<AgentCapability>> GetCapabilitiesAsync(CancellationToken cancellationToken = default);
}

public interface IAgentCapabilityCatalog
{
    Task<IReadOnlyCollection<AgentCapability>> ListAsync(CancellationToken cancellationToken = default);
}

public interface IAgentActionProposalExecutor
{
    Task<AgentResult<AgentProposalExecutionResult>> ExecuteAsync(AgentActionProposal proposal, CancellationToken cancellationToken = default);

    Task<AgentResult<AgentProposalExecutionResult>> CompleteClientAsync(AgentActionProposal proposal,
        AgentClientAcknowledgement acknowledgement, CancellationToken cancellationToken = default);
}

public interface IAgentProposalService
{
    Task<AgentActionProposal> AddAsync(AgentActionProposal proposal, CancellationToken cancellationToken = default);

    Task<AgentActionProposal?> FindAsync(string proposalId, CancellationToken cancellationToken = default);

    Task<AgentResult<AgentActionProposal>> ApproveAsync(string proposalId, string actorId, string? expectedRevision = null, string? comment = null, CancellationToken cancellationToken = default);

    Task<AgentResult<AgentActionProposal>> DenyAsync(string proposalId, string actorId, string? reason, CancellationToken cancellationToken = default);

    Task<AgentResult<AgentProposalExecutionResult>> ExecuteAsync(string proposalId, string actorId, string? expectedRevision = null, CancellationToken cancellationToken = default);

    Task<AgentResult<AgentProposalExecutionResult>> AcknowledgeAsync(string sessionId, string invocationId, string actorId,
        AgentClientAcknowledgement acknowledgement, CancellationToken cancellationToken = default);

    Task<AgentToolResult?> FindResultAsync(string proposalId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Preview: the session that owns the proposal, or null when it is not found, resolved without hydrating that
    /// session, so the caller can authorize on the session's owner first. The default reads <see cref="FindAsync"/>.
    /// </summary>
    async Task<string?> FindSessionIdAsync(string proposalId, CancellationToken cancellationToken = default)
        => (await FindAsync(proposalId, cancellationToken))?.SessionId;

    /// <summary>
    /// Preview: the session's proposals with their current statuses, oldest first. The default refuses with
    /// <c>agent.proposal.query_unsupported</c>.
    /// </summary>
    Task<AgentResult<IReadOnlyList<AgentActionProposal>>> ListAsync(string sessionId, CancellationToken cancellationToken = default)
        => Task.FromResult(AgentResult<IReadOnlyList<AgentActionProposal>>.Failure("weaver.proposal.query_unsupported",
            "The installed agent proposal service cannot list a session's proposals.", 409));

    /// <summary>
    /// Preview: records the owner's statement about a client-scope Uncertain operation on its proposal and releases only
    /// that operation's claim. It is not a receipt; a later truthful receipt still reconciles the operation. Repeating
    /// the same statement returns the proposal unchanged; a different statement is <c>agent.invocation.resolution_conflict</c>.
    /// Any other operation is refused with <c>agent.invocation.not_uncertain</c>. The default refuses with
    /// <c>agent.invocation.resolution_unsupported</c>.
    /// </summary>
    Task<AgentResult<AgentActionProposal>> ResolveClientInvocationAsync(string sessionId, string invocationId, string actorId,
        AgentOwnerResolutionClaim claim, CancellationToken cancellationToken = default)
        => Task.FromResult(AgentResult<AgentActionProposal>.Failure("weaver.invocation.resolution_unsupported",
            "The installed agent proposal service cannot record an owner's resolution.", 409));
}

public interface IAgentStreamingService
{
    /// <summary>Streams a turn only after complete disclosure provenance has been retained and authorized.</summary>
    /// <exception cref="AgentContributionException">An installed contribution or disclosure infrastructure boundary failed.
    /// The exception localizes the session/operation and retains the original cause; transport callers choose logging and redacted presentation.</exception>
    IAsyncEnumerable<AgentStreamEvent> StreamAsync(string sessionId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Preview: what a host restart does to this composition's Weaver sessions, as bootstrap reports it. Overridable, at most
/// one; normally not replaced, because only the default can see whether the default session owner participates with every
/// default lifecycle service over a fencing record store.
/// </summary>
public interface IAgentRecoverySupport
{
    /// <summary>One of the <see cref="AgentRecoveryModes"/> values.</summary>
    string Describe();
}

/// <summary>Preview: the values of <see cref="IAgentRecoverySupport.Describe"/> and of the bootstrap <c>recovery</c> field.</summary>
public static class AgentRecoveryModes
{
    /// <summary>A store other than the in-memory default backs a supported composition: sessions are recovered after a restart.</summary>
    public const string Durable = "durable";
    /// <summary>The in-memory default: sessions survive reloads while the process lives, and a restart ends them.</summary>
    public const string ProcessLifetime = "processLifetime";
    /// <summary>The composition cannot recover its sessions, or its support could not be evaluated.</summary>
    public const string Unsupported = "unsupported";
}

public interface IAgentFeedbackService
{
    Task<AgentFeedback> AddAsync(AgentFeedback feedback, CancellationToken cancellationToken = default);
}

/// <summary>
/// Replacement contract, at most one: the append facet of the audit store. The sink, <see cref="IAgentAuditReader"/>
/// and <see cref="IAgentAuditOwnership"/> are one store's facets and are replaced together, with
/// <c>services.SelectAgentAuditStore(...)</c> (<c>Elsa.Weaver.Extensions.AgentPersistenceSelection</c>); a second
/// registration of any facet fails the Agent registration validator.
/// </summary>
public interface IAgentAuditSink
{
    Task EmitAsync(AgentAuditEvent auditEvent, CancellationToken cancellationToken = default);
}

/// <summary>
/// Replacement contract, at most one: the query facet of the audit store. It is replaced together with
/// <see cref="IAgentAuditSink"/> and <see cref="IAgentAuditOwnership"/>, with <c>services.SelectAgentAuditStore(...)</c>
/// (<c>Elsa.Weaver.Extensions.AgentPersistenceSelection</c>); a second registration of any facet fails the Agent
/// registration validator.
/// </summary>
public interface IAgentAuditReader
{
    /// <summary>A trusted, unscoped read for in-process callers. It is not an authorization boundary.</summary>
    Task<IReadOnlyCollection<AgentAuditEvent>> ListAsync(string? sessionId = null, int? take = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Preview: the records of sessions associated with the query's actor and tenant,
    /// optionally narrowed to one session, newest first (ties: latest append first), at most
    /// <see cref="AgentAuditOwnerQuery.Take"/>. Ownership filtering and ordering happen in the reader before the
    /// limit. A record whose session has no ownership association, or no session, is never returned.
    /// </summary>
    /// <remarks>
    /// The default refuses with <see cref="AgentAuditQueryOutcome.Unsupported"/>, so a replacement that predates
    /// this query never silently falls back to a global read. Implementations throw
    /// <see cref="ArgumentException"/> for a blank actor or tenant and a limit outside 1..<see cref="AgentAuditOwnerQuery.MaxTake"/>.
    /// </remarks>
    Task<AgentAuditQueryResult> ListOwnedAsync(AgentAuditOwnerQuery query, CancellationToken cancellationToken = default)
        => Task.FromResult(AgentAuditQueryResult.Unsupported);
}

/// <summary>
/// Preview: the session owner's authoritative actor/tenant association for a session's
/// audit records. The session owner associates a session before it emits any record for it.
/// </summary>
public interface IAgentAuditOwnership
{
    /// <summary>
    /// Associates a session with its owner. The first association wins: repeating it returns true, and a
    /// conflicting actor or tenant returns false without changing it.
    /// </summary>
    Task<bool> AssociateAsync(string sessionId, string actorId, string tenantId, CancellationToken cancellationToken = default);
}

/// <summary>
/// The accumulated state handed to a provider for one step of a turn. The orchestrator owns the loop,
/// history, and tool execution; the provider maps this context to its SDK and yields the next step's
/// message deltas and tool-call requests.
/// </summary>
public sealed record AgentTurnContext(
    string SessionId,
    string ProviderSessionId,
    IReadOnlyList<AgentTurnMessage> History,
    IReadOnlyList<AgentToolResult> PendingToolResults,
    IReadOnlyCollection<AgentToolDescriptor> AvailableTools,
    IReadOnlyCollection<AgentContextAttachment> Context,
    int StepIndex,
    int MaxSteps)
{
    /// <summary>The most recent user message in the turn history, if any.</summary>
    public AgentTurnMessage? LatestUserMessage => History.LastOrDefault(x => x.Role == AgentRole.User);

    /// <summary>Convenience factory for a single-step, single-user-message turn (tests and simple callers).</summary>
    public static AgentTurnContext ForMessage(string sessionId, string content, IReadOnlyCollection<AgentContextAttachment> context)
        => new(sessionId, sessionId, [new AgentTurnMessage(AgentRole.User, content)], [], [], context, 0, 1);
}

public interface IAgentProvider
{
    string ProviderId { get; }

    Task<AgentProviderSession> CreateSessionAsync(AgentSession session, CancellationToken cancellationToken = default);

    /// <summary>
    /// Produces the next step of a turn. Yields <see cref="AgentStreamEventKind.MessageDelta"/> events for
    /// assistant text and <see cref="AgentStreamEventKind.ToolCallRequested"/> events for tools the provider
    /// wants run. The orchestrator executes any requested tools and calls this again with their results.
    /// </summary>
    IAsyncEnumerable<AgentStreamEvent> ContinueTurnAsync(AgentTurnContext context, CancellationToken cancellationToken = default);

    Task<AgentToolApprovalResult> ApproveToolAsync(AgentProviderToolApprovalRequest request, CancellationToken cancellationToken = default);

    Task<AgentProviderDiagnostics> GetDiagnosticsAsync(CancellationToken cancellationToken = default);
}

public interface IAgentProviderRegistry
{
    /// <summary>The single active agent provider, or null when no harness is enabled.</summary>
    IAgentProvider? Active { get; }
}
