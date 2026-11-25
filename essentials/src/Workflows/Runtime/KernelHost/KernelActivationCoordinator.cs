using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Options;
using Kernel.Native;
using Kernel.WorkflowActor;
using Microsoft.Extensions.Logging;

namespace Elsa.Workflows.Runtime.KernelHost;

/// <summary>
/// The activation coordinator of a kernel host (definitions on the kernel, owner decision 1). It keeps the old core's
/// duties: it mints or resumes the activation's source reference, runs each sequence under a root-write lease, enforces the
/// channel policy, answers an unchanged republish as <see cref="WorkflowActivationOutcome.AlreadyActive"/>, checks and
/// repairs serving, discards an activation that fails before the switch and reverts one that fails after it, and on
/// deactivate retires the references of the other serving activations. It moves the slot through
/// <see cref="IWorkflowActivationSwitch"/> with an empty projection in each projection store. What serves is its
/// <see cref="KernelServing"/>:
/// <list type="number">
/// <item>Map: the executable becomes a Native workflow (<see cref="NativeExecutableMapper"/>), or the activation fails at
/// <see cref="WorkflowActivationStep.Mapping"/> before anything is minted.</item>
/// <item>Check: the serving set's routes and schedules are built with the candidate in place of what its slot serves; a
/// conflict fails at <see cref="WorkflowActivationStep.ServingCheck"/>.</item>
/// <item>Switch: the slot moves, under the lease.</item>
/// <item>Serve: the workflow joins the registry, the routes are rebuilt, and each schedule's run is pointed at it and its
/// schedule set at the slot's revision. A failure reverts the switch.</item>
/// </list>
/// After each committed activation, deactivation and repair it tells the <see cref="IWorkflowTriggerIndexObserver"/>s, as
/// the old core does, so that Studio's workflow library refreshes; a failure after an activation or a deactivation reverts
/// it. A kernel host composes the observers it needs and leaves out the old core's HTTP route table.
/// Activations run one at a time on a host, also across coordinators that share its <see cref="KernelServing"/>.
/// </summary>
public sealed class KernelActivationCoordinator(
    IWorkflowActivationAuthority authority,
    IWorkflowActivationSwitch activationSwitch,
    IWorkflowExecutableSourceReferenceStore sourceReferenceStore,
    IWorkflowExecutableRootWriteLeaseManager rootWriteLeaseManager,
    IWorkflowExecutableStore executableStore,
    IWorkflowTriggerBindingStore triggerBindingStore,
    KernelServing serving,
    IScheduleActors schedules,
    TimeProvider timeProvider,
    IRecurringTriggerScheduleStore? recurringScheduleStore = null,
    ActivationChannelOptions? channels = null,
    IEnumerable<IWorkflowTriggerIndexObserver>? triggerObservers = null,
    ILogger<KernelActivationCoordinator>? logger = null) : IWorkflowActivationCoordinator
{
    public const string ReplacedRetireReason = "activation-replaced";
    public const string FailedRetireReason = "activation-failed";

    private const int MaximumDiagnosticLength = 512;
    private readonly ActivationChannelOptions _channels = channels ?? new();
    private readonly IReadOnlyCollection<IWorkflowTriggerIndexObserver> _observers = triggerObservers?.ToArray() ?? [];

    public async ValueTask<WorkflowActivationResult> ActivateAsync(WorkflowActivationCommand command, CancellationToken cancellationToken = default)
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
                $"The supplied source reference points at artifact '{command.Reference.ArtifactId}' but the executable is '{identity.ArtifactId}'.", nameof(command));

        return await serving.RunAsync(() => ActivateCoreAsync(command, cancellationToken), cancellationToken);
    }

    private async ValueTask<WorkflowActivationResult> ActivateCoreAsync(WorkflowActivationCommand command, CancellationToken cancellationToken)
    {
        var identity = command.Executable.Identity;
        var definitionId = identity.DefinitionId;
        if (!_channels.Allows(command.SlotName))
            return new(false, WorkflowActivationOutcome.ChannelNotAllowed,
                await authority.FindAsync(definitionId, command.SlotName, cancellationToken) ?? EmptySlot(definitionId, command.SlotName),
                Diagnostic: $"Activation channel '{command.SlotName}' is not declared for this shell.",
                FailedStep: WorkflowActivationStep.ChannelValidation);

        // 1. Map, before a reference is minted or a lease taken, so a refusal leaves nothing to discard.
        Workflow workflow;
        try
        {
            workflow = NativeExecutableMapper.Map(command.Executable);
        }
        catch (NativeMappingRefusedException refused)
        {
            return new(false, WorkflowActivationOutcome.Failed, await CurrentSlotAsync(definitionId, command.SlotName),
                Diagnostic: Truncate($"{refused.Code}: {refused.Message}"), FailedStep: WorkflowActivationStep.Mapping);
        }

        var slotId = WorkflowActivationSlotIdentity.Create(definitionId, command.SlotName);
        // A same-artifact request is answered from the slot once its activation serves; a slot that serves an activation
        // this host does not serve yet (after a restart) is served first.
        if (await authority.FindAsync(definitionId, command.SlotName, cancellationToken) is { ActiveActivationId: not null } occupied)
        {
            var (current, unserved) = await CheckServingAsync(occupied, cancellationToken);
            if (unserved is not null) return unserved;
            if (TryResolveSameArtifactNoOp(command, await FindLiveReferenceAsync(current, cancellationToken), current) is { } noOp)
                return noOp;
        }

        // 2. Check the routes and schedules over the serving set with the candidate in its slot's place.
        KernelServingPlan plan;
        try
        {
            plan = serving.Plan(slotId, new KernelServedSlot(slotId, definitionId, command.SlotName, command.ActivationId, workflow, 0));
        }
        catch (Exception refused) when (refused is ArgumentException or InvalidOperationException)
        {
            return new(false, WorkflowActivationOutcome.Failed, await CurrentSlotAsync(definitionId, command.SlotName),
                Diagnostic: Truncate(refused.Message), FailedStep: WorkflowActivationStep.ServingCheck);
        }

        var reference = command.Reference with
        {
            SourceReferenceId = WorkflowActivationReferenceIdentity.Create(command.ActivationId),
            ActivationId = command.ActivationId,
            SlotId = slotId,
        };

        // The lease is this call's alone, as on the old core (#2274).
        WorkflowActivationResult? result = null;
        try
        {
            var leaseId = $"activation:{command.ActivationId}:{Guid.NewGuid():N}";
            await rootWriteLeaseManager.ExecuteAsync(identity, leaseId,
                async leaseToken => result = await RunSequenceAsync(command, reference, plan, leaseToken), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (WorkflowActivationException) { throw; }
        catch (Exception exception) when (exception is WorkflowExecutableRootWriteLeaseUnavailableException or WorkflowExecutableRootWriteLeaseLostException) { throw; }
        catch (Exception exception)
        {
            throw new WorkflowActivationException(definitionId, command.SlotName, command.ActivationId,
                $"Activation '{command.ActivationId}' of definition '{definitionId}' slot '{command.SlotName}' could not acquire its executable retention lease.", exception);
        }

        return result ?? throw new WorkflowActivationException(definitionId, command.SlotName, command.ActivationId,
            $"Activation '{command.ActivationId}' produced no outcome; the retention lease did not run the activation sequence.");
    }

    private async ValueTask<WorkflowActivationResult> RunSequenceAsync(WorkflowActivationCommand command, WorkflowExecutableSourceReference reference,
        KernelServingPlan plan, CancellationToken cancellationToken)
    {
        var definitionId = command.Executable.Identity.DefinitionId;
        try
        {
            reference = await MintOrResumeSourceReferenceAsync(reference, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch (SourceReferenceResumeConflictException conflict)
        {
            return new(false, WorkflowActivationOutcome.Failed, await CurrentSlotAsync(definitionId, command.SlotName),
                Diagnostic: Truncate(SafeMessage(conflict)), FailedStep: WorkflowActivationStep.SourceReferenceMint);
        }
        catch (Exception exception)
        {
            return await AbandonAsync(command, reference, plan, cancellationToken, exception, WorkflowActivationStep.SourceReferenceMint);
        }

        try
        {
            await PrepareProjectionsAsync(command.ActivationId, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch (Exception exception)
        {
            return await AbandonAsync(command, reference, plan, cancellationToken, exception, WorkflowActivationStep.ProjectionPreparation);
        }

        // 3. Switch.
        WorkflowActivationTransition transition;
        try
        {
            transition = await activationSwitch.TryActivateAsync(new WorkflowActivationSlotRequest(definitionId, command.SlotName, command.ActivationId,
                command.Source, command.ExpectedRevision, timeProvider.GetUtcNow(), command.OwnershipIntent), cancellationToken);
        }
        catch (Exception exception)
        {
            // The switch may have committed before it threw; abandoning reads what it did, since a discard refuses an
            // activation that serves.
            return await AbandonAsync(command, reference, plan, cancellationToken, exception, WorkflowActivationStep.SlotTransition);
        }

        if (StringComparer.Ordinal.Equals(transition.ReplacedActivationId, command.ActivationId))
            transition = transition with { ReplacedActivationId = null, ReplacedSource = null };
        if (!transition.Succeeded)
        {
            var refused = await AbandonAsync(command, reference, plan,
                _ => new(false, WorkflowActivationOutcome.Conflict, transition.Slot, Conflict: transition.Conflict,
                    Diagnostic: Truncate(transition.Diagnostic ?? "The activation slot transition was refused.")),
                "lost its slot transition to another call activating it");
            cancellationToken.ThrowIfCancellationRequested();
            return refused;
        }

        // 4. Serve, then tell the trigger observers (Studio's workflow library among them). The activation stands from here
        // on, so a cancellation does not stop the serving it needs; a failure of either reverts it.
        try
        {
            await ServeAsync(plan, transition.Slot.Revision, CancellationToken.None);
        }
        catch (Exception exception)
        {
            return await RevertAsync(command, reference, plan, transition, exception, WorkflowActivationStep.ProjectionActivation);
        }
        try
        {
            await NotifyObserversAsync(definitionId, command.Executable.Identity.ArtifactId, CancellationToken.None);
        }
        catch (Exception exception)
        {
            return await RevertAsync(command, reference, plan, transition, exception, WorkflowActivationStep.TriggerObserverNotification);
        }

        return new(true, WorkflowActivationOutcome.Activated, transition.Slot, reference, transition.ReplacedActivationId);
    }

    // ---- serving -------------------------------------------------------------------------------------------------

    /// <summary>
    /// Serves <paramref name="plan"/>: its workflow joins the registry and its routes replace the table, then each schedule of
    /// the workflow it serves has its run pointed at it and its schedule set at <paramref name="revision"/>, and each schedule
    /// only the workflow it replaces had is stopped and its run removed, in that order.
    /// </summary>
    /// <param name="replaced">
    /// The workflow whose schedules the slot served before, when this host may not serve it (a deactivation after a restart);
    /// otherwise what the slot serves on this host.
    /// </param>
    private async ValueTask ServeAsync(KernelServingPlan plan, long revision, CancellationToken cancellationToken, Workflow? replaced = null)
    {
        var state = plan.State;
        if (plan.Served is { } served)
            // The slot's revision is known only once the switch commits.
            state = state with { Slots = new Dictionary<string, KernelServedSlot>(state.Slots, StringComparer.Ordinal) { [plan.SlotId] = served with { Revision = revision } } };
        var before = (replaced ?? serving.Find(plan.SlotId)?.Workflow) is { } previous ? serving.SchedulesOf(previous) : [];
        serving.Apply(plan with { State = state });
        var after = plan.Served is { } now ? [.. state.Schedules.All.Where(schedule => schedule.Workflow == now.Workflow.Id)] : Array.Empty<NativeSchedule>();
        foreach (var schedule in after)
        {
            await schedules.RegisterRunAsync(schedule.StartKey, schedule.Start(), cancellationToken);
            await schedules.SetAsync(schedule.Key, revision, Recurring(schedule), schedule.StartKey, cancellationToken);
        }
        foreach (var schedule in before.Where(old => after.All(now => now.Key != old.Key)))
        {
            // Stop first, so no occurrence is published without a run registered.
            await schedules.StopAsync(schedule.Key, revision, cancellationToken);
            await schedules.RegisterRunAsync(schedule.StartKey, null, cancellationToken);
        }
    }

    private static RecurringSchedule Recurring(NativeSchedule schedule) =>
        new(schedule.Kind == NativeSchedule.Cron ? RecurringSchedule.Cron : RecurringSchedule.Interval, schedule.Value);

    /// <summary>
    /// Serving failed after the switch committed: the switch reverts this call's transition while the slot still stands
    /// where it left it, and the serving state and schedules return to what the slot serves then.
    /// </summary>
    private async ValueTask<WorkflowActivationResult> RevertAsync(WorkflowActivationCommand command, WorkflowExecutableSourceReference reference,
        KernelServingPlan plan, WorkflowActivationTransition transition, Exception failure, WorkflowActivationStep failedStep)
    {
        var definitionId = command.Executable.Identity.DefinitionId;
        logger?.LogWarning(failure, "Activation {ActivationId} of definition {DefinitionId} slot {SlotName} could not be served; reverting",
            command.ActivationId, definitionId, command.SlotName);
        var failures = new List<string>();
        var reverted = false;
        await CaptureAsync(failures, "Authority compensation", async () =>
        {
            reverted = await activationSwitch.TryRevertAsync(new WorkflowActivationRevert(transition, reference, command.Source, timeProvider.GetUtcNow()),
                CancellationToken.None);
            if (!reverted)
                throw new InvalidOperationException("the slot moved on after this activation's transition, so it was left to the writer that moved it.");
        });
        var slot = await CurrentSlotAsync(definitionId, command.SlotName);
        if (reverted)
        {
            await CaptureAsync(failures, "Serving compensation", async () =>
            {
                await ServeAsync(serving.Plan(plan.SlotId, plan.Previous), slot.Revision, CancellationToken.None);
            });
            await CaptureAsync(failures, "Observer compensation", () =>
                NotifyObserversAsync(definitionId, plan.Previous?.Workflow.Id ?? command.Executable.Identity.ArtifactId, CancellationToken.None));
        }

        var compensationFailure = failures.Count == 0 ? null : string.Join(" ", failures);
        return new(false, WorkflowActivationOutcome.Failed, slot, ReplacedActivationId: transition.ReplacedActivationId,
            Diagnostic: Truncate(Join(SafeMessage(failure), compensationFailure)), FailedStep: failedStep,
            CompensationDiagnostic: compensationFailure);
    }

    // ---- deactivation --------------------------------------------------------------------------------------------

    public async ValueTask<WorkflowActivationResult> DeactivateAsync(WorkflowDeactivationCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.Executable);
        ArgumentNullException.ThrowIfNull(command.Source);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.SlotName);
        ArgumentOutOfRangeException.ThrowIfNegative(command.ExpectedRevision);

        return await serving.RunAsync(() => DeactivateCoreAsync(command, cancellationToken), cancellationToken);
    }

    private async ValueTask<WorkflowActivationResult> DeactivateCoreAsync(WorkflowDeactivationCommand command, CancellationToken cancellationToken)
    {
        var definitionId = command.Executable.Identity.DefinitionId;
        var slot = await authority.FindAsync(definitionId, command.SlotName, cancellationToken);
        if (slot?.ActiveActivationId is not { } activationId)
            return new(true, WorkflowActivationOutcome.AlreadyInactive, slot ?? EmptySlot(definitionId, command.SlotName));

        // What the slot serves, whether or not this host serves it (after a restart it may not), so its schedules are stopped.
        var served = await ServedOrMappedAsync(slot, activationId, command.Executable, cancellationToken);

        WorkflowActivationTransition transition;
        IReadOnlyCollection<string> others = [];
        try
        {
            others = await ListOtherServingActivationsAsync(slot.SlotId, activationId, cancellationToken);
            transition = await activationSwitch.TryDeactivateAsync(
                new(definitionId, command.SlotName, command.Source, command.ExpectedRevision, timeProvider.GetUtcNow()), others, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            // The switch may have committed before it threw. A slot that names no activation now was emptied by it, and the
            // deactivation is finished as one that committed; one that names another was moved by another writer, whose
            // activation is served here as it stands.
            var current = await CurrentSlotAsync(definitionId, command.SlotName);
            if (current.ActiveActivationId is null && current.Revision > slot.Revision)
            {
                logger?.LogWarning(exception, "The deactivation of definition {DefinitionId} slot {SlotName} committed before its answer was lost; it is finished",
                    definitionId, command.SlotName);
                transition = new WorkflowActivationTransition(true, current, activationId);
            }
            else
            {
                if (current.ActiveActivationId is { } other && other != activationId) await CheckServingAsync(current, CancellationToken.None);
                return new(false, WorkflowActivationOutcome.Failed, current, ReplacedActivationId: activationId,
                    Diagnostic: Truncate(SafeMessage(exception)), FailedStep: WorkflowActivationStep.SlotTransition);
            }
        }
        if (!transition.Succeeded)
            return new(false, WorkflowActivationOutcome.Conflict, transition.Slot, ReplacedActivationId: activationId, Conflict: transition.Conflict,
                Diagnostic: Truncate(transition.Diagnostic ?? "The activation slot transition was refused."));

        // 5. The slot stops serving: it leaves the serving set and the routes, and each schedule is stopped, then its run
        // removed. The registry keeps the workflow for the instances that run it. The deactivation stands either way: a
        // schedule that could not be stopped is reported in the result and the log.
        string? servingFailure = null;
        try
        {
            await ServeAsync(serving.Plan(slot.SlotId, null), transition.Slot.Revision, CancellationToken.None, served?.Workflow);
        }
        catch (Exception exception)
        {
            servingFailure = $"Serving compensation failed: {SafeMessage(exception)}";
            logger?.LogError(exception, "Definition {DefinitionId} slot {SlotName} is deactivated, but its schedules could not all be stopped", definitionId, command.SlotName);
        }

        try
        {
            await NotifyObserversAsync(definitionId, served?.Workflow.Id ?? command.Executable.Identity.ArtifactId, CancellationToken.None);
        }
        catch (Exception exception)
        {
            return await FailDeactivationAsync(command, slot, served, transition, exception);
        }

        await RetireReferencesAsync(slot, others);
        return new(true, WorkflowActivationOutcome.Deactivated, transition.Slot, ReplacedActivationId: activationId,
            Diagnostic: servingFailure, CompensationDiagnostic: servingFailure);
    }

    /// <summary>
    /// A trigger observer failed after the deactivation committed, which fails it, as on the old core: the activation is
    /// prepared again and switched back on at the revision the deactivation produced, so a writer that moved the slot since
    /// keeps it, and it serves again.
    /// </summary>
    private async ValueTask<WorkflowActivationResult> FailDeactivationAsync(WorkflowDeactivationCommand command, WorkflowActivationSlot slot,
        KernelServedSlot? served, WorkflowActivationTransition transition, Exception failure)
    {
        var definitionId = slot.WorkflowDefinitionId;
        var activationId = slot.ActiveActivationId!;
        logger?.LogWarning(failure, "Deactivation of activation {ActivationId} of definition {DefinitionId} slot {SlotName} failed at its trigger observers; compensating",
            activationId, definitionId, slot.SlotName);
        var failures = new List<string>();
        WorkflowActivationTransition? restored = null;
        await CaptureAsync(failures, "Projection preparation", () => PrepareProjectionsAsync(activationId, CancellationToken.None));
        await CaptureAsync(failures, "Authority compensation", async () =>
        {
            restored = await activationSwitch.TryActivateAsync(new WorkflowActivationSlotRequest(definitionId, slot.SlotName, activationId, command.Source,
                transition.Slot.Revision, timeProvider.GetUtcNow()), CancellationToken.None);
            if (!restored.Succeeded)
                throw new InvalidOperationException($"The deactivation slot transition could not be restored: {restored.Diagnostic}");
        });
        if (restored is { Succeeded: true })
        {
            if (served is not null)
                await CaptureAsync(failures, "Serving compensation", () =>
                    ServeAsync(serving.Plan(slot.SlotId, served), restored.Slot.Revision, CancellationToken.None));
            await CaptureAsync(failures, "Observer compensation", () =>
                NotifyObserversAsync(definitionId, served?.Workflow.Id ?? command.Executable.Identity.ArtifactId, CancellationToken.None));
        }

        var compensationFailure = failures.Count == 0 ? null : string.Join(" ", failures);
        return new(false, WorkflowActivationOutcome.Failed, await CurrentSlotAsync(definitionId, slot.SlotName), ReplacedActivationId: activationId,
            Diagnostic: Truncate(Join(SafeMessage(failure), compensationFailure)), FailedStep: WorkflowActivationStep.TriggerObserverNotification,
            CompensationDiagnostic: compensationFailure);
    }

    /// <summary>
    /// What <paramref name="slot"/>'s activation serves: this host's serving entry when it serves it, else its executable
    /// mapped again (the activation's reference names it; <paramref name="fallback"/> when it cannot be read). Null when it
    /// does not map, since then it never served here.
    /// </summary>
    private async ValueTask<KernelServedSlot?> ServedOrMappedAsync(WorkflowActivationSlot slot, string activationId, WorkflowExecutable fallback,
        CancellationToken cancellationToken)
    {
        if (serving.Find(slot.SlotId) is { } served && served.ActivationId == activationId) return served;
        var executable = fallback;
        try
        {
            if (await sourceReferenceStore.FindAsync(WorkflowActivationReferenceIdentity.Create(activationId), cancellationToken) is { } reference &&
                await executableStore.FindAsync(reference.ArtifactId, cancellationToken) is { } stored)
                executable = stored;
        }
        catch (Exception exception) when (NotRequestedCancellation(exception, cancellationToken))
        {
            logger?.LogWarning(exception, "The executable of activation {ActivationId} could not be read; the deactivation's own is used", activationId);
        }
        try
        {
            return new(slot.SlotId, slot.WorkflowDefinitionId, slot.SlotName, activationId, NativeExecutableMapper.Map(executable), slot.Revision);
        }
        catch (NativeMappingRefusedException) { return null; }
    }

    private async ValueTask<IReadOnlyCollection<string>> ListOtherServingActivationsAsync(string slotId, string activationId, CancellationToken cancellationToken)
    {
        var others = new SortedSet<string>(await triggerBindingStore.ListServingActivationIdsAsync(slotId, cancellationToken), StringComparer.Ordinal);
        if (recurringScheduleStore is not null)
            others.UnionWith(await recurringScheduleStore.ListServingActivationIdsAsync(slotId, cancellationToken));
        others.Remove(activationId);
        return others;
    }

    // ---- ensuring serving ----------------------------------------------------------------------------------------

    public async ValueTask<WorkflowActivationResult> EnsureServingAsync(string workflowDefinitionId, string slotName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowDefinitionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(slotName);
        return await serving.RunAsync(async () =>
        {
            var slot = await authority.FindAsync(workflowDefinitionId, slotName, cancellationToken);
            if (slot?.ActiveActivationId is null)
            {
                var empty = slot ?? EmptySlot(workflowDefinitionId, slotName);
                // A slot that serves nothing, while this host still serves something for it (a deactivation whose finish was
                // lost), stops serving here: its routes go, and its schedules are stopped and their runs removed.
                if (serving.Find(empty.SlotId) is not null)
                    await ServeAsync(serving.Plan(empty.SlotId, null), empty.Revision, cancellationToken);
                return new(true, WorkflowActivationOutcome.AlreadyInactive, empty);
            }
            var (current, unserved) = await CheckServingAsync(slot, cancellationToken);
            return unserved ?? new(true, current.ActiveActivationId is null ? WorkflowActivationOutcome.AlreadyInactive : WorkflowActivationOutcome.AlreadyActive, current);
        }, cancellationToken);
    }

    /// <summary>
    /// Serves every active slot, as a host does when it starts (step 6): the live activation references name the definitions
    /// with activations, the slot authority their slots, and each active one is served as <see cref="EnsureServingAsync"/> serves it.
    /// </summary>
    /// <returns>Each active slot's result; a slot that could not be served is reported failed, and the others still serve.</returns>
    public async ValueTask<IReadOnlyList<WorkflowActivationResult>> ServeActiveSlotsAsync(CancellationToken cancellationToken = default)
    {
        var definitions = new SortedSet<string>(StringComparer.Ordinal);
        string? continuation = null;
        do
        {
            var page = await sourceReferenceStore.ListPageAsync(new WorkflowExecutableSourceReferencePageQuery(liveOnly: true, now: timeProvider.GetUtcNow(),
                limit: RuntimeStorePageRequest.MaximumLimit, continuationToken: continuation), cancellationToken);
            foreach (var reference in page.Items)
                if (reference.ActivationId is not null) definitions.Add(reference.DefinitionId);
            continuation = page.NextContinuationToken;
        } while (continuation is not null);

        var results = new List<WorkflowActivationResult>();
        foreach (var slot in await authority.ListByDefinitionIdsAsync([.. definitions], cancellationToken))
            if (slot.ActiveActivationId is not null)
                results.Add(await EnsureServingAsync(slot.WorkflowDefinitionId, slot.SlotName, cancellationToken));
        return results;
    }

    /// <summary>
    /// Checks that the activation <paramref name="slot"/> names serves: through every projection store (a slot a version
    /// before #2230 left on its way to serving is repaired, as on the old core), and on this host. One this host does not
    /// serve yet, as after a restart, is mapped from its executable and served.
    /// </summary>
    private async ValueTask<(WorkflowActivationSlot Slot, WorkflowActivationResult? Unserved)> CheckServingAsync(WorkflowActivationSlot slot, CancellationToken cancellationToken)
    {
        var activationId = slot.ActiveActivationId!;
        var repaired = false;
        try
        {
            for (var repairs = 0; ; repairs++)
            {
                var states = await ProjectionStatesAsync(activationId, cancellationToken);
                if (states.All(state => state == WorkflowActivationProjectionState.Active))
                    break;
                var current = await authority.FindAsync(slot.WorkflowDefinitionId, slot.SlotName, cancellationToken) ?? EmptySlot(slot.WorkflowDefinitionId, slot.SlotName);
                if (current.Revision != slot.Revision)
                    return (current, null);
                if (repairs > 0 || !IsRepairable(states))
                    return (current, NotServingFailure(current, "its projection is missing or replaced, which only a fault or a manual change leaves. " +
                                                                 "Unpublish the slot, which turns every activation serving it off, then publish again."));
                var others = await ListOtherServingActivationsAsync(current.SlotId, activationId, cancellationToken);
                if (await activationSwitch.TryRepairAsync(current, others, cancellationToken))
                {
                    logger?.LogWarning("Activation {ActivationId} of definition {DefinitionId} slot {SlotName} did not serve everywhere and was repaired",
                        activationId, current.WorkflowDefinitionId, current.SlotName);
                    repaired = true;
                    break;
                }
            }

            // 6. Ensure this host serves it.
            if (serving.Find(slot.SlotId) is { } served && served.ActivationId == activationId && serving.Registry.Contains(served.Workflow.Id))
            {
                if (repaired) await NotifyServingAsync(slot, served.Workflow.Id);
                return (slot, null);
            }
            var reference = await sourceReferenceStore.FindAsync(WorkflowActivationReferenceIdentity.Create(activationId), cancellationToken)
                            ?? throw new InvalidOperationException($"Activation '{activationId}' has no source reference to name its artifact.");
            var executable = await executableStore.FindAsync(reference.ArtifactId, cancellationToken)
                             ?? throw new InvalidOperationException($"Artifact '{reference.ArtifactId}' of activation '{activationId}' is not in the executable store.");
            Workflow workflow;
            try { workflow = NativeExecutableMapper.Map(executable); }
            catch (NativeMappingRefusedException refused)
            {
                return (slot, NotServingFailure(slot, $"{refused.Code}: {refused.Message}", WorkflowActivationStep.Mapping));
            }
            KernelServingPlan plan;
            try { plan = serving.Plan(slot.SlotId, new KernelServedSlot(slot.SlotId, slot.WorkflowDefinitionId, slot.SlotName, activationId, workflow, slot.Revision)); }
            catch (Exception refused) when (refused is ArgumentException or InvalidOperationException)
            {
                return (slot, NotServingFailure(slot, refused.Message, WorkflowActivationStep.ServingCheck));
            }
            await ServeAsync(plan, slot.Revision, cancellationToken);
            logger?.LogInformation("Activation {ActivationId} of definition {DefinitionId} slot {SlotName} serves on this host again",
                activationId, slot.WorkflowDefinitionId, slot.SlotName);
            if (repaired) await NotifyServingAsync(slot, workflow.Id);
            return (slot, null);
        }
        catch (Exception exception) when (NotRequestedCancellation(exception, cancellationToken))
        {
            logger?.LogError(exception, "Whether activation {ActivationId} of definition {DefinitionId} slot {SlotName} serves could not be checked or repaired",
                activationId, slot.WorkflowDefinitionId, slot.SlotName);
            return (slot, NotServingFailure(slot, $"whether it serves could not be checked or repaired: {SafeMessage(exception)}"));
        }
    }

    /// <summary>
    /// Tells the trigger observers that the definition's serving changed, as the old core does after each committed
    /// activation, deactivation and repair: with no bindings, since a kernel host serves no old-core triggers, and a refresh
    /// required, which Studio's workflow library acts on.
    /// </summary>
    /// <remarks>
    /// Observers are told inside <see cref="KernelServing.RunAsync{T}"/>, whose lock is not reentrant: an observer must not
    /// call back into a coordinator, or it waits on itself.
    /// </remarks>
    private async ValueTask NotifyObserversAsync(string definitionId, string artifactId, CancellationToken cancellationToken)
    {
        foreach (var observer in _observers)
            await observer.OnTriggersIndexedAsync(new WorkflowTriggerIndexSnapshot(artifactId, []) { RequiresProjectionRefresh = true, DefinitionId = definitionId },
                cancellationToken);
    }

    /// <summary>A repaired slot serves already, so an observer that cannot be told is logged; the observers converge on their own.</summary>
    private async ValueTask NotifyServingAsync(WorkflowActivationSlot slot, string artifactId)
    {
        try { await NotifyObserversAsync(slot.WorkflowDefinitionId, artifactId, CancellationToken.None); }
        catch (Exception exception)
        {
            logger?.LogWarning(exception, "Definition {DefinitionId} slot {SlotName} serves, but its trigger observers could not be told", slot.WorkflowDefinitionId, slot.SlotName);
        }
    }

    private async ValueTask<WorkflowActivationProjectionState[]> ProjectionStatesAsync(string activationId, CancellationToken cancellationToken) =>
        recurringScheduleStore is null
            ? [await triggerBindingStore.FindActivationStateAsync(activationId, cancellationToken)]
            : [await triggerBindingStore.FindActivationStateAsync(activationId, cancellationToken), await recurringScheduleStore.FindActivationStateAsync(activationId, cancellationToken)];

    // A slot a version before #2230 left on its way to serving: prepared everywhere, or switched on in some stores and prepared in the others.
    private static bool IsRepairable(IReadOnlyCollection<WorkflowActivationProjectionState> states) =>
        states.Count > 0 && states.All(state => state is WorkflowActivationProjectionState.Prepared or WorkflowActivationProjectionState.Active) &&
        states.Any(state => state == WorkflowActivationProjectionState.Prepared);

    private static WorkflowActivationResult NotServingFailure(WorkflowActivationSlot slot, string reason, WorkflowActivationStep step = WorkflowActivationStep.ProjectionActivation) => new(
        false, WorkflowActivationOutcome.Failed, slot,
        Diagnostic: Truncate($"Activation '{slot.ActiveActivationId}' of definition '{slot.WorkflowDefinitionId}' slot '{slot.SlotName}' is named by the slot and does not serve: {reason}"),
        FailedStep: step);

    // ---- the old core's activation duties --------------------------------------------------------------------------

    private async ValueTask PrepareProjectionsAsync(string activationId, CancellationToken cancellationToken)
    {
        // The switch commits a slot with its serving projections; a kernel host serves from its own state, so each is empty.
        await triggerBindingStore.PrepareActivationAsync(activationId, [], cancellationToken);
        if (recurringScheduleStore is not null)
            await recurringScheduleStore.PrepareActivationAsync(activationId, [], cancellationToken);
    }

    private async ValueTask<WorkflowExecutableSourceReference?> FindLiveReferenceAsync(WorkflowActivationSlot slot, CancellationToken cancellationToken) =>
        slot.ActiveActivationId is { } activationId &&
        await sourceReferenceStore.FindAsync(WorkflowActivationReferenceIdentity.Create(activationId), cancellationToken) is { DeletedAt: null } live
            ? live
            : null;

    private static WorkflowActivationResult? TryResolveSameArtifactNoOp(WorkflowActivationCommand command, WorkflowExecutableSourceReference? activeReference,
        WorkflowActivationSlot slot)
    {
        if (slot.ActiveActivationId is null ||
            command.OwnershipIntent == WorkflowActivationOwnershipIntent.TakeOver && slot.Source is { } incumbent && !incumbent.IsSameOwnerAs(command.Source) ||
            activeReference is null ||
            !StringComparer.Ordinal.Equals(activeReference.ArtifactId, command.Executable.Identity.ArtifactId) ||
            !StringComparer.Ordinal.Equals(activeReference.TenantId, command.Reference.TenantId) ||
            !WorkflowExecutableSourceReferenceComparer.SameInspectionSidecars(activeReference, command.Reference))
            return null;
        return new(true, WorkflowActivationOutcome.AlreadyActive, slot, activeReference);
    }

    private async ValueTask<WorkflowExecutableSourceReference> MintOrResumeSourceReferenceAsync(WorkflowExecutableSourceReference candidate, CancellationToken cancellationToken)
    {
        var existing = await sourceReferenceStore.FindAsync(candidate.SourceReferenceId, cancellationToken);
        if (existing is null)
        {
            try
            {
                await sourceReferenceStore.SaveAsync(candidate, cancellationToken);
                return candidate;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception saveFailure)
            {
                // Another attempt with the same deterministic id may have won between the read and the insert.
                existing = await sourceReferenceStore.FindAsync(candidate.SourceReferenceId, cancellationToken);
                if (existing is null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(saveFailure).Throw();
            }
        }

        // A retry keeps the first attempt's timestamps and must carry the same identity and source.
        var atOriginalTime = candidate with { CreatedAt = existing!.CreatedAt, PublishedAt = existing.PublishedAt };
        if (!WorkflowExecutableSourceReferenceComparer.SameIdentity(existing, atOriginalTime))
            throw new SourceReferenceResumeConflictException($"Source reference '{candidate.SourceReferenceId}' already belongs to a different activation payload.");
        if (existing.DeletedAt is null) return existing;
        if (!StringComparer.Ordinal.Equals(existing.DeletedReason, FailedRetireReason))
            throw new SourceReferenceResumeConflictException(
                $"Source reference '{candidate.SourceReferenceId}' was retired for '{existing.DeletedReason ?? "an unspecified reason"}' and cannot be resumed.");
        var restored = existing with { DeletedAt = null, DeletedReason = null };
        if (!await sourceReferenceStore.TryRestoreAsync(existing, restored, cancellationToken))
            throw new SourceReferenceResumeConflictException($"Source reference '{candidate.SourceReferenceId}' changed while its failed activation was being resumed.");
        return restored;
    }

    private ValueTask<WorkflowActivationResult> AbandonAsync(WorkflowActivationCommand command, WorkflowExecutableSourceReference reference, KernelServingPlan plan,
        CancellationToken cancellationToken, Exception exception, WorkflowActivationStep failedStep)
    {
        if (!NotRequestedCancellation(exception, cancellationToken))
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception).Throw();
        return AbandonAsync(command, reference, plan,
            slot => new(false, WorkflowActivationOutcome.Failed, slot, Diagnostic: Truncate(SafeMessage(exception)), FailedStep: failedStep),
            $"failed at step {failedStep}", exception);
    }

    /// <summary>
    /// Discards the call's activation unless it serves (<see cref="IWorkflowActivationSwitch.TryDiscardAsync"/>). One that
    /// serves, because a switch committed before it threw, is left to the slot: when the slot names it, this host serves it
    /// and the call reports it activated.
    /// </summary>
    private async ValueTask<WorkflowActivationResult> AbandonAsync(WorkflowActivationCommand command, WorkflowExecutableSourceReference reference,
        KernelServingPlan plan, Func<WorkflowActivationSlot, WorkflowActivationResult> failed, string reason, Exception? failure = null)
    {
        var definitionId = command.Executable.Identity.DefinitionId;
        bool discarded;
        try { discarded = await activationSwitch.TryDiscardAsync(reference, CancellationToken.None); }
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
                logger?.LogWarning(failure, "Activation {ActivationId} of definition {DefinitionId} slot {SlotName} {Reason}; it was compensated",
                    command.ActivationId, definitionId, command.SlotName, reason);
            return failed(slot);
        }

        if (StringComparer.Ordinal.Equals(slot.ActiveActivationId, command.ActivationId))
        {
            try
            {
                await ServeAsync(plan, slot.Revision, CancellationToken.None);
                await NotifyServingAsync(slot, plan.Served!.Workflow.Id);
                return new(true, WorkflowActivationOutcome.Activated, slot, await FindLiveReferenceAsync(slot, CancellationToken.None) ?? reference);
            }
            catch (Exception exception)
            {
                logger?.LogError(exception, "Activation {ActivationId} of definition {DefinitionId} slot {SlotName} serves the slot but could not be served on this host",
                    command.ActivationId, definitionId, command.SlotName);
            }
        }

        var result = failed(slot);
        return result with { Diagnostic = Truncate(Join(result.Diagnostic, "The activation serves, so it was left to the slot rather than compensated.")) };
    }

    private async ValueTask RetireReferencesAsync(WorkflowActivationSlot slot, IEnumerable<string> activationIds)
    {
        foreach (var activationId in activationIds)
        {
            try
            {
                await sourceReferenceStore.RetireAsync(WorkflowActivationReferenceIdentity.Create(activationId), timeProvider.GetUtcNow(), ReplacedRetireReason, CancellationToken.None);
            }
            catch (Exception exception)
            {
                logger?.LogError(exception, "The source reference of activation {ActivationId}, which no longer serves definition {DefinitionId} slot {SlotName}, could not be retired",
                    activationId, slot.WorkflowDefinitionId, slot.SlotName);
            }
        }
    }

    private async ValueTask<WorkflowActivationSlot> CurrentSlotAsync(string definitionId, string slotName)
    {
        try { return await authority.FindAsync(definitionId, slotName, CancellationToken.None) ?? EmptySlot(definitionId, slotName); }
        catch (Exception exception)
        {
            logger?.LogWarning(exception, "Could not read back activation slot {DefinitionId}/{SlotName} after failure", definitionId, slotName);
            return EmptySlot(definitionId, slotName);
        }
    }

    private WorkflowActivationSlot EmptySlot(string definitionId, string slotName) =>
        new(WorkflowActivationSlotIdentity.Create(definitionId, slotName), definitionId, slotName, null, null, 0, timeProvider.GetUtcNow());

    private static async ValueTask CaptureAsync(List<string> failures, string label, Func<ValueTask> step)
    {
        try { await step(); }
        catch (Exception exception) { failures.Add($"{label} failed: {SafeMessage(exception)}"); }
    }

    private static bool NotRequestedCancellation(Exception exception, CancellationToken cancellationToken) =>
        exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested;

    private static string Join(string? message, string? compensationFailure) =>
        message is null ? compensationFailure ?? string.Empty : compensationFailure is null ? message : $"{message} {compensationFailure}";

    private static string Truncate(string message) => message.Length <= MaximumDiagnosticLength ? message : message[..MaximumDiagnosticLength];

    private static string SafeMessage(Exception exception) => Truncate(string.IsNullOrWhiteSpace(exception.Message) ? exception.GetType().Name : exception.Message);

    private sealed class SourceReferenceResumeConflictException(string message) : InvalidOperationException(message);
}
