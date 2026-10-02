using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Triggers;
using static Elsa.Workflows.Runtime.Services.Executables.WorkflowActivationSwitchRules;

namespace Elsa.Workflows.Runtime.Services.Executables;

/// <summary>
/// The in-memory <see cref="IWorkflowActivationSwitch"/> (#2230). It holds the lock of the in-memory slot authority and
/// of each in-memory projection store together, in that order, so every reader sees a slot transition and its projection
/// switch as one step.
/// </summary>
/// <remarks>
/// <para>
/// Source references are moved through their store, by compare-and-swap, after the locked step or before it, in an order
/// that keeps every state between the two safe: an activation never serves with a retired reference. A replaced
/// activation's reference is retired once it no longer serves; a reverted one's, or a candidate's that a discard retired,
/// is restored before it serves again, and retired again if the switch is refused; a discarded or reverted candidate's is
/// retired once nothing can switch it on.
/// Once the locked step is made, the switch is made, so those moves use <see cref="CancellationToken.None"/>.
/// </para>
/// <para>
/// It refuses, when constructed, a slot authority or a projection store that is not the in-memory one, because it could
/// not hold that store's state with the others: compose a backend's switch with that backend's stores.
/// </para>
/// </remarks>
public sealed class InMemoryWorkflowActivationSwitch : IWorkflowActivationSwitch
{
    private const int MaximumReferenceAttempts = 16;

    private readonly InMemoryWorkflowActivationAuthority _authority;
    private readonly IWorkflowExecutableSourceReferenceStore _references;
    private readonly TimeProvider _timeProvider;
    private readonly IInMemoryActivationProjection[] _projections;

    public InMemoryWorkflowActivationSwitch(
        IWorkflowActivationAuthority authority,
        IWorkflowExecutableSourceReferenceStore references,
        TimeProvider timeProvider,
        IWorkflowTriggerBindingStore? triggerBindingStore = null,
        IRecurringTriggerScheduleStore? recurringScheduleStore = null)
    {
        _authority = authority as InMemoryWorkflowActivationAuthority ?? throw Unsupported(authority);
        _references = references ?? throw new ArgumentNullException(nameof(references));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _projections = [.. Projection(triggerBindingStore), .. Projection(recurringScheduleStore)];
    }

    public async ValueTask<WorkflowActivationTransition> TryActivateAsync(WorkflowActivationSlotRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var transition = await LockedWithReferenceFirstAsync(
            WorkflowActivationReferenceIdentity.Create(request.ActivationId),
            current => ResumeFailed(current, request.ActivationId),
            current => current.DeletedAt is null ? current.Retire(request.UpdatedAt, WorkflowActivationCoordinator.FailedRetireReason) : null,
            () =>
            {
                var planned = _authority.PlanActivation(request);
                if (!planned.Succeeded)
                    return planned;
                var replaced = ReplacedActivation(planned);
                foreach (var projection in _projections)
                    projection.CheckSwitch(request.ActivationId, replaced);
                foreach (var projection in _projections)
                    projection.Switch(request.ActivationId, replaced);
                return _authority.Commit(planned);
            },
            planned => planned.Succeeded);

        if (ReplacedActivation(transition) is { } replacedActivationId)
            await MoveReferenceAsync(WorkflowActivationReferenceIdentity.Create(replacedActivationId), current => RetireReplaced(current, request.UpdatedAt));
        return transition;
    }

    public async ValueTask<bool> TryRevertAsync(WorkflowActivationRevert revert, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(revert);
        var transition = revert.Transition;
        var slot = transition.Slot;
        var candidate = slot.ActiveActivationId ?? throw new ArgumentException("Only a transition that switched an activation on can be reverted.", nameof(revert));
        var replaced = ReplacedActivation(transition);
        var reverted = await LockedWithReferenceFirstAsync(
            replaced is null ? null : WorkflowActivationReferenceIdentity.Create(replaced),
            current => RestoreReplaced(current, replaced!),
            current => RetireReplaced(current, revert.UpdatedAt),
            () =>
            {
                var planned = replaced is null
                    ? _authority.PlanDeactivation(new(slot.WorkflowDefinitionId, slot.SlotName, revert.Source, slot.Revision, revert.UpdatedAt))
                    : _authority.PlanActivation(new(
                        slot.WorkflowDefinitionId,
                        slot.SlotName,
                        replaced,
                        transition.ReplacedSource ?? revert.Source,
                        slot.Revision,
                        revert.UpdatedAt,
                        WorkflowActivationOwnershipIntent.TakeOver));
                if (!planned.Succeeded)
                    return false;
                if (replaced is not null)
                    foreach (var projection in _projections)
                        projection.CheckSwitch(replaced, candidate);
                foreach (var projection in _projections)
                {
                    if (replaced is not null)
                        projection.Switch(replaced, candidate);
                    projection.Delete(candidate);
                }

                _authority.Commit(planned);
                return true;
            },
            committed => committed);

        if (reverted)
            await MoveReferenceAsync(revert.Reference.SourceReferenceId, current => RetireFailed(current, revert.Reference, _timeProvider.GetUtcNow()));
        return reverted;
    }

    public ValueTask<WorkflowActivationTransition> TryDeactivateAsync(
        WorkflowDeactivationSlotRequest request,
        IReadOnlyCollection<string> alsoServing,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(alsoServing);
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(Locked(() =>
        {
            var planned = _authority.PlanDeactivation(request);
            if (!planned.Succeeded)
                return planned;
            string?[] serving = [planned.ReplacedActivationId, .. alsoServing];
            foreach (var projection in _projections)
            foreach (var activationId in serving.OfType<string>())
                projection.Delete(activationId);
            return _authority.Commit(planned);
        }));
    }

    public async ValueTask<bool> TryDiscardAsync(WorkflowExecutableSourceReference reference, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reference);
        var activationId = reference.ActivationId ?? throw new ArgumentException("Only an activation's source reference can be discarded.", nameof(reference));
        var discarded = Locked(() =>
        {
            if (_projections.Any(projection => projection.Serves(activationId)))
                return false;
            foreach (var projection in _projections)
                projection.Delete(activationId);
            return true;
        });

        if (discarded)
            await MoveReferenceAsync(reference.SourceReferenceId, current => RetireFailed(current, reference, _timeProvider.GetUtcNow()));
        return discarded;
    }

    /// <summary>
    /// Runs <paramref name="step"/> holding the slot authority's lock and every projection store's, in that order. A
    /// composition without the trigger serving spine has no projection store; the coordinator refuses to activate in it.
    /// </summary>
    private T Locked<T>(Func<T> step)
    {
        lock (_authority.Gate)
        {
            var held = 0;
            try
            {
                for (; held < _projections.Length; held++)
                    Monitor.Enter(_projections[held].SyncRoot);
                return step();
            }
            finally
            {
                while (held > 0)
                    Monitor.Exit(_projections[--held].SyncRoot);
            }
        }
    }

    /// <summary>
    /// Moves the reference <paramref name="sourceReferenceId"/> names as <paramref name="before"/> says, so it is live before
    /// the activation it belongs to serves, then runs <paramref name="step"/> under the locks, and moves the reference as
    /// <paramref name="undo"/> says when the step did not commit: a refused or failed switch changes nothing.
    /// </summary>
    private async ValueTask<T> LockedWithReferenceFirstAsync<T>(
        string? sourceReferenceId,
        Func<WorkflowExecutableSourceReference, WorkflowExecutableSourceReference?> before,
        Func<WorkflowExecutableSourceReference, WorkflowExecutableSourceReference?> undo,
        Func<T> step,
        Func<T, bool> committed)
    {
        var moved = sourceReferenceId is not null && await MoveReferenceAsync(sourceReferenceId, before);
        var done = false;
        try
        {
            var result = Locked(step);
            done = committed(result);
            return result;
        }
        finally
        {
            if (moved && !done)
                await MoveReferenceAsync(sourceReferenceId!, undo);
        }
    }

    /// <summary>
    /// Moves a reference as <paramref name="next"/> says, by compare-and-swap, reading it again when another writer changed
    /// it first, as the EF switch's transaction does; whether it moved. Nothing to move is not a failure.
    /// </summary>
    private async ValueTask<bool> MoveReferenceAsync(string sourceReferenceId, Func<WorkflowExecutableSourceReference, WorkflowExecutableSourceReference?> next)
    {
        for (var attempt = 0; attempt < MaximumReferenceAttempts; attempt++)
        {
            if (await _references.FindAsync(sourceReferenceId, CancellationToken.None) is not { } current || next(current) is not { } moved)
                return false;
            var swapped = current.DeletedAt is null
                ? await _references.TryRetireAsync(current, moved, CancellationToken.None)
                : await _references.TryRestoreAsync(current, moved, CancellationToken.None);
            if (swapped)
                return true;
        }

        throw new InvalidOperationException($"Source reference '{sourceReferenceId}' changed concurrently; retry the operation.");
    }

    private static IEnumerable<IInMemoryActivationProjection> Projection(object? store) => store switch
    {
        null => [],
        IInMemoryActivationProjection projection => [projection],
        _ => throw Unsupported(store)
    };

    private static InvalidOperationException Unsupported(object store) => new(
        $"The in-memory activation switch commits a slot and its serving projections together, and can only do so over the in-memory stores; '{store.GetType().Name}' is not one. " +
        "Compose the slot authority and the projection stores from one backend, whose persistence feature composes its own switch with them (#2230).");
}
