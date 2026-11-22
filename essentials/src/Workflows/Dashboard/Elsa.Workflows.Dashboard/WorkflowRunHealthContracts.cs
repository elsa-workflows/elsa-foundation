using System.Text.Json;
using System.Text.Json.Serialization;

namespace Elsa.Workflows.Dashboard;

public interface IWorkflowRunHealthDataSource
{
    ValueTask<WorkflowRunHealthAggregate> QueryAsync(
        WorkflowRunHealthDataQuery query,
        CancellationToken cancellationToken = default);

    bool IsAvailable { get; }
}

public sealed record WorkflowRunHealthDataQuery(
    WorkflowRunHealthQuery Query,
    IReadOnlyCollection<WorkflowRunHealthBucketRange> Buckets);

public sealed record WorkflowRunHealthBucketRange(int Index, DateTimeOffset From, DateTimeOffset To);

public sealed record WorkflowRunHealthAggregate(
    int StartedCount,
    int SucceededCount,
    int FailedCount,
    int CancelledCount,
    int IncompleteCount,
    int IncidentBearingRunCount,
    int IncidentCount,
    int RunningCount,
    IReadOnlyCollection<WorkflowRunHealthBucket> Buckets,
    IReadOnlyCollection<WorkflowFailureDefinitionSnapshot> HighestFailureDefinitions);

public interface IWorkflowRunHealthService
{
    ValueTask<WorkflowRunHealthSnapshot> QueryAsync(
        WorkflowRunHealthQuery query,
        CancellationToken cancellationToken = default);
}

public sealed record WorkflowRunHealthQuery(
    DateTimeOffset From,
    DateTimeOffset To,
    string TimeZone,
    WorkflowRunHealthBucketSize Bucket,
    string TenantId,
    bool IncludeTestRuns = false);

[JsonConverter(typeof(WorkflowDashboardCamelCaseEnumConverter))]
public enum WorkflowRunHealthBucketSize
{
    Hour,
    Day
}

[JsonConverter(typeof(WorkflowDashboardCamelCaseEnumConverter))]
public enum WorkflowRunHealthStatus
{
    Ready,
    Unavailable
}

public sealed record WorkflowRunHealthBucket(
    DateTimeOffset From,
    DateTimeOffset To,
    int StartedCount,
    int SucceededCount,
    int FailedCount,
    int CancelledCount,
    int IncompleteCount,
    int IncidentBearingRunCount,
    int IncidentCount);

public sealed record WorkflowFailureDefinitionSnapshot(
    string DefinitionId,
    int FailedCount);

public sealed record WorkflowRunHealthSnapshot(
    WorkflowRunHealthStatus Status,
    DateTimeOffset GeneratedAt,
    DateTimeOffset From,
    DateTimeOffset To,
    string TimeZone,
    WorkflowRunHealthBucketSize Bucket,
    bool IncludeTestRuns,
    int StartedCount,
    int SucceededCount,
    int FailedCount,
    int CancelledCount,
    int IncompleteCount,
    int IncidentBearingRunCount,
    int IncidentCount,
    int RunningCount,
    decimal FailurePercentage,
    decimal IncidentBearingPercentage,
    IReadOnlyCollection<WorkflowRunHealthBucket> Buckets,
    IReadOnlyCollection<WorkflowFailureDefinitionSnapshot> HighestFailureDefinitions,
    string? ErrorCode = null)
{
    public static WorkflowRunHealthSnapshot Unavailable(WorkflowRunHealthQuery query, DateTimeOffset generatedAt) =>
        new(
            WorkflowRunHealthStatus.Unavailable,
            generatedAt,
            query.From,
            query.To,
            query.TimeZone,
            query.Bucket,
            query.IncludeTestRuns,
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            [],
            [],
            "WORKFLOW_RUN_HEALTH_PROVIDER_UNAVAILABLE");
}

public sealed class WorkflowRunHealthQueryException(string message) : Exception(message);

public sealed class WorkflowDashboardCamelCaseEnumConverter()
    : JsonStringEnumConverter(JsonNamingPolicy.CamelCase);
