using Kernel.Foundation.Contracts;

namespace Kernel.WorkflowActor;

internal enum MigrationPlanStep { Idle, SelectionAdmitted, DispatchAdmitted, WaitingForOutcomes, CompletionAdmitted }

/// <summary>The host runs each step outside actor turns under a tenant's operator permission.</summary>
internal sealed class MigrationPlanOperator(IIngress ingress, IActorReads reads, MigrationPlanModel model,
    MigrationPlanAuthority authority, Func<BoundScope, bool> authorize, Func<bool> planCreatorsReady)
{
    internal ValueTask<Admission> StartAsync(BoundScope scope, string operation, MigrationPlanStart request,
        CancellationToken cancellationToken = default)
    {
        Demand(scope);
        if (request.Model is null) throw new ArgumentException("Automatic migration requires an exact execution model.", nameof(request));
        if (!planCreatorsReady()) throw new InvalidOperationException("Plan creation requires model-bound starts on all eligible owners.");
        return new WorkflowRuntime(ingress, reads).StartAsync(scope, operation, model.Start(request), cancellationToken: cancellationToken);
    }

    internal Runner Open(BoundScope scope, ActorRef plan)
    {
        Demand(scope);
        return new(this, scope, plan, reads, new(ingress, reads, authority, scope, plan), ingress);
    }

    private void Demand(BoundScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (!authorize(scope)) throw new UnauthorizedAccessException("Migration plans require tenant operator permission.");
    }

    internal sealed class Runner(MigrationPlanOperator owner, BoundScope scope, ActorRef plan, IActorReads reads,
        JournalMigrationDispatchState state, IIngress ingress)
    {
        private readonly SemaphoreSlim _gate = new(1, 1);
        private readonly MigrationPlanOutcomes _outcomes = state.Outcomes;

        internal async Task<MigrationPlanStep> AdvanceAsync(CancellationToken cancellationToken = default)
        {
            owner.Demand(scope);
            await _gate.WaitAsync(cancellationToken);
            try
            {
                if (await state.ReadProgressAsync(scope, plan, cancellationToken) is not { Model: { } executionModel } progress)
                    return MigrationPlanStep.Idle;
                if (progress.Pending is not null)
                {
                    var dispatched = await new MigrationPlanDispatch(ingress, state).DispatchAsync(scope, plan, cancellationToken);
                    return dispatched is null ? MigrationPlanStep.Idle : MigrationPlanStep.DispatchAdmitted;
                }
                if (!progress.AllSelected)
                {
                    return await SelectAsync(progress, executionModel, cancellationToken) ? MigrationPlanStep.SelectionAdmitted : MigrationPlanStep.Idle;
                }

                return await state.TryCompleteAsync(cancellationToken)
                    ? MigrationPlanStep.CompletionAdmitted
                    : MigrationPlanStep.WaitingForOutcomes;
            }
            finally { _gate.Release(); }
        }

        private async Task<bool> SelectAsync(MigrationPlanProgress progress, string executionModel, CancellationToken cancellationToken)
        {
            var query = new ActorQuery(WorkflowActor.ActorType, ActorStatus.Running,
                new Dictionary<string, string> { ["definition"] = progress.From });
            var page = await reads.ListAsync(scope, query, progress.Cursor, cancellationToken);
            if (page.Next is { } next && (next == progress.Cursor || next.Value is not { Length: > 0 and <= 1_024 }))
                throw new InvalidOperationException("The instance list must return an advancing, bounded cursor.");
            var candidates = page.Items.Where(view => view.Header.Tenant == scope.Tenant &&
                    WorkflowActor.CanSelectForMigration(view, executionModel, progress.From))
                .Select(view => view.Header.Ref);
            await RefreshOutcomesAsync(cancellationToken);
            var actors = _outcomes.Unselected(progress.Number, candidates, MigrationPlanModel.BatchLimit + 1);
            if (actors is null) return false;

            var number = checked(progress.Number + 1);
            var keepCursor = actors.Length > MigrationPlanModel.BatchLimit;
            var control = new MigrationPlanControl(number, keepCursor ? actors[..MigrationPlanModel.BatchLimit] : actors,
                progress.Cursor, keepCursor ? progress.Cursor : page.Next, AllSelected: !keepCursor && page.Next is null, KeepCursor: keepCursor);
            await state.SendAsync(MigrationPlanControl.Select, FormattableString.Invariant($"select-{number}"), control, cancellationToken);
            return true;
        }

        internal async Task<MigrationPlanView?> ReadOutcomesAsync(CancellationToken cancellationToken = default)
        {
            owner.Demand(scope);
            if (!WorkflowActor.IsMigrationPlan(await reads.GetAsync(scope, plan, cancellationToken), plan)) return null;
            await RefreshOutcomesAsync(cancellationToken);
            return _outcomes.Read();
        }

        internal async Task CloseAsync(CancellationToken cancellationToken = default)
        {
            owner.Demand(scope);
            if (WorkflowActor.IsMigrationPlan(await reads.GetAsync(scope, plan, cancellationToken), plan))
                await state.SendAsync(MigrationPlanControl.Close, "close", new(0), cancellationToken);
        }

        private async Task RefreshOutcomesAsync(CancellationToken cancellationToken)
        {
            while (await _outcomes.ProjectNextAsync(cancellationToken)) { }
        }
    }
}
