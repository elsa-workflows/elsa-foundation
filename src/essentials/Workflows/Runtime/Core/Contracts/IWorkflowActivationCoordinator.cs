using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Runtime-owned entry point for the complete activation and deactivation lifecycle.
/// </summary>
/// <remarks>
/// A caller-requested cancellation is rethrown after best-effort compensation whenever a lifecycle write may have
/// run. Compensation uses an uncancelled token so cancellation cannot leave the slot, projections, and references
/// split. Cancellation observed before the first write performs no lifecycle mutation.
/// </remarks>
public interface IWorkflowActivationCoordinator
{
    ValueTask<WorkflowActivationResult> ActivateAsync(
        WorkflowActivationCommand command,
        CancellationToken cancellationToken = default);

    ValueTask<WorkflowActivationResult> DeactivateAsync(
        WorkflowDeactivationCommand command,
        CancellationToken cancellationToken = default);
}
