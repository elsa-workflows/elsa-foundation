using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Explicit complete traversal over finite scheduler-work pages. Callers that need every current item opt into the
/// traversal rather than causing a provider to issue an unbounded queue read.
/// </summary>
public static class WorkflowSchedulerWorkQueuePagingExtensions
{
    public static async ValueTask<IReadOnlyCollection<RuntimeSchedulerWorkItem>> ListAllAsync(
        this IWorkflowSchedulerWorkQueue queue,
        string workflowExecutionId,
        CancellationToken cancellationToken = default)
        => await queue.ListAllAsync(
            new RuntimeSchedulerWorkQuery(workflowExecutionId),
            cancellationToken);

    public static async ValueTask<IReadOnlyCollection<RuntimeSchedulerWorkItem>> ListAllAsync(
        this IWorkflowSchedulerWorkQueue queue,
        RuntimeSchedulerWorkQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(queue);
        ArgumentNullException.ThrowIfNull(query);

        var items = new List<RuntimeSchedulerWorkItem>();
        var pageQuery = query;
        do
        {
            var page = await queue.ListAsync(pageQuery, cancellationToken);
            items.AddRange(page.Items);
            pageQuery = new RuntimeSchedulerWorkQuery(
                query.WorkflowExecutionId,
                query.Limit,
                page.NextContinuationToken);
        } while (pageQuery.ContinuationToken is not null);

        return items;
    }
}
