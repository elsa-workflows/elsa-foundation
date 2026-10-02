using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Runtime-owned entry point for the complete activation and deactivation lifecycle.
/// </summary>
/// <remarks>
/// <para>
/// The slot transition, the projection switch and the replaced activation's reference retirement are one commit of the
/// <see cref="IWorkflowActivationSwitch"/> (#2230), so a process that stops at any point leaves them agreeing: before the
/// commit nothing serves differently, and after it the activation is whole.
/// </para>
/// <para>
/// A caller-requested cancellation is rethrown. One observed before the switch commits is rethrown after the call's
/// activation is discarded, with an uncancelled token, unless it serves because a call sharing its activation id switched
/// it on. One observed after the switch commits leaves the activation activated, as a process that stopped there would:
/// the caller learns only that it stopped, and a caller that keeps its own record of the activation brings it into line
/// as it would after a crash. Cancellation observed before the first write performs no lifecycle mutation of its own.
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
    /// Reports whether the activation the slot names serves. Since #2230 there is nothing to complete, because a slot and
    /// its projections switch in one commit; this writes nothing. A slot that a version before #2230 left half done, naming
    /// an activation that does not serve, is reported as failed with the remedy, so nothing builds on it.
    /// </summary>
    /// <returns>
    /// <see cref="WorkflowActivationOutcome.AlreadyActive"/> when the slot's activation serves;
    /// <see cref="WorkflowActivationOutcome.AlreadyInactive"/> for an empty slot; and
    /// <see cref="WorkflowActivationOutcome.Failed"/> at <see cref="WorkflowActivationStep.ProjectionActivation"/> for a
    /// slot left half done, or when whether it serves could not be read.
    /// </returns>
    ValueTask<WorkflowActivationResult> CompleteAsync(
        string workflowDefinitionId,
        string slotName,
        CancellationToken cancellationToken = default);
}
