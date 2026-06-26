using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Coordinates command-triggered workflow execution draining after scheduler work has been recorded.
/// </summary>
public interface IWorkflowExecutionDrainCoordinator
{
    ValueTask<RuntimeSchedulerDrainResult> DrainAsync(
        WorkflowExecutionCommandEnvelope envelope,
        RuntimeSchedulerDrainRequest request,
        CancellationToken cancellationToken = default);
}
