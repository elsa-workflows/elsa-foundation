using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.Logging;

namespace Elsa.Workflows.Runtime.Services.Executables;

/// <summary>Same-activation deferral (#2251): a call that the slot already names completes its activation rather than compensating it.</summary>
public sealed partial class WorkflowActivationCoordinator
{
    /// <summary>
    /// Defers a call that stopped short of its own slot transition to the activation the slot names, when that activation
    /// is the call's own (<see cref="FindSlotNamingAsync"/>): the call completes it instead of compensating it, and the
    /// result is what the call reports. <see langword="null"/> when the slot does not name it, and the call compensates.
    /// </summary>
    /// <remarks>
    /// Like <see cref="CompleteAsync"/>, the call reports <see cref="WorkflowActivationOutcome.Activated"/> with the
    /// activation it replaced when completing it switched it on or retired that activation's reference, so a caller that
    /// keeps its own record of the replaced activation, as Publishing does, retires it; a completion failure is reported
    /// as such. Otherwise the activation already served, and the call reports it already active. When neither applies,
    /// because the slot moved on or the activation's reference is no longer live, the writer that changed them owns the
    /// activation, and the call reports <paramref name="uncompensated"/> of the slot that named it.
    /// </remarks>
    /// <param name="reason">Why the call stopped, as it reads in the log after the activation and its slot.</param>
    /// <param name="failure">The failure that stopped the call, logged as a warning; without one the deferral is informational.</param>
    private async ValueTask<WorkflowActivationResult?> TryDeferToSlotAsync(
        WorkflowActivationCommand command,
        Func<WorkflowActivationSlot, WorkflowActivationResult> uncompensated,
        string reason,
        Exception? failure = null)
    {
        if (await FindSlotNamingAsync(command) is not { } slot)
            return null;

        logger?.Log(
            failure is null ? LogLevel.Information : LogLevel.Warning,
            failure,
            "Activation {ActivationId} of definition {DefinitionId} slot {SlotName} {Reason}, but the slot names it already; completing it rather than compensating",
            command.ActivationId,
            command.Executable.Identity.DefinitionId,
            command.SlotName,
            reason);

        const string leftToSlot = "The slot names this activation, so it was left to the slot rather than compensated.";
        try
        {
            return await CompleteServingActivationAsync(slot, CancellationToken.None) ??
                await TryResolveSameArtifactNoOpAsync(command, command.Executable.Identity.ArtifactId, CancellationToken.None) ??
                Uncompensated(leftToSlot);
        }
        catch (Exception exception)
        {
            return Uncompensated($"{leftToSlot} {SafeMessage(exception)}");
        }

        WorkflowActivationResult Uncompensated(string note)
        {
            var result = uncompensated(slot);
            return result with { Diagnostic = Truncate(Join(result.Diagnostic, note)) };
        }
    }

    /// <summary>
    /// The slot, when it names this call's activation and that activation's projections are stored in every projection
    /// store, although this call has not moved the slot there: its source reference and projections are then the slot's,
    /// and compensating this call would take them from it (#2251).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Calls that activate the same artifact through the same source, such as two nodes reconciling one mounted set, share
    /// one activation id, and with it one source reference and one set of projections. The call that wins the slot serves
    /// through them, and the other cannot tell them from its own: compensating it would delete the winner's projections
    /// and retire its reference, leaving the slot naming an activation that serves nothing. A call whose own slot
    /// transition may have committed before it threw is in the same position, and is treated the same way.
    /// </para>
    /// <para>
    /// Projections stored in every store tell such a call apart from a retry of the slot's activation whose earlier
    /// compensation removed them and whose own preparation failed: that retry has nothing to complete, so it is still
    /// compensated rather than reported already active while nothing serves. When the projection state cannot be read,
    /// the call does not compensate either; completion reads it again and fails loudly if it still cannot. When the slot
    /// cannot be read, the call compensates as before.
    /// </para>
    /// <para>
    /// The slot and the projection state are read here, before the call compensates, not with it. A call that reads them
    /// before another call's slot transition lands compensates the shared activation; see the Runtime extension points.
    /// </para>
    /// </remarks>
    private async ValueTask<WorkflowActivationSlot?> FindSlotNamingAsync(WorkflowActivationCommand command)
    {
        var slot = await CurrentSlotAsync(command.Executable.Identity.DefinitionId, command.SlotName);
        if (!StringComparer.Ordinal.Equals(slot.ActiveActivationId, command.ActivationId))
            return null;

        try
        {
            var state = await ReadOccupantAsync(command.ActivationId, CancellationToken.None);
            return state.Triggers == WorkflowActivationProjectionState.Missing || state.Schedules == WorkflowActivationProjectionState.Missing
                ? null
                : slot;
        }
        catch (Exception exception)
        {
            logger?.LogWarning(
                exception,
                "The projections of activation {ActivationId}, which definition {DefinitionId} slot {SlotName} names, could not be read; it is left to the slot rather than compensated",
                command.ActivationId,
                slot.WorkflowDefinitionId,
                slot.SlotName);
            return slot;
        }
    }

    /// <summary>What a cancelled call would report when it defers; it rethrows its cancellation instead.</summary>
    private static WorkflowActivationResult Cancelled(WorkflowActivationSlot slot) =>
        new(false, WorkflowActivationOutcome.Failed, slot, Diagnostic: "The activation was cancelled.");
}
