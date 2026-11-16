using Elsa.Workflows.Design.Persistence.Core.Entities;

namespace Elsa.Workflows.Dashboard;

public interface IWorkflowPortfolioDataSource
{
    bool IsAvailable { get; }

    ValueTask<WorkflowPortfolioBaseCounts> QueryBaseCountsAsync(
        string tenantId,
        DateTimeOffset asOf,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<WorkflowDefinitionDraft> StreamCurrentDraftsAsync(
        string tenantId,
        CancellationToken cancellationToken = default);
}

public interface IWorkflowPortfolioService
{
    ValueTask<WorkflowPortfolioSnapshot> QueryAsync(string tenantId, CancellationToken cancellationToken = default);
}

public sealed record WorkflowPortfolioBaseCounts(
    int ActiveDefinitionCount,
    int PublishedDefinitionCount,
    int UnpublishedDraftCount);

public sealed record WorkflowPortfolioSnapshot(
    WorkflowPortfolioStatus Status,
    DateTimeOffset GeneratedAt,
    int ActiveDefinitionCount,
    int PublishedDefinitionCount,
    int UnpublishedDraftCount,
    int InvalidDraftCount,
    string? ErrorCode = null);

[System.Text.Json.Serialization.JsonConverter(typeof(WorkflowDashboardCamelCaseEnumConverter))]
public enum WorkflowPortfolioStatus
{
    Ready,
    Unavailable
}

public sealed class WorkflowPortfolioOptions
{
    public int MaximumConcurrentValidations { get; set; } = 4;
}
