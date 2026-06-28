using Elsa.Agent.Core.Models;

namespace Elsa.Agent.Api.Models;

public sealed record AgentProblemDetails(string Title, string Detail, int Status, string Code);

public sealed record AgentApiResponse<T>(T? Data, AgentError? Error)
{
    public static AgentApiResponse<T> Success(T data) => new(data, null);

    public static AgentApiResponse<T> Failure(AgentError error) => new(default, error);
}

public sealed record AgentBootstrapResponse(
    bool Enabled,
    string ProviderStatus,
    IReadOnlyCollection<string> Modes,
    IReadOnlyCollection<AgentCapabilityResponse> Capabilities,
    AgentProviderDiagnosticsResponse? Provider,
    AgentPolicySummaryResponse Policy);

public sealed record AgentPolicySummaryResponse(
    bool ContextVisibility,
    bool RequiresApprovalForMutations,
    string? RetentionLabel);

public sealed record AgentCapabilityResponse(
    string Id,
    string? ModuleId,
    string DisplayName,
    string Description,
    string Kind,
    string Risk,
    IReadOnlyCollection<string> Surfaces,
    IReadOnlyCollection<string> RequiredPermissions,
    bool RequiresApproval);

public sealed record AgentProviderDiagnosticsResponse(
    string ProviderId,
    bool IsAvailable,
    string Status,
    string ProviderKind,
    IReadOnlyCollection<string> SupportedOperations,
    string RiskProfile,
    IReadOnlyDictionary<string, string> Metadata);

public sealed class AgentCreateSessionRequest
{
    public string TenantId { get; init; } = "default";
    public string ConversationId { get; init; } = string.Empty;
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
    public string Mode { get; init; } = "explain";
    public AgentSurfaceRequest ActiveSurface { get; init; } = new();
    public AgentClientContextRequest ClientContext { get; init; } = new();
}

public sealed class AgentSurfaceRequest
{
    public string Route { get; init; } = "/";
    public string? ResourceType { get; init; }
    public string? ResourceId { get; init; }
    public object? Selection { get; init; }
}

public sealed class AgentClientContextRequest
{
    public string StudioVersion { get; init; } = string.Empty;
    public string SdkVersion { get; init; } = string.Empty;
    public IReadOnlyCollection<string> ModuleIds { get; init; } = [];
}

public sealed record AgentCreateSessionResponse(
    string SessionId,
    string Status,
    string? Title,
    IReadOnlyCollection<AgentContextAttachmentResponse> ContextAttachments);

public sealed record AgentSessionDetailsResponse(
    string SessionId,
    string Status,
    string? Title,
    IReadOnlyCollection<AgentContextAttachmentResponse> ContextAttachments,
    IReadOnlyCollection<AgentMessageViewModelResponse> Messages);

public sealed record AgentContextAttachmentResponse(
    string Id,
    string Source,
    string? SourceId,
    string Label,
    string ContentType,
    string Sensitivity,
    string Scope,
    object? Content);

public sealed record AgentMessageViewModelResponse(
    string Id,
    string Role,
    string Content,
    string Status);

public sealed record AgentMessageAcceptedResponse(
    AgentMessage Message,
    IReadOnlyCollection<AgentViolation> Warnings);

public sealed class AgentSessionRouteRequest
{
    public string SessionId { get; init; } = string.Empty;
}

public sealed class AgentTurnCancelRequest
{
    public string SessionId { get; init; } = string.Empty;
    public string TurnId { get; init; } = string.Empty;
}

public sealed record AgentTurnCancelResponse(string TurnId, bool Cancelled);

public sealed class AgentMessageRequest
{
    public string SessionId { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string Content { get; init; } = string.Empty;
    public string Role { get; init; } = "user";
    public string Mode { get; init; } = "explain";
    public IReadOnlyCollection<string> ContextAttachmentIds { get; init; } = [];
    public IReadOnlyCollection<AgentContextAttachmentRequest> ContextAttachments { get; init; } = [];
    public string? CapabilityId { get; init; }
    public IReadOnlyCollection<string> RequestedCapabilities { get; init; } = [];
}

public sealed class AgentContextAttachmentRequest
{
    public string Id { get; init; } = string.Empty;
    public string Kind { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Sensitivity { get; init; } = "internal";
    public string Summary { get; init; } = string.Empty;
    public IReadOnlyDictionary<string, string> References { get; init; } = new Dictionary<string, string>();
}

public sealed record AgentMessageResponse(
    string MessageId,
    string Status,
    string StreamUrl);

public sealed class AgentProposalDecisionRequest
{
    public string ProposalId { get; init; } = string.Empty;
    public string ActorId { get; init; } = "studio";
    public string? Revision { get; init; }
    public string? Comment { get; init; }
    public string? Reason { get; init; }
}

public sealed record AgentProposalDecisionResponse(
    string ProposalId,
    string ApprovalStatus,
    DateTimeOffset? ApprovedAt,
    AgentProposalExecutionResultResponse? Result);

public sealed record AgentProposalExecutionResultResponse(
    string ResourceType,
    string ResourceId,
    string Summary);

public sealed class AgentMessageFeedbackRequest
{
    public string MessageId { get; init; } = string.Empty;
    public string Rating { get; init; } = string.Empty;
    public string? Comment { get; init; }
}

public sealed class AgentFeedbackApiRequest
{
    public string SessionId { get; init; } = string.Empty;
    public string? MessageId { get; init; }
    public int Rating { get; init; }
    public string? Comment { get; init; }
    public string? ActorId { get; init; }
}

public sealed class AgentAuditQueryRequest
{
    public string? SessionId { get; init; }
    public int? Take { get; init; }
}

internal static class AgentApiMapping
{
    public static AgentProblemDetails ToProblem(this AgentError error, string? title = null)
        => new(title ?? "Agent request failed", error.Message, error.StatusCode, error.Code);

    public static AgentCapabilityResponse ToResponse(this AgentCapability capability)
        => new(
            capability.Id,
            capability.ModuleId,
            capability.DisplayName,
            capability.Description,
            capability.Kind.ToContractString(),
            capability.Risk.ToContractString(),
            capability.Surfaces,
            capability.RequiredPermissions,
            capability.Risk != AgentRisk.ReadOnly);

    public static AgentProviderDiagnosticsResponse ToResponse(this AgentProviderDiagnostics diagnostics)
        => new(
            diagnostics.ProviderId,
            diagnostics.IsAvailable,
            diagnostics.Status,
            diagnostics.Kind.ToContractString(),
            diagnostics.SupportedOperations.Select(x => x.ToContractString()).ToList(),
            diagnostics.RiskProfile.ToContractString(),
            diagnostics.Metadata);

    public static AgentContextAttachmentResponse ToResponse(this AgentContextAttachment attachment)
        => new(
            attachment.Id,
            attachment.Source,
            attachment.SourceId,
            attachment.Label,
            attachment.ContentType,
            attachment.Sensitivity.ToContractString(),
            attachment.Scope,
            attachment.Content);

    public static AgentContextAttachment ToDomain(this AgentContextAttachmentRequest attachment)
        => new(
            attachment.Id,
            string.IsNullOrWhiteSpace(attachment.Kind) ? "context" : attachment.Kind,
            attachment.References.TryGetValue("sourceId", out var sourceId) ? sourceId : null,
            attachment.DisplayName,
            string.IsNullOrWhiteSpace(attachment.Kind) ? "summary" : attachment.Kind,
            ParseSensitivity(attachment.Sensitivity),
            attachment.References.TryGetValue("scope", out var scope) ? scope : "selection",
            attachment.Summary,
            attachment.Summary,
            attachment.References);

    public static AgentMessageViewModelResponse ToViewModel(this AgentMessage message)
        => new(message.Id, message.Role.ToContractString(), message.Content, message.Status.ToContractString());

    public static AgentProposalDecisionResponse ToDecisionResponse(this AgentActionProposal proposal, AgentProposalExecutionResult? result = null)
        => new(
            proposal.Id,
            proposal.Status.ToContractString(),
            proposal.ApprovedAt,
            result is null ? null : new(result.ResourceType, result.ResourceId, result.Summary));

    public static string ToContractString(this AgentSessionStatus status) => status switch
    {
        AgentSessionStatus.Active => "active",
        AgentSessionStatus.Completed => "completed",
        AgentSessionStatus.Cancelled => "cancelled",
        AgentSessionStatus.Failed => "failed",
        AgentSessionStatus.Expired => "expired",
        _ => status.ToString().ToLowerInvariant()
    };

    public static string ToContractString(this AgentMessageStatus status) => status switch
    {
        AgentMessageStatus.Pending => "pending",
        AgentMessageStatus.Streaming => "streaming",
        AgentMessageStatus.Completed => "completed",
        AgentMessageStatus.Failed => "failed",
        AgentMessageStatus.Cancelled => "cancelled",
        _ => status.ToString().ToLowerInvariant()
    };

    public static string ToContractString(this AgentRole role) => role switch
    {
        AgentRole.User => "user",
        AgentRole.Assistant => "assistant",
        AgentRole.System => "system",
        AgentRole.Tool => "tool",
        AgentRole.Progress => "progress",
        AgentRole.Error => "error",
        _ => role.ToString().ToLowerInvariant()
    };

    public static string ToContractString(this AgentContextSensitivity sensitivity) => sensitivity switch
    {
        AgentContextSensitivity.Public => "public",
        AgentContextSensitivity.Internal => "internal",
        AgentContextSensitivity.Sensitive => "sensitive",
        AgentContextSensitivity.SecretRedacted => "secret-redacted",
        AgentContextSensitivity.Secret => "secret-redacted",
        _ => sensitivity.ToString().ToLowerInvariant()
    };

    public static string ToContractString(this AgentCapabilityKind kind) => kind switch
    {
        AgentCapabilityKind.Answer => "answer",
        AgentCapabilityKind.Context => "context",
        AgentCapabilityKind.PromptStarter => "prompt-starter",
        AgentCapabilityKind.Proposal => "proposal",
        AgentCapabilityKind.Action => "action",
        _ => kind.ToString().ToLowerInvariant()
    };

    public static string ToContractString(this AgentRisk risk) => risk switch
    {
        AgentRisk.ReadOnly => "read-only",
        AgentRisk.ReviewRequired => "review-required",
        AgentRisk.Destructive => "destructive",
        AgentRisk.Admin => "admin",
        _ => risk.ToString().ToLowerInvariant()
    };

    public static string ToContractString(this AgentProviderKind kind) => kind switch
    {
        AgentProviderKind.ProviderSdkBinding => "provider-sdk-binding",
        AgentProviderKind.AgentHarnessProvider => "agent-harness-provider",
        _ => kind.ToString().ToLowerInvariant()
    };

    public static string ToContractString(this AgentProviderOperation operation) => operation switch
    {
        AgentProviderOperation.Chat => "chat",
        AgentProviderOperation.Streaming => "streaming",
        AgentProviderOperation.ToolApproval => "tool-approval",
        AgentProviderOperation.RunStatus => "run-status",
        AgentProviderOperation.Artifacts => "artifacts",
        AgentProviderOperation.Skills => "skills",
        AgentProviderOperation.Memory => "memory",
        AgentProviderOperation.FileUpload => "file-upload",
        _ => operation.ToString().ToLowerInvariant()
    };

    public static string ToContractString(this AgentProviderRiskProfile riskProfile) => riskProfile switch
    {
        AgentProviderRiskProfile.ReadOnly => "read-only",
        AgentProviderRiskProfile.ReviewRequired => "review-required",
        AgentProviderRiskProfile.SandboxedExecution => "sandboxed-execution",
        AgentProviderRiskProfile.PrivilegedExecution => "privileged-execution",
        _ => riskProfile.ToString().ToLowerInvariant()
    };

    public static string ToContractString(this AgentActionProposalStatus status) => status switch
    {
        AgentActionProposalStatus.Draft => "draft",
        AgentActionProposalStatus.AwaitingApproval => "awaiting-approval",
        AgentActionProposalStatus.Approved => "approved",
        AgentActionProposalStatus.Denied => "denied",
        AgentActionProposalStatus.Edited => "edited",
        AgentActionProposalStatus.Expired => "expired",
        AgentActionProposalStatus.Executed => "executed",
        AgentActionProposalStatus.Failed => "failed",
        AgentActionProposalStatus.Cancelled => "cancelled",
        _ => status.ToString().ToLowerInvariant()
    };

    private static AgentContextSensitivity ParseSensitivity(string sensitivity) => sensitivity.Trim().ToLowerInvariant() switch
    {
        "public" => AgentContextSensitivity.Public,
        "internal" => AgentContextSensitivity.Internal,
        "confidential" => AgentContextSensitivity.Sensitive,
        "sensitive" => AgentContextSensitivity.Sensitive,
        "secret-redacted" => AgentContextSensitivity.SecretRedacted,
        "secret" => AgentContextSensitivity.Secret,
        _ => AgentContextSensitivity.Internal
    };
}
