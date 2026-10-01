using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Runtime-owned entry point for the complete activation and deactivation lifecycle.
/// </summary>
/// <remarks>
/// <para>
/// A caller-requested cancellation is rethrown after best-effort compensation whenever a lifecycle write may have
/// run. Compensation uses an uncancelled token so cancellation cannot leave the slot, projections, and references
/// split. Cancellation observed before the first write performs no lifecycle mutation of its own.
/// </para>
/// <para>
/// The slot transition commits before the projections switch, so a process that dies between the two leaves the
/// slot naming an activation whose trigger bindings and recurring schedules still serve nothing, or still serve the
/// activation it replaced. Every activation and deactivation therefore first completes the activation the slot
/// already names (see <see cref="CompleteAsync"/>); a same-artifact request reports
/// <see cref="WorkflowActivationOutcome.AlreadyActive"/> only once that activation serves.
/// </para>
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
    /// Finishes the activation the slot names when an interrupted call left it half-done: switches its projections
    /// on and the replaced activation's off, notifies trigger observers, and retires the replaced activation's
    /// source reference. Changes nothing when the activation already serves.
    /// </summary>
    /// <returns>
    /// <see cref="WorkflowActivationOutcome.Activated"/> when this call completed the activation, naming the activation
    /// it replaced; <see cref="WorkflowActivationOutcome.AlreadyActive"/> when there was nothing to complete or another
    /// writer moved the slot first; <see cref="WorkflowActivationOutcome.AlreadyInactive"/> for an empty slot; and
    /// <see cref="WorkflowActivationOutcome.Failed"/> when the activation could not be completed.
    /// </returns>
    ValueTask<WorkflowActivationResult> CompleteAsync(
        string workflowDefinitionId,
        string slotName,
        CancellationToken cancellationToken = default);
}
