using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.Logging;

namespace Elsa.Workflows.Runtime.Services.Executables;

/// <summary>
/// Owns the runtime activation lifecycle: source-reference minting, projection preparation, slot CAS,
/// projection activation, observer notification, predecessor retirement, best-effort compensation, and completion of
/// an activation that an interrupted call left half-done.
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

        // Before answering "already active" or replacing it, finish the activation the slot already names. Replacing
        // a half-done one would fail anyway: its projections are not active, so they cannot be switched off.
        if (await authority.FindAsync(definitionId, command.SlotName, cancellationToken) is { ActiveActivationId: not null } occupied &&
            await CompleteServingActivationAsync(occupied, cancellationToken) is { Outcome: WorkflowActivationOutcome.Failed } incomplete)
            return incomplete;

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

        // Deactivation completes nothing first: it switches nothing on. A process that died mid-activation can leave the
        // activation it replaced, or a stray, serving this slot, so every other activation that serves the slot is turned
        // off too, whatever the slot's history (#2193). The projection stores name them by slot, so one whose reference
        // is retired, expired or gone is found as well.
        IReadOnlyList<SlotOccupant> others;
        try
        {
            await RemoveProjectionsAsync(activationId, cancellationToken);
            others = await ListOtherOccupantsAsync(slot, activationId, cancellationToken);
            foreach (var serving in await ListOtherServingActivationsAsync(slot, activationId, others, cancellationToken))
                await RemoveProjectionsAsync(serving, cancellationToken);
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

        // The deactivation has happened by now and nothing serves the slot, so retiring the other activations' references
        // neither observes cancellation nor fails it.
        await RetireReplacedReferencesAsync(
            slot,
            others.Where(other => other.Serves || other.WasReplaced).Select(other => other.ActivationId),
            bestEffort: true,
            CancellationToken.None);
        return new(true, WorkflowActivationOutcome.Deactivated, transition.Slot, ReplacedActivationId: activationId);
    }

    /// <summary>
    /// The activations other than <paramref name="activationId"/> that serve <paramref name="slot"/>: those a projection
    /// store lists for the slot, whatever their source references say, and any listed occupant whose projection state
    /// serves though it has no rows.
    /// </summary>
    private async ValueTask<IReadOnlyCollection<string>> ListOtherServingActivationsAsync(
        WorkflowActivationSlot slot,
        string activationId,
        IReadOnlyList<SlotOccupant> others,
        CancellationToken cancellationToken)
    {
        var serving = new SortedSet<string>(others.Where(other => other.Serves).Select(other => other.ActivationId), StringComparer.Ordinal);
        serving.UnionWith(await triggerBindingStore!.ListServingActivationIdsAsync(slot.SlotId, cancellationToken));
        if (recurringScheduleStore is not null)
            serving.UnionWith(await recurringScheduleStore.ListServingActivationIdsAsync(slot.SlotId, cancellationToken));
        serving.Remove(activationId);
        return serving;
    }

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
        return await CompleteServingActivationAsync(slot, cancellationToken) ??
            new(true, WorkflowActivationOutcome.AlreadyActive, slot);
    }

    /// <summary>
    /// Finishes the activation <paramref name="slot"/> names when its sequence stopped after the slot transition (#2193):
    /// the process died, so no compensation ran either.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The property this restores: the activation a slot names serves through every projection store, and the
    /// activation it replaced serves through none and has a retired source reference. Completion covers the two places
    /// the tail of <see cref="RunSequenceAsync"/> can stop. Before the projection switch, the slot's activation is
    /// prepared or replaced in some store; completion switches it on and the replaced activation off, notifies
    /// observers, and retires the replaced activation's reference. After the switch but before step 6, the replaced
    /// activation serves nothing but its reference is live; completion retires it. A crash between notification and
    /// step 6 is the same second case. Every step it re-runs is idempotent.
    /// </para>
    /// <para>
    /// Which activation a transition replaced is not recorded durably. Completion lists the live references minted for
    /// this slot and reads each one's projection state. The replaced activation is the one still serving, and a leftover
    /// is one that served and was switched off. A candidate that has prepared but not won the slot has never served, so
    /// it is never touched: retiring its reference would sabotage it. Two serving activations are ambiguous, and
    /// completion fails rather than guess.
    /// </para>
    /// <para>
    /// The slot is read again before the projections switch and before a reference is retired; if another writer moved
    /// it, that writer owns what happens next. Completion may also race the activation's own sequence on another node.
    /// The stores make the switch a no-op whichever of the two makes it second, and if that sequence then fails, its
    /// compensation restores the reference completion retired. Each replaced activation's projection state is read again
    /// just before its reference is retired, and one that serves again is left alone: that sequence failed and its
    /// compensation handed the slot back.
    /// </para>
    /// <para>
    /// When nothing needs switching, retiring a leftover's reference is housekeeping: a failure is logged as an error and
    /// does not fail the activation that called this.
    /// </para>
    /// <para>
    /// Two windows remain, because the slot and the projections share no transaction; #2230 closes both by switching them
    /// in one. A first activation has no replaced activation whose projection fences the switch, so a completion that
    /// read the slot just before another writer completed and then replaced that activation switches it back on. It then
    /// serves beside its successor, and because its reference is retired, no later completion finds it: the double
    /// serving does not heal itself, and only unpublishing the slot clears it. Separately, the re-read before a retire
    /// narrows but does not close the race with compensation: one that hands the slot back between that read and the
    /// retire leaves the restored activation serving with a retired reference, so its artifact is no longer held
    /// against garbage collection until the slot's next activation replaces it.
    /// </para>
    /// </remarks>
    /// <returns>
    /// <see langword="null"/> when there is nothing to complete, the activation's reference is no longer live, or the
    /// slot moved; an <see cref="WorkflowActivationOutcome.Activated"/> result when this call completed it; and a
    /// <see cref="WorkflowActivationOutcome.Failed"/> result when it could not.
    /// </returns>
    private async ValueTask<WorkflowActivationResult?> CompleteServingActivationAsync(
        WorkflowActivationSlot slot,
        CancellationToken cancellationToken)
    {
        var activationId = slot.ActiveActivationId!;
        try
        {
            var state = await ReadOccupantAsync(activationId, cancellationToken);
            var switchTriggers = IsSwitchedOff(state.Triggers);
            var switchSchedules = IsSwitchedOff(state.Schedules);
            var switching = switchTriggers || switchSchedules;

            // A retired reference reads as nothing active: the activation was compensated, and the next activation of
            // this slot resumes it through the ordinary sequence.
            var reference = await sourceReferenceStore.FindAsync(WorkflowActivationReferenceIdentity.Create(activationId), cancellationToken);
            if (reference is not { DeletedAt: null })
                return null;

            var others = await ListOtherOccupantsAsync(slot, activationId, cancellationToken);
            var retire = others.Where(other => other.WasReplaced).Select(other => other.ActivationId).ToList();
            string? predecessor = null;
            if (switching)
            {
                var serving = others.Where(other => other.Serves).Select(other => other.ActivationId).ToArray();
                if (serving.Length > 1)
                    return Ambiguous(slot, serving);
                predecessor = serving.SingleOrDefault();
            }
            else if (retire.Count == 0)
                return null;

            if (!await StillNamesAsync(slot, cancellationToken))
                return null;

            if (switching)
            {
                if (switchTriggers)
                    await triggerBindingStore!.ActivateAsync(activationId, predecessor, cancellationToken);
                if (switchSchedules)
                    await recurringScheduleStore!.ActivateAsync(activationId, predecessor, cancellationToken);
                await NotifyTriggerObserversAsync(activationId, reference.ArtifactId, cancellationToken);
                if (predecessor is not null)
                    retire.Add(predecessor);
                if (!await StillNamesAsync(slot, cancellationToken))
                    retire.Clear();
            }

            var retired = await RetireReplacedReferencesAsync(slot, retire, bestEffort: !switching, cancellationToken);
            if (!switching && retired.Count == 0)
                return null;

            logger?.LogWarning(
                "Completed activation {ActivationId} of definition {DefinitionId} slot {SlotName}, which an interrupted call left half done; replaced activations {ReplacedActivationIds} no longer serve it",
                activationId,
                slot.WorkflowDefinitionId,
                slot.SlotName,
                retired);
            return new(true, WorkflowActivationOutcome.Activated, slot, reference, retired.FirstOrDefault());
        }
        catch (Exception exception) when (NotRequestedCancellation(exception, cancellationToken))
        {
            logger?.LogError(
                exception,
                "Activation {ActivationId} of definition {DefinitionId} slot {SlotName} was left half done and could not be completed",
                activationId,
                slot.WorkflowDefinitionId,
                slot.SlotName);
            return CompletionFailed(slot, SafeMessage(exception));
        }
    }

    /// <summary>
    /// Two or more other activations still serve the slot, so the one its activation replaced cannot be told apart.
    /// Nothing is switched. Unpublishing the slot turns every one of them off, whatever its history.
    /// </summary>
    private WorkflowActivationResult Ambiguous(WorkflowActivationSlot slot, IReadOnlyCollection<string> serving)
    {
        logger?.LogError(
            "Activation {ActivationId} of definition {DefinitionId} slot {SlotName} was left half done, and activations {ServingActivationIds} all still serve the slot, so the one it replaced cannot be told apart. Nothing was switched; unpublish the slot to turn every one of them off, then publish again",
            slot.ActiveActivationId,
            slot.WorkflowDefinitionId,
            slot.SlotName,
            serving);
        return CompletionFailed(
            slot,
            $"activations {string.Join(", ", serving.Select(x => $"'{x}'"))} all still serve the slot, so the one it replaced cannot be told apart; unpublish the slot to turn them off.");
    }

    private static WorkflowActivationResult CompletionFailed(WorkflowActivationSlot slot, string reason) => new(
        false,
        WorkflowActivationOutcome.Failed,
        slot,
        Diagnostic: Truncate($"Activation '{slot.ActiveActivationId}' of definition '{slot.WorkflowDefinitionId}' slot '{slot.SlotName}' was left half done and could not be completed: {reason}"),
        FailedStep: WorkflowActivationStep.ProjectionActivation);

    private static bool IsSwitchedOff(WorkflowActivationProjectionState? state) =>
        state is WorkflowActivationProjectionState.Prepared or WorkflowActivationProjectionState.Replaced;

    /// <summary>
    /// The activations other than <paramref name="activationId"/> that have a live Published reference minted for
    /// <paramref name="slot"/>, with where their projections stand. The page is narrowed to the slot's definition, so it
    /// reads that definition's live references only.
    /// </summary>
    private async ValueTask<IReadOnlyList<SlotOccupant>> ListOtherOccupantsAsync(
        WorkflowActivationSlot slot,
        string activationId,
        CancellationToken cancellationToken)
    {
        var occupants = new List<SlotOccupant>();
        var now = timeProvider.GetUtcNow();
        string? continuationToken = null;
        do
        {
            var page = await sourceReferenceStore.ListPageAsync(
                new WorkflowExecutableSourceReferencePageQuery(
                    WorkflowExecutableReferenceScope.Published,
                    liveOnly: true,
                    now,
                    continuationToken: continuationToken)
                {
                    DefinitionId = slot.WorkflowDefinitionId
                },
                cancellationToken);
            foreach (var reference in page.Items.Where(reference =>
                         reference.ActivationId is { } other &&
                         !StringComparer.Ordinal.Equals(other, activationId) &&
                         StringComparer.Ordinal.Equals(reference.SlotId, slot.SlotId)))
            {
                occupants.Add(await ReadOccupantAsync(reference.ActivationId!, cancellationToken));
            }

            continuationToken = page.NextContinuationToken;
        } while (continuationToken is not null);

        return occupants;
    }

    private async ValueTask<SlotOccupant> ReadOccupantAsync(string activationId, CancellationToken cancellationToken) => new(
        activationId,
        await triggerBindingStore!.FindActivationStateAsync(activationId, cancellationToken),
        recurringScheduleStore is null ? null : await recurringScheduleStore.FindActivationStateAsync(activationId, cancellationToken));

    /// <summary>
    /// Retires the references of activations that no longer serve <paramref name="slot"/>. Each one's projection state is
    /// read again first, and one that serves again is left alone: an in-flight activation that replaced it failed, and its
    /// compensation handed the slot back (#2193). When <paramref name="bestEffort"/>, a failure is logged as an error and
    /// the rest are still retired; otherwise it propagates.
    /// </summary>
    /// <returns>The activations whose references were retired.</returns>
    private async ValueTask<IReadOnlyList<string>> RetireReplacedReferencesAsync(
        WorkflowActivationSlot slot,
        IEnumerable<string> activationIds,
        bool bestEffort,
        CancellationToken cancellationToken)
    {
        var retired = new List<string>();
        foreach (var activationId in activationIds)
        {
            try
            {
                if ((await ReadOccupantAsync(activationId, cancellationToken)).Serves)
                    continue;
                await RetireReplacedReferenceAsync(activationId, cancellationToken);
                retired.Add(activationId);
            }
            catch (Exception exception) when (bestEffort && NotRequestedCancellation(exception, cancellationToken))
            {
                logger?.LogError(
                    exception,
                    "The source reference of activation {ActivationId}, which no longer serves definition {DefinitionId} slot {SlotName}, could not be retired. It stays live and keeps its artifact from garbage collection; retire it as described under Operator recovery in the Runtime extension points",
                    activationId,
                    slot.WorkflowDefinitionId,
                    slot.SlotName);
            }
        }

        return retired;
    }

    private async ValueTask<bool> StillNamesAsync(WorkflowActivationSlot slot, CancellationToken cancellationToken) =>
        await authority.FindAsync(slot.WorkflowDefinitionId, slot.SlotName, cancellationToken) is { } current &&
        current.Revision == slot.Revision &&
        StringComparer.Ordinal.Equals(current.ActiveActivationId, slot.ActiveActivationId);

    /// <summary>Another activation minted for a slot, and where its trigger and schedule projections stand.</summary>
    private sealed record SlotOccupant(
        string ActivationId,
        WorkflowActivationProjectionState Triggers,
        WorkflowActivationProjectionState? Schedules)
    {
        public bool Serves => Triggers == WorkflowActivationProjectionState.Active || Schedules == WorkflowActivationProjectionState.Active;

        /// <summary>It served, was switched off by its replacement, and serves nothing now: its reference is owed retirement.</summary>
        public bool WasReplaced => !Serves &&
            (Triggers == WorkflowActivationProjectionState.Replaced || Schedules == WorkflowActivationProjectionState.Replaced);
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
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await CompensateUnlessSlotNamesAsync(command, reference);
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
            await CompensateUnlessSlotNamesAsync(command, reference);
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
            var refusal = transition.Diagnostic ?? "The activation slot transition was refused.";
            if (await FindSlotNamingAsync(command) is { } named)
            {
                logger?.LogInformation(
                    "Activation {ActivationId} of definition {DefinitionId} slot {SlotName} lost its slot transition to another call activating it; completing it rather than compensating",
                    command.ActivationId,
                    command.Executable.Identity.DefinitionId,
                    command.SlotName);
                return await DeferToSlotAsync(
                    command,
                    named,
                    new(false, WorkflowActivationOutcome.Conflict, transition.Slot, Conflict: transition.Conflict, Diagnostic: Truncate(refusal)));
            }

            var compensationFailure = await CompensateAsync(command, reference, null);
            return new(
                false,
                WorkflowActivationOutcome.Conflict,
                transition.Slot,
                Conflict: transition.Conflict,
                Diagnostic: Truncate(Join(refusal, compensationFailure)),
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
        // existing row is reused. The activation root lease serializes this recovery with competing attempts.
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

        await RetireReplacedReferenceAsync(replaced, cancellationToken);
    }

    private async ValueTask RetireReplacedReferenceAsync(string replacedActivationId, CancellationToken cancellationToken) =>
        await sourceReferenceStore.RetireAsync(
            WorkflowActivationReferenceIdentity.Create(replacedActivationId),
            timeProvider.GetUtcNow(),
            ReplacedRetireReason,
            cancellationToken);

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
        if (activatedSlot is null && await FindSlotNamingAsync(command) is { } named)
        {
            logger?.LogWarning(
                failure,
                "Activation {ActivationId} of definition {DefinitionId} slot {SlotName} failed at step {FailedStep}, but the slot names it already; completing it rather than compensating",
                command.ActivationId,
                command.Executable.Identity.DefinitionId,
                command.SlotName,
                failedStep);
            return await DeferToSlotAsync(
                command,
                named,
                new(false, WorkflowActivationOutcome.Failed, named, Diagnostic: Truncate(SafeMessage(failure)), FailedStep: failedStep));
        }

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

    /// <summary>
    /// Answers a call whose activation <paramref name="slot"/> names (<see cref="FindSlotNamingAsync"/>) as
    /// <see cref="ActivateAsync"/> would answer it a moment later: it completes the activation, then reports it already
    /// active. Nothing is compensated. When there is nothing to report, because the slot moved on or the activation's
    /// reference is no longer live, the writer that changed them owns the activation, and the call reports
    /// <paramref name="uncompensated"/>.
    /// </summary>
    private async ValueTask<WorkflowActivationResult> DeferToSlotAsync(
        WorkflowActivationCommand command,
        WorkflowActivationSlot slot,
        WorkflowActivationResult uncompensated)
    {
        const string leftToSlot = "The slot names this activation, so it was left to the slot rather than compensated.";
        try
        {
            if (await CompleteServingActivationAsync(slot, CancellationToken.None) is { Outcome: WorkflowActivationOutcome.Failed } incomplete)
                return incomplete;
            return await TryResolveSameArtifactNoOpAsync(command, command.Executable.Identity.ArtifactId, CancellationToken.None) ??
                uncompensated with { Diagnostic = Truncate(Join(uncompensated.Diagnostic!, leftToSlot)) };
        }
        catch (Exception exception)
        {
            return uncompensated with { Diagnostic = Truncate(Join(uncompensated.Diagnostic!, $"{leftToSlot} {SafeMessage(exception)}")) };
        }
    }

    /// <summary>Compensates a cancelled call that has not attempted its slot transition, unless the slot names its activation.</summary>
    private async ValueTask CompensateUnlessSlotNamesAsync(WorkflowActivationCommand command, WorkflowExecutableSourceReference reference)
    {
        if (await FindSlotNamingAsync(command) is null)
            await CompensateAsync(command, reference, null);
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
        var authorityRestored = false;
        if (flipped)
        {
            authorityRestored = await CaptureAsync(failures, "Authority compensation", () => CompensateAuthorityAsync(command, activatedSlot!));
            await CaptureAsync(failures, "Replaced projection compensation", () => RestoreProjectionsAsync(command, activatedSlot!.ReplacedActivationId));
        }

        await CaptureAsync(failures, "Candidate projection compensation", () => RemoveProjectionsAsync(command.ActivationId, CancellationToken.None));
        await CaptureAsync(failures, "Reference compensation", () => RetireFailedReferenceAsync(reference));
        if (predecessorReferenceRetirementAttempted)
            await CaptureAsync(
                failures,
                "Predecessor reference compensation",
                () => RestorePredecessorReferenceAsync(predecessorReference));
        // The snapshot restore above owns a retirement this sequence made itself, and fails closed. The restore below
        // covers one it did not make: a completion's, made before this sequence captured the predecessor or reached
        // that step at all.
        var retiredHere = predecessorReferenceRetirementAttempted && predecessorReference is { DeletedAt: null };
        if (authorityRestored && !retiredHere && activatedSlot!.ReplacedActivationId is { } replaced)
            await CaptureAsync(
                failures,
                "Replaced reference compensation",
                () => RestoreReplacedReferenceAsync(replaced));
        if (flipped)
            await CaptureAsync(
                failures,
                "Observer compensation",
                () => NotifyTriggerObserversAsync(activatedSlot!.ReplacedActivationId ?? command.ActivationId, command.Executable.Identity.ArtifactId, CancellationToken.None));
        return failures.Count == 0 ? null : string.Join(" ", failures);
    }

    /// <summary>
    /// The slot serves the replaced activation again, so its reference must be live. A completion of this sequence on
    /// another node (<see cref="CompleteAsync"/>) may already have retired it as replaced, before this sequence captured
    /// it or without its knowledge (#2193). Only that retirement is undone: a reference retired for any other reason,
    /// or replaced by another activation's record, is left alone.
    /// </summary>
    private async ValueTask RestoreReplacedReferenceAsync(string replacedActivationId)
    {
        var current = await sourceReferenceStore.FindAsync(WorkflowActivationReferenceIdentity.Create(replacedActivationId), CancellationToken.None);
        if (current is not { DeletedAt: not null } ||
            !StringComparer.Ordinal.Equals(current.DeletedReason, ReplacedRetireReason) ||
            !StringComparer.Ordinal.Equals(current.ActivationId, replacedActivationId))
            return;

        if (!await sourceReferenceStore.TryRestoreAsync(current, current with { DeletedAt = null, DeletedReason = null }, CancellationToken.None))
            throw new InvalidOperationException(
                $"The replaced source reference '{current.SourceReferenceId}' could not be restored because it changed or the store does not support compare-and-restore.");
    }

    private async ValueTask RestorePredecessorReferenceAsync(WorkflowExecutableSourceReference? predecessorReference)
    {
        // This restores the snapshot this sequence retired. A predecessor already retired when it was captured is left to
        // RestoreReplacedReferenceAsync, which undoes only a completion's retirement and only once the predecessor holds
        // the slot again. Do not create a missing reference or overwrite a live/different record that another writer
        // may have installed meanwhile.
        if (predecessorReference is not { DeletedAt: null })
            return;

        var current = await sourceReferenceStore.FindAsync(predecessorReference.SourceReferenceId, CancellationToken.None);
        if (current is not { DeletedAt: not null } ||
            !StringComparer.Ordinal.Equals(current.DeletedReason, ReplacedRetireReason) ||
            !WorkflowExecutableSourceReferenceComparer.SameIdentity(current, predecessorReference))
            return;

        // The writer owns the compare-and-restore operation. It must condition the write on this exact retired
        // snapshot, so a superseding writer that wins between the read above and compensation is left untouched.
        if (!await sourceReferenceStore.TryRestoreAsync(current, predecessorReference, CancellationToken.None))
            throw new InvalidOperationException(
                $"The predecessor source reference '{predecessorReference.SourceReferenceId}' could not be restored because it changed or the store does not support compare-and-restore.");
    }

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

    private async ValueTask RetireFailedReferenceAsync(WorkflowExecutableSourceReference reference)
    {
        var current = await sourceReferenceStore.FindAsync(reference.SourceReferenceId, CancellationToken.None);
        if (current is not { DeletedAt: null } ||
            !WorkflowExecutableSourceReferenceComparer.SameIdentity(current, reference))
            return;

        var retired = current.Retire(timeProvider.GetUtcNow(), FailedRetireReason);
        if (!await sourceReferenceStore.TryRetireAsync(current, retired, CancellationToken.None))
        {
            throw new InvalidOperationException(
                $"Source reference '{reference.SourceReferenceId}' changed while failed-activation compensation was retiring it.");
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

    private static async ValueTask<bool> CaptureAsync(List<string> failures, string label, Func<ValueTask> step)
    {
        try
        {
            await step();
            return true;
        }
        catch (Exception exception)
        {
            failures.Add($"{label} failed: {SafeMessage(exception)}");
            return false;
        }
    }

    private static bool NotRequestedCancellation(Exception exception, CancellationToken cancellationToken) =>
        exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested;

    private static string Join(string message, string? compensationFailure) =>
        compensationFailure is null ? message : $"{message} {compensationFailure}";

    private static string Truncate(string message) => message.Length <= MaximumDiagnosticLength ? message : message[..MaximumDiagnosticLength];

    private static string SafeMessage(Exception exception) =>
        Truncate(string.IsNullOrWhiteSpace(exception.Message) ? exception.GetType().Name : exception.Message);

    private sealed class SourceReferenceResumeConflictException(string message) : InvalidOperationException(message);
}
