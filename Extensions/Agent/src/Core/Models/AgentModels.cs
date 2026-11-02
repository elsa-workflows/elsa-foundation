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
    ToolInvoked
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

public sealed record AgentError(string Code, string Message, int StatusCode = 400);

public sealed record AgentResult<T>(T? Value, AgentError? Error)
{
    public bool Succeeded => Error is null;

    public static AgentResult<T> Success(T value) => new(value, null);

    public static AgentResult<T> Failure(string code, string message, int statusCode = 400) => new(default, new(code, message, statusCode));
}

public sealed record AgentViolation(string Code, string Message, string? Target = null);

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
    /// Preview (#3440): the owning session store's revision of <see cref="Policy"/>, or null when the installed
    /// store does not own conditional policy updates.
    /// </summary>
    public long? PolicyRevision { get; init; }
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
    /// Experimental: the frozen session policy and revision current when the owning store accepted this message.
    /// A later policy change never relabels it; null when the installed store does not own policy revisions.
    /// </summary>
    public AgentSessionPolicyState? AcceptedPolicy { get; init; }
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
    /// Experimental: the session policy revision that was the approval basis when this operation was prepared,
    /// or null when the installed services do not own policy revisions.
    /// </summary>
    public long? PolicyRevision { get; init; }
}

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

/// <summary>Preview (#3440): an owner-bounded audit query; <see cref="Take"/> must be within 1..<see cref="MaxTake"/>.</summary>
public sealed record AgentAuditOwnerQuery(string ActorId, string TenantId, string? SessionId, int Take)
{
    public const int MaxTake = 1000;
}

/// <summary>Preview (#3440).</summary>
public enum AgentAuditQueryOutcome
{
    /// <summary>The events are the eligible records, already filtered, ordered and limited by the owning reader.</summary>
    Succeeded,
    /// <summary>The installed reader does not support owner-bounded queries. No records are returned.</summary>
    Unsupported
}

/// <summary>Preview (#3440).</summary>
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
    IReadOnlyDictionary<string, string> Metadata);

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
    /// Experimental: the policy revision read when this delivery was authorized. An owning store refuses the
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
/// Preview (#3440): a session's acknowledged policy and the owner's monotonically increasing revision of it.
/// The public wire shape is the session-policy response, never this domain record.
/// </summary>
public sealed record AgentSessionPolicyState(AgentPolicy Policy, long Revision);

/// <summary>
/// Experimental: an autonomy-only conditional change. Only <see cref="AgentPolicy.AutonomyMode"/> can change;
/// the requested mode is clamped to the current ceiling and every other policy field survives.
/// </summary>
/// <param name="ExpectedRevision">The policy revision the caller last observed; a different current revision is a conflict.</param>
public sealed record AgentSessionAutonomyChangeRequest(
    string SessionId,
    string ActorId,
    string TenantId,
    AgentAutonomyMode RequestedMode,
    long ExpectedRevision);

/// <summary>Experimental: the explicit disposition of a session policy read or conditional change.</summary>
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

/// <summary>Experimental: the outcome of a session policy operation and, when known to the caller, the current state.</summary>
public sealed record AgentSessionPolicyResult(AgentSessionPolicyOutcome Outcome, AgentSessionPolicyState? State)
{
    public bool Succeeded => Outcome == AgentSessionPolicyOutcome.Succeeded;
}
