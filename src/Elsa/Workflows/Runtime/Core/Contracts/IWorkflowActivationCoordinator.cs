using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Runtime-owned entry point for the complete activation and deactivation lifecycle.
/// </summary>
public interface IWorkflowActivationCoordinator
{
    ValueTask<WorkflowActivationResult> ActivateAsync(
        WorkflowActivationCommand command,
        CancellationToken cancellationToken = default);

    ValueTask<WorkflowActivationResult> DeactivateAsync(
        WorkflowDeactivationCommand command,
        CancellationToken cancellationToken = default);
}
