using System.Text.Json.Serialization;
using Elsa.Agent.Core.Contracts;

namespace Elsa.Agent.Core.Models;

public enum AgentRole
{
    User,
    Assistant,
    System,
    Tool,
    Progress,
    Error
}

public enum AgentSessionStatus
{
    Active,
    Completed,
    Cancelled,
    Failed,
    Expired
}

public enum AgentMessageStatus
{
    Pending,
    Streaming,
    Completed,
    Failed,
    Cancelled
}

public enum AgentContextSensitivity
{
    Public = 0,
    Internal = 1,
    Sensitive = 2,
    SecretRedacted = 3,
    Secret = 4
}

public enum AgentCapabilityKind
{
    Answer,
    Context,
    PromptStarter,
    Proposal,
    Action
}

public enum AgentRisk
{
    ReadOnly,
    ReviewRequired,
    Destructive,
    Admin
}

/// <summary>
/// How much the agent may do without per-mutation human approval. Ordered from most to least restrictive:
/// the integer value doubles as the autonomy "level" used when clamping a requested mode to a deployment ceiling.
/// </summary>
public enum AgentAutonomyMode
{
    /// <summary>Every mutating tool becomes a reviewable proposal; read-only tools still run inline.</summary>
    Manual = 0,

    /// <summary>Read-only tools run inline; mutating tools become reviewable (and undoable) proposals.</summary>
    AutoReadOnly = 1,

    /// <summary>Mutating tools run inline without a proposal step.</summary>
    FullAuto = 2
}

public enum AgentActionProposalStatus
{
    Draft,
    AwaitingApproval,
    Approved,
    Denied,
    Edited,
    Expired,
    Executed,
    Failed,
    Cancelled,
    Executing,
    AwaitingClient,
    Uncertain
}

public enum AgentAuditEventKind
{
    SessionCreated,
    MessageAccepted,
    ContextCollected,
    ContextDenied,
    ProposalCreated,
    ProposalApproved,
    ProposalDenied,
    ProposalExecuted,
    ProposalFailed,
    FeedbackReceived,
    ProviderDiagnostic,
    ModuleInteraction,
    ToolInvoked,
    /// <summary>Preview: the owner stated the outcome of an uncertain client operation. The statement is not a receipt.</summary>
    ProposalResolved
}

public enum AgentStreamEventKind
{
    Started,
    MessageDelta,
    ToolApprovalRequested,
    ProposalCreated,
    Completed,
    Error,
    ClarificationRequested,
    ContributedResult,
    StepStarted,
    StepCompleted,
    ToolCallRequested,
    ToolCallStarted,
    ToolCallCompleted,
    PlanUpdated,
    Progress,
    TurnCancelled,
    ContextAdmitted
}

public enum AgentResultKind
{
    Message,
    Clarification,
    ModuleResult,
    Proposal,
    Error
}

public enum AgentProviderKind
{
    ProviderSdkBinding,
    AgentHarnessProvider
}

public enum AgentProviderOperation
{
    Chat,
    Streaming,
    ToolApproval,
    RunStatus,
    Artifacts,
    Skills,
    Memory,
    FileUpload
}

public enum AgentProviderRiskProfile
{
    ReadOnly,
    ReviewRequired,
    SandboxedExecution,
    PrivilegedExecution
}

public sealed record AgentError(string Code, string Message, int StatusCode = 400)
{
    /// <summary>
    /// Preview: what an <c>agent.context.disclosure_budget_exceeded</c> refusal reached. The session limits, recorded on the
    /// session and offered a continuation: <c>snapshots</c> (64 distinct snapshots) and <c>bytes</c> (8 MiB of recorded
    /// disclosure, with less than 64 KiB left, so the session is treated as full). Refusals of one disclosure, never recorded:
    /// <c>snapshot-too-large</c> (larger than any session can record) and <c>snapshot-does-not-fit</c> (larger than the room
    /// left in a session that is not full). Null for every other error.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Limit { get; init; }
}

public sealed record AgentResult<T>(T? Value, AgentError? Error)
{
    public bool Succeeded => Error is null;

    public static AgentResult<T> Success(T value) => new(value, null);

    public static AgentResult<T> Failure(string code, string message, int statusCode = 400) => new(default, new(code, message, statusCode));
}

public sealed record AgentViolation(string Code, string Message, string? Target = null)
{
    /// <summary>
    /// Preview: the owning module could not reach a decision, as opposed to refusing. Still a refusal (fail closed), but the
    /// tool invoker reports it as unavailable (503) with this violation's <see cref="Code"/> and a fixed, cause-free message
    /// of its own, instead of the generic forbidden outcome. The violation's <see cref="Message"/> is never forwarded.
    /// </summary>
    /// <remarks>
    /// Honoured only on a violation from the installed tool's own <c>IAgentToolAuthorization</c> decision; a host
    /// <c>IAgentToolAuthorizer</c> violation that sets it stays an ordinary denial. The code must be the owner's own dotted
    /// identifier (ASCII letters, digits, <c>.</c>, <c>_</c> or <c>-</c>, at most 128 characters), outside the engine's
    /// reserved <c>agent.</c> namespace; any other code stays an ordinary denial. Omitted from the wire when unset.
    /// </remarks>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool OwnerUnavailable { get; init; }
}

public sealed record AgentPolicy(
    string Id,
    bool Enabled,
    bool ContextVisibility,
    AgentContextSensitivity MaxContextSensitivity,
    IReadOnlyCollection<string> AllowedContextKinds,
    AgentAutonomyMode AutonomyMode,
    AgentAutonomyMode MaxAutonomyMode,
    IReadOnlyCollection<string> DeniedCapabilityIds,
    string RetentionLabel)
{
    public static AgentPolicy Default { get; } = new(
        "default",
        Enabled: true,
        ContextVisibility: true,
        AgentContextSensitivity.Sensitive,
        // Wildcards apply only after an installed module admits the context kind/schema.
        ["*"],
        // A new session opens on the safe "auto-apply low-risk" default, but the ceiling permits clients
        // to opt up to FullAuto ("Autopilot") — mutating tools run inline, risky edits still defer to review.
        AutonomyMode: AgentAutonomyMode.AutoReadOnly,
        MaxAutonomyMode: AgentAutonomyMode.FullAuto,
        DeniedCapabilityIds: [],
        RetentionLabel: "Configured by administrator");

    /// <summary>Whether a mutating tool must be routed through a proposal under the effective autonomy mode.</summary>
    public bool RequiresApprovalForMutations => AutonomyMode != AgentAutonomyMode.FullAuto;

    /// <summary>The autonomy modes a client may request without exceeding <see cref="MaxAutonomyMode"/>, most restrictive first.</summary>
    public IReadOnlyCollection<AgentAutonomyMode> AllowedAutonomyModes =>
        Enum.GetValues<AgentAutonomyMode>().Where(mode => mode <= MaxAutonomyMode).ToList();

    /// <summary>Clamps a requested autonomy mode to this policy's <see cref="MaxAutonomyMode"/> ceiling.</summary>
    public AgentAutonomyMode Clamp(AgentAutonomyMode requested) => requested <= MaxAutonomyMode ? requested : MaxAutonomyMode;
}

public sealed record AgentCapability(
    string Id,
    string? ModuleId,
    string DisplayName,
    string Description,
    AgentCapabilityKind Kind,
    AgentRisk Risk,
    IReadOnlyCollection<string> Surfaces,
    IReadOnlyCollection<string> RequiredPermissions,
    IReadOnlyCollection<string> ContextKinds)
{
    public IReadOnlyCollection<string> Modes { get; init; } = [];
}

public sealed record AgentSession(
    string Id,
    string? Title,
    string TenantId,
    string ActorId,
    string ConversationId,
    string ProviderId,
    string Mode,
    AgentPolicy Policy,
    AgentSessionStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ExpiresAt,
    IReadOnlyDictionary<string, string> Metadata)
{
    /// <summary>
    /// Preview: the owning session store's revision of <see cref="Policy"/>, or null when the installed
    /// store does not own conditional policy updates.
    /// </summary>
    public long? PolicyRevision { get; init; }
    /// <summary>Preview: the workspace stamped when the session was created. It only narrows owner-scoped lists.</summary>
    public string? Workspace { get; init; }
    /// <summary>Preview: the session this one was created to continue past a limit; nothing else is carried over.</summary>
    public string? ContinuedFrom { get; init; }
    /// <summary>
    /// Preview: the documented session limit this session reached (<c>snapshots</c> or <c>bytes</c>, as
    /// <see cref="AgentError.Limit"/> names it), or null. Recorded when a turn is refused at the limit, so a reopened
    /// session shows the limit and its continuation before another prompt is refused.
    /// </summary>
    public string? LimitReached { get; init; }
}

public sealed record AgentMessage(
    string Id,
    string SessionId,
    AgentRole Role,
    string Content,
    AgentMessageStatus Status,
    string? CapabilityId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt,
    AgentError? Error,
    IReadOnlyCollection<string> ContextAttachmentIds,
    IReadOnlyCollection<string> ProposalIds,
    IReadOnlyCollection<AgentContextAttachment> Context)
{
    public string? RequestId { get; init; }
    public AgentPromptSource? Source { get; init; }
    public AgentClientBinding? Client { get; init; }
    public string? PromptMode { get; init; }
    public string? DeliveryFingerprint { get; init; }
    /// <summary>Observed tool outcomes whose current disclosure must be established before transcript reuse.</summary>
    public IReadOnlyList<AgentToolResult> ToolResults { get; init; } = [];
    /// <summary>The engine tracks this message and the resource/result disclosures of its turn.</summary>
    public bool HasDisclosureProvenance { get; init; }
    /// <summary>Complete immutable disclosure snapshots behind generated transcript content; null denotes an older/incomplete journal.</summary>
    public IReadOnlyList<AgentDisclosureSnapshot>? DisclosureHistory { get; init; }
    /// <summary>
    /// Preview: the frozen session policy and revision current when the owning store accepted this message.
    /// A later policy change never relabels it; null when the installed store does not own policy revisions.
    /// </summary>
    public AgentSessionPolicyState? AcceptedPolicy { get; init; }
    /// <summary>
    /// Preview: why a <see cref="AgentMessageStatus.Cancelled"/> message stopped: <c>interrupted</c> (the process
    /// stopped while it streamed), <c>withdrawn</c> (the owner withdrew a prompt that never started) or
    /// <c>user-stop</c>. Null for any other status and for older records.
    /// </summary>
    public string? StatusReason { get; init; }
}

public sealed record AgentContextAttachment(
    string Id,
    string Source,
    string? SourceId,
    string Label,
    string ContentType,
    AgentContextSensitivity Sensitivity,
    string Scope,
    string Summary,
    object? Content,
    IReadOnlyDictionary<string, string> References)
{
    /// <summary>Original source identity stamped by installed admission, never accepted from caller transport.</summary>
    public string? ContributorId { get; init; }
    public string ModuleId { get; init; } = string.Empty;
    public string SchemaVersion { get; init; } = "1";
    public AgentResourceTarget? Target { get; init; }
    public AgentContextOrigin Origin { get; init; } = AgentContextOrigin.ResourceData;
    public AgentClientBinding? Client { get; init; }
    public IReadOnlyCollection<string> AgentScopes { get; init; } = [];
}

public sealed record AgentActionChange(
    string Path,
    string ChangeType,
    string Summary);

public sealed record AgentActionProposal(
    string Id,
    string SessionId,
    string? MessageId,
    string CapabilityId,
    string Kind,
    string Title,
    string Summary,
    AgentRisk Risk,
    string? BaseRevision,
    IReadOnlyCollection<AgentActionChange> Changes,
    IReadOnlyCollection<IReadOnlyDictionary<string, object?>> Operations,
    IReadOnlyCollection<string> Risks,
    string? Rollback,
    IReadOnlyCollection<string> RequiredPermissions,
    string? ResourceType,
    string? ResourceId,
    bool RequiresApproval,
    AgentActionProposalStatus Status,
    string? ApprovedBy,
    DateTimeOffset? ApprovedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public AgentPreparedToolInvocation? PreparedInvocation { get; init; }
    public AgentResourceTarget? ResourceTarget => PreparedInvocation?.Target;
    public string? ResultRendererId { get; init; }
    public AgentPayload? Result { get; init; }
    public string? ResultType => Result?.TypeId ?? PreparedInvocation?.Preview.TypeId;
    /// <summary>
    /// Preview: the session policy revision that was the approval basis when this operation was prepared,
    /// or null when the installed services do not own policy revisions.
    /// </summary>
    public long? PolicyRevision { get; init; }
    /// <summary>
    /// Preview: the owner's statement about an uncertain client operation, or null when none was made. It is not a
    /// receipt: a later truthful receipt still reconciles the operation and supersedes it, and the statement is kept.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AgentOwnerResolution? OwnerResolution { get; init; }
}

/// <summary>Preview: what the owner states about an uncertain client operation.</summary>
public enum AgentOwnerResolutionClaim
{
    /// <summary>The owner states that the operation was applied.</summary>
    Applied,
    /// <summary>The owner states that the operation was not applied.</summary>
    NotApplied,
    /// <summary>The owner cannot tell whether the operation was applied.</summary>
    Unknown
}

/// <summary>Preview: an owner's recorded statement about an uncertain client operation (<see cref="AgentActionProposal.OwnerResolution"/>).</summary>
public sealed record AgentOwnerResolution(AgentOwnerResolutionClaim Claim, string ActorId, DateTimeOffset ResolvedAt);

public sealed record AgentProposalExecutionResult(
    string ProposalId,
    bool Executed,
    string ResourceType,
    string ResourceId,
    string Summary)
{
    public string Message => Summary;
    public string Status { get; init; } = "executed";
    public AgentPreparedToolInvocation? ClientInvocation { get; init; }
    public AgentToolResult? ToolResult { get; init; }
}

public sealed record AgentAuditEvent(
    string Id,
    AgentAuditEventKind Kind,
    string? SessionId,
    string? ActorId,
    string Summary,
    DateTimeOffset CreatedAt,
    IReadOnlyDictionary<string, string> Metadata);

/// <summary>Preview: an owner-bounded audit query; <see cref="Take"/> must be within 1..<see cref="MaxTake"/>.</summary>
public sealed record AgentAuditOwnerQuery(string ActorId, string TenantId, string? SessionId, int Take)
{
    public const int MaxTake = 1000;
}

/// <summary>Preview.</summary>
public enum AgentAuditQueryOutcome
{
    /// <summary>The events are the eligible records, already filtered, ordered and limited by the owning reader.</summary>
    Succeeded,
    /// <summary>The installed reader does not support owner-bounded queries. No records are returned.</summary>
    Unsupported
}

/// <summary>Preview.</summary>
public sealed record AgentAuditQueryResult(AgentAuditQueryOutcome Outcome, IReadOnlyList<AgentAuditEvent> Events)
{
    public static AgentAuditQueryResult Unsupported { get; } = new(AgentAuditQueryOutcome.Unsupported, []);

    public bool Succeeded => Outcome == AgentAuditQueryOutcome.Succeeded;
}

public sealed record AgentFeedback(
    string Id,
    string SessionId,
    string? MessageId,
    string Rating,
    string? Comment,
    string? ActorId,
    DateTimeOffset CreatedAt);

public sealed record AgentPolicyDecision(
    bool Allowed,
    IReadOnlyCollection<AgentViolation> Violations);

public sealed record AgentSessionCreateRequest(
    string TenantId,
    string ActorId,
    string ConversationId,
    string ProviderId,
    string Mode,
    string? Title,
    AgentPolicy? Policy,
    IReadOnlyDictionary<string, string> Metadata)
{
    /// <summary>Preview: the longest <see cref="Workspace"/> a session records, in characters.</summary>
    public const int MaxWorkspaceLength = 256;

    /// <summary>Preview: the longest <see cref="ContinuedFrom"/> link a session records, in characters.</summary>
    public const int MaxContinuedFromLength = 128;

    /// <summary>Preview: the workspace to stamp on the new session. It only narrows owner-scoped lists.</summary>
    public string? Workspace { get; init; }
    /// <summary>Preview: the session this one continues past a limit. Only the link is recorded.</summary>
    public string? ContinuedFrom { get; init; }
}

public sealed record AgentMessageCreateRequest(
    AgentRole Role,
    string Content,
    AgentMessageStatus Status,
    string? CapabilityId,
    IReadOnlyCollection<string> ContextAttachmentIds,
    IReadOnlyCollection<AgentContextAttachment> Context)
{
    public string? RequestId { get; init; }
    public AgentPromptSource? Source { get; init; }
    public AgentClientBinding? Client { get; init; }
    public string? PromptMode { get; init; }
    public string? DeliveryFingerprint { get; init; }
    public IReadOnlyList<AgentToolResult> ToolResults { get; init; } = [];
    public bool HasDisclosureProvenance { get; init; }
    /// <summary>Complete disclosure dependencies for generated content, preserved unchanged by the owning session store.</summary>
    public IReadOnlyList<AgentDisclosureSnapshot>? DisclosureHistory { get; init; }
    /// <summary>
    /// Preview: the policy revision read when this delivery was authorized. An owning store refuses the
    /// insertion with <see cref="AgentSessionPolicyChangedException"/> when the revision moved meanwhile.
    /// </summary>
    public long? ExpectedPolicyRevision { get; init; }
}

public sealed record AgentContextRequest(
    string SessionId,
    string ScopeKind,
    IReadOnlyDictionary<string, string> Inputs)
{
    public AgentResourceTarget? Target { get; init; }
    public IReadOnlyCollection<AgentContextAttachment> CallerAttachments { get; init; } = [];
    public AgentClientBinding? Client { get; init; }
    /// <summary>Server-only retained evidence admission; caller attachments cannot select this phase.</summary>
    public AgentContextDisclosureRequest? Disclosure { get; init; }
}

public sealed record AgentProviderSession(
    string Id,
    string ProviderId,
    IReadOnlyDictionary<string, string> Metadata);

public sealed record AgentProviderMessage(
    string SessionId,
    string Content,
    IReadOnlyCollection<AgentContextAttachment> Context);

public sealed record AgentStreamEvent(
    string Id,
    AgentStreamEventKind Kind,
    string? Content,
    string? ProposalId,
    AgentError? Error,
    DateTimeOffset CreatedAt,
    AgentResultKind? ResultKind = null,
    object? Payload = null);

/// <summary>A single message in the running turn's history fed to the provider on each step.</summary>
public sealed record AgentTurnMessage(AgentRole Role, string Content, string? ToolCallId = null, string? ToolName = null);

/// <summary>The result of executing one tool, fed back to the provider on the next step.</summary>
public sealed record AgentToolResult(string ToolCallId, string ToolName, bool Succeeded, string Content, AgentPayload? Payload = null)
{
    public AgentEffectReceipt? Receipt { get; init; }
    /// <summary>Preparation identity stamped by the engine for current authority checks before later disclosure.</summary>
    public AgentToolResultAccess? Access { get; init; }
}

/// <summary>Payload for <see cref="AgentStreamEventKind.ToolCallRequested"/>: the provider asks the orchestrator to run a tool.</summary>
public sealed record AgentToolCallRequest(string ToolCallId, string ToolName, string Arguments, AgentRisk Risk, bool RequiresApproval);

/// <summary>Payload for the turn-level <see cref="AgentStreamEventKind.Started"/> and <see cref="AgentStreamEventKind.TurnCancelled"/> events. Carries the turn id used to cancel the turn.</summary>
public sealed record AgentTurnStarted(string TurnId, int MaxSteps);

/// <summary>Payload for <see cref="AgentStreamEventKind.StepStarted"/> / <see cref="AgentStreamEventKind.StepCompleted"/>.</summary>
public sealed record AgentStepDescriptor(int StepIndex, int MaxSteps);

/// <summary>Payload for <see cref="AgentStreamEventKind.ToolCallStarted"/> / <see cref="AgentStreamEventKind.ToolCallCompleted"/>.</summary>
public sealed record AgentToolCallOutcome(string ToolCallId, string ToolName, bool Succeeded, string Summary, object? Result = null);

/// <summary>A single entry in the agent's working plan.</summary>
public sealed record AgentPlanStep(string Id, string Title, string Status);

/// <summary>Payload for <see cref="AgentStreamEventKind.PlanUpdated"/>.</summary>
public sealed record AgentPlanUpdate(IReadOnlyCollection<AgentPlanStep> Steps);

public sealed record AgentProviderToolApprovalRequest(
    string ProviderSessionId,
    string ToolCallId,
    bool Approved,
    string? Reason);

public sealed record AgentToolApprovalResult(
    bool Accepted,
    string Message);

public sealed record AgentProviderDiagnostics(
    string ProviderId,
    bool IsAvailable,
    string Status,
    AgentProviderKind Kind,
    IReadOnlyCollection<AgentProviderOperation> SupportedOperations,
    AgentProviderRiskProfile RiskProfile,
    IReadOnlyDictionary<string, string> Metadata);

/// <summary>
/// Preview: a session's acknowledged policy and the owner's monotonically increasing revision of it.
/// The public wire shape is the session-policy response, never this domain record.
/// </summary>
public sealed record AgentSessionPolicyState(AgentPolicy Policy, long Revision);

/// <summary>
/// Preview: an autonomy-only conditional change. Only <see cref="AgentPolicy.AutonomyMode"/> can change;
/// the requested mode is clamped to the current ceiling and every other policy field survives.
/// </summary>
/// <param name="ExpectedRevision">The policy revision the caller last observed; a different current revision is a conflict.</param>
public sealed record AgentSessionAutonomyChangeRequest(
    string SessionId,
    string ActorId,
    string TenantId,
    AgentAutonomyMode RequestedMode,
    long ExpectedRevision);

/// <summary>Preview: the explicit disposition of a session policy read or conditional change.</summary>
public enum AgentSessionPolicyOutcome
{
    /// <summary>The returned state is authoritative. An unchanged idle request acknowledges the current revision.</summary>
    Succeeded,
    /// <summary>The expected revision is not current. Nothing changed; the current state is returned for reconciliation.</summary>
    StaleRevision,
    /// <summary>Admitted work owns the current policy. Nothing changed; the current state is returned.</summary>
    Busy,
    /// <summary>The requested autonomy mode is not a defined mode. Nothing changed.</summary>
    InvalidMode,
    /// <summary>The session is closed or expired. Nothing changed.</summary>
    SessionClosed,
    /// <summary>The session is missing or is not owned by the actor and tenant. Nothing changed.</summary>
    NotFound,
    /// <summary>The installed session/lifecycle services cannot change policy atomically. Nothing changed.</summary>
    Unsupported
}

/// <summary>Preview: the outcome of a session policy operation and, when known to the caller, the current state.</summary>
public sealed record AgentSessionPolicyResult(AgentSessionPolicyOutcome Outcome, AgentSessionPolicyState? State)
{
    public bool Succeeded => Outcome == AgentSessionPolicyOutcome.Succeeded;
}

/// <summary>Preview: one process's ownership of a session aggregate. Every guarded record write names it.</summary>
/// <param name="OwnerToken">The owning process's opaque token.</param>
/// <param name="Fence">Increases monotonically on every takeover; a write under an older fence is refused.</param>
public sealed record AgentSessionFence(string OwnerToken, long Fence);

/// <summary>Preview: the kinds of duplicate-detection keys recorded with a session aggregate.</summary>
public static class AgentSessionKeyKinds
{
    /// <summary>A tool call (session, turn and call identity); the value is the proposal id.</summary>
    public const string Call = "call";
    /// <summary>A prepared invocation identity; the value is the proposal id.</summary>
    public const string Invocation = "invocation";
    /// <summary>The one mutation a turn may acquire; the value is the call key.</summary>
    public const string TurnMutation = "turn-mutation";
    /// <summary>The input digest of a tool call; the value is the digest.</summary>
    public const string CallInput = "call-input";
}

/// <summary>Preview: the values of <see cref="AgentSessionSummary.RecoveryState"/>.</summary>
public static class AgentSessionRecoveryStates
{
    /// <summary>The prefix of a session that reached a documented limit; the limit (<c>snapshots</c> or <c>bytes</c>) follows it.</summary>
    public const string LimitReachedPrefix = "limit-reached:";

    /// <summary>The recovery state a session with <paramref name="limitReached"/> reports, or null when nothing needs attention.</summary>
    public static string? Of(string? limitReached) => string.IsNullOrEmpty(limitReached) ? null : LimitReachedPrefix + limitReached;
}

/// <summary>Preview: one unique duplicate-detection key of a session aggregate and the value it maps to.</summary>
public sealed record AgentSessionUniqueKey(string Kind, string Key, string Value);

/// <summary>Preview: a proposal with its retained execution outcome, result and accepted acknowledgement.</summary>
public sealed record AgentProposalRecord(AgentActionProposal Proposal)
{
    /// <summary>The retained execution outcome, written together with the proposal status.</summary>
    public AgentResult<AgentProposalExecutionResult>? Execution { get; init; }
    /// <summary>The retained tool result or lifecycle envelope.</summary>
    public AgentToolResult? Result { get; init; }
    /// <summary>The digest of the accepted client acknowledgement; a different later acknowledgement is a conflict.</summary>
    public string? AcknowledgementDigest { get; init; }
    /// <summary>
    /// The record's version. A load returns the recorded version (1 after the first record). A write names the version
    /// its writer read, 0 for a new proposal: the store refuses the write with
    /// <see cref="AgentSessionRecordOutcome.VersionConflict"/> when the recorded version differs, and otherwise records
    /// the next version. A transition therefore applies only to the state it was decided on.
    /// </summary>
    public long Version { get; init; }
}

/// <summary>
/// Preview: one distinct disclosure snapshot of a session, recorded once and referenced by its digest. The owner computes
/// both: <see cref="Canonical"/> is the canonical JSON text (sorted keys, raw scalars) the digest was taken over, and a
/// store keeps it byte-identical, so a recorded fingerprint stays verifiable after any number of restarts.
/// </summary>
/// <param name="Digest">The lowercase hexadecimal SHA-256 of <see cref="Canonical"/> as UTF-8.</param>
public sealed record AgentDisclosureSnapshotRecord(string Digest, string Canonical)
{
    /// <summary>The snapshot's recorded size in bytes; it counts toward the session's disclosure size limit.</summary>
    public long Size => System.Text.Encoding.UTF8.GetByteCount(Canonical);
}

/// <summary>
/// Preview: a message as the record store holds it. Its cumulative disclosure history is not stored inline:
/// <see cref="AgentMessage.DisclosureHistory"/> is null here and <see cref="DisclosureDigests"/> lists the same snapshots,
/// in order, by digest. Every other member, including <see cref="AgentMessage.DeliveryFingerprint"/>, is stored verbatim.
/// </summary>
public sealed record AgentMessageRecord(AgentMessage Message)
{
    /// <summary>The message's disclosure history as ordered snapshot digests, or null when the message has none.</summary>
    public IReadOnlyList<string>? DisclosureDigests { get; init; }
    /// <summary>The digest of the complete ordered history, recorded with it and re-verified when the session is hydrated.</summary>
    public string? DisclosureHistoryFingerprint { get; init; }
}

/// <summary>
/// Preview: the paused turn as the record store holds it. <see cref="AgentPausedTurn.DisclosureHistory"/> is null here and
/// <see cref="DisclosureDigests"/> lists the same snapshots in order; the pause fingerprint
/// (<see cref="AgentPausedTurn.DisclosureHistoryFingerprint"/>) is stored verbatim.
/// </summary>
public sealed record AgentPausedTurnRecord(AgentPausedTurn Turn)
{
    /// <summary>The paused turn's disclosure history as ordered snapshot digests, or null when it has none.</summary>
    public IReadOnlyList<string>? DisclosureDigests { get; init; }
}

/// <summary>Preview: one session aggregate as the record store holds it.</summary>
/// <remarks>Claims, gates, monitors and the turn registry are process-local and never recorded; they are rebuilt from these facts.</remarks>
public sealed record AgentSessionRecord(AgentSession Session)
{
    /// <summary>The messages in insertion order.</summary>
    public IReadOnlyList<AgentMessageRecord> Messages { get; init; } = [];
    /// <summary>The session's distinct disclosure snapshots in first-recorded order, each referenced by digest.</summary>
    public IReadOnlyList<AgentDisclosureSnapshotRecord> Snapshots { get; init; } = [];
    /// <summary>The retained session context, unique by attachment identity, in insertion order.</summary>
    public IReadOnlyList<AgentContextAttachment> Context { get; init; } = [];
    /// <summary>The paused turn, if any. Its presence is the durable fact behind the Pause claim.</summary>
    public AgentPausedTurnRecord? PausedTurn { get; init; }
    /// <summary>The session's proposals in insertion order.</summary>
    public IReadOnlyList<AgentProposalRecord> Proposals { get; init; } = [];
    /// <summary>The recorded duplicate-detection keys.</summary>
    public IReadOnlyList<AgentSessionUniqueKey> Keys { get; init; } = [];
    /// <summary>
    /// Audit records a recorded recovery settlement still owes: written with the settlement and cleared once emitted, so a
    /// process that stops between the two emits them at the session's next hydration instead of losing them.
    /// </summary>
    public IReadOnlyList<AgentAuditEvent> PendingAudits { get; init; } = [];
    /// <summary>The current owner, or null when no process owns the aggregate.</summary>
    public AgentSessionFence? Owner { get; init; }
    /// <summary>When the current owner's lease lapses, in the store's time.</summary>
    public DateTimeOffset? LeaseExpiresAt { get; init; }
    /// <summary>The optimistic concurrency token; it changes on every write.</summary>
    public long Version { get; init; }
}

/// <summary>
/// Preview: one transition of a session aggregate, written atomically under <see cref="Fence"/>. Every member is
/// optional: messages and proposals are upserted by identity, context is appended by attachment identity, and keys
/// are inserted all or nothing together with everything else.
/// </summary>
public sealed record AgentSessionRecordWrite(string SessionId, AgentSessionFence Fence)
{
    /// <summary>The session's new state, or null to keep it.</summary>
    public AgentSession? Session { get; init; }
    /// <summary>Messages to insert or replace by identity.</summary>
    public IReadOnlyList<AgentMessageRecord> Messages { get; init; } = [];
    /// <summary>
    /// Snapshots the written messages or paused turn reference for the first time. A digest already recorded keeps its
    /// recorded text; every digest a recorded message or paused turn references is recorded.
    /// </summary>
    public IReadOnlyList<AgentDisclosureSnapshotRecord> Snapshots { get; init; } = [];
    /// <summary>Context attachments to append; an attachment identity already recorded is kept unchanged.</summary>
    public IReadOnlyList<AgentContextAttachment> Context { get; init; } = [];
    /// <summary>The paused turn to record, or null to keep the recorded one (see <see cref="RemovePausedTurn"/>).</summary>
    public AgentPausedTurnRecord? PausedTurn { get; init; }
    /// <summary>Removes the recorded paused turn. Ignored when <see cref="PausedTurn"/> is set.</summary>
    public bool RemovePausedTurn { get; init; }
    /// <summary>Proposal records to insert or replace by proposal identity.</summary>
    public IReadOnlyList<AgentProposalRecord> Proposals { get; init; } = [];
    /// <summary>Keys to insert. A key already recorded with the same value is kept; a different value refuses the whole write.</summary>
    public IReadOnlyList<AgentSessionUniqueKey> Keys { get; init; } = [];
    /// <summary>The audit records the aggregate owes (<see cref="AgentSessionRecord.PendingAudits"/>), or null to keep them; empty clears them.</summary>
    public IReadOnlyList<AgentAuditEvent>? PendingAudits { get; init; }
}

/// <summary>Preview: the disposition of a record store operation.</summary>
public enum AgentSessionRecordOutcome
{
    /// <summary>The operation took effect.</summary>
    Succeeded,
    /// <summary>The store has no such session. Nothing changed.</summary>
    NotFound,
    /// <summary>A session with this identity is already recorded. Nothing changed.</summary>
    AlreadyExists,
    /// <summary>Another owner holds a live lease. Nothing changed; the caller may retry later.</summary>
    OwnedElsewhere,
    /// <summary>The write's owner or fence is no longer current. Nothing changed.</summary>
    OwnershipLost,
    /// <summary>A key is already recorded with a different value. Nothing changed; <see cref="AgentSessionRecordResult.ConflictingKeys"/> holds the recorded values.</summary>
    KeyConflict,
    /// <summary>A proposal record changed since its writer read it (<see cref="AgentProposalRecord.Version"/>). Nothing changed.</summary>
    VersionConflict
}

/// <summary>Preview: the result of a record store operation.</summary>
public sealed record AgentSessionRecordResult(AgentSessionRecordOutcome Outcome)
{
    /// <summary>The fence the caller now owns, after a successful create or ownership acquisition.</summary>
    public AgentSessionFence? Fence { get; init; }
    /// <summary>When the caller's lease lapses after a successful operation, in the store's time.</summary>
    public DateTimeOffset? LeaseExpiresAt { get; init; }
    /// <summary>The recorded values of the keys that refused a write.</summary>
    public IReadOnlyList<AgentSessionUniqueKey> ConflictingKeys { get; init; } = [];

    public bool Succeeded => Outcome == AgentSessionRecordOutcome.Succeeded;
}

/// <summary>
/// Preview: an owner-scoped session list query. Actor and tenant filter first, then the optional workspace, then
/// the result is ordered by last update (newest first) and limited to <see cref="Take"/>, at most <see cref="MaxTake"/>.
/// </summary>
/// <param name="Before">An opaque cursor from <see cref="AgentSessionSummaryPage.Next"/>; only older sessions are returned.</param>
public sealed record AgentSessionOwnerQuery(string ActorId, string TenantId, string? Workspace = null, int Take = AgentSessionOwnerQuery.DefaultTake, string? Before = null)
{
    public const int DefaultTake = 20;
    public const int MaxTake = 100;
}

/// <summary>Preview: an owner's view of one recorded session, read without decoding its protected payloads.</summary>
/// <param name="PendingReviewCount">Proposals awaiting a review decision.</param>
/// <param name="RecoveryState">What recovery found that the owner should look at, or null when nothing needs attention; today only a reached session limit (<see cref="AgentSessionRecoveryStates"/>).</param>
public sealed record AgentSessionSummary(
    string Id,
    string? Title,
    AgentSessionStatus Status,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ExpiresAt,
    int PendingReviewCount,
    string? RecoveryState);

/// <summary>Preview: one page of owner-scoped session summaries.</summary>
/// <param name="Next">The cursor for the next older page, or null when this page is the last.</param>
public sealed record AgentSessionSummaryPage(IReadOnlyList<AgentSessionSummary> Items, string? Next);

/// <summary>
/// Preview: a recorded session's plain owner columns. Authorization reads them before anything decodes a payload,
/// hydrates the session or takes its ownership.
/// </summary>
public sealed record AgentSessionOwner(string ActorId, string TenantId);

/// <summary>Preview.</summary>
public enum AgentSessionQueryOutcome
{
    /// <summary>The page holds the owner's sessions, already filtered, ordered and limited by the owning store.</summary>
    Succeeded,
    /// <summary>The installed session service cannot answer owner-scoped session queries. Nothing is returned.</summary>
    Unsupported
}

/// <summary>Preview: the outcome of an owner-scoped session list query.</summary>
public sealed record AgentSessionListResult(AgentSessionQueryOutcome Outcome, AgentSessionSummaryPage? Page)
{
    public static AgentSessionListResult Unsupported { get; } = new(AgentSessionQueryOutcome.Unsupported, null);

    public bool Succeeded => Outcome == AgentSessionQueryOutcome.Succeeded;
}

/// <summary>
/// Preview: an explicit provider outcome shown to the user as a <see cref="AgentStreamEventKind.Progress"/> event, such
/// as a provider conversation that could not be resumed and was replaced by a new one. The orchestrator records it on
/// the turn's audit trail. It never counts as an accepted outcome.
/// </summary>
public sealed record AgentProviderNotice(string Code, string Label);
