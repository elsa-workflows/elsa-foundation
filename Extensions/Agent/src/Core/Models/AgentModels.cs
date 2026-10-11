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
    Cancelled
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
    WorkflowAuthoringInteraction,
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
    WorkflowGraphOperationBatchCreated,
    StepStarted,
    StepCompleted,
    ToolCallRequested,
    ToolCallStarted,
    ToolCallCompleted,
    PlanUpdated,
    Progress,
    TurnCancelled
}

public enum AgentResultKind
{
    Message,
    Clarification,
    WorkflowGraphOperationBatch,
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
        ["workflow.definition", "workflow.instance", "workflow.execution", "workflow.diagnostics"],
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
    IReadOnlyCollection<string> ContextKinds);

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
    IReadOnlyDictionary<string, string> Metadata);

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
    IReadOnlyCollection<AgentContextAttachment> Context);

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
    IReadOnlyDictionary<string, string> References);

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
    DateTimeOffset UpdatedAt);

public sealed record AgentProposalExecutionResult(
    string ProposalId,
    bool Executed,
    string ResourceType,
    string ResourceId,
    string Summary)
{
    public string Message => Summary;
}

public sealed record AgentAuditEvent(
    string Id,
    AgentAuditEventKind Kind,
    string? SessionId,
    string? ActorId,
    string Summary,
    DateTimeOffset CreatedAt,
    IReadOnlyDictionary<string, string> Metadata);

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
    IReadOnlyCollection<AgentContextAttachment> Context);

public sealed record AgentContextRequest(
    string SessionId,
    string ScopeKind,
    IReadOnlyDictionary<string, string> Inputs);

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
public sealed record AgentToolResult(string ToolCallId, string ToolName, bool Succeeded, string Content, object? Payload = null);

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
