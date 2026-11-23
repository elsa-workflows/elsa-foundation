using System.Text.Json.Serialization;
using Elsa.Weaver.Core.Models;

namespace Elsa.Weaver.Api.Models;

public sealed record WeaverProblemDetails(string Title, string Detail, int Status, string Code);

public sealed record WeaverApiResponse<T>(T? Data, WeaverError? Error)
{
    public static WeaverApiResponse<T> Success(T data) => new(data, null);

    public static WeaverApiResponse<T> Failure(WeaverError error) => new(default, error);
}

public sealed record WeaverBindingErrorResponse(
    int StatusCode,
    string Message,
    IReadOnlyDictionary<string, string[]> Errors);

public sealed record WeaverBootstrapResponse(
    bool Enabled,
    string ProviderStatus,
    IReadOnlyCollection<string> Modes,
    IReadOnlyCollection<WeaverCapabilityResponse> Capabilities,
    WeaverProviderDiagnosticsResponse? Provider,
    WeaverPolicySummaryResponse Policy)
{
    /// <summary>
    /// Preview (#3441): what a host restart does to this server's Weaver sessions: <c>durable</c> (they are recovered),
    /// <c>processLifetime</c> (they survive reloads while the process lives; a restart ends them) or <c>unsupported</c>
    /// (this composition cannot recover them).
    /// </summary>
    public string Recovery { get; init; } = "unsupported";
}

public sealed record WeaverPolicySummaryResponse(
    bool ContextVisibility,
    string DefaultAutonomyMode,
    string MaxAutonomyMode,
    IReadOnlyCollection<string> AllowedAutonomyModes,
    string? RetentionLabel);

public sealed record WeaverCapabilityResponse(
    string Id,
    string? ModuleId,
    string DisplayName,
    string Description,
    string Kind,
    string Risk,
    IReadOnlyCollection<string> Surfaces,
    IReadOnlyCollection<string> RequiredPermissions,
    bool RequiresApproval);

public sealed record WeaverProviderDiagnosticsResponse(
    string ProviderId,
    bool IsAvailable,
    string Status,
    string ProviderKind,
    IReadOnlyCollection<string> SupportedOperations,
    string RiskProfile,
    IReadOnlyDictionary<string, string> Metadata);

public sealed class WeaverCreateSessionRequest
{
    public string TenantId { get; init; } = "default";
    public string ConversationId { get; init; } = string.Empty;
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
    public string Mode { get; init; } = "explain";

    /// <summary>
    /// The autonomy mode the client requests for the session (<c>manual</c>, <c>auto-read-only</c>, or
    /// <c>full-auto</c>). The server clamps this to the deployment ceiling; an unset or unrecognized value
    /// falls back to the deployment default.
    /// </summary>
    public string? AutonomyMode { get; init; }

    public WeaverSurfaceRequest ActiveSurface { get; init; } = new();
    public WeaverClientContextRequest ClientContext { get; init; } = new();

    /// <summary>Preview: the Studio backend the session belongs to. It only narrows the owner's session list.</summary>
    public string? Workspace { get; init; }

    /// <summary>Preview: the session this one continues past a limit. Only the link is recorded; nothing is carried over.</summary>
    public string? ContinuedFrom { get; init; }
}

public sealed class WeaverSurfaceRequest
{
    public string Route { get; init; } = "/";
    public string? ResourceType { get; init; }
    public string? ResourceId { get; init; }
    public object? Selection { get; init; }
}

public sealed class WeaverClientContextRequest
{
    public string StudioVersion { get; init; } = string.Empty;
    public string SdkVersion { get; init; } = string.Empty;
    public IReadOnlyCollection<string> ModuleIds { get; init; } = [];
}

public sealed record WeaverCreateSessionResponse(
    string SessionId,
    string Status,
    string? Title,
    IReadOnlyCollection<WeaverContextAttachmentResponse> ContextAttachments)
{
    /// <summary>Preview: the session's acknowledged policy; absent only when the composition cannot read it back.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public WeaverSessionPolicyResponse? Policy { get; init; }
}

public sealed record WeaverSessionDetailsResponse(
    string SessionId,
    string Status,
    string? Title,
    IReadOnlyCollection<WeaverContextAttachmentResponse> ContextAttachments,
    IReadOnlyCollection<WeaverMessageViewModelResponse> Messages)
{
    /// <summary>Preview: the session's acknowledged policy; absent only when the composition cannot read it back.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public WeaverSessionPolicyResponse? Policy { get; init; }

    /// <summary>
    /// Preview (#3441): the documented session limit the session reached (<c>snapshots</c> or <c>bytes</c>); omitted when it
    /// reached none. A client offers continuation in a new, linked session.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LimitReached { get; init; }
}

/// <summary>
/// Preview: a session's effective autonomy, its ceiling, the modes a client may request
/// and the owner's policy revision.
/// </summary>
public sealed record WeaverSessionPolicyResponse(
    string AutonomyMode,
    string MaxAutonomyMode,
    IReadOnlyCollection<string> AllowedAutonomyModes,
    long Revision);

/// <summary>
/// Preview: an autonomy-only conditional change. Both members are required; the caller
/// cannot name an owner, actor or tenant.
/// </summary>
public sealed class WeaverSessionAutonomyRequest
{
    /// <summary><c>manual</c>, <c>auto-read-only</c> or <c>full-auto</c>; clamped to the session's current ceiling.</summary>
    public string? AutonomyMode { get; init; }

    /// <summary>The policy revision the caller last acknowledged.</summary>
    public long? ExpectedRevision { get; init; }
}

public sealed record WeaverContextAttachmentResponse(
    string Id,
    string Source,
    string? SourceId,
    string Label,
    string ContentType,
    string Sensitivity,
    string Scope,
    object? Content)
{
    public string ModuleId { get; init; } = string.Empty;
    public string SchemaVersion { get; init; } = "1";
    public WeaverResourceTarget? Target { get; init; }
    public WeaverContextOrigin Origin { get; init; }
    public WeaverClientBinding? Client { get; init; }
    public IReadOnlyCollection<string> WeaverScopes { get; init; } = [];
    public string Summary { get; init; } = string.Empty;
    public IReadOnlyDictionary<string, string> References { get; init; } = new Dictionary<string, string>();
}

public sealed record WeaverMessageViewModelResponse(
    string Id,
    string Role,
    string Content,
    string Status)
{
    /// <summary>Preview: the policy revision the owner accepted this message under, when it owns revisions.</summary>
    public long? AcceptedPolicyRevision { get; init; }

    /// <summary>
    /// Preview (#3441): the public session policy (mode, ceiling, allowed modes and revision) the owner accepted this
    /// message under; omitted when the owner did not attribute it. Revisions are per session, so a reopened session names
    /// each message's mode from this record rather than from a revision number seen elsewhere.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public WeaverSessionPolicyResponse? AcceptedPolicy { get; init; }

    /// <summary>Preview: why a cancelled message stopped (<c>interrupted</c>, <c>withdrawn</c> or <c>user-stop</c>); absent otherwise.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? StatusReason { get; init; }
}

/// <summary>Preview: the query of the owner-scoped session list (<c>GET /_elsa/weaver/sessions</c>).</summary>
public sealed class WeaverSessionListQueryRequest
{
    /// <summary>Narrows the owner's sessions to one workspace.</summary>
    public string? Workspace { get; init; }

    /// <summary>At most this many sessions, 1 to 100; 20 when absent.</summary>
    public int? Take { get; init; }

    /// <summary>The <c>next</c> cursor of a previous page; only older sessions are returned.</summary>
    public string? Before { get; init; }
}

/// <summary>Preview: one page of the caller's own sessions, newest first.</summary>
/// <param name="Next">The cursor for the next older page, or null when this page is the last.</param>
public sealed record WeaverSessionListResponse(IReadOnlyCollection<WeaverSessionSummaryResponse> Items, string? Next);

/// <summary>Preview: one of the caller's sessions, as the owning store summarizes it.</summary>
public sealed record WeaverSessionSummaryResponse(
    string Id,
    string? Title,
    string Status,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ExpiresAt,
    int PendingReviewCount,
    string? RecoveryState);

/// <summary>Preview: the owner's statement about an uncertain client operation. It is never a receipt.</summary>
public sealed class WeaverClientInvocationResolutionRequest
{
    /// <summary><c>applied</c>, <c>notApplied</c> or <c>unknown</c>.</summary>
    public string? Claim { get; init; }
}

public sealed record WeaverMessageAcceptedResponse(
    WeaverAcceptedMessageResponse Message,
    IReadOnlyCollection<WeaverViolation> Warnings);

/// <summary>
/// The accepted message as the client sees it: the members the domain <see cref="WeaverMessage"/> has always
/// serialized, with the frozen accepted policy projected to its public session-policy shape. The client never
/// receives denied capabilities, admitted context kinds, sensitivity ceilings or retention labels.
/// </summary>
public sealed record WeaverAcceptedMessageResponse(
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
    public IReadOnlyList<WeaverToolResult> ToolResults { get; init; } = [];
    public bool HasDisclosureProvenance { get; init; }
    public IReadOnlyList<WeaverDisclosureSnapshot>? DisclosureHistory { get; init; }

    /// <summary>Preview: the public policy the owner accepted this message under, or null when it does not own revisions.</summary>
    public WeaverSessionPolicyResponse? AcceptedPolicy { get; init; }

    /// <summary>Preview: why a cancelled message stopped (<c>interrupted</c>, <c>withdrawn</c> or <c>user-stop</c>).</summary>
    public string? StatusReason { get; init; }
}

public sealed class WeaverSessionRouteRequest
{
    public string SessionId { get; init; } = string.Empty;
}

public sealed class WeaverTurnCancelRequest
{
    public string SessionId { get; init; } = string.Empty;
    public string TurnId { get; init; } = string.Empty;
}

public sealed record WeaverTurnCancelResponse(string TurnId, bool Cancelled);

public sealed class WeaverMessageRequest
{
    public string SessionId { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string Content { get; init; } = string.Empty;
    public string Role { get; init; } = "user";
    public string Mode { get; init; } = "explain";
    public IReadOnlyCollection<string> ContextAttachmentIds { get; init; } = [];
    public IReadOnlyCollection<WeaverContextAttachmentRequest> ContextAttachments { get; init; } = [];
    public string? CapabilityId { get; init; }
    public IReadOnlyCollection<string> RequestedCapabilities { get; init; } = [];
    public string? RequestId { get; init; }
    public WeaverPromptSource? Source { get; init; }
    public string? PromptMode { get; init; }
    /// <summary>Current surface for this prompt. It is an untrusted admission hint, never an invocation target or authority.</summary>
    public WeaverSurfaceRequest? ActiveSurface { get; init; }
}

public sealed class WeaverContextAttachmentRequest
{
    public string Id { get; init; } = string.Empty;
    public string Kind { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Sensitivity { get; init; } = "internal";
    public string Summary { get; init; } = string.Empty;

    /// <summary>
    /// Module-owned structured caller input. Installed sources admit its schema, bound its content
    /// and establish provenance before provider use; client metadata never grants authority.
    /// </summary>
    public object? Content { get; init; }

    public IReadOnlyDictionary<string, string> References { get; init; } = new Dictionary<string, string>();
    public string Source { get; init; } = string.Empty;
    public string? SourceId { get; init; }
    public string Label { get; init; } = string.Empty;
    public string ContentType { get; init; } = string.Empty;
    public string Scope { get; init; } = "selection";
    public string ModuleId { get; init; } = string.Empty;
    public string SchemaVersion { get; init; } = "1";
    public WeaverResourceTarget? Target { get; init; }
    public WeaverClientBinding? Client { get; init; }
}

public sealed class WeaverProposalDecisionRequest
{
    public string ProposalId { get; init; } = string.Empty;
    public string ActorId { get; init; } = "studio";
    public string? Revision { get; init; }
    public string? Comment { get; init; }
    public string? Reason { get; init; }
}

public sealed record WeaverProposalDecisionResponse(
    string ProposalId,
    string ApprovalStatus,
    DateTimeOffset? ApprovedAt,
    WeaverProposalExecutionResultResponse? Result);

public sealed record WeaverProposalExecutionResultResponse(
    string ResourceType,
    string ResourceId,
    string Summary);

public sealed class WeaverMessageFeedbackRequest
{
    public string MessageId { get; init; } = string.Empty;
    public string Rating { get; init; } = string.Empty;
    public string? Comment { get; init; }
}

public sealed class WeaverFeedbackApiRequest
{
    public string SessionId { get; init; } = string.Empty;
    public string? MessageId { get; init; }
    public int Rating { get; init; }
    public string? Comment { get; init; }
    public string? ActorId { get; init; }
}

public sealed class WeaverAuditQueryRequest
{
    public string? SessionId { get; init; }
    public int? Take { get; init; }
}

public static class WeaverApiMapping
{
    public static WeaverProblemDetails ToProblem(this WeaverError error, string? title = null)
        => new(title ?? "Weaver request failed", error.Message, error.StatusCode, error.Code);

    public static WeaverCapabilityResponse ToResponse(this WeaverCapability capability)
        => new(
            capability.Id,
            capability.ModuleId,
            capability.DisplayName,
            capability.Description,
            capability.Kind.ToContractString(),
            capability.Risk.ToContractString(),
            capability.Surfaces,
            capability.RequiredPermissions,
            capability.Risk != WeaverRisk.ReadOnly);

    public static WeaverProviderDiagnosticsResponse ToResponse(this WeaverProviderDiagnostics diagnostics)
        => new(
            diagnostics.ProviderId,
            diagnostics.IsAvailable,
            diagnostics.Status,
            diagnostics.Kind.ToContractString(),
            diagnostics.SupportedOperations.Select(x => x.ToContractString()).ToList(),
            diagnostics.RiskProfile.ToContractString(),
            diagnostics.Metadata);

    public static WeaverContextAttachmentResponse ToResponse(this WeaverContextAttachment attachment)
        => new(
            attachment.Id,
            attachment.Source,
            attachment.SourceId,
            attachment.Label,
            attachment.ContentType,
            attachment.Sensitivity.ToContractString(),
            attachment.Scope,
            attachment.Content)
        {
            ModuleId = attachment.ModuleId,
            SchemaVersion = attachment.SchemaVersion,
            Target = attachment.Target,
            Origin = attachment.Origin,
            Client = attachment.Client,
            WeaverScopes = attachment.WeaverScopes,
            Summary = attachment.Summary,
            References = attachment.References
        };

    public static WeaverContextAttachment ToDomain(this WeaverContextAttachmentRequest attachment)
        => new(
            attachment.Id,
            string.IsNullOrWhiteSpace(attachment.Source) ? attachment.Kind : attachment.Source,
            attachment.SourceId,
            string.IsNullOrWhiteSpace(attachment.Label) ? attachment.DisplayName : attachment.Label,
            string.IsNullOrWhiteSpace(attachment.ContentType) ? attachment.Kind : attachment.ContentType,
            ParseSensitivity(attachment.Sensitivity),
            attachment.Scope,
            attachment.Summary,
            attachment.Content ?? attachment.Summary,
            attachment.References)
        {
            ModuleId = attachment.ModuleId,
            SchemaVersion = attachment.SchemaVersion,
            Target = attachment.Target,
            Client = attachment.Client,
            Origin = WeaverContextOrigin.ClientSnapshot,
            WeaverScopes = []
        };

    public static WeaverMessageViewModelResponse ToViewModel(this WeaverMessage message)
        => new(message.Id, message.Role.ToContractString(), message.Content, message.Status.ToContractString())
        {
            AcceptedPolicyRevision = message.AcceptedPolicy?.Revision,
            AcceptedPolicy = message.AcceptedPolicy?.ToResponse(),
            StatusReason = message.StatusReason
        };

    public static WeaverSessionSummaryResponse ToResponse(this WeaverSessionSummary summary)
        => new(summary.Id, summary.Title, summary.Status.ToContractString(), summary.UpdatedAt, summary.ExpiresAt, summary.PendingReviewCount, summary.RecoveryState);

    /// <summary>Parses a wire owner statement (<c>applied</c>, <c>notApplied</c> or <c>unknown</c>); null when unset or unrecognized.</summary>
    public static WeaverOwnerResolutionClaim? ParseResolutionClaim(string? claim) => claim?.Trim() switch
    {
        "applied" => WeaverOwnerResolutionClaim.Applied,
        "notApplied" => WeaverOwnerResolutionClaim.NotApplied,
        "unknown" => WeaverOwnerResolutionClaim.Unknown,
        _ => null
    };

    /// <summary>Projects an accepted domain message for the client; the frozen policy stays with the owner for attribution.</summary>
    public static WeaverMessageAcceptedResponse ToAcceptedResponse(this WeaverMessage message, IReadOnlyCollection<WeaverViolation> warnings)
        => new(new(
                message.Id,
                message.SessionId,
                message.Role,
                message.Content,
                message.Status,
                message.CapabilityId,
                message.CreatedAt,
                message.CompletedAt,
                message.Error,
                message.ContextAttachmentIds,
                message.ProposalIds,
                message.Context)
            {
                RequestId = message.RequestId,
                Source = message.Source,
                Client = message.Client,
                PromptMode = message.PromptMode,
                DeliveryFingerprint = message.DeliveryFingerprint,
                ToolResults = message.ToolResults,
                HasDisclosureProvenance = message.HasDisclosureProvenance,
                DisclosureHistory = message.DisclosureHistory,
                AcceptedPolicy = message.AcceptedPolicy?.ToResponse(),
                StatusReason = message.StatusReason
            },
            warnings);

    public static WeaverSessionPolicyResponse ToResponse(this WeaverSessionPolicyState state)
        => new(
            state.Policy.AutonomyMode.ToContractString(),
            state.Policy.MaxAutonomyMode.ToContractString(),
            state.Policy.AllowedAutonomyModes.Select(x => x.ToContractString()).ToList(),
            state.Revision);

    public static WeaverProposalDecisionResponse ToDecisionResponse(this WeaverActionProposal proposal, WeaverProposalExecutionResult? result = null)
        => new(
            proposal.Id,
            proposal.Status.ToContractString(),
            proposal.ApprovedAt,
            result is null ? null : new(result.ResourceType, result.ResourceId, result.Summary));

    public static string ToContractString(this WeaverSessionStatus status) => status switch
    {
        WeaverSessionStatus.Active => "active",
        WeaverSessionStatus.Completed => "completed",
        WeaverSessionStatus.Cancelled => "cancelled",
        WeaverSessionStatus.Failed => "failed",
        WeaverSessionStatus.Expired => "expired",
        _ => status.ToString().ToLowerInvariant()
    };

    public static string ToContractString(this WeaverMessageStatus status) => status switch
    {
        WeaverMessageStatus.Pending => "pending",
        WeaverMessageStatus.Streaming => "streaming",
        WeaverMessageStatus.Completed => "completed",
        WeaverMessageStatus.Failed => "failed",
        WeaverMessageStatus.Cancelled => "cancelled",
        _ => status.ToString().ToLowerInvariant()
    };

    public static string ToContractString(this WeaverRole role) => role switch
    {
        WeaverRole.User => "user",
        WeaverRole.Assistant => "assistant",
        WeaverRole.System => "system",
        WeaverRole.Tool => "tool",
        WeaverRole.Progress => "progress",
        WeaverRole.Error => "error",
        _ => role.ToString().ToLowerInvariant()
    };

    public static string ToContractString(this WeaverContextSensitivity sensitivity) => sensitivity switch
    {
        WeaverContextSensitivity.Public => "public",
        WeaverContextSensitivity.Internal => "internal",
        WeaverContextSensitivity.Sensitive => "sensitive",
        WeaverContextSensitivity.SecretRedacted => "secret-redacted",
        WeaverContextSensitivity.Secret => "secret-redacted",
        _ => sensitivity.ToString().ToLowerInvariant()
    };

    public static string ToContractString(this WeaverCapabilityKind kind) => kind switch
    {
        WeaverCapabilityKind.Answer => "answer",
        WeaverCapabilityKind.Context => "context",
        WeaverCapabilityKind.PromptStarter => "prompt-starter",
        WeaverCapabilityKind.Proposal => "proposal",
        WeaverCapabilityKind.Action => "action",
        _ => kind.ToString().ToLowerInvariant()
    };

    public static string ToContractString(this WeaverRisk risk) => risk switch
    {
        WeaverRisk.ReadOnly => "read-only",
        WeaverRisk.ReviewRequired => "review-required",
        WeaverRisk.Destructive => "destructive",
        WeaverRisk.Admin => "admin",
        _ => risk.ToString().ToLowerInvariant()
    };

    public static string ToContractString(this WeaverProviderKind kind) => kind switch
    {
        WeaverProviderKind.ProviderSdkBinding => "provider-sdk-binding",
        WeaverProviderKind.WeaverHarnessProvider => "weaver-harness-provider",
        _ => kind.ToString().ToLowerInvariant()
    };

    public static string ToContractString(this WeaverProviderOperation operation) => operation switch
    {
        WeaverProviderOperation.Chat => "chat",
        WeaverProviderOperation.Streaming => "streaming",
        WeaverProviderOperation.ToolApproval => "tool-approval",
        WeaverProviderOperation.RunStatus => "run-status",
        WeaverProviderOperation.Artifacts => "artifacts",
        WeaverProviderOperation.Skills => "skills",
        WeaverProviderOperation.Memory => "memory",
        WeaverProviderOperation.FileUpload => "file-upload",
        _ => operation.ToString().ToLowerInvariant()
    };

    public static string ToContractString(this WeaverProviderRiskProfile riskProfile) => riskProfile switch
    {
        WeaverProviderRiskProfile.ReadOnly => "read-only",
        WeaverProviderRiskProfile.ReviewRequired => "review-required",
        WeaverProviderRiskProfile.SandboxedExecution => "sandboxed-execution",
        WeaverProviderRiskProfile.PrivilegedExecution => "privileged-execution",
        _ => riskProfile.ToString().ToLowerInvariant()
    };

    public static string ToContractString(this WeaverAutonomyMode mode) => mode switch
    {
        WeaverAutonomyMode.Manual => "manual",
        WeaverAutonomyMode.AutoReadOnly => "auto-read-only",
        WeaverAutonomyMode.FullAuto => "full-auto",
        _ => mode.ToString().ToLowerInvariant()
    };

    /// <summary>Parses a wire autonomy mode (<c>manual</c>/<c>auto-read-only</c>/<c>full-auto</c>); returns null when unset or unrecognized.</summary>
    public static WeaverAutonomyMode? ParseAutonomyMode(string? mode) => mode?.Trim().ToLowerInvariant() switch
    {
        "manual" => WeaverAutonomyMode.Manual,
        "auto-read-only" => WeaverAutonomyMode.AutoReadOnly,
        "full-auto" => WeaverAutonomyMode.FullAuto,
        _ => null
    };

    public static string ToContractString(this WeaverActionProposalStatus status) => status switch
    {
        WeaverActionProposalStatus.Draft => "draft",
        WeaverActionProposalStatus.AwaitingApproval => "awaiting-approval",
        WeaverActionProposalStatus.Approved => "approved",
        WeaverActionProposalStatus.Denied => "denied",
        WeaverActionProposalStatus.Edited => "edited",
        WeaverActionProposalStatus.Expired => "expired",
        WeaverActionProposalStatus.Executed => "executed",
        WeaverActionProposalStatus.Failed => "failed",
        WeaverActionProposalStatus.Cancelled => "cancelled",
        _ => status.ToString().ToLowerInvariant()
    };

    private static WeaverContextSensitivity ParseSensitivity(string sensitivity) => sensitivity?.Trim().ToLowerInvariant() switch
    {
        "public" => WeaverContextSensitivity.Public,
        "internal" => WeaverContextSensitivity.Internal,
        "confidential" => WeaverContextSensitivity.Sensitive,
        "sensitive" => WeaverContextSensitivity.Sensitive,
        "secret-redacted" or "secretredacted" => WeaverContextSensitivity.Secret,
        "secret" => WeaverContextSensitivity.Secret,
        _ => WeaverContextSensitivity.Secret
    };
}
