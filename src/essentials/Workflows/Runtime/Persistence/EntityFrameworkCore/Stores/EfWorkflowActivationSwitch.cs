using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Executables;
using static Elsa.Workflows.Runtime.Services.Executables.WorkflowActivationSwitchRules;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

/// <summary>
/// The EF Core <see cref="IWorkflowActivationSwitch"/> (#2230): each operation is one transaction of the shared
/// <see cref="RuntimeDbContext"/> over the slot row, both projection stores' states and rows, and the source references it
/// moves, staged through those stores' own rules.
/// </summary>
/// <remarks>
/// <para>
/// Every row it writes carries its revision as a concurrency token. So two operations that write one slot, or one
/// activation's projection, serialize: whichever commits second loses its write, rolls back, and reads everything again,
/// and decides again on what it reads. A discard that read an activation as prepared therefore cannot delete it once a
/// switch has made it serve, and a switch cannot switch on a projection a discard has deleted. A projection state that
/// moved while it was read is read again too (<see cref="ProjectionStaging.Moved"/>). Lost writes, unique-key races and
/// deadlocks are retried within <see cref="WorkflowActivationSwitchRules.MaximumAttempts"/> attempts; then the operation
/// fails with "changed concurrently", having committed nothing.
/// </para>
/// <para>
/// It is composed only over the EF slot authority and EF projection and reference stores that share its context, which
/// <c>AddRuntimeEntityFrameworkCore</c> registers together, and refuses any other: nothing else could commit with them.
/// </para>
/// </remarks>
public sealed class EfWorkflowActivationSwitch : IWorkflowActivationSwitch
{
    private static readonly EfWriteRetry Commits = new(
        MaximumAttempts,
        exception => EfRelationalExceptionClassifier.IsSaveConflict(exception, EfWriteConflict.Concurrency | EfWriteConflict.UniqueKey | EfWriteConflict.Transient));

    private readonly RuntimeDbContext _context;
    private readonly IPersistenceAccessContextAccessor _accessContextAccessor;
    private readonly TimeProvider _timeProvider;
    private readonly EfWorkflowActivationAuthority _authority;
    private readonly EfWorkflowTriggerBindingStore _bindings;
    private readonly EfRecurringTriggerScheduleStore? _schedules;
    private readonly EfWorkflowExecutableSourceReferenceStore _references;

    public EfWorkflowActivationSwitch(
        IWorkflowActivationAuthority authority,
        IWorkflowTriggerBindingStore triggerBindingStore,
        IWorkflowExecutableSourceReferenceStore sourceReferenceStore,
        IPersistenceAccessContextAccessor accessContextAccessor,
        TimeProvider timeProvider,
        IRecurringTriggerScheduleStore? recurringScheduleStore = null)
    {
        _authority = authority as EfWorkflowActivationAuthority ?? throw Unsupported(authority);
        _context = _authority.Context;
        _bindings = Shared(triggerBindingStore as EfWorkflowTriggerBindingStore, triggerBindingStore, store => store.Context);
        _references = Shared(sourceReferenceStore as EfWorkflowExecutableSourceReferenceStore, sourceReferenceStore, store => store.Context);
        _schedules = recurringScheduleStore is null ? null : Shared(recurringScheduleStore as EfRecurringTriggerScheduleStore, recurringScheduleStore, store => store.Context);
        _accessContextAccessor = accessContextAccessor ?? throw new ArgumentNullException(nameof(accessContextAccessor));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public ValueTask<WorkflowActivationTransition> TryActivateAsync(WorkflowActivationSlotRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return RunAsync(
            $"Activation '{request.ActivationId}' of definition '{request.WorkflowDefinitionId}' slot '{request.SlotName}'",
            async scope =>
            {
                var transition = await _authority.StageActivationAsync(request, cancellationToken);
                if (!transition.Succeeded)
                    return Step.Discard(transition);
                var replaced = ReplacedActivation(transition);
                if (await StageSwitchAsync(scope, request.ActivationId, replaced, deleteReplaced: false, cancellationToken))
                    return Step.Moved<WorkflowActivationTransition>();
                await _references.StageMoveAsync(WorkflowActivationReferenceIdentity.Create(request.ActivationId), current => ResumeFailed(current, request.ActivationId), cancellationToken);
                if (replaced is not null)
                    await _references.StageMoveAsync(WorkflowActivationReferenceIdentity.Create(replaced), current => RetireReplaced(current, request.UpdatedAt), cancellationToken);
                return Step.Commit(transition);
            },
            cancellationToken);
    }

    public ValueTask<bool> TryRevertAsync(WorkflowActivationRevert revert, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(revert);
        var transition = revert.Transition;
        var slot = transition.Slot;
        var candidate = slot.ActiveActivationId ?? throw new ArgumentException("Only a transition that switched an activation on can be reverted.", nameof(revert));
        var replaced = ReplacedActivation(transition);
        return RunAsync(
            $"Revert of activation '{candidate}' of definition '{slot.WorkflowDefinitionId}' slot '{slot.SlotName}'",
            async scope =>
            {
                var reverted = replaced is null
                    ? await _authority.StageDeactivationAsync(new(slot.WorkflowDefinitionId, slot.SlotName, revert.Source, slot.Revision, revert.UpdatedAt), cancellationToken)
                    : await _authority.StageActivationAsync(
                        new(slot.WorkflowDefinitionId, slot.SlotName, replaced, transition.ReplacedSource ?? revert.Source, slot.Revision, revert.UpdatedAt, WorkflowActivationOwnershipIntent.TakeOver),
                        cancellationToken);
                if (!reverted.Succeeded)
                    return Step.Discard(false);
                var moved = replaced is null
                    ? await StageDeletionAsync(scope, candidate, unlessServing: false, cancellationToken) == ProjectionStaging.Moved
                    : await StageSwitchAsync(scope, replaced, candidate, deleteReplaced: true, cancellationToken);
                if (moved)
                    return Step.Moved<bool>();
                if (replaced is not null)
                    await _references.StageMoveAsync(WorkflowActivationReferenceIdentity.Create(replaced), current => RestoreReplaced(current, replaced), cancellationToken);
                await _references.StageMoveAsync(revert.Reference.SourceReferenceId, current => RetireFailed(current, revert.Reference, _timeProvider.GetUtcNow()), cancellationToken);
                return Step.Commit(true);
            },
            cancellationToken);
    }

    public ValueTask<WorkflowActivationTransition> TryDeactivateAsync(
        WorkflowDeactivationSlotRequest request,
        IReadOnlyCollection<string> alsoServing,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(alsoServing);
        return RunAsync(
            $"Deactivation of definition '{request.WorkflowDefinitionId}' slot '{request.SlotName}'",
            async scope =>
            {
                var transition = await _authority.StageDeactivationAsync(request, cancellationToken);
                if (!transition.Succeeded)
                    return Step.Discard(transition);
                string?[] serving = [transition.ReplacedActivationId, .. alsoServing];
                foreach (var activationId in serving.OfType<string>().Distinct(StringComparer.Ordinal))
                    if (await StageDeletionAsync(scope, activationId, unlessServing: false, cancellationToken) == ProjectionStaging.Moved)
                        return Step.Moved<WorkflowActivationTransition>();
                return Step.Commit(transition);
            },
            cancellationToken);
    }

    public ValueTask<bool> TryDiscardAsync(WorkflowExecutableSourceReference reference, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reference);
        var activationId = reference.ActivationId ?? throw new ArgumentException("Only an activation's source reference can be discarded.", nameof(reference));
        return RunAsync(
            $"Discard of activation '{activationId}'",
            async scope =>
            {
                switch (await StageDeletionAsync(scope, activationId, unlessServing: true, cancellationToken))
                {
                    case ProjectionStaging.Moved:
                        return Step.Moved<bool>();
                    case ProjectionStaging.Serves:
                        return Step.Discard(false);
                }
                await _references.StageMoveAsync(reference.SourceReferenceId, current => RetireFailed(current, reference, _timeProvider.GetUtcNow()), cancellationToken);
                return Step.Commit(true);
            },
            cancellationToken);
    }

    public ValueTask<bool> TryRepairAsync(
        WorkflowActivationSlot slot,
        IReadOnlyCollection<string> alsoServing,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(slot);
        ArgumentNullException.ThrowIfNull(alsoServing);
        var activationId = slot.ActiveActivationId ?? throw new ArgumentException("Only a slot that names an activation can be repaired.", nameof(slot));
        var others = alsoServing.Where(other => !StringComparer.Ordinal.Equals(other, activationId)).Distinct(StringComparer.Ordinal).ToArray();
        return RunAsync(
            $"Repair of activation '{activationId}' of definition '{slot.WorkflowDefinitionId}' slot '{slot.SlotName}'",
            async scope =>
            {
                // The slot row is read, not written: every operation that moves the slot off this activation writes its
                // projection state, which this repair writes too, so the two serialize on that state's revision.
                if (await _authority.StageFindAsync(slot.WorkflowDefinitionId, slot.SlotName, cancellationToken) is not { } stands ||
                    stands.Revision != slot.Revision ||
                    !StringComparer.Ordinal.Equals(stands.ActiveActivationId, activationId) ||
                    !await PreparedEverywhereAsync(scope, activationId, cancellationToken))
                    return Step.Discard(false);
                if (await StageSwitchAsync(scope, activationId, null, deleteReplaced: false, cancellationToken))
                    return Step.Moved<bool>();
                foreach (var other in others)
                    if (await StageDeletionAsync(scope, other, unlessServing: false, cancellationToken) == ProjectionStaging.Moved)
                        return Step.Moved<bool>();
                var now = _timeProvider.GetUtcNow();
                await _references.StageMoveAsync(WorkflowActivationReferenceIdentity.Create(activationId), current => ResumeFailed(current, activationId), cancellationToken);
                foreach (var other in others)
                    await _references.StageMoveAsync(WorkflowActivationReferenceIdentity.Create(other), current => RetireReplaced(current, now), cancellationToken);
                return Step.Commit(true);
            },
            cancellationToken);
    }

    private async ValueTask<bool> PreparedEverywhereAsync(string scope, string activationId, CancellationToken cancellationToken) =>
        await _bindings.StageStateAsync(scope, activationId, cancellationToken) == WorkflowActivationProjectionState.Prepared &&
        (_schedules is null || await _schedules.StageStateAsync(scope, activationId, cancellationToken) == WorkflowActivationProjectionState.Prepared);

    /// <summary>Stages a projection switch in both stores; whether a state moved while it was read.</summary>
    private async ValueTask<bool> StageSwitchAsync(string scope, string activationId, string? replacedActivationId, bool deleteReplaced, CancellationToken cancellationToken) =>
        await _bindings.StageSwitchAsync(scope, activationId, replacedActivationId, deleteReplaced, cancellationToken) == ProjectionStaging.Moved ||
        _schedules is not null && await _schedules.StageSwitchAsync(scope, activationId, replacedActivationId, deleteReplaced, cancellationToken) == ProjectionStaging.Moved;

    /// <summary>
    /// Stages the deletion of an activation's projections in both stores: <see cref="ProjectionStaging.Moved"/> when a state
    /// moved while it was read, and with <paramref name="unlessServing"/> <see cref="ProjectionStaging.Serves"/> when either
    /// serves, which leaves both for the caller to roll back.
    /// </summary>
    private async ValueTask<ProjectionStaging> StageDeletionAsync(string scope, string activationId, bool unlessServing, CancellationToken cancellationToken)
    {
        var triggers = await _bindings.StageDeletionAsync(scope, activationId, unlessServing, cancellationToken);
        if (triggers is ProjectionStaging.Moved or ProjectionStaging.Serves || _schedules is null)
            return triggers;
        return await _schedules.StageDeletionAsync(scope, activationId, unlessServing, cancellationToken);
    }

    /// <summary>
    /// Runs <paramref name="stage"/> in one transaction until it settles: a step that commits is saved and committed, one
    /// that discards is rolled back with its value, and one whose state moved, or whose commit lost a race, runs again.
    /// </summary>
    private ValueTask<T> RunAsync<T>(string operation, Func<string, ValueTask<Step<T>>> stage, CancellationToken cancellationToken) =>
        Commits.RunUntilSettledAsync<T>(
            _context,
            async () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var scope = EfRuntimeOperationalStoreSupport.RequireScope(_accessContextAccessor);
                _context.ChangeTracker.Clear();
                await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
                Step<T> step;
                try
                {
                    step = await stage(scope);
                }
                catch
                {
                    await _context.RollbackAndClearAsync(transaction);
                    throw;
                }

                if (!step.Commits)
                {
                    await _context.RollbackAndClearAsync(transaction);
                    return step.HasMoved ? EfWriteAttempt<T>.Retry() : step.Value;
                }

                return await _context.TryCommitAndClearAsync(transaction, Commits, operation, cancellationToken) is { } conflict
                    ? EfWriteAttempt<T>.Retry(conflict)
                    : step.Value;
            },
            conflict => throw EfRuntimeOperationalStoreSupport.CommitFailure(operation, conflict),
            cancellationToken);

    private TStore Shared<TStore>(TStore? store, object contract, Func<TStore, RuntimeDbContext> context) where TStore : class =>
        store is not null && ReferenceEquals(context(store), _context)
            ? store
            : throw Unsupported(contract);

    private static InvalidOperationException Unsupported(object store) => new(
        $"The EF activation switch commits a slot and its serving projections in one transaction of one RuntimeDbContext, and '{store.GetType().Name}' is not an EF Runtime store over that context. " +
        "Compose the slot authority, the projection stores and the source-reference store through AddRuntimeEntityFrameworkCore (#2230).");

    /// <summary>What one attempt staged: a value to commit, a value to roll back, or a state that moved.</summary>
    private readonly record struct Step<T>(T Value, bool Commits, bool HasMoved);

    private static class Step
    {
        public static Step<T> Commit<T>(T value) => new(value, true, false);

        public static Step<T> Discard<T>(T value) => new(value, false, false);

        public static Step<T> Moved<T>() => new(default!, false, true);
    }
}
