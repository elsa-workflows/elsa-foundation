using System.Runtime.ExceptionServices;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.Logging;

namespace Elsa.Workflows.Runtime.Services.Executables;

/// <summary>
/// Abandoning a call that stopped short of a switch it knows it made: its activation is discarded unless it serves, by the
/// rules of <see cref="Core.Contracts.IWorkflowActivationSwitch.TryDiscardAsync"/> (#2251, #2230).
/// </summary>
public sealed partial class WorkflowActivationCoordinator
{
    /// <summary>
    /// Abandons a call that failed, or was cancelled, at <paramref name="failedStep"/>. A cancellation is rethrown once the
    /// call is abandoned; a failure is answered as <see cref="WorkflowActivationOutcome.Failed"/> at that step, unless its
    /// activation serves.
    /// </summary>
    /// <param name="slotBeforeTransition">The slot as the call read it before a switch that threw, which may have committed.</param>
    private async ValueTask<WorkflowActivationResult> AbandonAsync(
        WorkflowActivationCommand command,
        WorkflowExecutableSourceReference reference,
        CancellationToken cancellationToken,
        Exception exception,
        WorkflowActivationStep failedStep,
        WorkflowActivationSlot? slotBeforeTransition = null)
    {
        var cancelled = !NotRequestedCancellation(exception, cancellationToken);
        var result = await AbandonAsync(
            command,
            reference,
            slot => new(false, WorkflowActivationOutcome.Failed, slot, Diagnostic: Truncate(SafeMessage(exception)), FailedStep: failedStep),
            cancelled ? "was cancelled" : $"failed at step {failedStep}",
            slotBeforeTransition,
            cancelled ? null : exception);
        if (cancelled)
            ExceptionDispatchInfo.Throw(exception);
        return result;
    }

    /// <summary>
    /// Discards the call's activation unless it serves, and answers <paramref name="failed"/> of the slot as it then stands.
    /// An activation that serves is left to the slot: the call reports it already active when the slot names it, or
    /// activated when the switch that made it serve is the one <paramref name="slotBeforeTransition"/> saw this call
    /// attempt. A discard that fails is reported as a compensation failure, and the call's own failure stands.
    /// </summary>
    /// <param name="reason">Why the call stopped, as it reads in the log after the activation and its slot.</param>
    /// <param name="failure">The failure that stopped the call, logged as a warning; without one the log is informational.</param>
    private async ValueTask<WorkflowActivationResult> AbandonAsync(
        WorkflowActivationCommand command,
        WorkflowExecutableSourceReference reference,
        Func<WorkflowActivationSlot, WorkflowActivationResult> failed,
        string reason,
        WorkflowActivationSlot? slotBeforeTransition = null,
        Exception? failure = null)
    {
        var definitionId = command.Executable.Identity.DefinitionId;
        bool discarded;
        try
        {
            discarded = await activationSwitch.TryDiscardAsync(reference, CancellationToken.None);
        }
        catch (Exception exception)
        {
            var compensationFailure = $"Candidate compensation failed: {SafeMessage(exception)}";
            var uncompensated = failed(await CurrentSlotAsync(definitionId, command.SlotName));
            return uncompensated with { Diagnostic = Truncate(Join(uncompensated.Diagnostic, compensationFailure)), CompensationDiagnostic = compensationFailure };
        }

        var slot = await CurrentSlotAsync(definitionId, command.SlotName);
        if (discarded)
        {
            if (failure is not null)
                logger?.LogWarning(failure, "Activation {ActivationId} of definition {DefinitionId} slot {SlotName} {Reason}; it was compensated", command.ActivationId, definitionId, command.SlotName, reason);
            return failed(slot);
        }

        logger?.Log(
            failure is null ? LogLevel.Information : LogLevel.Warning,
            failure,
            "Activation {ActivationId} of definition {DefinitionId} slot {SlotName} {Reason}, but it serves; it was left to the slot rather than compensated",
            command.ActivationId,
            definitionId,
            command.SlotName,
            reason);
        if (StringComparer.Ordinal.Equals(slot.ActiveActivationId, command.ActivationId))
        {
            if (slotBeforeTransition is not null && slot.Revision == slotBeforeTransition.Revision + 1)
                return await ActivatedByASwitchWhoseAnswerWasLostAsync(command, reference, slot, slotBeforeTransition);
            if (TryResolveSameArtifactNoOp(command, await FindLiveReferenceAsync(slot, CancellationToken.None), slot) is { } alreadyActive)
                return alreadyActive;
        }

        var result = failed(slot);
        return result with { Diagnostic = Truncate(Join(result.Diagnostic, "The activation serves, so it was left to the slot rather than compensated.")) };
    }

    /// <summary>
    /// This call's switch threw, but the slot names its activation at the revision after the one it read: that switch, or
    /// one a call sharing the activation id made from the same revision, committed and replaced the activation the slot
    /// named before. The call reports it activated, naming that activation, so a caller that keeps its own record of it,
    /// as Publishing does, retires the record; and it notifies the trigger observers, logging rather than failing on them.
    /// </summary>
    private async ValueTask<WorkflowActivationResult> ActivatedByASwitchWhoseAnswerWasLostAsync(
        WorkflowActivationCommand command,
        WorkflowExecutableSourceReference reference,
        WorkflowActivationSlot slot,
        WorkflowActivationSlot slotBeforeTransition)
    {
        await NotifyServingAsync(slot, command.ActivationId, command.Executable.Identity.ArtifactId);
        var replaced = slotBeforeTransition.ActiveActivationId is { } previous && !StringComparer.Ordinal.Equals(previous, command.ActivationId) ? previous : null;
        return new(true, WorkflowActivationOutcome.Activated, slot, await FindLiveReferenceAsync(slot, CancellationToken.None) ?? reference, replaced);
    }
}
