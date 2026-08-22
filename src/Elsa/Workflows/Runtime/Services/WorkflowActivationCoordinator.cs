using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.Logging;

namespace Elsa.Workflows.Runtime.Services;

/// <summary>
/// The one activation lifecycle, shared by every path that makes a workflow executable live — or stops it being
/// live (FR-B-006).
/// </summary>
/// <remarks>
/// <para>
/// <b>Both directions live here (T121).</b> Retraction used to be a second component in the publishing family
/// (<c>PublicationProjectionReconciler</c>) that independently knew how to prepare, activate and remove the same
/// projections. Two paths obliged to stay in step is a defect generator, and it generated one: T044b changed how
/// the recurring projection is prepared here and not there, so a failed unpublish silently stopped a workflow's
/// timers. Deactivation is a public member of this type so that the preparation, its ordering and its
/// compensation have exactly one implementation.
/// </para>
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
    IRecurringTriggerScheduleProjectionPreparer? recurringSchedulePreparer = null,
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

        GuardComposition(definitionId, command.SlotName, command.ActivationId);

        var noOp = await TryResolveSameArtifactNoOpAsync(command, identity.ArtifactId, cancellationToken);
        if (noOp is not null)
            return noOp;

        // The coordinator owns reference identity so that any activation id resolves to its reference with a
        // plain FindAsync (see WorkflowActivationReferenceIdentity); the caller's provenance fields ride along
        // untouched.
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
        catch (Exception exception) when (exception is WorkflowExecutableRootWriteLeaseUnavailableException
                                             or WorkflowExecutableRootWriteLeaseLostException)
        {
            // §2.23.5 wraps INFRASTRUCTURE faults (JsonException, IOException, storage) so nothing raw crosses a
            // feature boundary. These two are already DOMAIN exceptions of this very layer: they carry their own
            // identifiers and callers — publishing's deletion-guard contract among them — catch them by type.
            // Wrapping them would add no information and destroy that type-based catch.
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
    /// The retraction half of the lifecycle: CAS the slot empty, remove the retracted activation's serving
    /// projections, notify observers — and put all three back if the removal does not finish.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Deliberately <b>not</b> under the root-write lease that <see cref="ActivateAsync"/> takes. That lease
    /// exists to fence reference garbage collection out while an artifact is gaining its first live reference;
    /// deactivation writes no reference and removes projections, and the caller retires the reference afterwards,
    /// so a GC pass that runs concurrently sees a slot that is already empty and an artifact that is already
    /// unreferenced — which is precisely what it is entitled to reclaim.
    /// </para>
    /// <para>
    /// The compensation reuses <see cref="PrepareProjectionsAsync"/> and <see cref="ActivateProjectionsAsync"/>
    /// rather than owning a second copy of them (T121). Anything that changes how a projection is prepared
    /// therefore changes both directions of the lifecycle at once.
    /// </para>
    /// </remarks>
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

        // Nothing live means nothing to retract. Writing anyway would move the slot revision for a request that
        // changed nothing, which turns a harmless repeated unpublish into a CAS conflict for the next writer.
        if (slot?.ActiveActivationId is not { } activationId)
            return new WorkflowActivationResult(
                true,
                WorkflowActivationOutcome.AlreadyInactive,
                slot ?? EmptySlot(definitionId, command.SlotName));

        // The same composition guards activation runs, for a sharper reason: this path's compensation RE-PREPARES
        // the projections. A recurring store with no preparer would restore an activation whose timers never fire
        // again, reported nowhere. Refuse before the first write.
        GuardComposition(definitionId, command.SlotName, activationId);

        // Step 1 — the slot CAS. The authority owns the revision and ownership rules, so a foreign source or a
        // stale revision is refused here and nothing else in the sequence runs.
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
        }
        catch (Exception exception) when (NotRequestedCancellation(exception, cancellationToken))
        {
            // Nothing flipped, so there is nothing to compensate.
            return new WorkflowActivationResult(
                false,
                WorkflowActivationOutcome.Failed,
                slot,
                null,
                activationId,
                WorkflowActivationConflict.None,
                Truncate(SafeMessage(exception)),
                WorkflowActivationStep.SlotTransition);
        }

        if (!transition.Succeeded)
            return new WorkflowActivationResult(
                false,
                WorkflowActivationOutcome.Conflict,
                transition.Slot,
                null,
                activationId,
                transition.Conflict,
                Truncate(transition.Diagnostic ?? "The activation slot transition was refused."));

        // Step 2 — the retracted activation's projections stop existing. The slot already stopped serving them,
        // so this is cleanup behind a decision that has been made, not part of making it.
        try
        {
            await RemoveProjectionsAsync(activationId, cancellationToken);
        }
        catch (Exception exception) when (NotRequestedCancellation(exception, cancellationToken))
        {
            return await FailDeactivationAsync(command, activationId, transition, WorkflowActivationStep.ProjectionRemoval, exception);
        }

        // Step 3 — notify derived projections (route tables and the like), after the removal, so an observer
        // never reads a serving surface that is halfway retracted.
        try
        {
            await NotifyTriggerObserversAsync(activationId, command.Executable.Identity.ArtifactId, cancellationToken);
        }
        catch (Exception exception) when (NotRequestedCancellation(exception, cancellationToken))
        {
            return await FailDeactivationAsync(command, activationId, transition, WorkflowActivationStep.TriggerObserverNotification, exception);
        }

        return new WorkflowActivationResult(
            true,
            WorkflowActivationOutcome.Deactivated,
            transition.Slot,
            null,
            activationId);
    }

    /// <summary>
    /// The composition this coordinator refuses to run at all, checked before the first write of either
    /// lifecycle direction.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The trigger serving spine is registered by <c>WorkflowsRuntimeTriggers</c>, not by <c>AddWorkflowRuntime()</c>.
    /// An activation without it would silently produce a definition that no stimulus can ever start.
    /// </para>
    /// <para>
    /// Same rule, one projection over: a recurring store with no preparer means the recurring projection is never
    /// written, and the store refuses to activate an unprepared projection. Left unchecked that surfaces
    /// <i>after</i> the slot CAS, so the request lands in compensation instead of failing fast. The two are
    /// registered together by <c>WorkflowsRuntimeRecurringTriggers</c>; this refuses the composition that pulled
    /// them apart.
    /// </para>
    /// </remarks>
    private void GuardComposition(string definitionId, string slotName, string activationId)
    {
        if (triggerIndexer is null || triggerBindingStore is null)
            throw new WorkflowActivationException(
                definitionId,
                slotName,
                activationId,
                "Workflow activation requires the trigger serving spine (IWorkflowTriggerIndexer and IWorkflowTriggerBindingStore). " +
                "Compose the WorkflowsRuntimeTriggers feature before activating.");

        if (recurringScheduleStore is not null && recurringSchedulePreparer is null)
            throw new WorkflowActivationException(
                definitionId,
                slotName,
                activationId,
                "Workflow activation found an IRecurringTriggerScheduleStore with no IRecurringTriggerScheduleProjectionPreparer. " +
                "The recurring projection would never be prepared and the activation would fail after the slot transition. " +
                "Compose the WorkflowsRuntimeRecurringTriggers feature, which registers both, or register a preparer.");
    }

    /// <summary>
    /// FR-B-006's idempotent no-op: a request for the artifact the slot already serves writes nothing and
    /// succeeds — unless it carries an explicit takeover intent against a different owner, in which case
    /// ownership must move even though nothing else does.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This check lives here rather than on the authority on purpose — the slot carries no <c>ArtifactId</c>, so
    /// only the coordinator (which owns activation↔reference identity) can answer "same artifact?". Pushing it
    /// down would force the ledger to duplicate artifact state it has no other use for.
    /// </para>
    /// <para>
    /// "Same artifact" has to mean "same activation outcome", so the candidate's <b>tenant</b> is part of the
    /// comparison. Two tenants legitimately share one content-addressed artifact while each expecting its own
    /// live source reference; matching on the artifact id alone would swallow the second tenant's activation and
    /// leave it serving the first tenant's provenance. This is not a tenant axis on the slot — the slot stays
    /// keyed <c>(DefinitionId, SlotName)</c> — it is the candidate reference's own tenant, which rides along on
    /// the command, being compared against the reference the slot currently serves.
    /// </para>
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

        // Ownership is decided before, and independently of, whether the projections change (FR-B-006, amended
        // 2026-08-22). Sameness is evaluated first here, so without this an explicit takeover of an artifact the
        // slot already serves was swallowed as a no-op: the caller got a success, ownership stayed with the
        // incumbent, and the later unpublish refused with a foreign-owner conflict. Falling through runs the real
        // sequence, which transfers ownership and consumes a revision even though the serving projections are
        // byte-identical -- an ownership-only transition is still a transition. A request *without* takeover
        // still no-ops, which is what stops reconciliation reclaiming a slot publishing owns.
        if (command.OwnershipIntent == WorkflowActivationOwnershipIntent.TakeOver &&
            current.Source is { } incumbent &&
            !incumbent.IsSameOwnerAs(command.Source))
        {
            logger?.LogDebug(
                "Activation {ActivationId} of definition {DefinitionId} slot {SlotName} serves the same artifact as "
                + "{ActiveActivationId}, but carries takeover intent against owner {Owner}; running the full sequence "
                + "so ownership transfers.",
                command.ActivationId,
                command.Executable.Identity.DefinitionId,
                command.SlotName,
                activeActivationId,
                incumbent.Describe());
            return null;
        }

        var activeReference = await sourceReferenceStore.FindAsync(
            WorkflowActivationReferenceIdentity.Create(activeActivationId),
            cancellationToken);

        // A retired reference means the active activation is no longer backed by live serving provenance, so it
        // cannot stand in for the candidate — fall through and activate properly. Same for a different tenant:
        // the artifact matches but the activation the caller asked for does not exist yet.
        if (activeReference is not { DeletedAt: null } ||
            !StringComparer.Ordinal.Equals(activeReference.ArtifactId, candidateArtifactId) ||
            !StringComparer.Ordinal.Equals(activeReference.TenantId, command.Reference.TenantId))
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
            return await FailAsync(command, reference, activatedSlot: null, WorkflowActivationStep.SourceReferenceMint, exception);
        }

        // Step 2 — prepare BOTH projections in non-serving state.
        try
        {
            await PrepareProjectionsAsync(command.Executable, command.ActivationId, slotId, cancellationToken);
        }
        catch (Exception exception) when (NotRequestedCancellation(exception, cancellationToken))
        {
            return await FailAsync(command, reference, activatedSlot: null, WorkflowActivationStep.ProjectionPreparation, exception);
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
                    timeProvider.GetUtcNow(),
                    // Passed through verbatim. The coordinator neither adds nor removes takeover intent: the
                    // caller is the only party that knows whether an explicit operator decision is behind the
                    // request, and inferring it here would put the rule back inside the runtime (T118).
                    command.OwnershipIntent),
                cancellationToken);
        }
        catch (Exception exception) when (NotRequestedCancellation(exception, cancellationToken))
        {
            return await FailAsync(command, reference, activatedSlot: null, WorkflowActivationStep.SlotTransition, exception);
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
                Truncate(Join(transition.Diagnostic ?? "The activation slot transition was refused.", conflictCompensation)),
                CompensationDiagnostic: conflictCompensation);
        }

        // Step 4 — make both projections serve, and retire the replaced activation's projections with them.
        try
        {
            await ActivateProjectionsAsync(command.ActivationId, transition.ReplacedActivationId, cancellationToken);
        }
        catch (Exception exception) when (NotRequestedCancellation(exception, cancellationToken))
        {
            return await FailAsync(command, reference, transition, WorkflowActivationStep.ProjectionActivation, exception);
        }

        // Step 5 — notify derived projections (route tables and the like). An observer throw fails the
        // activation, matching the indexer's existing rule: a stale derived projection is an unindexed trigger.
        try
        {
            await NotifyTriggerObserversAsync(command.ActivationId, command.Executable.Identity.ArtifactId, cancellationToken);
        }
        catch (Exception exception) when (NotRequestedCancellation(exception, cancellationToken))
        {
            return await FailAsync(command, reference, transition, WorkflowActivationStep.TriggerObserverNotification, exception);
        }

        // Step 6 — retire the predecessor's reference so the GC can eventually reclaim its artifact.
        try
        {
            await RetirePredecessorReferenceAsync(command, transition.ReplacedActivationId, cancellationToken);
        }
        catch (Exception exception) when (NotRequestedCancellation(exception, cancellationToken))
        {
            return await FailAsync(command, reference, transition, WorkflowActivationStep.PredecessorReferenceRetirement, exception);
        }

        return new WorkflowActivationResult(
            true,
            WorkflowActivationOutcome.Activated,
            transition.Slot,
            reference,
            transition.ReplacedActivationId);
    }

    /// <summary>
    /// Prepares every serving projection through exactly <b>one</b> owned write per projection (FR-B-006 writer
    /// census, finding 3).
    /// </summary>
    /// <remarks>
    /// <para>
    /// One contract per projection, each called here in its own right (T044b): <see cref="IWorkflowTriggerIndexer"/>
    /// prepares the trigger bindings and <see cref="IRecurringTriggerScheduleProjectionPreparer"/> prepares the
    /// recurring schedules — the latter unconditionally, so an engine that composes the recurring store with no
    /// recurring providers still gets an explicit empty projection. The read-back-then-re-prepare this replaced
    /// (inherited from the deleted <c>PublicationProjectionReconciler</c>) wrote the schedule projection a second
    /// time and, worse, made the recurring projection's durability depend on a separate delivery record from the
    /// one that governed the write that actually produced it.
    /// </para>
    /// <para>
    /// The recurring preparer runs <b>first</b>, which is the ordering the retired decorator provided from the
    /// inside: it materializes and validates every recurrence before writing, so an invalid or exhausted
    /// recurrence fails the activation with neither projection mutated.
    /// </para>
    /// <para>
    /// <b>This is the only preparation in the engine</b> (T121). Activation calls it forwards and deactivation's
    /// compensation calls it to put a retracted activation back, so the ordering invariant above cannot be
    /// half-updated: there is no second copy of it to forget. The retraction path used to have one, and when
    /// T044b changed the participants here it was not changed there — which is the defect this consolidation
    /// makes unrepresentable rather than merely documented against.
    /// </para>
    /// </remarks>
    private async ValueTask PrepareProjectionsAsync(
        WorkflowExecutable executable,
        string activationId,
        string slotId,
        CancellationToken cancellationToken)
    {
        // ORDERING INVARIANT: recurrences are materialized and validated BEFORE any binding is written.
        if (recurringSchedulePreparer is not null)
            await recurringSchedulePreparer.PrepareActivationAsync(executable, activationId, slotId, cancellationToken);

        await triggerIndexer!.PrepareActivationAsync(executable, activationId, slotId, cancellationToken);
    }

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

        var bindings = await triggerBindingStore!.ListAllByActivationAsync(activationId, cancellationToken);
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
        WorkflowActivationStep failedStep,
        Exception failure)
    {
        logger?.LogWarning(
            failure,
            "Activation {ActivationId} of definition {DefinitionId} slot {SlotName} failed at step {FailedStep}; compensating",
            command.ActivationId,
            command.Executable.Identity.DefinitionId,
            command.SlotName,
            failedStep);

        var compensationFailure = await CompensateAsync(command, reference, activatedSlot);
        var slot = await CurrentSlotAsync(command.Executable.Identity.DefinitionId, command.SlotName);
        return new WorkflowActivationResult(
            false,
            WorkflowActivationOutcome.Failed,
            slot,
            null,
            activatedSlot?.ReplacedActivationId,
            WorkflowActivationConflict.None,
            Truncate(Join(SafeMessage(failure), compensationFailure)),
            failedStep,
            compensationFailure);
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
        var slot = await CurrentSlotAsync(command.Executable.Identity.DefinitionId, command.SlotName);
        return new WorkflowActivationResult(
            false,
            WorkflowActivationOutcome.Failed,
            slot,
            null,
            activationId,
            WorkflowActivationConflict.None,
            Truncate(Join(SafeMessage(failure), compensationFailure)),
            failedStep,
            compensationFailure);
    }

    /// <summary>
    /// Puts the retracted activation back in the same order activation builds it up: projections prepared, then
    /// the slot, then the projections activated, then the observers that derive from them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Restoring the projections means <b>re-preparing them from the artifact</b>, not re-activating what is
    /// still there: <see cref="RemoveProjectionsAsync"/> deletes rows, and a partially completed removal has
    /// nothing left to flip back on. This is the force-replay the deleted publication reconciler performed
    /// through its delivery ledger, expressed as what it always was — an unconditional re-preparation — and it
    /// runs through the <see cref="PrepareProjectionsAsync"/> every activation uses, so the recurring projection
    /// cannot be forgotten here while activation remembers it.
    /// </para>
    /// <para>
    /// Best-effort, like activation's compensation: every step is attempted even if an earlier one failed, and
    /// what did not converge is reported rather than thrown, so the original failure is never masked.
    /// </para>
    /// </remarks>
    private async ValueTask<string?> CompensateDeactivationAsync(
        WorkflowDeactivationCommand command,
        string activationId,
        WorkflowActivationTransition transition)
    {
        var definitionId = command.Executable.Identity.DefinitionId;
        var slotId = WorkflowActivationSlotIdentity.Create(definitionId, command.SlotName);
        var failures = new List<string>();

        // Prepared BEFORE the slot goes back, mirroring activation's prepare -> CAS -> activate. Restoring the
        // slot first would republish an activation whose projections had just been deleted, so a stimulus landing
        // in that window would find a live slot serving nothing -- the exact state activation's ordering exists to
        // make unreachable. Preparation writes rows that are inert until activated, so doing it first is safe even
        // if the slot never comes back.
        await CaptureAsync(
            failures,
            "Projection preparation",
            () => PrepareProjectionsAsync(command.Executable, activationId, slotId, CancellationToken.None));

        await CaptureAsync(failures, "Authority compensation", async () =>
        {
            // The CAS presents the POST-flip revision: we are undoing our own transition. Ownership is safe to
            // assert with the retracting source because a foreign-owned slot could not have been deactivated.
            var compensation = await authority.TryActivateAsync(
                new WorkflowActivationSlotRequest(
                    definitionId,
                    command.SlotName,
                    activationId,
                    command.Source,
                    transition.Slot.Revision,
                    timeProvider.GetUtcNow()),
                CancellationToken.None);
            if (!compensation.Succeeded)
                throw new WorkflowActivationException(
                    definitionId,
                    command.SlotName,
                    activationId,
                    $"Deactivation of activation '{activationId}' failed and the slot authority could not be restored: {compensation.Diagnostic}");
        });

        await CaptureAsync(
            failures,
            "Projection activation",
            () => ActivateProjectionsAsync(activationId, replacedActivationId: null, CancellationToken.None));

        // Observers last, so they are reconciled only once the restored activation is fully serving again.
        await CaptureAsync(
            failures,
            "Observer compensation",
            () => NotifyTriggerObserversAsync(activationId, command.Executable.Identity.ArtifactId, CancellationToken.None));

        return failures.Count == 0 ? null : string.Join(" ", failures);
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

        await CaptureAsync(failures, "Candidate projection compensation", () => RemoveProjectionsAsync(command.ActivationId, CancellationToken.None));
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
        //
        // The predecessor is restored under ITS OWN source, with an explicit takeover so the restore is not
        // refused by the ownership we just installed. Asserting the candidate's source here instead would leave a
        // rolled-back takeover (T118) serving the predecessor's activation under the claimant's name — and the
        // next reconcile pass would then be refused for a slot the claimant does not, in fact, hold.
        var compensation = activatedSlot.ReplacedActivationId is { } replacedActivationId
            ? await authority.TryActivateAsync(
                new WorkflowActivationSlotRequest(
                    definitionId,
                    command.SlotName,
                    replacedActivationId,
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

    /// <summary>
    /// Re-activates the predecessor's projections. Unconditional by design — this IS the force-replay semantic:
    /// the coordinator carries no delivery-intent ledger that could short-circuit a repeated activate.
    /// </summary>
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

    private async ValueTask<WorkflowActivationSlot> CurrentSlotAsync(string definitionId, string slotName)
    {
        WorkflowActivationSlot? slot = null;
        try
        {
            slot = await authority.FindAsync(definitionId, slotName, CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger?.LogWarning(
                exception,
                "Could not read back the activation slot of definition {DefinitionId} slot {SlotName} after a failed activation",
                definitionId,
                slotName);
        }

        return slot ?? EmptySlot(definitionId, slotName);
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

    private static string Truncate(string message) =>
        message.Length <= MaximumDiagnosticLength ? message : message[..MaximumDiagnosticLength];

    private static string SafeMessage(Exception exception) =>
        Truncate(string.IsNullOrWhiteSpace(exception.Message) ? exception.GetType().Name : exception.Message);
}
