using Elsa.Events.Core.Contracts;
using Elsa.Workflows.Design.Validations.Core;

namespace Elsa.Workflows.Dashboard;

public sealed class WorkflowPortfolioService(
    IWorkflowPortfolioDataSource dataSource,
    IInlineEventPublisher eventPublisher,
    WorkflowPortfolioOptions options,
    TimeProvider? timeProvider = null) : IWorkflowPortfolioService
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async ValueTask<WorkflowPortfolioSnapshot> QueryAsync(
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
            throw new WorkflowRunHealthQueryException("An authenticated tenant scope is required.");
        if (options.MaximumConcurrentValidations <= 0)
            throw new InvalidOperationException("Portfolio validation concurrency must be positive.");

        var generatedAt = _timeProvider.GetUtcNow();
        if (!dataSource.IsAvailable)
            return new(WorkflowPortfolioStatus.Unavailable, generatedAt, 0, 0, 0, 0, "WORKFLOW_PORTFOLIO_PROVIDER_UNAVAILABLE");

        var counts = await dataSource.QueryBaseCountsAsync(tenantId, generatedAt, cancellationToken);
        var invalid = 0;
        await Parallel.ForEachAsync(
            dataSource.StreamCurrentDraftsAsync(tenantId, cancellationToken),
            new ParallelOptions
            {
                MaxDegreeOfParallelism = options.MaximumConcurrentValidations,
                CancellationToken = cancellationToken
            },
            async (draft, token) =>
            {
                if ((await eventPublisher.TryDeriveValidationErrorsAsync(draft, token)).Count > 0)
                    Interlocked.Increment(ref invalid);
            });

        return new(
            WorkflowPortfolioStatus.Ready,
            generatedAt,
            counts.ActiveDefinitionCount,
            counts.PublishedDefinitionCount,
            counts.UnpublishedDraftCount,
            invalid);
    }
}
