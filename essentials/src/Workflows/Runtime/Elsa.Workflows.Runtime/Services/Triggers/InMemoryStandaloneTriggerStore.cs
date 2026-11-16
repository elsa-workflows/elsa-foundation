using System.Collections.ObjectModel;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Elsa.Workflows.Runtime.Services.Checkpoints;

namespace Elsa.Workflows.Runtime.Services.Triggers;

internal sealed class InMemoryStandaloneTriggerStore(InMemoryRuntimeCheckpointStoreState state,
    IPersistenceAccessContextAccessor access, TimeProvider time) : IStandaloneTriggerStore
{
    internal InMemoryRuntimeCheckpointStoreState State => state;

    public ValueTask<TriggerAggregate?> FindAsync(TriggerScope scope, string triggerId, CancellationToken cancellationToken = default)
    {
        CheckScope(scope, cancellationToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(triggerId);
        lock (state.SyncRoot)
            return ValueTask.FromResult(state.StandaloneTriggers.GetValueOrDefault((scope.Persistence.Value, scope.TenantId, triggerId)));
    }

    public ValueTask<TriggerResourcePage> ListAsync(TriggerScope scope, TriggerResourceQuery query, CancellationToken cancellationToken = default)
    {
        CheckScope(scope, cancellationToken);
        ArgumentNullException.ThrowIfNull(query);
        CheckLimit(query.Limit);
        lock (state.SyncRoot)
        {
            var rows = state.StandaloneTriggers.Where(pair => pair.Key.Scope == scope.Persistence.Value && pair.Key.Tenant == scope.TenantId)
                .Select(pair => pair.Value.Trigger).Where(trigger => trigger.DesiredState != TriggerDesiredState.Deleted);
            if (query.After is not null) rows = rows.Where(trigger => StringComparer.Ordinal.Compare(trigger.Id, query.After) > 0);
            if (!string.IsNullOrWhiteSpace(query.Search)) rows = rows.Where(trigger => trigger.Name.Contains(query.Search, StringComparison.OrdinalIgnoreCase));
            if (query.TypeKey is not null) rows = rows.Where(trigger => trigger.TypeKey == query.TypeKey);
            if (query.DesiredState is { } desired) rows = rows.Where(trigger => trigger.DesiredState == desired);
            var page = rows.OrderBy(trigger => trigger.Id, StringComparer.Ordinal).Take(query.Limit + 1).ToArray();
            return ValueTask.FromResult(new TriggerResourcePage(Array.AsReadOnly(page.Take(query.Limit).ToArray()),
                page.Length > query.Limit ? page[query.Limit - 1].Id : null));
        }
    }

    public ValueTask<TriggerSubscription?> FindSubscriptionAsync(TriggerScope scope, string subscriptionId,
        CancellationToken cancellationToken = default)
    {
        CheckScope(scope, cancellationToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(subscriptionId);
        lock (state.SyncRoot)
        {
            if (!state.TriggerSubscriptionOwners.TryGetValue((scope.Persistence.Value, scope.TenantId, subscriptionId), out var triggerId) ||
                !state.StandaloneTriggers.TryGetValue((scope.Persistence.Value, scope.TenantId, triggerId), out var aggregate) ||
                aggregate.Trigger.DesiredState == TriggerDesiredState.Deleted)
                return ValueTask.FromResult<TriggerSubscription?>(null);
            return ValueTask.FromResult(aggregate.Subscriptions.SingleOrDefault(subscription => subscription.Id == subscriptionId));
        }
    }

    public ValueTask<TriggerSubscriptionPage> ListSubscriptionsAsync(TriggerScope scope, TriggerSubscriptionQuery query,
        CancellationToken cancellationToken = default)
    {
        CheckScope(scope, cancellationToken);
        ArgumentNullException.ThrowIfNull(query);
        CheckLimit(query.Limit);
        if ((query.TriggerId is null) == (query.DefinitionId is null) || query.TriggerId is not null && query.SlotName is not null)
            throw new TriggerOperationException("validation", "Select associations by exactly one trigger or logical workflow.");
        lock (state.SyncRoot)
        {
            var rows = state.StandaloneTriggers.Where(pair => pair.Key.Scope == scope.Persistence.Value && pair.Key.Tenant == scope.TenantId &&
                    pair.Value.Trigger.DesiredState != TriggerDesiredState.Deleted)
                .SelectMany(pair => pair.Value.Subscriptions);
            rows = query.TriggerId is { } trigger ? rows.Where(row => row.TriggerId == trigger) : rows.Where(row => row.DefinitionId == query.DefinitionId);
            if (query.SlotName is not null) rows = rows.Where(row => row.SlotName == query.SlotName);
            if (query.After is not null) rows = rows.Where(row => StringComparer.Ordinal.Compare(row.Id, query.After) > 0);
            var page = rows.OrderBy(row => row.Id, StringComparer.Ordinal).Take(query.Limit + 1).ToArray();
            return ValueTask.FromResult(new TriggerSubscriptionPage(Array.AsReadOnly(page.Take(query.Limit).ToArray()),
                page.Length > query.Limit ? page[query.Limit - 1].Id : null));
        }
    }

    public ValueTask<TriggerMutationReceipt?> FindMutationAsync(TriggerScope scope, string actorId, string requestId,
        CancellationToken cancellationToken = default)
    {
        CheckScope(scope, cancellationToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(actorId);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);
        lock (state.SyncRoot)
            return ValueTask.FromResult(state.TriggerMutations.GetValueOrDefault((scope.Persistence.Value, scope.TenantId, actorId, requestId)));
    }

    public async ValueTask<TriggerMutationReceipt> WriteAsync(TriggerScope scope, TriggerStoreWrite write,
        CancellationToken cancellationToken = default)
        => await state.ExecuteWriteGateAsync(_ => ValueTask.FromResult(StageWrite(scope, write, cancellationToken)), cancellationToken);

    /// <summary>Stages the resource and mutation receipt while the existing shared write gate is already held.</summary>
    internal TriggerMutationReceipt StageWrite(TriggerScope scope, TriggerStoreWrite write, CancellationToken cancellationToken)
    {
        CheckScope(scope, cancellationToken);
        ArgumentNullException.ThrowIfNull(write);
        ArgumentException.ThrowIfNullOrWhiteSpace(write.RequestId);
        ArgumentException.ThrowIfNullOrWhiteSpace(write.RequestFingerprint);
        ArgumentException.ThrowIfNullOrWhiteSpace(write.ActorId);
        ArgumentNullException.ThrowIfNull(write.Trigger);
        ArgumentNullException.ThrowIfNull(write.Subscriptions);
        var subscriptions = write.Subscriptions.ToArray();
            lock (state.SyncRoot)
            {
                var requestKey = (scope.Persistence.Value, scope.TenantId, write.ActorId, write.RequestId);
                if (state.TriggerMutations.TryGetValue(requestKey, out var replay))
                {
                    if (replay.RequestFingerprint != write.RequestFingerprint ||
                        write.ExpectedRevision is not null && replay.TriggerId != write.Trigger.Id)
                        throw new TriggerOperationException("conflict", "The mutation identity already names different content.");
                    return replay;
                }
                var key = (scope.Persistence.Value, scope.TenantId, write.Trigger.Id);
                var previous = state.StandaloneTriggers.GetValueOrDefault(key);
                ValidateReplacement(scope, write, subscriptions, previous);
                foreach (var subscription in subscriptions)
                {
                    if (state.TriggerSubscriptionOwners.TryGetValue((scope.Persistence.Value, scope.TenantId, subscription.Id), out var owner) &&
                        (owner != write.Trigger.Id || previous?.Subscriptions.All(row => row.Id != subscription.Id) != false))
                        throw new TriggerOperationException("conflict", "The association identity belongs to another or removed association.");
                }
                var snapshot = new TriggerAggregate(write.Trigger, new ReadOnlyCollection<TriggerSubscription>(subscriptions));
                var receipt = new TriggerMutationReceipt(write.RequestId, write.RequestFingerprint, write.ActorId,
                    write.Trigger.Id, write.Trigger.Revision, time.GetUtcNow());
                state.StandaloneTriggers[key] = snapshot;
                state.TriggerMutations.Add(requestKey, receipt);
                foreach (var subscription in subscriptions)
                    state.TriggerSubscriptionOwners[(scope.Persistence.Value, scope.TenantId, subscription.Id)] = write.Trigger.Id;
                return receipt;
            }
    }

    private void CheckScope(TriggerScope scope, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(scope.Persistence);
        access.Current.RequireScope();
        access.Current.EnsureScope(scope.Persistence);
        if (scope.TenantId is not null) ArgumentException.ThrowIfNullOrWhiteSpace(scope.TenantId);
    }

    private static void ValidateReplacement(TriggerScope scope, TriggerStoreWrite write,
        TriggerSubscription[] subscriptions, TriggerAggregate? previous)
    {
        if (write.Trigger.TenantId != scope.TenantId)
            throw new TriggerOperationException("validation", "The prepared resource differs from its trusted scope.");
        if (write.ExpectedRevision != previous?.Trigger.Revision || write.Trigger.Revision != (write.ExpectedRevision ?? 0) + 1)
            throw new TriggerOperationException("conflict", "The trigger changed; reload and review the intended change.");
        if (previous is { } old && (old.Trigger.TypeKey != write.Trigger.TypeKey || old.Trigger.CreatedAt != write.Trigger.CreatedAt ||
            old.Trigger.CreatedBy != write.Trigger.CreatedBy || old.Trigger.Origin.Kind != write.Trigger.Origin.Kind ||
            old.Trigger.Origin.SourceId != write.Trigger.Origin.SourceId || old.Trigger.DesiredState == TriggerDesiredState.Deleted))
            throw new TriggerOperationException("validation", "A resource update cannot replace its identity, origin or deleted lifecycle.");
        if (subscriptions.Any(subscription => subscription is null || subscription.TriggerId != write.Trigger.Id ||
            subscription.Grant is { } grant && grant.SourceTypeKey != write.Trigger.TypeKey) ||
            subscriptions.Select(subscription => subscription.Id).Distinct(StringComparer.Ordinal).Count() != subscriptions.Length ||
            subscriptions.Select(subscription => (subscription.DefinitionId, subscription.SlotName)).Distinct().Count() != subscriptions.Length)
            throw new TriggerOperationException("validation", "Associations must belong to the trigger and have distinct identities and target slots.");
        if (subscriptions.Length == 0 && write.Trigger.DesiredState == TriggerDesiredState.Enabled)
            throw new TriggerOperationException("validation", "A trigger without associations must remain paused.");
    }

    private static void CheckLimit(int limit)
    {
        if (limit is < 1 or > 100) throw new TriggerOperationException("validation", "Page size must be between 1 and 100.");
    }
}
