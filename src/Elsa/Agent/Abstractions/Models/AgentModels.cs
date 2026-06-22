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
    ProviderDiagnostic
}

public enum AgentStreamEventKind
{
    Started,
    MessageDelta,
    ToolApprovalRequested,
    ProposalCreated,
    Completed,
    Error
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
    bool RequireProposalApproval,
    IReadOnlyCollection<string> DeniedCapabilityIds,
    string RetentionLabel)
{
    public static AgentPolicy Default { get; } = new(
        "default",
        Enabled: true,
        ContextVisibility: true,
        AgentContextSensitivity.Sensitive,
        ["workflow.definition", "workflow.instance", "workflow.execution", "workflow.diagnostics"],
        RequireProposalApproval: true,
        DeniedCapabilityIds: [],
        RetentionLabel: "Configured by administrator");
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
    DateTimeOffset CreatedAt);

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
    IReadOnlyDictionary<string, string> Metadata);
