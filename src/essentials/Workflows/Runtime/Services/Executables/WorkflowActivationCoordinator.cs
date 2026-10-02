using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.Logging;

namespace Elsa.Workflows.Runtime.Services.Executables;

/// <summary>
/// Owns the runtime activation lifecycle: source-reference minting, projection preparation, the slot switch, observer
/// notification, and compensation. The slot transition, the projection switch and the replaced activation's reference
/// retirement are one commit of the <see cref="IWorkflowActivationSwitch"/> (#2230), so nothing is ever left half done;
/// a call that stops short of its own switch discards its activation unless that activation serves, because a call that
/// shares its activation id switched it on with the slot (#2251; <c>WorkflowActivationCoordinator.SameActivation.cs</c>).
/// </summary>
/// <remarks>
/// Once a call's switch commits, the activation stands: a cancellation or a process that stops after it leaves it
/// activated. Only a trigger observer's failure, which fails the activation by contract, reverts it, and only through
/// <see cref="IWorkflowActivationSwitch.TryRevertAsync"/>, which undoes this call's own transition and nothing a later
/// writer made.
/// </remarks>
public sealed partial class WorkflowActivationCoordinator(
    IWorkflowActivationAuthority authority,
    IWorkflowActivationSwitch activationSwitch,
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

        // A same-artifact request is answered from the slot, and a replacement replaces its activation, only once that
        // activation serves; a slot that a version before #2230 left half done is reported instead.
        if (await authority.FindAsync(definitionId, command.SlotName, cancellationToken) is { ActiveActivationId: not null } occupied)
        {
            var (current, unserved) = await VerifyServingAsync(occupied, cancellationToken);
            if (unserved is not null)
                return unserved;
            if (TryResolveSameArtifactNoOp(command, await FindLiveReferenceAsync(current, cancellationToken), current) is { } noOp)
                return noOp;
        }

        var reference = command.Reference with
        {
            SourceReferenceId = WorkflowActivationReferenceIdentity.Create(command.ActivationId),
            ActivationId = command.ActivationId,
            SlotId = slotId
        };

        // The lease is this call's alone (#2274). Concurrent calls for one activation share its id, and a store hands an
        // unexpired lease with the same id to every acquirer; the first call to release it would then end it for the
        // others, whose next renewal would fail and discard their result, and whose artifact closure nothing would fence
        // from reference garbage collection meanwhile.
        WorkflowActivationResult? result = null;
        try
        {
            await rootWriteLeaseManager.ExecuteAsync(
                identity,
                $"activation:{command.ActivationId}:{Guid.NewGuid():N}",
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

        // The slot's activation is the only one that serves it, unless a version before #2230 stopped part way and left
        // the activation it replaced, or a stray, serving beside it. Those are turned off in the same commit, whatever the
        // slot's history: the projection stores name them by slot, so one whose reference is retired is found too.
        WorkflowActivationTransition transition;
        IReadOnlyCollection<string> others = [];
        try
        {
            others = await ListOtherServingActivationsAsync(slot.SlotId, activationId, cancellationToken);
            transition = await activationSwitch.TryDeactivateAsync(
                new(definitionId, command.SlotName, command.Source, command.ExpectedRevision, timeProvider.GetUtcNow()),
                others,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Committed or not, the slot and its projections agree.
            throw;
        }
        catch (Exception exception) when (NotRequestedCancellation(exception, cancellationToken))
        {
            return new(
                false,
                WorkflowActivationOutcome.Failed,
                await CurrentSlotAsync(definitionId, command.SlotName),
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

        // The deactivation stands from here on; a cancellation leaves it made, as a process that stops here does.
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            await NotifyTriggerObserversAsync(activationId, command.Executable.Identity.ArtifactId, cancellationToken);
        }
        catch (Exception exception) when (NotRequestedCancellation(exception, cancellationToken))
        {
            return await FailDeactivationAsync(command, activationId, transition, exception);
        }

        // Nothing serves the slot by now, so retiring the other activations' references neither observes cancellation nor
        // fails the deactivation.
        await RetireReferencesAsync(slot, others);
        return new(true, WorkflowActivationOutcome.Deactivated, transition.Slot, ReplacedActivationId: activationId);
    }

    /// <summary>The activations other than <paramref name="activationId"/> that a projection store lists as serving the slot.</summary>
    private async ValueTask<IReadOnlyCollection<string>> ListOtherServingActivationsAsync(
        string slotId,
        string activationId,
        CancellationToken cancellationToken)
    {
        var serving = new SortedSet<string>(await triggerBindingStore!.ListServingActivationIdsAsync(slotId, cancellationToken), StringComparer.Ordinal);
        if (recurringScheduleStore is not null)
            serving.UnionWith(await recurringScheduleStore.ListServingActivationIdsAsync(slotId, cancellationToken));
        serving.Remove(activationId);
        return serving;
    }

    /// <summary>
    /// Since #2230 there is nothing to complete: the slot transition and the projection switch are one commit. This reports
    /// whether the activation the slot names serves, and fails loudly for a slot that a version before #2230 left half done.
    /// </summary>
    public async ValueTask<WorkflowActivationResult> CompleteAsync(
        string workflowDefinitionId,
        string slotName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowDefinitionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(slotName);

        var slot = await authority.FindAsync(workflowDefinitionId, slotName, cancellationToken);
        if (slot?.ActiveActivationId is not { } activationId)
            return new(true, WorkflowActivationOutcome.AlreadyInactive, slot ?? EmptySlot(workflowDefinitionId, slotName));

        GuardComposition(workflowDefinitionId, slotName, activationId);
        var (current, unserved) = await VerifyServingAsync(slot, cancellationToken);
        return unserved ?? new(
            true,
            current.ActiveActivationId is null ? WorkflowActivationOutcome.AlreadyInactive : WorkflowActivationOutcome.AlreadyActive,
            current);
    }

    /// <summary>
    /// Checks that the activation <paramref name="slot"/> names serves through every projection store. A switch commits a
    /// slot transition with its projection switch (#2230), so it does, unless a version before #2230 stopped between the
    /// two. The slot is read again when the activation does not serve: a slot that moved meanwhile was moved by a switch
    /// that made its new activation serve, and is answered as it now stands.
    /// </summary>
    /// <returns>The slot as it now stands, and the failure to report when its activation does not serve or that could not be read.</returns>
    private async ValueTask<(WorkflowActivationSlot Slot, WorkflowActivationResult? Unserved)> VerifyServingAsync(
        WorkflowActivationSlot slot,
        CancellationToken cancellationToken)
    {
        try
        {
            if (await ServesAsync(slot.ActiveActivationId!, cancellationToken))
                return (slot, null);
            var current = await authority.FindAsync(slot.WorkflowDefinitionId, slot.SlotName, cancellationToken) ?? EmptySlot(slot.WorkflowDefinitionId, slot.SlotName);
            return (current, current.Revision == slot.Revision ? LeftHalfDone(current) : null);
        }
        catch (Exception exception) when (NotRequestedCancellation(exception, cancellationToken))
        {
            logger?.LogError(
                exception,
                "Whether activation {ActivationId} of definition {DefinitionId} slot {SlotName} serves could not be read",
                slot.ActiveActivationId,
                slot.WorkflowDefinitionId,
                slot.SlotName);
            return (slot, HalfDoneFailure(slot, $"whether it serves could not be read: {SafeMessage(exception)}"));
        }
    }

    private async ValueTask<bool> ServesAsync(string activationId, CancellationToken cancellationToken) =>
        await triggerBindingStore!.FindActivationStateAsync(activationId, cancellationToken) == WorkflowActivationProjectionState.Active &&
        (recurringScheduleStore is null ||
         await recurringScheduleStore.FindActivationStateAsync(activationId, cancellationToken) == WorkflowActivationProjectionState.Active);

    /// <summary>
    /// The slot names an activation that does not serve. Only a version before #2230, which committed the slot transition
    /// before the projection switch, could leave it so; nothing is built on it, and an operator clears it by deactivating
    /// (unpublishing) the slot, which turns every activation that serves it off, then activating again.
    /// </summary>
    private WorkflowActivationResult LeftHalfDone(WorkflowActivationSlot slot)
    {
        logger?.LogError(
            "Activation {ActivationId} of definition {DefinitionId} slot {SlotName} is named by the slot but does not serve: a version before #2230 switched the slot and its projections separately and stopped between them. Unpublish the slot, which turns every activation serving it off, then publish again",
            slot.ActiveActivationId,
            slot.WorkflowDefinitionId,
            slot.SlotName);
        return HalfDoneFailure(slot, "it does not serve, because a version before #2230 stopped between its slot transition and its projection switch; unpublish the slot to turn every activation serving it off, then publish again.");
    }

    private static WorkflowActivationResult HalfDoneFailure(WorkflowActivationSlot slot, string reason) => new(
        false,
        WorkflowActivationOutcome.Failed,
        slot,
        Diagnostic: Truncate($"Activation '{slot.ActiveActivationId}' of definition '{slot.WorkflowDefinitionId}' slot '{slot.SlotName}' is named by the slot, but {reason}"),
        FailedStep: WorkflowActivationStep.ProjectionActivation);

    private void GuardComposition(string definitionId, string slotName, string activationId)
    {
        if (triggerIndexer is null || triggerBindingStore is null)
            throw new WorkflowActivationException(
                definitionId,
                slotName,
                activationId,
                "Workflow activation requires the trigger serving spine (IWorkflowTriggerIndexer and IWorkflowTriggerBindingStore). Compose the WorkflowsRuntimeTriggers feature before activating.");
    }

    private async ValueTask<WorkflowExecutableSourceReference?> FindLiveReferenceAsync(WorkflowActivationSlot slot, CancellationToken cancellationToken) =>
        slot.ActiveActivationId is { } activationId &&
        await sourceReferenceStore.FindAsync(WorkflowActivationReferenceIdentity.Create(activationId), cancellationToken) is { DeletedAt: null } live
            ? live
            : null;

    /// <summary>
    /// <see cref="WorkflowActivationOutcome.AlreadyActive"/> when <paramref name="slot"/>'s activation activates the
    /// command's artifact for its tenant through <paramref name="activeReference"/>, and the command does not take the slot
    /// over from another owner; otherwise <see langword="null"/>.
    /// </summary>
    private WorkflowActivationResult? TryResolveSameArtifactNoOp(
        WorkflowActivationCommand command,
        WorkflowExecutableSourceReference? activeReference,
        WorkflowActivationSlot slot)
    {
        if (slot.ActiveActivationId is not { } activeActivationId ||
            command.OwnershipIntent == WorkflowActivationOwnershipIntent.TakeOver && slot.Source is { } incumbent && !incumbent.IsSameOwnerAs(command.Source) ||
            activeReference is null ||
            !StringComparer.Ordinal.Equals(activeReference.ArtifactId, command.Executable.Identity.ArtifactId) ||
            !StringComparer.Ordinal.Equals(activeReference.TenantId, command.Reference.TenantId))
            return null;

        logger?.LogDebug(
            "Activation {ActivationId} of definition {DefinitionId} slot {SlotName} is already active as {ActiveActivationId}",
            command.ActivationId,
            command.Executable.Identity.DefinitionId,
            command.SlotName,
            activeActivationId);
        return new(true, WorkflowActivationOutcome.AlreadyActive, slot, activeReference);
    }

    private async ValueTask<WorkflowActivationResult> RunSequenceAsync(
        WorkflowActivationCommand command,
        WorkflowExecutableSourceReference reference,
        string slotId,
        CancellationToken cancellationToken)
    {
        try
        {
            reference = await MintOrResumeSourceReferenceAsync(reference, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch (SourceReferenceResumeConflictException exception)
        {
            return new(
                false,
                WorkflowActivationOutcome.Failed,
                await CurrentSlotAsync(command.Executable.Identity.DefinitionId, command.SlotName),
                Diagnostic: Truncate(SafeMessage(exception)),
                FailedStep: WorkflowActivationStep.SourceReferenceMint);
        }
        catch (Exception exception)
        {
            return await AbandonAsync(command, reference, cancellationToken, exception, WorkflowActivationStep.SourceReferenceMint);
        }

        try
        {
            await PrepareProjectionsAsync(command.Executable, command.ActivationId, slotId, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch (Exception exception)
        {
            return await AbandonAsync(command, reference, cancellationToken, exception, WorkflowActivationStep.ProjectionPreparation);
        }

        WorkflowActivationSlot? slotBeforeTransition;
        try
        {
            slotBeforeTransition = await authority.FindAsync(command.Executable.Identity.DefinitionId, command.SlotName, CancellationToken.None);
        }
        catch (Exception exception)
        {
            return await AbandonAsync(command, reference, cancellationToken, exception, WorkflowActivationStep.SlotTransition);
        }

        WorkflowActivationTransition transition;
        try
        {
            transition = await activationSwitch.TryActivateAsync(
                new WorkflowActivationSlotRequest(
                    command.Executable.Identity.DefinitionId,
                    command.SlotName,
                    command.ActivationId,
                    command.Source,
                    command.ExpectedRevision,
                    timeProvider.GetUtcNow(),
                    command.OwnershipIntent),
                cancellationToken);
        }
        catch (Exception exception)
        {
            // The switch may have committed before it threw, as a provider that observes cancellation or loses its
            // connection while committing does. Abandoning reads what it did: discarding refuses an activation that serves.
            return await AbandonAsync(command, reference, cancellationToken, exception, WorkflowActivationStep.SlotTransition, slotBeforeTransition);
        }

        if (StringComparer.Ordinal.Equals(transition.ReplacedActivationId, command.ActivationId))
            transition = transition with { ReplacedActivationId = null, ReplacedSource = null };

        // A refusal is known not to have committed, so it is answered as one before a cancellation is honoured (#2274).
        if (!transition.Succeeded)
        {
            var refusal = transition.Diagnostic ?? "The activation slot transition was refused.";
            var refused = await AbandonAsync(
                command,
                reference,
                _ => new(false, WorkflowActivationOutcome.Conflict, transition.Slot, Conflict: transition.Conflict, Diagnostic: Truncate(refusal)),
                "lost its slot transition to another call activating it");
            cancellationToken.ThrowIfCancellationRequested();
            return refused;
        }

        // The activation stands from here on: the slot names it, it alone serves, and the activation it replaced is retired.
        // A cancellation leaves it so, as a process that stops here does.
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            await NotifyTriggerObserversAsync(command.ActivationId, command.Executable.Identity.ArtifactId, cancellationToken);
        }
        catch (Exception exception) when (NotRequestedCancellation(exception, cancellationToken))
        {
            return await RevertAsync(command, reference, transition, exception);
        }

        return new(true, WorkflowActivationOutcome.Activated, transition.Slot, reference, transition.ReplacedActivationId);
    }

    private async ValueTask<WorkflowExecutableSourceReference> MintOrResumeSourceReferenceAsync(
        WorkflowExecutableSourceReference candidate,
        CancellationToken cancellationToken)
    {
        var existing = await sourceReferenceStore.FindAsync(candidate.SourceReferenceId, cancellationToken);
        if (existing is null)
        {
            try
            {
                await sourceReferenceStore.SaveAsync(candidate, cancellationToken);
                return candidate;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception saveFailure)
            {
                // Create-only stores may report a duplicate after another activation with the same deterministic
                // id wins between the read and insert. Re-read before deciding whether this is an idempotent retry
                // or a conflicting payload. When no winner is visible, preserve the original store failure.
                existing = await sourceReferenceStore.FindAsync(candidate.SourceReferenceId, cancellationToken);
                if (existing is null)
                    System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(saveFailure).Throw();
            }
        }

        // A retry may rebuild the command later, so its wall-clock provenance can differ. Preserve the first
        // attempt's timestamps and require every other persisted identity/source field to be identical before an
        // existing row is reused. Nothing serializes this recovery with a competing attempt for the same activation:
        // each call holds a root-write lease of its own (#2274). The compare-and-restore below admits one of them; an
        // attempt that read the same retired row and lost it reports a resume conflict.
        var candidateAtOriginalTime = candidate with
        {
            CreatedAt = existing.CreatedAt,
            PublishedAt = existing.PublishedAt
        };
        if (!WorkflowExecutableSourceReferenceComparer.SameIdentity(existing, candidateAtOriginalTime))
        {
            throw new SourceReferenceResumeConflictException(
                $"Source reference '{candidate.SourceReferenceId}' already belongs to a different activation payload.");
        }

        if (existing.DeletedAt is null)
            return existing;

        if (!StringComparer.Ordinal.Equals(existing.DeletedReason, FailedRetireReason))
        {
            throw new SourceReferenceResumeConflictException(
                $"Source reference '{candidate.SourceReferenceId}' was retired for '{existing.DeletedReason ?? "an unspecified reason"}' and cannot be resumed.");
        }

        var restored = existing with { DeletedAt = null, DeletedReason = null };
        if (!await sourceReferenceStore.TryRestoreAsync(existing, restored, cancellationToken))
        {
            throw new SourceReferenceResumeConflictException(
                $"Source reference '{candidate.SourceReferenceId}' changed while its failed activation was being resumed.");
        }

        return restored;
    }

    private async ValueTask PrepareProjectionsAsync(
        WorkflowExecutable executable,
        string activationId,
        string slotId,
        CancellationToken cancellationToken) =>
        await triggerIndexer!.PrepareActivationAsync(executable, activationId, slotId, cancellationToken);

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

    /// <summary>
    /// A trigger observer failed after this call's switch committed, which fails the activation (see
    /// <see cref="IWorkflowTriggerIndexObserver"/>). The switch reverts this call's own transition in one commit, and only
    /// while the slot still stands where that transition left it; a writer that moved it since owns what serves.
    /// </summary>
    private async ValueTask<WorkflowActivationResult> RevertAsync(
        WorkflowActivationCommand command,
        WorkflowExecutableSourceReference reference,
        WorkflowActivationTransition transition,
        Exception failure)
    {
        logger?.LogWarning(
            failure,
            "Activation {ActivationId} of definition {DefinitionId} slot {SlotName} failed at step {FailedStep}; reverting",
            command.ActivationId,
            command.Executable.Identity.DefinitionId,
            command.SlotName,
            WorkflowActivationStep.TriggerObserverNotification);

        var failures = new List<string>();
        var reverted = false;
        await CaptureAsync(failures, "Authority compensation", async () =>
        {
            reverted = await activationSwitch.TryRevertAsync(
                new WorkflowActivationRevert(transition, reference, command.Source, timeProvider.GetUtcNow()),
                CancellationToken.None);
            if (!reverted)
                throw new InvalidOperationException("the slot moved on after this activation's transition, so it was left to the writer that moved it.");
        });
        if (reverted)
            await CaptureAsync(
                failures,
                "Observer compensation",
                () => NotifyTriggerObserversAsync(transition.ReplacedActivationId ?? command.ActivationId, command.Executable.Identity.ArtifactId, CancellationToken.None));

        var compensationFailure = failures.Count == 0 ? null : string.Join(" ", failures);
        return new(
            false,
            WorkflowActivationOutcome.Failed,
            await CurrentSlotAsync(command.Executable.Identity.DefinitionId, command.SlotName),
            ReplacedActivationId: transition.ReplacedActivationId,
            Diagnostic: Truncate(Join(SafeMessage(failure), compensationFailure)),
            FailedStep: WorkflowActivationStep.TriggerObserverNotification,
            CompensationDiagnostic: compensationFailure);
    }

    /// <summary>
    /// A trigger observer failed after the deactivation committed. The activation is prepared again and switched back on
    /// at the revision the deactivation produced, so a writer that moved the slot since keeps it.
    /// </summary>
    private async ValueTask<WorkflowActivationResult> FailDeactivationAsync(
        WorkflowDeactivationCommand command,
        string activationId,
        WorkflowActivationTransition transition,
        Exception failure)
    {
        logger?.LogWarning(
            failure,
            "Deactivation of activation {ActivationId} of definition {DefinitionId} slot {SlotName} failed at step {FailedStep}; compensating",
            activationId,
            command.Executable.Identity.DefinitionId,
            command.SlotName,
            WorkflowActivationStep.TriggerObserverNotification);

        var definitionId = command.Executable.Identity.DefinitionId;
        var failures = new List<string>();
        await CaptureAsync(failures, "Projection preparation", () => PrepareProjectionsAsync(command.Executable, activationId, transition.Slot.SlotId, CancellationToken.None));
        await CaptureAsync(failures, "Authority compensation", async () =>
        {
            var restored = await activationSwitch.TryActivateAsync(
                new WorkflowActivationSlotRequest(definitionId, command.SlotName, activationId, command.Source, transition.Slot.Revision, timeProvider.GetUtcNow()),
                CancellationToken.None);
            if (!restored.Succeeded)
                throw new WorkflowActivationException(
                    definitionId,
                    command.SlotName,
                    activationId,
                    $"The deactivation slot transition could not be restored: {restored.Diagnostic}");
        });
        await CaptureAsync(failures, "Observer compensation", () => NotifyTriggerObserversAsync(activationId, command.Executable.Identity.ArtifactId, CancellationToken.None));

        var compensationFailure = failures.Count == 0 ? null : string.Join(" ", failures);
        return new(
            false,
            WorkflowActivationOutcome.Failed,
            await CurrentSlotAsync(definitionId, command.SlotName),
            ReplacedActivationId: activationId,
            Diagnostic: Truncate(Join(SafeMessage(failure), compensationFailure)),
            FailedStep: WorkflowActivationStep.TriggerObserverNotification,
            CompensationDiagnostic: compensationFailure);
    }

    /// <summary>
    /// Retires the references of activations a deactivation turned off beside the slot's own. A failure is logged as an
    /// error and leaves that reference live, and the rest are still retired.
    /// </summary>
    private async ValueTask RetireReferencesAsync(WorkflowActivationSlot slot, IEnumerable<string> activationIds)
    {
        foreach (var activationId in activationIds)
        {
            try
            {
                await sourceReferenceStore.RetireAsync(
                    WorkflowActivationReferenceIdentity.Create(activationId),
                    timeProvider.GetUtcNow(),
                    ReplacedRetireReason,
                    CancellationToken.None);
            }
            catch (Exception exception)
            {
                logger?.LogError(
                    exception,
                    "The source reference of activation {ActivationId}, which no longer serves definition {DefinitionId} slot {SlotName}, could not be retired. It stays live and keeps its artifact from garbage collection; retire it as described under Operator recovery in the Runtime extension points",
                    activationId,
                    slot.WorkflowDefinitionId,
                    slot.SlotName);
            }
        }
    }

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

    private static string Join(string? message, string? compensationFailure) =>
        message is null ? compensationFailure ?? string.Empty
        : compensationFailure is null ? message
        : $"{message} {compensationFailure}";

    private static string Truncate(string message) => message.Length <= MaximumDiagnosticLength ? message : message[..MaximumDiagnosticLength];

    private static string SafeMessage(Exception exception) =>
        Truncate(string.IsNullOrWhiteSpace(exception.Message) ? exception.GetType().Name : exception.Message);

    private sealed class SourceReferenceResumeConflictException(string message) : InvalidOperationException(message);
}
