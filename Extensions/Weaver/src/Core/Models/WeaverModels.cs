using System.Text.Json.Serialization;
using Elsa.Weaver.Core.Contracts;

namespace Elsa.Weaver.Core.Models;

public enum WeaverRole
{
    User,
    Assistant,
    System,
    Tool,
    Progress,
    Error
}

public enum WeaverSessionStatus
{
    Active,
    Completed,
    Cancelled,
    Failed,
    Expired
}

public enum WeaverMessageStatus
{
    Pending,
    Streaming,
    Completed,
    Failed,
    Cancelled
}

public enum WeaverContextSensitivity
{
    Public = 0,
    Internal = 1,
    Sensitive = 2,
    SecretRedacted = 3,
    Secret = 4
}

public enum WeaverCapabilityKind
{
    Answer,
    Context,
    PromptStarter,
    Proposal,
    Action
}

public enum WeaverRisk
{
    ReadOnly,
    ReviewRequired,
    Destructive,
    Admin
}

/// <summary>Host privileges required in addition to the current actor's owning-resource permissions.</summary>
public enum WeaverPrivilegeClass
{
    Administrative = 0,
    Irrecoverable = 1,
    Activation = 2
}

/// <summary>
/// How much Weaver may do without per-mutation human approval. Ordered from most to least restrictive:
/// the integer value doubles as the autonomy "level" used when clamping a requested mode to a deployment ceiling.
/// </summary>
public enum WeaverAutonomyMode
{
    /// <summary>Every mutating tool becomes a reviewable proposal; read-only tools still run inline.</summary>
    Manual = 0,

    /// <summary>Read-only tools run inline; mutating tools become reviewable (and undoable) proposals.</summary>
    AutoReadOnly = 1,

    /// <summary>Mutating tools run inline without a proposal step.</summary>
    FullAuto = 2
}

public enum WeaverActionProposalStatus
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
    Uncertain,
    /// <summary>The action ran with owning-resource evidence and no persisted change to that resource.</summary>
    Performed = 12
}

public enum WeaverAuditEventKind
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

public enum WeaverStreamEventKind
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

public enum WeaverResultKind
{
    Message,
    Clarification,
    ModuleResult,
    Proposal,
    Error
}

public enum WeaverProviderKind
{
    ProviderSdkBinding,
    WeaverHarnessProvider
}

public enum WeaverProviderOperation
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

public enum WeaverProviderRiskProfile
{
    ReadOnly,
    ReviewRequired,
    SandboxedExecution,
    PrivilegedExecution
}

public sealed record WeaverError(string Code, string Message, int StatusCode = 400)
{
    /// <summary>The installed owner could not decide. This transport value alone does not establish trusted owner origin.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool OwnerUnavailable { get; init; }

    /// <summary>
    /// Preview: what a <c>weaver.context.disclosure_budget_exceeded</c> refusal reached. The session limits, recorded on the
    /// session and offered a continuation: <c>snapshots</c> (64 distinct snapshots) and <c>bytes</c> (8 MiB of recorded
    /// disclosure, with less than 64 KiB left, so the session is treated as full). Refusals of one disclosure, never recorded:
    /// <c>snapshot-too-large</c> (larger than any session can record) and <c>snapshot-does-not-fit</c> (larger than the room
    /// left in a session that is not full). Null for every other error.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Limit { get; init; }
}

public sealed record WeaverResult<T>(T? Value, WeaverError? Error)
{
    public bool Succeeded => Error is null;

    public static WeaverResult<T> Success(T value) => new(value, null);

    public static WeaverResult<T> Failure(string code, string message, int statusCode = 400) => new(default, new(code, message, statusCode));
}

public sealed record WeaverViolation(string Code, string Message, string? Target = null)
{
    /// <summary>
    /// Preview: the owning module could not reach a decision, as opposed to refusing. Still a refusal (fail closed), but the
    /// tool invoker reports it as unavailable (503) with this violation's <see cref="Code"/> and a fixed, cause-free message
    /// of its own, instead of the generic forbidden outcome. The violation's <see cref="Message"/> is never forwarded.
    /// </summary>
    /// <remarks>
    /// Honoured only on a violation from the installed tool's own <c>IWeaverToolAuthorization</c> decision; a host
    /// <c>IWeaverToolAuthorizer</c> violation that sets it stays an ordinary denial. The code must be the owner's own dotted
    /// identifier (ASCII letters, digits, <c>.</c>, <c>_</c> or <c>-</c>, at most 128 characters), outside the engine's
    /// reserved <c>weaver.</c> namespace; any other code stays an ordinary denial. Omitted from the wire when unset.
    /// </remarks>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool OwnerUnavailable { get; init; }
}

public sealed record WeaverPolicy(
    string Id,
    bool Enabled,
    bool ContextVisibility,
    WeaverContextSensitivity MaxContextSensitivity,
    IReadOnlyCollection<string> AllowedContextKinds,
    WeaverAutonomyMode AutonomyMode,
    WeaverAutonomyMode MaxAutonomyMode,
    IReadOnlyCollection<string> DeniedCapabilityIds,
    string RetentionLabel)
{
    public static WeaverPolicy Default { get; } = new(
        "default",
        Enabled: true,
        ContextVisibility: true,
        WeaverContextSensitivity.Sensitive,
        // Wildcards apply only after an installed module admits the context kind/schema.
        ["*"],
        // A new session opens on the safe "auto-apply low-risk" default, but the ceiling permits clients
        // to opt up to FullAuto ("Autopilot") — mutating tools run inline, risky edits still defer to review.
        AutonomyMode: WeaverAutonomyMode.AutoReadOnly,
        MaxAutonomyMode: WeaverAutonomyMode.FullAuto,
        DeniedCapabilityIds: [],
        RetentionLabel: "Configured by administrator");

    /// <summary>Whether a mutating tool must be routed through a proposal under the effective autonomy mode.</summary>
    public bool RequiresApprovalForMutations => AutonomyMode != WeaverAutonomyMode.FullAuto;

    /// <summary>The autonomy modes a client may request without exceeding <see cref="MaxAutonomyMode"/>, most restrictive first.</summary>
    public IReadOnlyCollection<WeaverAutonomyMode> AllowedAutonomyModes =>
        Enum.GetValues<WeaverAutonomyMode>().Where(mode => mode <= MaxAutonomyMode).ToList();

    /// <summary>Clamps a requested autonomy mode to this policy's <see cref="MaxAutonomyMode"/> ceiling.</summary>
    public WeaverAutonomyMode Clamp(WeaverAutonomyMode requested) => requested <= MaxAutonomyMode ? requested : MaxAutonomyMode;
}

public sealed record WeaverCapability(
    string Id,
    string? ModuleId,
    string DisplayName,
    string Description,
    WeaverCapabilityKind Kind,
    WeaverRisk Risk,
    IReadOnlyCollection<string> Surfaces,
    IReadOnlyCollection<string> RequiredPermissions,
    IReadOnlyCollection<string> ContextKinds)
{
    public IReadOnlyCollection<string> Modes { get; init; } = [];
}

public sealed record WeaverSession(
    string Id,
    string? Title,
    string TenantId,
    string ActorId,
    string ConversationId,
    string ProviderId,
    string Mode,
    WeaverPolicy Policy,
    WeaverSessionStatus Status,
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
    /// <see cref="WeaverError.Limit"/> names it), or null. Recorded when a turn is refused at the limit, so a reopened
    /// session shows the limit and its continuation before another prompt is refused.
    /// </summary>
    public string? LimitReached { get; init; }
}

public sealed record WeaverMessage(
    string Id,
    string SessionId,
    WeaverRole Role,
    string Content,
    WeaverMessageStatus Status,
    string? CapabilityId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt,
    WeaverError? Error,
    IReadOnlyCollection<string> ContextAttachmentIds,
    IReadOnlyCollection<string> ProposalIds,
    IReadOnlyCollection<WeaverContextAttachment> Context)
{
    public string? RequestId { get; init; }
    public WeaverPromptSource? Source { get; init; }
    public WeaverClientBinding? Client { get; init; }
    public string? PromptMode { get; init; }
    public string? DeliveryFingerprint { get; init; }
    /// <summary>Observed tool outcomes whose current disclosure must be established before transcript reuse.</summary>
    public IReadOnlyList<WeaverToolResult> ToolResults { get; init; } = [];
    /// <summary>The engine tracks this message and the resource/result disclosures of its turn.</summary>
    public bool HasDisclosureProvenance { get; init; }
    /// <summary>Complete immutable disclosure snapshots behind generated transcript content; null denotes an older/incomplete journal.</summary>
    public IReadOnlyList<WeaverDisclosureSnapshot>? DisclosureHistory { get; init; }
    /// <summary>
    /// Preview: the frozen session policy and revision current when the owning store accepted this message.
    /// A later policy change never relabels it; null when the installed store does not own policy revisions.
    /// </summary>
    public WeaverSessionPolicyState? AcceptedPolicy { get; init; }
    /// <summary>
    /// Preview: why a <see cref="WeaverMessageStatus.Cancelled"/> message stopped: <c>interrupted</c> (the process
    /// stopped while it streamed), <c>withdrawn</c> (the owner withdrew a prompt that never started) or
    /// <c>user-stop</c>. Null for any other status and for older records.
    /// </summary>
    public string? StatusReason { get; init; }
}

public sealed record WeaverContextAttachment(
    string Id,
    string Source,
    string? SourceId,
    string Label,
    string ContentType,
    WeaverContextSensitivity Sensitivity,
    string Scope,
    string Summary,
    object? Content,
    IReadOnlyDictionary<string, string> References)
{
    /// <summary>Original source identity stamped by installed admission, never accepted from caller transport.</summary>
    public string? ContributorId { get; init; }
    public string ModuleId { get; init; } = string.Empty;
    public string SchemaVersion { get; init; } = "1";
    public WeaverResourceTarget? Target { get; init; }
    public WeaverContextOrigin Origin { get; init; } = WeaverContextOrigin.ResourceData;
    public WeaverClientBinding? Client { get; init; }
    public IReadOnlyCollection<string> WeaverScopes { get; init; } = [];
}

public sealed record WeaverActionChange(
    string Path,
    string ChangeType,
    string Summary);

public sealed record WeaverActionProposal(
    string Id,
    string SessionId,
    string? MessageId,
    string CapabilityId,
    string Kind,
    string Title,
    string Summary,
    WeaverRisk Risk,
    string? BaseRevision,
    IReadOnlyCollection<WeaverActionChange> Changes,
    IReadOnlyCollection<IReadOnlyDictionary<string, object?>> Operations,
    IReadOnlyCollection<string> Risks,
    string? Rollback,
    IReadOnlyCollection<string> RequiredPermissions,
    string? ResourceType,
    string? ResourceId,
    bool RequiresApproval,
    WeaverActionProposalStatus Status,
    string? ApprovedBy,
    DateTimeOffset? ApprovedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public WeaverPreparedToolInvocation? PreparedInvocation { get; init; }
    public WeaverResourceTarget? ResourceTarget => PreparedInvocation?.Target;
    public string? ResultRendererId { get; init; }
    public WeaverPayload? Result { get; init; }
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
    public WeaverOwnerResolution? OwnerResolution { get; init; }
}

/// <summary>Preview: what the owner states about an uncertain client operation.</summary>
public enum WeaverOwnerResolutionClaim
{
    /// <summary>The owner states that the operation was applied.</summary>
    Applied,
    /// <summary>The owner states that the operation was not applied.</summary>
    NotApplied,
    /// <summary>The owner cannot tell whether the operation was applied.</summary>
    Unknown
}

/// <summary>Preview: an owner's recorded statement about an uncertain client operation (<see cref="WeaverActionProposal.OwnerResolution"/>).</summary>
public sealed record WeaverOwnerResolution(WeaverOwnerResolutionClaim Claim, string ActorId, DateTimeOffset ResolvedAt);

public sealed record WeaverProposalExecutionResult(
    string ProposalId,
    bool Executed,
    string ResourceType,
    string ResourceId,
    string Summary)
{
    public string Message => Summary;
    public string Status { get; init; } = "executed";
    public WeaverPreparedToolInvocation? ClientInvocation { get; init; }
    public WeaverToolResult? ToolResult { get; init; }
}

public sealed record WeaverAuditEvent(
    string Id,
    WeaverAuditEventKind Kind,
    string? SessionId,
    string? ActorId,
    string Summary,
    DateTimeOffset CreatedAt,
    IReadOnlyDictionary<string, string> Metadata);

/// <summary>Preview: an owner-bounded audit query; <see cref="Take"/> must be within 1..<see cref="MaxTake"/>.</summary>
public sealed record WeaverAuditOwnerQuery(string ActorId, string TenantId, string? SessionId, int Take)
{
    public const int MaxTake = 1000;
}

/// <summary>Preview.</summary>
public enum WeaverAuditQueryOutcome
{
    /// <summary>The events are the eligible records, already filtered, ordered and limited by the owning reader.</summary>
    Succeeded,
    /// <summary>The installed reader does not support owner-bounded queries. No records are returned.</summary>
    Unsupported
}

/// <summary>Preview.</summary>
public sealed record WeaverAuditQueryResult(WeaverAuditQueryOutcome Outcome, IReadOnlyList<WeaverAuditEvent> Events)
{
    public static WeaverAuditQueryResult Unsupported { get; } = new(WeaverAuditQueryOutcome.Unsupported, []);

    public bool Succeeded => Outcome == WeaverAuditQueryOutcome.Succeeded;
}

public sealed record WeaverFeedback(
    string Id,
    string SessionId,
    string? MessageId,
    string Rating,
    string? Comment,
    string? ActorId,
    DateTimeOffset CreatedAt);

public sealed record WeaverPolicyDecision(
    bool Allowed,
    IReadOnlyCollection<WeaverViolation> Violations);

public sealed record WeaverSessionCreateRequest(
    string TenantId,
    string ActorId,
    string ConversationId,
    string ProviderId,
    string Mode,
    string? Title,
    WeaverPolicy? Policy,
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

public sealed record WeaverMessageCreateRequest(
    WeaverRole Role,
    string Content,
    WeaverMessageStatus Status,
    string? CapabilityId,
    IReadOnlyCollection<string> ContextAttachmentIds,
    IReadOnlyCollection<WeaverContextAttachment> Context)
{
    public string? RequestId { get; init; }
    public WeaverPromptSource? Source { get; init; }
    public WeaverClientBinding? Client { get; init; }
    public string? PromptMode { get; init; }
    public string? DeliveryFingerprint { get; init; }
    public IReadOnlyList<WeaverToolResult> ToolResults { get; init; } = [];
    public bool HasDisclosureProvenance { get; init; }
    /// <summary>Complete disclosure dependencies for generated content, preserved unchanged by the owning session store.</summary>
    public IReadOnlyList<WeaverDisclosureSnapshot>? DisclosureHistory { get; init; }
    /// <summary>
    /// Preview: the policy revision read when this delivery was authorized. An owning store refuses the
    /// insertion with <see cref="WeaverSessionPolicyChangedException"/> when the revision moved meanwhile.
    /// </summary>
    public long? ExpectedPolicyRevision { get; init; }
}

public sealed record WeaverContextRequest(
    string SessionId,
    string ScopeKind,
    IReadOnlyDictionary<string, string> Inputs)
{
    public WeaverResourceTarget? Target { get; init; }
    public IReadOnlyCollection<WeaverContextAttachment> CallerAttachments { get; init; } = [];
    public WeaverClientBinding? Client { get; init; }
    /// <summary>Server-only retained evidence admission; caller attachments cannot select this phase.</summary>
    public WeaverContextDisclosureRequest? Disclosure { get; init; }
}

public sealed record WeaverProviderSession(
    string Id,
    string ProviderId,
    IReadOnlyDictionary<string, string> Metadata);

public sealed record WeaverProviderMessage(
    string SessionId,
    string Content,
    IReadOnlyCollection<WeaverContextAttachment> Context);

public sealed record WeaverStreamEvent(
    string Id,
    WeaverStreamEventKind Kind,
    string? Content,
    string? ProposalId,
    WeaverError? Error,
    DateTimeOffset CreatedAt,
    WeaverResultKind? ResultKind = null,
    object? Payload = null);

/// <summary>A single message in the running turn's history fed to the provider on each step.</summary>
public sealed record WeaverTurnMessage(WeaverRole Role, string Content, string? ToolCallId = null, string? ToolName = null);

/// <summary>The result of executing one tool, fed back to the provider on the next step.</summary>
public sealed record WeaverToolResult(string ToolCallId, string ToolName, bool Succeeded, string Content, WeaverPayload? Payload = null)
{
    public WeaverEffectReceipt? Receipt { get; init; }
    /// <summary>Preparation identity stamped by the engine for current authority checks before later disclosure.</summary>
    public WeaverToolResultAccess? Access { get; init; }
}

/// <summary>Payload for <see cref="WeaverStreamEventKind.ToolCallRequested"/>: the provider asks the orchestrator to run a tool.</summary>
public sealed record WeaverToolCallRequest(string ToolCallId, string ToolName, string Arguments, WeaverRisk Risk, bool RequiresApproval);

/// <summary>Payload for the turn-level <see cref="WeaverStreamEventKind.Started"/> and <see cref="WeaverStreamEventKind.TurnCancelled"/> events. Carries the turn id used to cancel the turn.</summary>
public sealed record WeaverTurnStarted(string TurnId, int MaxSteps);

/// <summary>Payload for <see cref="WeaverStreamEventKind.StepStarted"/> / <see cref="WeaverStreamEventKind.StepCompleted"/>.</summary>
public sealed record WeaverStepDescriptor(int StepIndex, int MaxSteps);

/// <summary>Payload for <see cref="WeaverStreamEventKind.ToolCallStarted"/> / <see cref="WeaverStreamEventKind.ToolCallCompleted"/>.</summary>
public sealed record WeaverToolCallOutcome(string ToolCallId, string ToolName, bool Succeeded, string Summary, object? Result = null);

/// <summary>A single entry in Weaver's working plan.</summary>
public sealed record WeaverPlanStep(string Id, string Title, string Status);

/// <summary>Payload for <see cref="WeaverStreamEventKind.PlanUpdated"/>.</summary>
public sealed record WeaverPlanUpdate(IReadOnlyCollection<WeaverPlanStep> Steps);

public sealed record WeaverProviderToolApprovalRequest(
    string ProviderSessionId,
    string ToolCallId,
    bool Approved,
    string? Reason);

public sealed record WeaverToolApprovalResult(
    bool Accepted,
    string Message);

public sealed record WeaverProviderDiagnostics(
    string ProviderId,
    bool IsAvailable,
    string Status,
    WeaverProviderKind Kind,
    IReadOnlyCollection<WeaverProviderOperation> SupportedOperations,
    WeaverProviderRiskProfile RiskProfile,
    IReadOnlyDictionary<string, string> Metadata);

/// <summary>
/// Preview: a session's acknowledged policy and the owner's monotonically increasing revision of it.
/// The public wire shape is the session-policy response, never this domain record.
/// </summary>
public sealed record WeaverSessionPolicyState(WeaverPolicy Policy, long Revision);

/// <summary>
/// Preview: an autonomy-only conditional change. Only <see cref="WeaverPolicy.AutonomyMode"/> can change;
/// the requested mode is clamped to the current ceiling and every other policy field survives.
/// </summary>
/// <param name="ExpectedRevision">The policy revision the caller last observed; a different current revision is a conflict.</param>
public sealed record WeaverSessionAutonomyChangeRequest(
    string SessionId,
    string ActorId,
    string TenantId,
    WeaverAutonomyMode RequestedMode,
    long ExpectedRevision);

/// <summary>Preview: the explicit disposition of a session policy read or conditional change.</summary>
public enum WeaverSessionPolicyOutcome
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
public sealed record WeaverSessionPolicyResult(WeaverSessionPolicyOutcome Outcome, WeaverSessionPolicyState? State)
{
    public bool Succeeded => Outcome == WeaverSessionPolicyOutcome.Succeeded;
}

/// <summary>Preview: one process's ownership of a session aggregate. Every guarded record write names it.</summary>
/// <param name="OwnerToken">The owning process's opaque token.</param>
/// <param name="Fence">Increases monotonically on every takeover; a write under an older fence is refused.</param>
public sealed record WeaverSessionFence(string OwnerToken, long Fence);

/// <summary>Preview: the kinds of duplicate-detection keys recorded with a session aggregate.</summary>
public static class WeaverSessionKeyKinds
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

/// <summary>Preview: the values of <see cref="WeaverSessionSummary.RecoveryState"/>.</summary>
public static class WeaverSessionRecoveryStates
{
    /// <summary>The prefix of a session that reached a documented limit; the limit (<c>snapshots</c> or <c>bytes</c>) follows it.</summary>
    public const string LimitReachedPrefix = "limit-reached:";

    /// <summary>The recovery state a session with <paramref name="limitReached"/> reports, or null when nothing needs attention.</summary>
    public static string? Of(string? limitReached) => string.IsNullOrEmpty(limitReached) ? null : LimitReachedPrefix + limitReached;
}

/// <summary>Preview: one unique duplicate-detection key of a session aggregate and the value it maps to.</summary>
public sealed record WeaverSessionUniqueKey(string Kind, string Key, string Value);

/// <summary>Preview: a proposal with its retained execution outcome, result and accepted acknowledgement.</summary>
public sealed record WeaverProposalRecord(WeaverActionProposal Proposal)
{
    /// <summary>The retained execution outcome, written together with the proposal status.</summary>
    public WeaverResult<WeaverProposalExecutionResult>? Execution { get; init; }
    /// <summary>The retained tool result or lifecycle envelope.</summary>
    public WeaverToolResult? Result { get; init; }
    /// <summary>The digest of the accepted client acknowledgement; a different later acknowledgement is a conflict.</summary>
    public string? AcknowledgementDigest { get; init; }
    /// <summary>
    /// The record's version. A load returns the recorded version (1 after the first record). A write names the version
    /// its writer read, 0 for a new proposal: the store refuses the write with
    /// <see cref="WeaverSessionRecordOutcome.VersionConflict"/> when the recorded version differs, and otherwise records
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
public sealed record WeaverDisclosureSnapshotRecord(string Digest, string Canonical)
{
    /// <summary>The snapshot's recorded size in bytes; it counts toward the session's disclosure size limit.</summary>
    public long Size => System.Text.Encoding.UTF8.GetByteCount(Canonical);
}

/// <summary>
/// Preview: a message as the record store holds it. Its cumulative disclosure history is not stored inline:
/// <see cref="WeaverMessage.DisclosureHistory"/> is null here and <see cref="DisclosureDigests"/> lists the same snapshots,
/// in order, by digest. Every other member, including <see cref="WeaverMessage.DeliveryFingerprint"/>, is stored verbatim.
/// </summary>
public sealed record WeaverMessageRecord(WeaverMessage Message)
{
    /// <summary>The message's disclosure history as ordered snapshot digests, or null when the message has none.</summary>
    public IReadOnlyList<string>? DisclosureDigests { get; init; }
    /// <summary>The digest of the complete ordered history, recorded with it and re-verified when the session is hydrated.</summary>
    public string? DisclosureHistoryFingerprint { get; init; }
}

/// <summary>
/// Preview: the paused turn as the record store holds it. <see cref="WeaverPausedTurn.DisclosureHistory"/> is null here and
/// <see cref="DisclosureDigests"/> lists the same snapshots in order; the pause fingerprint
/// (<see cref="WeaverPausedTurn.DisclosureHistoryFingerprint"/>) is stored verbatim.
/// </summary>
public sealed record WeaverPausedTurnRecord(WeaverPausedTurn Turn)
{
    /// <summary>The paused turn's disclosure history as ordered snapshot digests, or null when it has none.</summary>
    public IReadOnlyList<string>? DisclosureDigests { get; init; }
}

/// <summary>Preview: one session aggregate as the record store holds it.</summary>
/// <remarks>Claims, gates, monitors and the turn registry are process-local and never recorded; they are rebuilt from these facts.</remarks>
public sealed record WeaverSessionRecord(WeaverSession Session)
{
    /// <summary>The messages in insertion order.</summary>
    public IReadOnlyList<WeaverMessageRecord> Messages { get; init; } = [];
    /// <summary>The session's distinct disclosure snapshots in first-recorded order, each referenced by digest.</summary>
    public IReadOnlyList<WeaverDisclosureSnapshotRecord> Snapshots { get; init; } = [];
    /// <summary>The retained session context, unique by attachment identity, in insertion order.</summary>
    public IReadOnlyList<WeaverContextAttachment> Context { get; init; } = [];
    /// <summary>The paused turn, if any. Its presence is the durable fact behind the Pause claim.</summary>
    public WeaverPausedTurnRecord? PausedTurn { get; init; }
    /// <summary>The session's proposals in insertion order.</summary>
    public IReadOnlyList<WeaverProposalRecord> Proposals { get; init; } = [];
    /// <summary>The recorded duplicate-detection keys.</summary>
    public IReadOnlyList<WeaverSessionUniqueKey> Keys { get; init; } = [];
    /// <summary>
    /// Audit records a recorded recovery settlement still owes: written with the settlement and cleared once emitted, so a
    /// process that stops between the two emits them at the session's next hydration instead of losing them.
    /// </summary>
    public IReadOnlyList<WeaverAuditEvent> PendingAudits { get; init; } = [];
    /// <summary>The current owner, or null when no process owns the aggregate.</summary>
    public WeaverSessionFence? Owner { get; init; }
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
public sealed record WeaverSessionRecordWrite(string SessionId, WeaverSessionFence Fence)
{
    /// <summary>The session's new state, or null to keep it.</summary>
    public WeaverSession? Session { get; init; }
    /// <summary>Messages to insert or replace by identity.</summary>
    public IReadOnlyList<WeaverMessageRecord> Messages { get; init; } = [];
    /// <summary>
    /// Snapshots the written messages or paused turn reference for the first time. A digest already recorded keeps its
    /// recorded text; every digest a recorded message or paused turn references is recorded.
    /// </summary>
    public IReadOnlyList<WeaverDisclosureSnapshotRecord> Snapshots { get; init; } = [];
    /// <summary>Context attachments to append; an attachment identity already recorded is kept unchanged.</summary>
    public IReadOnlyList<WeaverContextAttachment> Context { get; init; } = [];
    /// <summary>The paused turn to record, or null to keep the recorded one (see <see cref="RemovePausedTurn"/>).</summary>
    public WeaverPausedTurnRecord? PausedTurn { get; init; }
    /// <summary>Removes the recorded paused turn. Ignored when <see cref="PausedTurn"/> is set.</summary>
    public bool RemovePausedTurn { get; init; }
    /// <summary>Proposal records to insert or replace by proposal identity.</summary>
    public IReadOnlyList<WeaverProposalRecord> Proposals { get; init; } = [];
    /// <summary>Keys to insert. A key already recorded with the same value is kept; a different value refuses the whole write.</summary>
    public IReadOnlyList<WeaverSessionUniqueKey> Keys { get; init; } = [];
    /// <summary>The audit records the aggregate owes (<see cref="WeaverSessionRecord.PendingAudits"/>), or null to keep them; empty clears them.</summary>
    public IReadOnlyList<WeaverAuditEvent>? PendingAudits { get; init; }
}

/// <summary>Preview: the disposition of a record store operation.</summary>
public enum WeaverSessionRecordOutcome
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
    /// <summary>A key is already recorded with a different value. Nothing changed; <see cref="WeaverSessionRecordResult.ConflictingKeys"/> holds the recorded values.</summary>
    KeyConflict,
    /// <summary>A proposal record changed since its writer read it (<see cref="WeaverProposalRecord.Version"/>). Nothing changed.</summary>
    VersionConflict
}

/// <summary>Preview: the result of a record store operation.</summary>
public sealed record WeaverSessionRecordResult(WeaverSessionRecordOutcome Outcome)
{
    /// <summary>The fence the caller now owns, after a successful create or ownership acquisition.</summary>
    public WeaverSessionFence? Fence { get; init; }
    /// <summary>When the caller's lease lapses after a successful operation, in the store's time.</summary>
    public DateTimeOffset? LeaseExpiresAt { get; init; }
    /// <summary>The recorded values of the keys that refused a write.</summary>
    public IReadOnlyList<WeaverSessionUniqueKey> ConflictingKeys { get; init; } = [];

    public bool Succeeded => Outcome == WeaverSessionRecordOutcome.Succeeded;
}

/// <summary>
/// Preview: an owner-scoped session list query. Actor and tenant filter first, then the optional workspace, then
/// the result is ordered by last update (newest first) and limited to <see cref="Take"/>, at most <see cref="MaxTake"/>.
/// </summary>
/// <param name="Before">An opaque cursor from <see cref="WeaverSessionSummaryPage.Next"/>; only older sessions are returned.</param>
public sealed record WeaverSessionOwnerQuery(string ActorId, string TenantId, string? Workspace = null, int Take = WeaverSessionOwnerQuery.DefaultTake, string? Before = null)
{
    public const int DefaultTake = 20;
    public const int MaxTake = 100;
}

/// <summary>Preview: an owner's view of one recorded session, read without decoding its protected payloads.</summary>
/// <param name="PendingReviewCount">Proposals awaiting a review decision.</param>
/// <param name="RecoveryState">What recovery found that the owner should look at, or null when nothing needs attention; today only a reached session limit (<see cref="WeaverSessionRecoveryStates"/>).</param>
public sealed record WeaverSessionSummary(
    string Id,
    string? Title,
    WeaverSessionStatus Status,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ExpiresAt,
    int PendingReviewCount,
    string? RecoveryState);

/// <summary>Preview: one page of owner-scoped session summaries.</summary>
/// <param name="Next">The cursor for the next older page, or null when this page is the last.</param>
public sealed record WeaverSessionSummaryPage(IReadOnlyList<WeaverSessionSummary> Items, string? Next);

/// <summary>
/// Preview: a recorded session's plain owner columns. Authorization reads them before anything decodes a payload,
/// hydrates the session or takes its ownership.
/// </summary>
public sealed record WeaverSessionOwner(string ActorId, string TenantId);

/// <summary>Preview.</summary>
public enum WeaverSessionQueryOutcome
{
    /// <summary>The page holds the owner's sessions, already filtered, ordered and limited by the owning store.</summary>
    Succeeded,
    /// <summary>The installed session service cannot answer owner-scoped session queries. Nothing is returned.</summary>
    Unsupported
}

/// <summary>Preview: the outcome of an owner-scoped session list query.</summary>
public sealed record WeaverSessionListResult(WeaverSessionQueryOutcome Outcome, WeaverSessionSummaryPage? Page)
{
    public static WeaverSessionListResult Unsupported { get; } = new(WeaverSessionQueryOutcome.Unsupported, null);

    public bool Succeeded => Outcome == WeaverSessionQueryOutcome.Succeeded;
}

/// <summary>
/// Preview: an explicit provider outcome shown to the user as a <see cref="WeaverStreamEventKind.Progress"/> event, such
/// as a provider conversation that could not be resumed and was replaced by a new one. The orchestrator records it on
/// the turn's audit trail. It never counts as an accepted outcome.
/// </summary>
public sealed record WeaverProviderNotice(string Code, string Label);
