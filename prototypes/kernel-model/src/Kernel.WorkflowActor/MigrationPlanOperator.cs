using Kernel.Foundation.Contracts;

namespace Kernel.WorkflowActor;

internal enum MigrationPlanStep { Idle, SelectionAdmitted, DispatchAdmitted, WaitingForOutcomes, CompletionAdmitted }

/// <summary>The host runs each step outside actor turns under a tenant's operator permission.</summary>
internal sealed class MigrationPlanOperator(IIngress ingress, IActorReads reads, MigrationPlanModel model,
    MigrationPlanAuthority authority, Func<BoundScope, bool> authorize)
{
    internal ValueTask<Admission> StartAsync(BoundScope scope, string operation, MigrationPlanStart request,
        CancellationToken cancellationToken = default)
    {
        Demand(scope);
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
        private readonly MigrationPlanOutcomes _outcomes = new(reads, scope, plan);

        internal async Task<MigrationPlanStep> AdvanceAsync(CancellationToken cancellationToken = default)
        {
            owner.Demand(scope);
            await _gate.WaitAsync(cancellationToken);
            try
            {
                if (await state.ReadProgressAsync(scope, plan, cancellationToken) is not { } progress)
                    return MigrationPlanStep.Idle;
                if (progress.Pending is not null)
                {
                    var dispatched = await new MigrationPlanDispatch(ingress, state).DispatchAsync(scope, plan, cancellationToken);
                    return dispatched is null ? MigrationPlanStep.Idle : MigrationPlanStep.DispatchAdmitted;
                }
                if (!progress.AllSelected)
                {
                    await SelectAsync(progress, cancellationToken);
                    return MigrationPlanStep.SelectionAdmitted;
                }

                await RefreshOutcomesAsync(cancellationToken);
                if (!_outcomes.CanComplete(progress.Number)) return MigrationPlanStep.WaitingForOutcomes;
                await state.SendAsync(MigrationPlanControl.Complete, "complete", new(progress.Number), cancellationToken);
                return MigrationPlanStep.CompletionAdmitted;
            }
            finally { _gate.Release(); }
        }

        private async Task SelectAsync(MigrationPlanProgress progress, CancellationToken cancellationToken)
        {
            var query = new ActorQuery(WorkflowActor.ActorType, ActorStatus.Running,
                new Dictionary<string, string> { ["definition"] = progress.From });
            var page = await reads.ListAsync(scope, query, progress.Cursor, cancellationToken);
            if (page.Next is { } next && (next == progress.Cursor || next.Value.Length > 1_024))
                throw new InvalidOperationException("The instance list must return an advancing, bounded cursor.");
            var actors = page.Items.Where(view => view.Header.Tenant == scope.Tenant &&
                    WorkflowActor.CanSelectForMigration(view, progress.From))
                .Select(view => view.Header.Ref).Distinct().ToArray();
            if (actors.Length > MigrationPlanModel.BatchLimit)
                throw new InvalidOperationException("The instance-list page exceeds the migration batch capacity.");

            var number = checked(progress.Number + 1);
            var control = new MigrationPlanControl(number, actors, progress.Cursor, page.Next, AllSelected: page.Next is null);
            await state.SendAsync(MigrationPlanControl.Select, FormattableString.Invariant($"select-{number}"), control, cancellationToken);
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
