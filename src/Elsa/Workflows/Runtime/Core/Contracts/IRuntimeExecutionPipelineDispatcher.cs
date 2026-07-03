using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Dispatches a drained scheduler work item through the runtime execution pipeline appropriate to its kind, running
/// the selected work handler as the pipeline's inner terminal delegate ("wrap, do not replace"). This is the single
/// seam the drainer invokes to make the pipeline the real execution spine (ADR 0029, Move 1).
/// </summary>
public interface IRuntimeExecutionPipelineDispatcher
{
    /// <summary>
    /// Selects the pipeline for <paramref name="workItem"/>, builds its context, and invokes the pipeline around
    /// <c>() => handler.HandleAsync(workItem, cancellationToken)</c>.
    /// </summary>
    ValueTask DispatchAsync(
        RuntimeSchedulerWorkItem workItem,
        IWorkflowSchedulerWorkHandler handler,
        CancellationToken cancellationToken = default);
}
