using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Runtime-owned entry point for the complete activation and deactivation lifecycle.
/// </summary>
/// <remarks>
/// Every call moves its slot through one commit of <see cref="IWorkflowActivationSwitch"/>, whose documentation describes
/// what a refusal, a failure, a cancellation or a stopped process leaves behind.
/// </remarks>
public interface IWorkflowActivationCoordinator
{
    ValueTask<WorkflowActivationResult> ActivateAsync(
        WorkflowActivationCommand command,
        CancellationToken cancellationToken = default);

    ValueTask<WorkflowActivationResult> DeactivateAsync(
        WorkflowDeactivationCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Makes sure the activation the slot names serves, and writes nothing when it does. A slot a version before #2230 left
    /// on its way to serving is repaired (<see cref="IWorkflowActivationSwitch.TryRepairAsync"/>) and the trigger
    /// observers are told, as of an activation, without failing on them. Any other slot whose activation does not serve is
    /// reported as failed, naming how to clear it, so nothing builds on it. <see cref="ActivateAsync"/> does the same first.
    /// </summary>
    /// <returns>
    /// <see cref="WorkflowActivationOutcome.AlreadyActive"/> when the slot's activation serves, repaired or not;
    /// <see cref="WorkflowActivationOutcome.AlreadyInactive"/> for an empty slot; and
    /// <see cref="WorkflowActivationOutcome.Failed"/> at <see cref="WorkflowActivationStep.ProjectionActivation"/> for a
    /// slot whose activation does not serve and cannot be repaired, or when whether it serves could not be read.
    /// </returns>
    ValueTask<WorkflowActivationResult> EnsureServingAsync(
        string workflowDefinitionId,
        string slotName,
        CancellationToken cancellationToken = default);
}
