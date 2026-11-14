using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Elsa.Workflows.Runtime.Services.Triggers;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

internal sealed partial class EfRuntimeIngressDeliveryStore : IRuntimeIngressTransportObserver
{
    public ValueTask<RuntimePostCommitOutboxItem?> FindDeliveryAsync(TriggerScope scope, string sourceOutboxItemId, CancellationToken cancellationToken = default) =>
        EfTriggerBoundary.RunAsync(() => FindDeliveryAsyncCore(scope, sourceOutboxItemId, cancellationToken), scope?.Persistence.Value, sourceOutboxItemId);

    public ValueTask<RuntimeIngressDeliveryPage> ListDeliveriesAsync(TriggerScope scope, RuntimeIngressDeliveryQuery query, CancellationToken cancellationToken = default) =>
        EfTriggerBoundary.RunAsync(() => ListDeliveriesAsyncCore(scope, query, cancellationToken), scope?.Persistence.Value, null);

    public ValueTask<RuntimeIngressControlResult> ControlAsync(RuntimeIngressControlRequest request, CancellationToken cancellationToken = default) =>
        EfTriggerBoundary.RunAsync(() => ControlAsyncCore(request, cancellationToken), request?.Scope?.Persistence.Value, request?.SourceOutboxItemId);

    public async ValueTask RecordAsync(TriggerScope scope, RuntimeIngressTransportObservation observation, CancellationToken cancellationToken = default)
    {
        await EfTriggerBoundary.RunAsync(async () => { await RecordAsyncCore(scope, observation, cancellationToken); return true; }, scope?.Persistence.Value, null);
    }

    public bool SupportsInspection => SupportsAdmission;
    public bool SupportsControls => SupportsAdmission;
    public bool SupportsObservations => SupportsAdmission;

    private async ValueTask<RuntimePostCommitOutboxItem?> FindDeliveryAsyncCore(TriggerScope scope, string sourceOutboxItemId, CancellationToken cancellationToken = default)
    {
        Require(scope, cancellationToken); RequireOwnedBoundary();
        var row = await LoadAsync(scope.Persistence, sourceOutboxItemId, false, cancellationToken);
        if (row is null) return null;
        var item = Read(row, scope.Persistence, sourceOutboxItemId);
        if (item.SubscriptionDelivery is null || RuntimeIngressAdmissionValidation.Tenant(item) != scope.TenantId) return null;
        RuntimeIngressAdmissionValidation.CheckSource(item, scope); return item;
    }

    private async ValueTask<RuntimeIngressDeliveryPage> ListDeliveriesAsyncCore(TriggerScope scope, RuntimeIngressDeliveryQuery query, CancellationToken cancellationToken = default)
    {
        Require(scope, cancellationToken); RequireOwnedBoundary(); RuntimeStorePageRequest.ValidateLimit(query.Limit, nameof(query));
        var scopeHash = EfRuntimeOperationalStoreSupport.Hash(scope.Persistence.Value);
        var cursor = query.After;
        var result = new List<RuntimePostCommitOutboxItem>();
        var rows = context.RuntimePostCommitOutbox.AsNoTracking().Where(row => row.ScopeKeyHash == scopeHash && row.SourceKind != null);
        if (cursor is not null)
        {
            var order = EfRuntimePostCommitOutboxStore.OutboxOrderKey(cursor);
            rows = rows.Where(row => string.Compare(row.OutboxItemIdOrderKey, order) > 0);
        }
        var scanLimit = query.Limit;
        var physicalPage = await rows.OrderBy(row => row.OutboxItemIdOrderKey).Take(scanLimit).ToArrayAsync(cancellationToken);
        // Keep the last inspected cursor, so filtering and an early full page never discard overfetched sources.
        foreach (var row in physicalPage)
        {
            var id = EfRuntimeOperationalStoreSupport.Decode(row.OutboxItemId);
            var item = Read(row, scope.Persistence, id); cursor = id;
            if (item.SubscriptionDelivery is not null && RuntimeIngressAdmissionValidation.Tenant(item) == scope.TenantId &&
                (query.TriggerId is null || RuntimeIngressControlRules.BelongsTo(item, query.TriggerId)))
            { RuntimeIngressAdmissionValidation.CheckSource(item, scope); result.Add(item); }
            if (result.Count >= query.Limit) return new(result, cursor);
        }
        return new(result, physicalPage.Length == scanLimit ? cursor : null);
    }

    private async ValueTask<RuntimeIngressControlResult> ControlAsyncCore(RuntimeIngressControlRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request); Require(request.Scope, cancellationToken); RequireOwnedBoundary();
        RuntimeIngressControlResult? result = null;
        await ChangeObservedAsync(request.Scope.Persistence, request.SourceOutboxItemId, async (current, token) =>
        {
            if (RuntimeIngressControlRules.Replay(current, request) is { } replay)
            { result = replay; return current; }
            if (request.ExpectedTriggerRevision is not > 0 || resources is not EfStandaloneTriggerStore selected || !ReferenceEquals(selected.Context, context) ||
                !await EfStandaloneTriggerStore.LockResourceAsync(context, request.Scope, request.TriggerId, request.ExpectedTriggerRevision.Value, token))
                throw new TriggerOperationException("conflict", "The trigger changed; review its current scoped revision before controlling accepted work.");
            var resource = await resources.FindAsync(request.Scope, request.TriggerId, token);
            if (resource is null || resource.Trigger.TenantId != request.Scope.TenantId || resource.Trigger.Revision != request.ExpectedTriggerRevision)
                throw new TriggerOperationException("conflict", "The current resource or deletion tombstone is unavailable.");
            if (request.Kind == RuntimeIngressControlKind.RetryRecipients && current.Intent is { } intent)
            {
                var publisher = await executions.FindAsync(intent.WorkflowExecutionId, token) ?? throw new InvalidDataException("The physical workflow publisher is missing.");
                RuntimeIngressAdmissionValidation.CheckPublisher(current, publisher, request.Scope.Persistence);
            }
            result = RuntimeIngressControlRules.Apply(current, request, time.GetUtcNow());
            return result.Item;
        }, cancellationToken);
        return result ?? throw new InvalidOperationException("The source control operation did not produce a committed result.");
    }

    private async ValueTask RecordAsyncCore(TriggerScope scope, RuntimeIngressTransportObservation observation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(observation); Require(scope, cancellationToken); RequireOwnedBoundary();
        await ChangeAsync(scope.Persistence, observation.SourceOutboxItemId, current => RuntimeIngressControlRules.Observe(current, scope, observation), cancellationToken);
    }
}
