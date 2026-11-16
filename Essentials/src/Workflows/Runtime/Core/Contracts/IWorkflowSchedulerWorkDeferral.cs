namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Optional queue capability that delays one queued item by its identity, wherever it sits in its execution's FIFO.
/// </summary>
/// <remarks>
/// The item ends as a release of its claim leaves it: unclaimed, hidden until the given instant, and with its fencing
/// token advanced so a lapsed claimant can no longer complete or consume it. Strict FIFO is unchanged: a delayed head
/// hides every later item of its execution until it is due, and items ahead of a delayed item stay claimable. The
/// coalescing working set uses it under the workflow execution's single-writer lease to persist a delay it recorded,
/// because the durable head may by then be work another writer enqueued (#3690). It does not acquire ownership.
/// </remarks>
public interface IWorkflowSchedulerWorkDeferral
{
    /// <summary>
    /// Hides the item until <paramref name="visibleAt"/>. Returns <see langword="false"/>, changing nothing, when the
    /// item is not queued or a claim that has not lapsed at <paramref name="now"/> holds it.
    /// </summary>
    ValueTask<bool> DeferAsync(
        string workflowExecutionId,
        string workItemId,
        DateTimeOffset now,
        DateTimeOffset visibleAt,
        CancellationToken cancellationToken = default);
}
