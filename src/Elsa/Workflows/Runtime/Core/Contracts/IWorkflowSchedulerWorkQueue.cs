using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Stores scheduler work recorded by execution agents, isolated by workflow execution ID.
/// </summary>
public interface IWorkflowSchedulerWorkQueue
{
    ValueTask<RuntimeSchedulerWorkItem> EnqueueAsync(RuntimeSchedulerWorkItem workItem, CancellationToken cancellationToken = default);
    ValueTask<IReadOnlyCollection<RuntimeSchedulerWorkItem>> ListAsync(RuntimeSchedulerWorkQuery query, CancellationToken cancellationToken = default);
    ValueTask<RuntimeSchedulerWorkItem?> DequeueAsync(string workflowExecutionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the distinct workflow execution IDs that currently have pending scheduler work, ordered
    /// deterministically (ordinal). Used by system-wide resumption sweeps to discover executions whose
    /// queued work survived a process restart and would otherwise never be drained.
    /// </summary>
    ValueTask<IReadOnlyCollection<string>> ListPendingWorkflowExecutionIdsAsync(int limit, CancellationToken cancellationToken = default);
}
