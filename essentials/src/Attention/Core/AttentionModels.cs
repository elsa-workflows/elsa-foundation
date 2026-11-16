using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Elsa.Attention.Core;

public sealed record AttentionQueryContext(ClaimsPrincipal Principal, string? TenantId);

public sealed record AttentionQuery(
    AttentionQueryContext Context,
    IReadOnlyCollection<string>? ContributorIds = null);

public sealed record AttentionContributorDescriptor(
    string Id,
    string DisplayName,
    string? RequiredPermission,
    IReadOnlyCollection<AttentionThresholdDescriptor>? Thresholds = null);

public sealed record AttentionThresholdDescriptor(
    string Key,
    string DisplayName,
    string Description,
    string DefaultValue);

public sealed record AttentionDestination(string Path, string Label);

public sealed record AttentionCorrelation(string Kind, string Value);

[JsonConverter(typeof(AttentionCamelCaseEnumConverter))]
public enum AttentionSeverity
{
    Critical,
    Warning,
    Info
}

[JsonConverter(typeof(AttentionCamelCaseEnumConverter))]
public enum AttentionSensitivity
{
    Metadata,
    Restricted
}

public sealed record AttentionItem(
    string Id,
    string Generation,
    AttentionSeverity Severity,
    string Title,
    string? Summary,
    DateTimeOffset OccurredAt,
    DateTimeOffset LastObservedAt,
    int Count,
    AttentionDestination Destination,
    IReadOnlyCollection<AttentionCorrelation> Correlations,
    AttentionSensitivity Sensitivity);

public sealed record AttentionContribution(
    AttentionContributionStatus Status,
    IReadOnlyCollection<AttentionItem> Items,
    int TotalCount,
    bool IsTruncated,
    string? ErrorCode = null,
    string? Detail = null,
    AttentionDestination? DiagnosticDestination = null)
{
    public static AttentionContribution Ready(
        IReadOnlyCollection<AttentionItem> items,
        int? totalCount = null,
        bool isTruncated = false) =>
        new(AttentionContributionStatus.Ready, items, totalCount ?? items.Count, isTruncated);

    public static AttentionContribution Unavailable(
        string errorCode,
        string? detail = null,
        AttentionDestination? diagnosticDestination = null) =>
        new(AttentionContributionStatus.Unavailable, [], 0, false, errorCode, detail, diagnosticDestination);
}

public enum AttentionContributionStatus
{
    Ready,
    Unavailable
}

[JsonConverter(typeof(AttentionCamelCaseEnumConverter))]
public enum AttentionContributorStatus
{
    Ready,
    Forbidden,
    Unavailable,
    TimedOut,
    Failed
}

public sealed record AttentionContributorResult(
    string ContributorId,
    string DisplayName,
    AttentionContributorStatus Status,
    DateTimeOffset EvaluatedAt,
    int TotalCount,
    bool IsTruncated,
    IReadOnlyCollection<AttentionItem> Items,
    string? ErrorCode = null,
    string? Detail = null,
    AttentionDestination? DiagnosticDestination = null);

public sealed record AttentionAggregationResult(
    DateTimeOffset GeneratedAt,
    IReadOnlyCollection<AttentionContributorResult> Contributors);

public sealed class AttentionCamelCaseEnumConverter()
    : JsonStringEnumConverter(JsonNamingPolicy.CamelCase);
