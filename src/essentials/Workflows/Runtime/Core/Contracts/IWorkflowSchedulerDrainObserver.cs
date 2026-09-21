using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Observes scheduler drain results produced by command processing.
/// </summary>
public interface IWorkflowSchedulerDrainObserver
{
    ValueTask OnDrainedAsync(
        WorkflowExecutionCommandEnvelope envelope,
        RuntimeSchedulerDrainResult result,
        CancellationToken cancellationToken = default);
}
