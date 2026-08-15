using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.Logging;

namespace Elsa.Workflows.Runtime.Services;

/// <summary>
/// The one activation lifecycle, shared by every path that makes a workflow executable live (FR-B-006).
/// </summary>
/// <remarks>
/// <para>
/// Behavior-preserving by construction: the sequence and its compensation are absorbed from publishing's
/// <c>PublishWorkflowRequestHandler</c> (lease, reference mint, predecessor retire) and <c>PublicationActivator</c>
/// (projection prepare → slot CAS → projection activate, plus <c>CompensateActivationFailureAsync</c>), joined
/// into one owner instead of being split across a handler and an activator.
/// </para>
/// <para>
/// What deliberately did NOT come down from publishing: the <c>PublicationRecord</c> journal (publishing's record
/// of requests it made to the authority, never serving truth) and the projection intent-store retry machinery.
/// A caller's recovery unit is its own next attempt — the next reconcile pass for the importer, a re-publish for
/// the publish pipeline — which is safe because the sequence is idempotent by activation id and compensation
/// leaves no half-activated state behind.
/// </para>
/// </remarks>
public sealed class WorkflowActivationCoordinator(
    IWorkflowActivationAuthority authority,
    IWorkflowExecutableSourceReferenceStore sourceReferenceStore,
    IWorkflowExecutableRootWriteLeaseManager rootWriteLeaseManager,
    TimeProvider timeProvider,
    IWorkflowTriggerIndexer? triggerIndexer = null,
    IWorkflowTriggerBindingStore? triggerBindingStore = null,
    IRecurringTriggerScheduleStore? recurringScheduleStore = null,
    IEnumerable<IWorkflowTriggerIndexObserver>? triggerObservers = null,
    ILogger<WorkflowActivationCoordinator>? logger = null) : IWorkflowActivationCoordinator
{
    /// <summary>Retire reason stamped on the predecessor's reference once the candidate is live.</summary>
    public const string ReplacedRetireReason = "activation-replaced";

    /// <summary>Retire reason stamped on the candidate's reference when its activation did not complete.</summary>
    public const string FailedRetireReason = "activation-failed";

    private const int MaximumDiagnosticLength = 512;

    private readonly IReadOnlyCollection<IWorkflowTriggerIndexObserver> _triggerObservers = triggerObservers?.ToArray() ?? [];

    public async ValueTask<WorkflowActivationResult> ActivateAsync(
        WorkflowActivationCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.Executable);
        ArgumentNullException.ThrowIfNull(command.Reference);
        ArgumentNullException.ThrowIfNull(command.Source);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.SlotName);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.ActivationId);
        ArgumentOutOfRangeException.ThrowIfNegative(command.ExpectedRevision);

        var identity = command.Executable.Identity;
        if (!StringComparer.Ordinal.Equals(command.Reference.ArtifactId, identity.ArtifactId))
            throw new ArgumentException(
                $"The supplied source reference points at artifact '{command.Reference.ArtifactId}' but the executable is '{identity.ArtifactId}'.",
                nameof(command));

        var definitionId = identity.DefinitionId;
        var slotId = WorkflowActivationSlotIdentity.Create(definitionId, command.SlotName);

        // The trigger serving spine is registered by WorkflowsRuntimeTriggers, not by AddWorkflowRuntime(). An
        // activation without it would silently produce a definition that no stimulus can ever start, so refuse
        // loudly BEFORE the first write rather than half-activating.
        if (triggerIndexer is null || triggerBindingStore is null)
            throw new WorkflowActivationException(
                definitionId,
                command.SlotName,
                command.ActivationId,
                "Workflow activation requires the trigger serving spine (IWorkflowTriggerIndexer and IWorkflowTriggerBindingStore). " +
                "Compose the WorkflowsRuntimeTriggers feature before activating.");

        var noOp = await TryResolveSameArtifactNoOpAsync(command, identity.ArtifactId, cancellationToken);
        if (noOp is not null)
            return noOp;

        // The coordinator owns reference identity so that any activation id resolves to its reference with a
        // plain FindAsync (see WorkflowActivationReferenceIdentity); the caller's provenance fields ride along
        // untouched.
        var reference = command.Reference with
        {
            SourceReferenceId = WorkflowActivationReferenceIdentity.Create(command.ActivationId),
            PublicationId = command.ActivationId,
            SlotId = slotId
        };

        WorkflowActivationResult? result = null;
        try
        {
            await rootWriteLeaseManager.ExecuteAsync(
                identity,
                $"activation:{command.ActivationId}",
                async leaseToken => result = await RunSequenceAsync(command, reference, slotId, leaseToken),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (WorkflowActivationException)
        {
            throw;
        }
        catch (Exception exception)
        {
            // §2.23.5: the lease manager surfaces store faults directly. Nothing raw crosses this boundary.
            throw new WorkflowActivationException(
                definitionId,
                command.SlotName,
                command.ActivationId,
                $"Activation '{command.ActivationId}' of definition '{definitionId}' slot '{command.SlotName}' could not acquire its executable retention lease.",
                exception);
        }

        return result ?? throw new WorkflowActivationException(
            definitionId,
            command.SlotName,
            command.ActivationId,
            $"Activation '{command.ActivationId}' produced no outcome; the retention lease did not run the activation sequence.");
    }

    /// <summary>
    /// FR-B-006's idempotent no-op: a request for the artifact the slot already serves writes nothing and
    /// succeeds, whichever source asks.
    /// </summary>
    /// <remarks>
    /// This check lives here rather than on the authority on purpose — the slot carries no <c>ArtifactId</c>, so
    /// only the coordinator (which owns activation↔reference identity) can answer "same artifact?". Pushing it
    /// down would force the ledger to duplicate artifact state it has no other use for.
    /// </remarks>
    private async ValueTask<WorkflowActivationResult?> TryResolveSameArtifactNoOpAsync(
        WorkflowActivationCommand command,
        string candidateArtifactId,
        CancellationToken cancellationToken)
    {
        var current = await authority.FindAsync(
            command.Executable.Identity.DefinitionId,
            command.SlotName,
            cancellationToken);
        if (current?.ActiveActivationId is not { } activeActivationId)
            return null;

        var activeReference = await sourceReferenceStore.FindAsync(
            WorkflowActivationReferenceIdentity.Create(activeActivationId),
            cancellationToken);

        // A retired reference means the active activation is no longer backed by live serving provenance, so it
        // cannot stand in for the candidate — fall through and activate properly.
        if (activeReference is not { DeletedAt: null } ||
            !StringComparer.Ordinal.Equals(activeReference.ArtifactId, candidateArtifactId))
            return null;

        logger?.LogDebug(
            "Activation {ActivationId} of definition {DefinitionId} slot {SlotName} is a no-op: activation {ActiveActivationId} already serves artifact {ArtifactId}",
            command.ActivationId,
            command.Executable.Identity.DefinitionId,
            command.SlotName,
            activeActivationId,
            candidateArtifactId);

        return new WorkflowActivationResult(
            true,
            WorkflowActivationOutcome.AlreadyActive,
            current,
            activeReference);
    }

    private async ValueTask<WorkflowActivationResult> RunSequenceAsync(
        WorkflowActivationCommand command,
        WorkflowExecutableSourceReference reference,
        string slotId,
        CancellationToken cancellationToken)
    {
        var definitionId = command.Executable.Identity.DefinitionId;

        // Step 1 — mint the live source reference. It is written first so that the reference GC, which is fenced
        // out by the lease we hold, can never observe projections pointing at an unreferenced artifact.
        try
        {
            await sourceReferenceStore.SaveAsync(reference, cancellationToken);
        }
        catch (Exception exception) when (NotRequestedCancellation(exception, cancellationToken))
        {
            return await FailAsync(command, reference, activatedSlot: null, exception);
        }

        // Step 2 — prepare BOTH projections in non-serving state.
        try
        {
            await PrepareProjectionsAsync(command, slotId, cancellationToken);
        }
        catch (Exception exception) when (NotRequestedCancellation(exception, cancellationToken))
        {
            return await FailAsync(command, reference, activatedSlot: null, exception);
        }

        // Step 3 — the slot CAS. The authority is the sole decider; everything before this point is invisible to
        // serving, everything after it is visible.
        WorkflowActivationTransition transition;
        try
        {
            transition = await authority.TryActivateAsync(
                new WorkflowActivationSlotRequest(
                    definitionId,
                    command.SlotName,
                    command.ActivationId,
                    command.Source,
                    command.ExpectedRevision,
                    timeProvider.GetUtcNow()),
                cancellationToken);
        }
        catch (Exception exception) when (NotRequestedCancellation(exception, cancellationToken))
        {
            return await FailAsync(command, reference, activatedSlot: null, exception);
        }

        if (!transition.Succeeded)
        {
            // A refusal is not a failure of this coordinator: nothing flipped, so only the candidate's own
            // prepared state is rolled back and the authority's diagnostic is surfaced verbatim.
            var conflictCompensation = await CompensateAsync(command, reference, activatedSlot: null);
            return new WorkflowActivationResult(
                false,
                WorkflowActivationOutcome.Conflict,
                transition.Slot,
                null,
                null,
                transition.Conflict,
                Truncate(Join(transition.Diagnostic ?? "The activation slot transition was refused.", conflictCompensation)));
        }

        // Step 4 — make both projections serve, and retire the replaced activation's projections with them.
        try
        {
            await ActivateProjectionsAsync(command.ActivationId, transition.ReplacedActivationId, cancellationToken);
        }
        catch (Exception exception) when (NotRequestedCancellation(exception, cancellationToken))
        {
            return await FailAsync(command, reference, transition, exception);
        }

        // Step 5 — notify derived projections (route tables and the like). An observer throw fails the
        // activation, matching the indexer's existing rule: a stale derived projection is an unindexed trigger.
        try
        {
            await NotifyTriggerObserversAsync(command.ActivationId, command.Executable.Identity.ArtifactId, cancellationToken);
        }
        catch (Exception exception) when (NotRequestedCancellation(exception, cancellationToken))
        {
            return await FailAsync(command, reference, transition, exception);
        }

        // Step 6 — retire the predecessor's reference so the GC can eventually reclaim its artifact.
        try
        {
            await RetirePredecessorReferenceAsync(command, transition.ReplacedActivationId, cancellationToken);
        }
        catch (Exception exception) when (NotRequestedCancellation(exception, cancellationToken))
        {
            return await FailAsync(command, reference, transition, exception);
        }

        return new WorkflowActivationResult(
            true,
            WorkflowActivationOutcome.Activated,
            transition.Slot,
            reference,
            transition.ReplacedActivationId);
    }

    private async ValueTask PrepareProjectionsAsync(
        WorkflowActivationCommand command,
        string slotId,
        CancellationToken cancellationToken)
    {
        await triggerIndexer!.PreparePublicationAsync(command.Executable, command.ActivationId, slotId, cancellationToken);

        if (recurringScheduleStore is null)
            return;

        // A host can compose the recurring store without composing any recurring providers, in which case the
        // indexer chain prepared nothing. Reading back and re-preparing makes the empty projection explicit, so
        // a later activate/compensate has a projection to move rather than silently nothing. Carried over from
        // PublicationProjectionReconciler; the census's schedule double-write is collapsed by its own task.
        var schedules = await RuntimeOperationalStorePagingExtensions.ListAllByPublicationAsync(
            recurringScheduleStore,
            command.ActivationId,
            cancellationToken);
        await recurringScheduleStore.PreparePublicationAsync(command.ActivationId, schedules, cancellationToken);
    }

    private async ValueTask ActivateProjectionsAsync(
        string activationId,
        string? replacedActivationId,
        CancellationToken cancellationToken)
    {
        await triggerBindingStore!.ActivatePublicationAsync(activationId, replacedActivationId, cancellationToken);

        if (recurringScheduleStore is not null)
            await recurringScheduleStore.ActivatePublicationAsync(activationId, replacedActivationId, cancellationToken);
    }

    private async ValueTask RetirePredecessorReferenceAsync(
        WorkflowActivationCommand command,
        string? replacedActivationId,
        CancellationToken cancellationToken)
    {
        if (replacedActivationId is not { } replaced ||
            StringComparer.Ordinal.Equals(replaced, command.ActivationId))
            return;

        var referenceId = WorkflowActivationReferenceIdentity.Create(replaced);
        logger?.LogInformation(
            "Retiring source reference {SourceReferenceId} of workflow definition {DefinitionId} because activation {ActivationId} was replaced by {ReplacementActivationId}",
            referenceId,
            command.Executable.Identity.DefinitionId,
            replaced,
            command.ActivationId);
        await sourceReferenceStore.RetireAsync(
            referenceId,
            timeProvider.GetUtcNow(),
            ReplacedRetireReason,
            cancellationToken);
    }

    private async ValueTask NotifyTriggerObserversAsync(
        string activationId,
        string fallbackArtifactId,
        CancellationToken cancellationToken)
    {
        if (_triggerObservers.Count == 0)
            return;

        var bindings = await triggerBindingStore!.ListAllByPublicationAsync(activationId, cancellationToken);
        var artifactId = bindings.FirstOrDefault()?.ArtifactId ?? fallbackArtifactId;
        // Authority can change while the artifact's extracted bindings stay identical (or empty), so consumers
        // must not apply their ordinary repeated-indexing skip to this notification.
        var snapshot = new WorkflowTriggerIndexSnapshot(artifactId, bindings)
        {
            RequiresProjectionRefresh = true
        };
        foreach (var observer in _triggerObservers)
            await observer.OnTriggersIndexedAsync(snapshot, cancellationToken);
    }

    private async ValueTask<WorkflowActivationResult> FailAsync(
        WorkflowActivationCommand command,
        WorkflowExecutableSourceReference reference,
        WorkflowActivationTransition? activatedSlot,
        Exception failure)
    {
        logger?.LogWarning(
            failure,
            "Activation {ActivationId} of definition {DefinitionId} slot {SlotName} failed; compensating",
            command.ActivationId,
            command.Executable.Identity.DefinitionId,
            command.SlotName);

        var compensationFailure = await CompensateAsync(command, reference, activatedSlot);
        var slot = await CurrentSlotAsync(command);
        return new WorkflowActivationResult(
            false,
            WorkflowActivationOutcome.Failed,
            slot,
            null,
            activatedSlot?.ReplacedActivationId,
            WorkflowActivationConflict.None,
            Truncate(Join(SafeMessage(failure), compensationFailure)));
    }

    /// <summary>
    /// Undoes as much of the sequence as ran. Best-effort by design: every step is attempted even if an earlier
    /// one failed, and what did not converge is reported rather than thrown, so the original failure is never
    /// masked by a compensation failure.
    /// </summary>
    /// <param name="activatedSlot">
    /// The successful slot transition, or <c>null</c> when the slot never flipped. When null only the candidate's
    /// own writes are rolled back — there is no predecessor to restore and the authority was never moved.
    /// </param>
    private async ValueTask<string?> CompensateAsync(
        WorkflowActivationCommand command,
        WorkflowExecutableSourceReference reference,
        WorkflowActivationTransition? activatedSlot)
    {
        var failures = new List<string>();
        var flipped = activatedSlot is { Succeeded: true };

        if (flipped)
        {
            await CaptureAsync(failures, "Authority compensation", () => CompensateAuthorityAsync(command, activatedSlot!));
            await CaptureAsync(failures, "Replaced projection compensation", () => RestoreProjectionsAsync(command, activatedSlot!.ReplacedActivationId));
        }

        await CaptureAsync(failures, "Candidate projection compensation", () => RemoveProjectionsAsync(command.ActivationId));
        await CaptureAsync(failures, "Reference compensation", () => RetireFailedReferenceAsync(command, reference));

        // Observers are reconciled only after BOTH sides reached their final serving state. A refresh between
        // restoring the predecessor and removing the candidate could project both route sets and then have the
        // candidate-removal notification skipped by an observer optimization.
        if (flipped)
            await CaptureAsync(
                failures,
                "Observer compensation",
                () => NotifyTriggerObserversAsync(
                    activatedSlot!.ReplacedActivationId ?? command.ActivationId,
                    command.Executable.Identity.ArtifactId,
                    CancellationToken.None));

        return failures.Count == 0 ? null : string.Join(" ", failures);
    }

    private async ValueTask CompensateAuthorityAsync(
        WorkflowActivationCommand command,
        WorkflowActivationTransition activatedSlot)
    {
        var definitionId = command.Executable.Identity.DefinitionId;
        // The CAS presents the POST-flip revision: we are undoing our own transition, not racing a third writer.
        // Ownership is safe to assert with the candidate's source because a foreign-owned slot could not have
        // been flipped in the first place.
        var compensation = activatedSlot.ReplacedActivationId is { } replacedActivationId
            ? await authority.TryActivateAsync(
                new WorkflowActivationSlotRequest(
                    definitionId,
                    command.SlotName,
                    replacedActivationId,
                    command.Source,
                    activatedSlot.Slot.Revision,
                    timeProvider.GetUtcNow()),
                CancellationToken.None)
            : await authority.TryDeactivateAsync(
                definitionId,
                command.SlotName,
                command.Source,
                activatedSlot.Slot.Revision,
                timeProvider.GetUtcNow(),
                CancellationToken.None);

        if (!compensation.Succeeded)
            throw new WorkflowActivationException(
                definitionId,
                command.SlotName,
                command.ActivationId,
                $"Activation '{command.ActivationId}' failed and the prior slot authority could not be restored: {compensation.Diagnostic}");
    }

    /// <summary>
    /// Re-activates the predecessor's projections. Unconditional by design — this IS the force-replay semantic:
    /// the coordinator carries no delivery-intent ledger that could short-circuit a repeated activate.
    /// </summary>
    private async ValueTask RestoreProjectionsAsync(WorkflowActivationCommand command, string? replacedActivationId)
    {
        if (replacedActivationId is not { } replaced)
            return;

        await triggerBindingStore!.ActivatePublicationAsync(replaced, command.ActivationId, CancellationToken.None);

        if (recurringScheduleStore is not null)
            await recurringScheduleStore.ActivatePublicationAsync(replaced, command.ActivationId, CancellationToken.None);
    }

    private async ValueTask RemoveProjectionsAsync(string activationId)
    {
        await triggerBindingStore!.DeleteByPublicationAsync(activationId, CancellationToken.None);

        if (recurringScheduleStore is not null)
            await recurringScheduleStore.DeleteByPublicationAsync(activationId, CancellationToken.None);
    }

    private async ValueTask RetireFailedReferenceAsync(
        WorkflowActivationCommand command,
        WorkflowExecutableSourceReference reference)
    {
        logger?.LogWarning(
            "Retiring source reference {SourceReferenceId} of workflow definition {DefinitionId} because activation {ActivationId} failed",
            reference.SourceReferenceId,
            command.Executable.Identity.DefinitionId,
            command.ActivationId);
        await sourceReferenceStore.RetireAsync(
            reference.SourceReferenceId,
            timeProvider.GetUtcNow(),
            FailedRetireReason,
            CancellationToken.None);
    }

    private async ValueTask<WorkflowActivationSlot> CurrentSlotAsync(WorkflowActivationCommand command)
    {
        var definitionId = command.Executable.Identity.DefinitionId;
        WorkflowActivationSlot? slot = null;
        try
        {
            slot = await authority.FindAsync(definitionId, command.SlotName, CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger?.LogWarning(
                exception,
                "Could not read back the activation slot of definition {DefinitionId} slot {SlotName} after a failed activation",
                definitionId,
                command.SlotName);
        }

        return slot ?? new WorkflowActivationSlot(
            WorkflowActivationSlotIdentity.Create(definitionId, command.SlotName),
            definitionId,
            command.SlotName,
            null,
            null,
            0,
            timeProvider.GetUtcNow());
    }

    private static async ValueTask CaptureAsync(List<string> failures, string label, Func<ValueTask> step)
    {
        try
        {
            await step();
        }
        catch (Exception exception)
        {
            failures.Add($"{label} failed: {SafeMessage(exception)}");
        }
    }

    private static bool NotRequestedCancellation(Exception exception, CancellationToken cancellationToken) =>
        exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested;

    private static string Join(string message, string? compensationFailure) =>
        compensationFailure is null ? message : $"{message} {compensationFailure}";

    private static string Truncate(string message) =>
        message.Length <= MaximumDiagnosticLength ? message : message[..MaximumDiagnosticLength];

    private static string SafeMessage(Exception exception) =>
        Truncate(string.IsNullOrWhiteSpace(exception.Message) ? exception.GetType().Name : exception.Message);
}
