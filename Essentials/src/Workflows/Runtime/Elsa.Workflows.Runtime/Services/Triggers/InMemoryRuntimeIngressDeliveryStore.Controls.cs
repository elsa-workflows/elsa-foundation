using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;

namespace Elsa.Workflows.Runtime.Services.Triggers;

internal sealed partial class InMemoryRuntimeIngressDeliveryStore : IRuntimeIngressTransportObserver
{
    public bool SupportsInspection => SupportsAdmission;
    public bool SupportsControls => SupportsAdmission;
    public bool SupportsObservations => SupportsAdmission;

    public ValueTask<RuntimePostCommitOutboxItem?> FindDeliveryAsync(TriggerScope scope, string sourceOutboxItemId, CancellationToken cancellationToken = default)
    {
        Require(scope, cancellationToken);
        lock (State.SyncRoot)
        {
            if (!State.OutboxItems.TryGetValue(sourceOutboxItemId, out var item) || item.SourceScope != scope.Persistence ||
                item.SubscriptionDelivery is null || RuntimeIngressAdmissionValidation.Tenant(item) != scope.TenantId)
                return ValueTask.FromResult<RuntimePostCommitOutboxItem?>(null);
            RuntimeIngressAdmissionValidation.CheckSource(item, scope);
            return ValueTask.FromResult<RuntimePostCommitOutboxItem?>(item);
        }
    }

    public ValueTask<RuntimeIngressDeliveryPage> ListDeliveriesAsync(TriggerScope scope, RuntimeIngressDeliveryQuery query, CancellationToken cancellationToken = default)
    {
        Require(scope, cancellationToken); RuntimeStorePageRequest.ValidateLimit(query.Limit, nameof(query));
        lock (State.SyncRoot)
        {
            var rows = State.OutboxItems.Values.Where(item => item.SourceScope == scope.Persistence && item.SubscriptionDelivery is not null &&
                RuntimeIngressAdmissionValidation.Tenant(item) == scope.TenantId && (query.TriggerId is null || RuntimeIngressControlRules.BelongsTo(item, query.TriggerId)) &&
                (query.After is null || StringComparer.Ordinal.Compare(item.OutboxItemId, query.After) > 0))
                .OrderBy(item => item.OutboxItemId, StringComparer.Ordinal).Take(query.Limit + 1).ToArray();
            foreach (var item in rows) RuntimeIngressAdmissionValidation.CheckSource(item, scope);
            return ValueTask.FromResult(new RuntimeIngressDeliveryPage(rows.Take(query.Limit).ToArray(), rows.Length > query.Limit ? rows[query.Limit - 1].OutboxItemId : null));
        }
    }

    public async ValueTask<RuntimeIngressControlResult> ControlAsync(RuntimeIngressControlRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request); Require(request.Scope, cancellationToken);
        return await State.ExecuteWriteGateAsync(async token =>
        {
            RuntimePostCommitOutboxItem current;
            lock (State.SyncRoot) current = Find(request.SourceOutboxItemId);
            if (RuntimeIngressControlRules.Replay(current, request) is { } replay) return replay;
            if (request.ExpectedTriggerRevision is not > 0 || resources is not InMemoryStandaloneTriggerStore selected || !ReferenceEquals(selected.State, State))
                throw new TriggerOperationException("conflict", "Delivery control requires the current scoped resource revision.");
            lock (State.SyncRoot)
                if (!State.StandaloneTriggers.TryGetValue((request.Scope.Persistence.Value, request.Scope.TenantId, request.TriggerId), out var resource) ||
                    resource.Trigger.TenantId != request.Scope.TenantId || resource.Trigger.Revision != request.ExpectedTriggerRevision)
                    throw new TriggerOperationException("conflict", "The trigger changed; review its current state before controlling accepted work.");
            if (request.Kind == RuntimeIngressControlKind.RetryRecipients && current.Intent is not null)
                await CheckPublisherAsync(current, request.Scope.Persistence, token);
            lock (State.SyncRoot)
            {
                var result = RuntimeIngressControlRules.Apply(Find(request.SourceOutboxItemId), request, time.GetUtcNow());
                State.OutboxItems[request.SourceOutboxItemId] = result.Item;
                return result;
            }
        }, cancellationToken);
    }

    public async ValueTask RecordAsync(TriggerScope scope, RuntimeIngressTransportObservation observation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(observation); Require(scope, cancellationToken);
        await State.ExecuteWriteGateAsync(_ =>
        {
            lock (State.SyncRoot)
                State.OutboxItems[observation.SourceOutboxItemId] = RuntimeIngressControlRules.Observe(Find(observation.SourceOutboxItemId), scope, observation);
            return ValueTask.FromResult(true);
        }, cancellationToken);
    }
}
