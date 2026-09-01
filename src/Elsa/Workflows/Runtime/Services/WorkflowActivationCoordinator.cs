using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.Logging;

namespace Elsa.Workflows.Runtime.Services;

/// <summary>
/// Owns the runtime activation lifecycle: source-reference minting, projection preparation, slot CAS,
/// projection activation, observer notification, predecessor retirement, and best-effort compensation.
/// </summary>
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
    public const string ReplacedRetireReason = "activation-replaced";
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
        GuardComposition(definitionId, command.SlotName, command.ActivationId);

        var noOp = await TryResolveSameArtifactNoOpAsync(command, identity.ArtifactId, cancellationToken);
        if (noOp is not null)
            return noOp;

        var reference = command.Reference with
        {
            SourceReferenceId = WorkflowActivationReferenceIdentity.Create(command.ActivationId),
            ActivationId = command.ActivationId,
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
        catch (Exception exception) when (exception is WorkflowExecutableRootWriteLeaseUnavailableException or WorkflowExecutableRootWriteLeaseLostException)
        {
            throw;
        }
        catch (Exception exception)
        {
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

    public async ValueTask<WorkflowActivationResult> DeactivateAsync(
        WorkflowDeactivationCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.Executable);
        ArgumentNullException.ThrowIfNull(command.Source);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.SlotName);
        ArgumentOutOfRangeException.ThrowIfNegative(command.ExpectedRevision);

        var definitionId = command.Executable.Identity.DefinitionId;
        var slot = await authority.FindAsync(definitionId, command.SlotName, cancellationToken);
        if (slot?.ActiveActivationId is not { } activationId)
            return new(true, WorkflowActivationOutcome.AlreadyInactive, slot ?? EmptySlot(definitionId, command.SlotName));

        GuardComposition(definitionId, command.SlotName, activationId);

        WorkflowActivationTransition transition;
        try
        {
            transition = await authority.TryDeactivateAsync(
                definitionId,
                command.SlotName,
                command.Source,
                command.ExpectedRevision,
                timeProvider.GetUtcNow(),
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            var ambiguousTransition = await InferDeactivationTransitionAfterCancellationAsync(command, slot, activationId);
            if (ambiguousTransition is not null)
                await CompensateDeactivationAsync(command, activationId, ambiguousTransition);
            throw;
        }
        catch (Exception exception) when (NotRequestedCancellation(exception, cancellationToken))
        {
            return new(
                false,
                WorkflowActivationOutcome.Failed,
                slot,
                ReplacedActivationId: activationId,
                Diagnostic: Truncate(SafeMessage(exception)),
                FailedStep: WorkflowActivationStep.SlotTransition);
        }

        if (!transition.Succeeded)
            return new(
                false,
                WorkflowActivationOutcome.Conflict,
                transition.Slot,
                ReplacedActivationId: activationId,
                Conflict: transition.Conflict,
                Diagnostic: Truncate(transition.Diagnostic ?? "The activation slot transition was refused."));

        try
        {
            await RemoveProjectionsAsync(activationId, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await CompensateDeactivationAsync(command, activationId, transition);
            throw;
        }
        catch (Exception exception) when (NotRequestedCancellation(exception, cancellationToken))
        {
            return await FailDeactivationAsync(command, activationId, transition, WorkflowActivationStep.ProjectionRemoval, exception);
        }

        try
        {
            await NotifyTriggerObserversAsync(activationId, command.Executable.Identity.ArtifactId, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await CompensateDeactivationAsync(command, activationId, transition);
            throw;
        }
        catch (Exception exception) when (NotRequestedCancellation(exception, cancellationToken))
        {
            return await FailDeactivationAsync(command, activationId, transition, WorkflowActivationStep.TriggerObserverNotification, exception);
        }

        return new(true, WorkflowActivationOutcome.Deactivated, transition.Slot, ReplacedActivationId: activationId);
    }

    private void GuardComposition(string definitionId, string slotName, string activationId)
    {
        if (triggerIndexer is null || triggerBindingStore is null)
            throw new WorkflowActivationException(
                definitionId,
                slotName,
                activationId,
                "Workflow activation requires the trigger serving spine (IWorkflowTriggerIndexer and IWorkflowTriggerBindingStore). Compose the WorkflowsRuntimeTriggers feature before activating.");
    }

    private async ValueTask<WorkflowActivationResult?> TryResolveSameArtifactNoOpAsync(
        WorkflowActivationCommand command,
        string candidateArtifactId,
        CancellationToken cancellationToken)
    {
        var current = await authority.FindAsync(command.Executable.Identity.DefinitionId, command.SlotName, cancellationToken);
        if (current?.ActiveActivationId is not { } activeActivationId)
            return null;

        if (command.OwnershipIntent == WorkflowActivationOwnershipIntent.TakeOver &&
            current.Source is { } incumbent && !incumbent.IsSameOwnerAs(command.Source))
            return null;

        var activeReference = await sourceReferenceStore.FindAsync(
            WorkflowActivationReferenceIdentity.Create(activeActivationId),
            cancellationToken);
        if (activeReference is not { DeletedAt: null } ||
            !StringComparer.Ordinal.Equals(activeReference.ArtifactId, candidateArtifactId) ||
            !StringComparer.Ordinal.Equals(activeReference.TenantId, command.Reference.TenantId))
            return null;

        logger?.LogDebug(
            "Activation {ActivationId} of definition {DefinitionId} slot {SlotName} is already active as {ActiveActivationId}",
            command.ActivationId,
            command.Executable.Identity.DefinitionId,
            command.SlotName,
            activeActivationId);
        return new(true, WorkflowActivationOutcome.AlreadyActive, current, activeReference);
    }

    private async ValueTask<WorkflowActivationResult> RunSequenceAsync(
        WorkflowActivationCommand command,
        WorkflowExecutableSourceReference reference,
        string slotId,
        CancellationToken cancellationToken)
    {
        try
        {
            await sourceReferenceStore.SaveAsync(reference, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await CompensateAsync(command, reference, null);
            throw;
        }
        catch (Exception exception) when (NotRequestedCancellation(exception, cancellationToken))
        {
            return await FailAsync(command, reference, null, WorkflowActivationStep.SourceReferenceMint, exception);
        }

        try
        {
            await PrepareProjectionsAsync(command.Executable, command.ActivationId, slotId, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await CompensateAsync(command, reference, null);
            throw;
        }
        catch (Exception exception) when (NotRequestedCancellation(exception, cancellationToken))
        {
            return await FailAsync(command, reference, null, WorkflowActivationStep.ProjectionPreparation, exception);
        }

        WorkflowActivationSlot? slotBeforeTransition;
        try
        {
            slotBeforeTransition = await authority.FindAsync(
                command.Executable.Identity.DefinitionId,
                command.SlotName,
                CancellationToken.None);
        }
        catch (Exception exception) when (NotRequestedCancellation(exception, CancellationToken.None))
        {
            return await FailAsync(command, reference, null, WorkflowActivationStep.SlotTransition, exception);
        }
        WorkflowActivationTransition transition;
        try
        {
            transition = await authority.TryActivateAsync(
                new WorkflowActivationSlotRequest(
                    command.Executable.Identity.DefinitionId,
                    command.SlotName,
                    command.ActivationId,
                    command.Source,
                    command.ExpectedRevision,
                    timeProvider.GetUtcNow(),
                    command.OwnershipIntent),
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (StringComparer.Ordinal.Equals(transition.ReplacedActivationId, command.ActivationId))
                transition = transition with { ReplacedActivationId = null, ReplacedSource = null };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // A provider may apply a CAS and then observe cancellation while returning. Read back with an
            // uncancelled token and compensate only when the candidate is still authoritative; never invent a
            // restore transition for a CAS that demonstrably did not win (or has already been superseded).
            var ambiguousTransition = await InferActivationTransitionAfterCancellationAsync(command, slotBeforeTransition);
            await CompensateAsync(command, reference, ambiguousTransition);
            throw;
        }
        catch (Exception exception) when (NotRequestedCancellation(exception, cancellationToken))
        {
            return await FailAsync(command, reference, null, WorkflowActivationStep.SlotTransition, exception);
        }

        if (!transition.Succeeded)
        {
            var compensationFailure = await CompensateAsync(command, reference, null);
            return new(
                false,
                WorkflowActivationOutcome.Conflict,
                transition.Slot,
                Conflict: transition.Conflict,
                Diagnostic: Truncate(Join(transition.Diagnostic ?? "The activation slot transition was refused.", compensationFailure)),
                CompensationDiagnostic: compensationFailure);
        }

        try
        {
            await ActivateProjectionsAsync(command.ActivationId, transition.ReplacedActivationId, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await CompensateAsync(command, reference, transition);
            throw;
        }
        catch (Exception exception) when (NotRequestedCancellation(exception, cancellationToken))
        {
            return await FailAsync(command, reference, transition, WorkflowActivationStep.ProjectionActivation, exception);
        }

        try
        {
            await NotifyTriggerObserversAsync(command.ActivationId, command.Executable.Identity.ArtifactId, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await CompensateAsync(command, reference, transition);
            throw;
        }
        catch (Exception exception) when (NotRequestedCancellation(exception, cancellationToken))
        {
            return await FailAsync(command, reference, transition, WorkflowActivationStep.TriggerObserverNotification, exception);
        }

        WorkflowExecutableSourceReference? predecessorReference = null;
        if (transition.ReplacedActivationId is { } replacedActivationId &&
            !StringComparer.Ordinal.Equals(replacedActivationId, command.ActivationId))
        {
            try
            {
                // Capture the live predecessor before its retirement. The read is intentionally uncancelled so a
                // cancellation at this boundary can still distinguish an unattempted retirement from an ambiguous
                // one and compensation can avoid restoring a superseding writer's reference.
                predecessorReference = await sourceReferenceStore.FindAsync(
                    WorkflowActivationReferenceIdentity.Create(replacedActivationId),
                    CancellationToken.None);
                cancellationToken.ThrowIfCancellationRequested();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                await CompensateAsync(command, reference, transition);
                throw;
            }
            catch (Exception exception) when (NotRequestedCancellation(exception, CancellationToken.None))
            {
                return await FailAsync(command, reference, transition, WorkflowActivationStep.PredecessorReferenceRetirement, exception);
            }
        }

        var predecessorReferenceRetirementAttempted = false;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            predecessorReferenceRetirementAttempted = predecessorReference is not null;
            await RetirePredecessorReferenceAsync(command, transition.ReplacedActivationId, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await CompensateAsync(command, reference, transition, predecessorReference, predecessorReferenceRetirementAttempted);
            throw;
        }
        catch (Exception exception) when (NotRequestedCancellation(exception, cancellationToken))
        {
            return await FailAsync(
                command,
                reference,
                transition,
                WorkflowActivationStep.PredecessorReferenceRetirement,
                exception,
                predecessorReference,
                predecessorReferenceRetirementAttempted);
        }

        return new(true, WorkflowActivationOutcome.Activated, transition.Slot, reference, transition.ReplacedActivationId);
    }

    private async ValueTask PrepareProjectionsAsync(
        WorkflowExecutable executable,
        string activationId,
        string slotId,
        CancellationToken cancellationToken) =>
        await triggerIndexer!.PrepareActivationAsync(executable, activationId, slotId, cancellationToken);

    private async ValueTask ActivateProjectionsAsync(
        string activationId,
        string? replacedActivationId,
        CancellationToken cancellationToken)
    {
        await triggerBindingStore!.ActivateAsync(activationId, replacedActivationId, cancellationToken);
        if (recurringScheduleStore is not null)
            await recurringScheduleStore.ActivateAsync(activationId, replacedActivationId, cancellationToken);
    }

    private async ValueTask RetirePredecessorReferenceAsync(
        WorkflowActivationCommand command,
        string? replacedActivationId,
        CancellationToken cancellationToken)
    {
        if (replacedActivationId is not { } replaced || StringComparer.Ordinal.Equals(replaced, command.ActivationId))
            return;

        await sourceReferenceStore.RetireAsync(
            WorkflowActivationReferenceIdentity.Create(replaced),
            timeProvider.GetUtcNow(),
            ReplacedRetireReason,
            cancellationToken);
    }

    private async ValueTask NotifyTriggerObserversAsync(
        string activationId,
        string fallbackArtifactId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_triggerObservers.Count == 0)
            return;

        var bindings = await triggerBindingStore!.ListAllByActivationAsync(activationId, cancellationToken);
        var artifactId = bindings.FirstOrDefault()?.ArtifactId ?? fallbackArtifactId;
        var snapshot = new WorkflowTriggerIndexSnapshot(artifactId, bindings) { RequiresProjectionRefresh = true };
        foreach (var observer in _triggerObservers)
        {
            await observer.OnTriggersIndexedAsync(snapshot, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
        }
    }

    private async ValueTask<WorkflowActivationResult> FailAsync(
        WorkflowActivationCommand command,
        WorkflowExecutableSourceReference reference,
        WorkflowActivationTransition? activatedSlot,
        WorkflowActivationStep failedStep,
        Exception failure,
        WorkflowExecutableSourceReference? predecessorReference = null,
        bool predecessorReferenceRetirementAttempted = false)
    {
        logger?.LogWarning(
            failure,
            "Activation {ActivationId} of definition {DefinitionId} slot {SlotName} failed at step {FailedStep}; compensating",
            command.ActivationId,
            command.Executable.Identity.DefinitionId,
            command.SlotName,
            failedStep);

        var compensationFailure = await CompensateAsync(
            command,
            reference,
            activatedSlot,
            predecessorReference,
            predecessorReferenceRetirementAttempted);
        return new(
            false,
            WorkflowActivationOutcome.Failed,
            await CurrentSlotAsync(command.Executable.Identity.DefinitionId, command.SlotName),
            ReplacedActivationId: activatedSlot?.ReplacedActivationId,
            Diagnostic: Truncate(Join(SafeMessage(failure), compensationFailure)),
            FailedStep: failedStep,
            CompensationDiagnostic: compensationFailure);
    }

    private async ValueTask<WorkflowActivationResult> FailDeactivationAsync(
        WorkflowDeactivationCommand command,
        string activationId,
        WorkflowActivationTransition transition,
        WorkflowActivationStep failedStep,
        Exception failure)
    {
        logger?.LogWarning(
            failure,
            "Deactivation of activation {ActivationId} of definition {DefinitionId} slot {SlotName} failed at step {FailedStep}; compensating",
            activationId,
            command.Executable.Identity.DefinitionId,
            command.SlotName,
            failedStep);
        var compensationFailure = await CompensateDeactivationAsync(command, activationId, transition);
        return new(
            false,
            WorkflowActivationOutcome.Failed,
            await CurrentSlotAsync(command.Executable.Identity.DefinitionId, command.SlotName),
            ReplacedActivationId: activationId,
            Diagnostic: Truncate(Join(SafeMessage(failure), compensationFailure)),
            FailedStep: failedStep,
            CompensationDiagnostic: compensationFailure);
    }

    private async ValueTask<string?> CompensateDeactivationAsync(
        WorkflowDeactivationCommand command,
        string activationId,
        WorkflowActivationTransition transition)
    {
        var failures = new List<string>();
        var slotId = WorkflowActivationSlotIdentity.Create(command.Executable.Identity.DefinitionId, command.SlotName);
        await CaptureAsync(failures, "Projection preparation", () => PrepareProjectionsAsync(command.Executable, activationId, slotId, CancellationToken.None));
        await CaptureAsync(failures, "Authority compensation", async () =>
        {
            var compensation = await authority.TryActivateAsync(
                new WorkflowActivationSlotRequest(
                    command.Executable.Identity.DefinitionId,
                    command.SlotName,
                    activationId,
                    command.Source,
                    transition.Slot.Revision,
                    timeProvider.GetUtcNow()),
                CancellationToken.None);
            if (!compensation.Succeeded)
                throw new WorkflowActivationException(
                    command.Executable.Identity.DefinitionId,
                    command.SlotName,
                    activationId,
                    $"The deactivation slot transition could not be restored: {compensation.Diagnostic}");
        });
        await CaptureAsync(failures, "Projection activation", () => ActivateProjectionsAsync(activationId, null, CancellationToken.None));
        await CaptureAsync(failures, "Observer compensation", () => NotifyTriggerObserversAsync(activationId, command.Executable.Identity.ArtifactId, CancellationToken.None));
        return failures.Count == 0 ? null : string.Join(" ", failures);
    }

    private async ValueTask<string?> CompensateAsync(
        WorkflowActivationCommand command,
        WorkflowExecutableSourceReference reference,
        WorkflowActivationTransition? activatedSlot,
        WorkflowExecutableSourceReference? predecessorReference = null,
        bool predecessorReferenceRetirementAttempted = false)
    {
        var failures = new List<string>();
        var flipped = activatedSlot is { Succeeded: true };
        if (flipped)
        {
            await CaptureAsync(failures, "Authority compensation", () => CompensateAuthorityAsync(command, activatedSlot!));
            await CaptureAsync(failures, "Replaced projection compensation", () => RestoreProjectionsAsync(command, activatedSlot!.ReplacedActivationId));
        }

        await CaptureAsync(failures, "Candidate projection compensation", () => RemoveProjectionsAsync(command.ActivationId, CancellationToken.None));
        await CaptureAsync(failures, "Reference compensation", () => RetireFailedReferenceAsync(command, reference));
        if (predecessorReferenceRetirementAttempted)
            await CaptureAsync(
                failures,
                "Predecessor reference compensation",
                () => RestorePredecessorReferenceAsync(predecessorReference));
        if (flipped)
            await CaptureAsync(
                failures,
                "Observer compensation",
                () => NotifyTriggerObserversAsync(activatedSlot!.ReplacedActivationId ?? command.ActivationId, command.Executable.Identity.ArtifactId, CancellationToken.None));
        return failures.Count == 0 ? null : string.Join(" ", failures);
    }

    private async ValueTask RestorePredecessorReferenceAsync(WorkflowExecutableSourceReference? predecessorReference)
    {
        // A predecessor that was already retired before this sequence must remain retired. Likewise, do not create a
        // missing reference or overwrite a live/different record that another writer may have installed meanwhile.
        if (predecessorReference is not { DeletedAt: null })
            return;

        var current = await sourceReferenceStore.FindAsync(predecessorReference.SourceReferenceId, CancellationToken.None);
        if (current is not { DeletedAt: not null } ||
            !StringComparer.Ordinal.Equals(current.DeletedReason, ReplacedRetireReason) ||
            !IsSameReference(current, predecessorReference))
            return;

        // Source-reference creation is deliberately create-only in the v2 adapter. DeleteAsync rechecks the row
        // with its provider CAS before removing the retirement, so a superseding writer wins instead of being
        // overwritten by this compensation. SaveAsync then recreates the captured live reference with no caller
        // cancellation token; if a writer fills the key between those operations, create-only save refuses it.
        if (!await sourceReferenceStore.DeleteAsync(predecessorReference.SourceReferenceId, CancellationToken.None))
            return;
        await sourceReferenceStore.SaveAsync(predecessorReference, CancellationToken.None);
    }

    private static bool IsSameReference(
        WorkflowExecutableSourceReference current,
        WorkflowExecutableSourceReference captured) =>
        StringComparer.Ordinal.Equals(current.SourceReferenceId, captured.SourceReferenceId) &&
        StringComparer.Ordinal.Equals(current.ArtifactId, captured.ArtifactId) &&
        StringComparer.Ordinal.Equals(current.SourceKind, captured.SourceKind) &&
        StringComparer.Ordinal.Equals(current.SourceId, captured.SourceId) &&
        StringComparer.Ordinal.Equals(current.SourceVersion, captured.SourceVersion) &&
        StringComparer.Ordinal.Equals(current.DefinitionId, captured.DefinitionId) &&
        StringComparer.Ordinal.Equals(current.DefinitionVersionId, captured.DefinitionVersionId) &&
        StringComparer.Ordinal.Equals(current.ArtifactVersion, captured.ArtifactVersion) &&
        current.CreatedAt == captured.CreatedAt &&
        current.PublishedAt == captured.PublishedAt &&
        current.Scope == captured.Scope &&
        current.ExpiresAt == captured.ExpiresAt &&
        StringComparer.Ordinal.Equals(current.ActivationId, captured.ActivationId) &&
        StringComparer.Ordinal.Equals(current.SlotId, captured.SlotId) &&
        StringComparer.Ordinal.Equals(current.TenantId, captured.TenantId);

    private async ValueTask<WorkflowActivationTransition?> InferActivationTransitionAfterCancellationAsync(
        WorkflowActivationCommand command,
        WorkflowActivationSlot? slotBeforeTransition)
    {
        WorkflowActivationSlot? current;
        try
        {
            current = await authority.FindAsync(
                command.Executable.Identity.DefinitionId,
                command.SlotName,
                CancellationToken.None);
        }
        catch
        {
            // Without read-back evidence, leave authority untouched. Candidate projection/reference cleanup still
            // runs, and a later reconcile attempt can safely resolve the unknown authority state.
            return null;
        }

        if (current?.ActiveActivationId is not { } activeActivationId ||
            !StringComparer.Ordinal.Equals(activeActivationId, command.ActivationId))
            return null;

        var replacedActivationId = slotBeforeTransition?.ActiveActivationId;
        if (StringComparer.Ordinal.Equals(replacedActivationId, command.ActivationId))
            replacedActivationId = null;
        return new WorkflowActivationTransition(
            true,
            current,
            replacedActivationId,
            ReplacedSource: slotBeforeTransition?.Source);
    }

    private async ValueTask<WorkflowActivationTransition?> InferDeactivationTransitionAfterCancellationAsync(
        WorkflowDeactivationCommand command,
        WorkflowActivationSlot slotBeforeTransition,
        string activationId)
    {
        WorkflowActivationSlot? current;
        try
        {
            current = await authority.FindAsync(
                command.Executable.Identity.DefinitionId,
                command.SlotName,
                CancellationToken.None);
        }
        catch
        {
            return null;
        }

        // A successful deactivation increments the slot revision and clears the activation. If another writer
        // has already moved the slot, do not overwrite that writer during cancellation compensation.
        if (current is null ||
            current.ActiveActivationId is not null ||
            current.Revision <= slotBeforeTransition.Revision)
            return null;

        return new WorkflowActivationTransition(
            true,
            current,
            activationId,
            ReplacedSource: slotBeforeTransition.Source);
    }

    private async ValueTask CompensateAuthorityAsync(WorkflowActivationCommand command, WorkflowActivationTransition activatedSlot)
    {
        var definitionId = command.Executable.Identity.DefinitionId;
        var compensation = activatedSlot.ReplacedActivationId is { } replaced
            ? await authority.TryActivateAsync(
                new WorkflowActivationSlotRequest(
                    definitionId,
                    command.SlotName,
                    replaced,
                    activatedSlot.ReplacedSource ?? command.Source,
                    activatedSlot.Slot.Revision,
                    timeProvider.GetUtcNow(),
                    WorkflowActivationOwnershipIntent.TakeOver),
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

    private async ValueTask RestoreProjectionsAsync(WorkflowActivationCommand command, string? replacedActivationId)
    {
        if (replacedActivationId is not { } replaced)
            return;
        await triggerBindingStore!.ActivateAsync(replaced, command.ActivationId, CancellationToken.None);
        if (recurringScheduleStore is not null)
            await recurringScheduleStore.ActivateAsync(replaced, command.ActivationId, CancellationToken.None);
    }

    private async ValueTask RemoveProjectionsAsync(string activationId, CancellationToken cancellationToken)
    {
        await triggerBindingStore!.DeleteByActivationAsync(activationId, cancellationToken);
        if (recurringScheduleStore is not null)
            await recurringScheduleStore.DeleteByActivationAsync(activationId, cancellationToken);
    }

    private async ValueTask RetireFailedReferenceAsync(WorkflowActivationCommand command, WorkflowExecutableSourceReference reference) =>
        await sourceReferenceStore.RetireAsync(
            reference.SourceReferenceId,
            timeProvider.GetUtcNow(),
            FailedRetireReason,
            CancellationToken.None);

    private async ValueTask<WorkflowActivationSlot> CurrentSlotAsync(string definitionId, string slotName)
    {
        try
        {
            return await authority.FindAsync(definitionId, slotName, CancellationToken.None) ?? EmptySlot(definitionId, slotName);
        }
        catch (Exception exception)
        {
            logger?.LogWarning(exception, "Could not read back activation slot {DefinitionId}/{SlotName} after failure", definitionId, slotName);
            return EmptySlot(definitionId, slotName);
        }
    }

    private WorkflowActivationSlot EmptySlot(string definitionId, string slotName) => new(
        WorkflowActivationSlotIdentity.Create(definitionId, slotName),
        definitionId,
        slotName,
        null,
        null,
        0,
        timeProvider.GetUtcNow());

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

    private static string Truncate(string message) => message.Length <= MaximumDiagnosticLength ? message : message[..MaximumDiagnosticLength];

    private static string SafeMessage(Exception exception) =>
        Truncate(string.IsNullOrWhiteSpace(exception.Message) ? exception.GetType().Name : exception.Message);
}
