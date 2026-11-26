using Kernel.Foundation.Contracts;

namespace Kernel.WorkflowActor;

/// <param name="Definition">The definition label the source's instances carry: <paramref name="From"/> unless the plan names another.</param>
internal sealed record MigrationPlanProgress(string From, string? Model, long Number, PageToken? Cursor, bool AllSelected,
    MigrationDispatchBatch? Pending, string Definition);

/// <summary>Replays committed plan transitions and retains the bookmark and at most one pending batch.</summary>
internal sealed class JournalMigrationDispatchState(IIngress ingress, IActorReads reads, MigrationPlanAuthority authority,
    BoundScope bound, ActorRef plan) : IMigrationDispatchState
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private PageToken? _bookmark;
    private MigrationPlanJournal.Created? _created;
    private MigrationDispatchBatch? _pending;
    private long _number;
    private PageToken? _cursor;
    private bool _allSelected;
    private bool _closed;
    internal MigrationPlanOutcomes Outcomes { get; } = new(reads, bound, plan);

    public async ValueTask<MigrationDispatchBatch?> ReadPendingAsync(BoundScope scope, ActorRef target,
        CancellationToken cancellationToken) => (await ReadProgressAsync(scope, target, cancellationToken))?.Pending;

    internal async ValueTask<MigrationPlanProgress?> ReadProgressAsync(BoundScope scope, ActorRef target,
        CancellationToken cancellationToken)
    {
        CheckScope(scope, target);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_closed || !await IsDispatchable(_created?.Deadline, cancellationToken)) return null;
            while (!_closed)
            {
                var page = await reads.TailAsync(scope, plan, _bookmark, cancellationToken);
                if (page.Items.Count == 0) break;
                if (page.Next is null || page.Next == _bookmark)
                    throw new InvalidOperationException("A plan journal page must advance its bookmark.");
                var records = page.Items.Select(row => MigrationPlanJournal.Read(row.Entry)).ToArray();
                foreach (var record in records)
                    Apply(record);
                _bookmark = page.Next;
            }
            if (_closed || _created is null || !await IsDispatchable(_created.Deadline, cancellationToken)) return null;
            var pending = _pending is { } batch ? batch with { Actors = batch.Actors.ToArray() } : null;
            return new(_created.From, _created.Model, _number, _cursor, _allSelected, pending, _created.Definition ?? _created.From);
        }
        finally { _gate.Release(); }
    }

    public async ValueTask AcknowledgeAsync(BoundScope scope, ActorRef target, long batch, string operationId,
        CancellationToken cancellationToken)
    {
        CheckScope(scope, target);
        await SendAsync(MigrationPlanControl.Acknowledge, operationId, new(batch), cancellationToken);
    }

    internal async ValueTask<bool> TryCompleteAsync(CancellationToken cancellationToken = default)
    {
        if (!await IsDispatchable(null, cancellationToken)) return false;
        while (await Outcomes.ProjectNextAsync(cancellationToken)) { }
        if (Outcomes.Completion() is not { } ready || !await IsDispatchable(ready.Deadline, cancellationToken))
            return false;
        await SendControlAsync(MigrationPlanControl.Complete, "complete", new(ready.Batch), cancellationToken);
        return true;
    }

    internal ValueTask SendAsync(string kind, string operationId, MigrationPlanControl control,
        CancellationToken cancellationToken = default) =>
        kind == MigrationPlanControl.Complete ? throw new ArgumentException("Completion requires the outcome projection.", nameof(kind))
            : SendControlAsync(kind, operationId, control, cancellationToken);

    private async ValueTask SendControlAsync(string kind, string operationId, MigrationPlanControl control,
        CancellationToken cancellationToken = default)
    {
        var id = authority.OperationId(bound.Tenant, plan, kind, operationId);
        var payload = authority.Protect(bound.Tenant, plan, kind, id, control);
        var receipt = await ingress.SendAsync(bound, new Target.Ref(plan), id, kind, payload,
            cancellationToken: cancellationToken);
        if (receipt.Actor != plan || receipt.Outcome is not (AdmissionOutcome.Accepted or AdmissionOutcome.Duplicate))
            throw new InvalidOperationException($"The exact plan refused {kind} ({receipt.Outcome}, {receipt.Reason}).");
    }

    private void CheckScope(BoundScope scope, ActorRef target)
    {
        if (scope.Tenant != bound.Tenant || target != plan)
            throw new ArgumentException("This reader belongs to one tenant and exact plan.");
    }

    private async ValueTask<bool> IsDispatchable(long? deadline, CancellationToken cancellationToken)
    {
        var view = await reads.GetAsync(bound, plan, cancellationToken);
        return view is { Park: null } && WorkflowActor.IsRunningMigrationPlan(view, plan) &&
            (deadline is null || view.AsOf < deadline);
    }

    private void Apply(MigrationPlanJournal? entry)
    {
        if (_closed) return;
        switch (entry)
        {
            case MigrationPlanJournal.Created created:
                _created = created;
                break;
            case MigrationPlanJournal.Selected { Number: > 0 } selected when _created is { } created:
                _number = selected.Number;
                _cursor = selected.After;
                _allSelected = selected.AllSelected;
                _pending = selected.Actors.Length > 0 ? new(selected.Number, created.From, created.To, selected.Actors, created.Model) : null;
                break;
            case MigrationPlanJournal.Dispatched dispatched when _pending?.Number == dispatched.Number:
                _pending = null;
                break;
            case MigrationPlanJournal.Closed:
                _pending = null;
                _closed = true;
                break;
        }
    }
}
