using System.Collections.ObjectModel;
using System.Text.Json;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using static Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores.EfRuntimeOperationalStoreSupport;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

internal sealed class EfStandaloneTriggerStore(RuntimeDbContext context, IPersistenceAccessContextAccessor access,
    TimeProvider time) : IStandaloneTriggerStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    internal RuntimeDbContext Context => context;

    public ValueTask<TriggerAggregate?> FindAsync(TriggerScope scope, string triggerId, CancellationToken cancellationToken = default) =>
        EfTriggerBoundary.RunAsync(() => FindAsyncCore(scope, triggerId, cancellationToken), scope?.Persistence.Value, triggerId);

    public ValueTask<TriggerSubscription?> FindSubscriptionAsync(TriggerScope scope, string subscriptionId, CancellationToken cancellationToken = default) =>
        EfTriggerBoundary.RunAsync(() => FindSubscriptionAsyncCore(scope, subscriptionId, cancellationToken), scope?.Persistence.Value, subscriptionId);

    public ValueTask<TriggerResourcePage> ListAsync(TriggerScope scope, TriggerResourceQuery query, CancellationToken cancellationToken = default) =>
        EfTriggerBoundary.RunAsync(() => ListAsyncCore(scope, query, cancellationToken), scope?.Persistence.Value, null);

    public ValueTask<TriggerSubscriptionPage> ListSubscriptionsAsync(TriggerScope scope, TriggerSubscriptionQuery query, CancellationToken cancellationToken = default) =>
        EfTriggerBoundary.RunAsync(() => ListSubscriptionsAsyncCore(scope, query, cancellationToken), scope?.Persistence.Value, null);

    public ValueTask<TriggerMutationReceipt?> FindMutationAsync(TriggerScope scope, string actorId, string requestId, CancellationToken cancellationToken = default) =>
        EfTriggerBoundary.RunAsync(() => FindMutationAsyncCore(scope, actorId, requestId, cancellationToken), scope?.Persistence.Value, requestId);

    public ValueTask<TriggerMutationReceipt> WriteAsync(TriggerScope scope, TriggerStoreWrite write, CancellationToken cancellationToken = default) =>
        EfTriggerBoundary.RunAsync(() => WriteAsyncCore(scope, write, cancellationToken), scope?.Persistence.Value, write?.Trigger?.Id);

    private async ValueTask<TriggerAggregate?> FindAsyncCore(TriggerScope scope, string triggerId, CancellationToken cancellationToken = default)
    {
        CheckScope(scope, cancellationToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(triggerId);
        var id = ResourceId(scope, triggerId);
        // The resource revision fences its whole association set. A two-read check prevents mixed read projections.
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var row = await context.StandaloneTriggers.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
            if (row is null) return null;
            var trigger = ReadResource(row, scope);
            var subscriptions = await context.TriggerSubscriptions.AsNoTracking()
                .Where(item => item.TriggerRowId == id && !item.IsDeleted).OrderBy(item => item.SubscriptionOrderKey)
                .ToArrayAsync(cancellationToken);
            var values = subscriptions.Select(item => ReadSubscription(item, scope).Subscription).ToArray();
            var current = await context.StandaloneTriggers.AsNoTracking().Where(item => item.Id == id)
                .Select(item => (long?)item.Revision).SingleOrDefaultAsync(cancellationToken);
            if (current == trigger.Revision) return new(trigger, new ReadOnlyCollection<TriggerSubscription>(values));
        }
        throw new TriggerOperationException("conflict", "The trigger changed while its associations were read; reload it.");
    }

    private async ValueTask<TriggerSubscription?> FindSubscriptionAsyncCore(TriggerScope scope, string subscriptionId,
        CancellationToken cancellationToken = default)
    {
        CheckScope(scope, cancellationToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(subscriptionId);
        var id = SubscriptionId(scope, subscriptionId);
        var row = await context.TriggerSubscriptions.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (row is null) return null;
        var journal = ReadSubscription(row, scope);
        if (journal.IsDeleted || !await context.StandaloneTriggers.AsNoTracking()
            .AnyAsync(item => item.Id == row.TriggerRowId && item.DesiredState != (int)TriggerDesiredState.Deleted, cancellationToken)) return null;
        return journal.Subscription;
    }

    private async ValueTask<TriggerResourcePage> ListAsyncCore(TriggerScope scope, TriggerResourceQuery query,
        CancellationToken cancellationToken = default)
    {
        CheckScope(scope, cancellationToken);
        ArgumentNullException.ThrowIfNull(query);
        CheckLimit(query.Limit);
        var scopeKey = Encode(scope.Persistence.Value); var scopeHash = Hash(scope.Persistence.Value);
        var tenantKey = Encode(TenantTag(scope)); var tenantHash = Hash(TenantTag(scope));
        var rows = context.StandaloneTriggers.AsNoTracking().Where(row => row.ScopeKey == scopeKey && row.ScopeHash == scopeHash &&
            row.TenantKey == tenantKey && row.TenantHash == tenantHash && row.DesiredState != (int)TriggerDesiredState.Deleted);
        if (query.After is not null) { var after = Order(query.After); rows = rows.Where(row => row.TriggerOrderKey.CompareTo(after) > 0); }
        if (!string.IsNullOrWhiteSpace(query.Search)) { var search = query.Search.ToUpperInvariant(); rows = rows.Where(row => row.SearchName.Contains(search)); }
        if (query.TypeKey is not null) { var type = Encode(query.TypeKey); rows = rows.Where(row => row.TypeKey == type); }
        if (query.DesiredState is { } desired) rows = rows.Where(row => row.DesiredState == (int)desired);
        var page = await rows.OrderBy(row => row.TriggerOrderKey).Take(query.Limit + 1).ToArrayAsync(cancellationToken);
        var values = page.Select(row => ReadResource(row, scope)).ToArray();
        return new(Array.AsReadOnly(values.Take(query.Limit).ToArray()), values.Length > query.Limit ? values[query.Limit - 1].Id : null);
    }

    private async ValueTask<TriggerSubscriptionPage> ListSubscriptionsAsyncCore(TriggerScope scope, TriggerSubscriptionQuery query,
        CancellationToken cancellationToken = default)
    {
        CheckScope(scope, cancellationToken);
        ArgumentNullException.ThrowIfNull(query);
        CheckLimit(query.Limit);
        if ((query.TriggerId is null) == (query.DefinitionId is null) || query.TriggerId is not null && query.SlotName is not null)
            throw new TriggerOperationException("validation", "Select associations by exactly one trigger or logical workflow.");
        var scopeKey = Encode(scope.Persistence.Value); var scopeHash = Hash(scope.Persistence.Value);
        var tenantKey = Encode(TenantTag(scope)); var tenantHash = Hash(TenantTag(scope));
        var activeResources = context.StandaloneTriggers.Where(resource => resource.DesiredState != (int)TriggerDesiredState.Deleted)
            .Select(resource => resource.Id);
        var rows = context.TriggerSubscriptions.AsNoTracking().Where(row => row.ScopeKey == scopeKey && row.ScopeHash == scopeHash &&
            row.TenantKey == tenantKey && row.TenantHash == tenantHash && !row.IsDeleted && activeResources.Contains(row.TriggerRowId));
        if (query.TriggerId is { } trigger) { var triggerId = ResourceId(scope, trigger); rows = rows.Where(row => row.TriggerRowId == triggerId); }
        else { var definition = Encode(query.DefinitionId!); var hash = Hash(query.DefinitionId!); rows = rows.Where(row => row.DefinitionKey == definition && row.DefinitionHash == hash); }
        if (query.SlotName is not null) { var slot = Encode(query.SlotName); rows = rows.Where(row => row.SlotKey == slot); }
        if (query.After is not null) { var after = Order(query.After); rows = rows.Where(row => row.SubscriptionOrderKey.CompareTo(after) > 0); }
        var page = await rows.OrderBy(row => row.SubscriptionOrderKey).Take(query.Limit + 1).ToArrayAsync(cancellationToken);
        var values = page.Select(row => ReadSubscription(row, scope).Subscription).ToArray();
        return new(Array.AsReadOnly(values.Take(query.Limit).ToArray()), values.Length > query.Limit ? values[query.Limit - 1].Id : null);
    }

    private async ValueTask<TriggerMutationReceipt?> FindMutationAsyncCore(TriggerScope scope, string actorId, string requestId,
        CancellationToken cancellationToken = default)
    {
        CheckScope(scope, cancellationToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(actorId);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);
        var id = MutationId(scope, actorId, requestId);
        var row = await context.TriggerMutations.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        return row is null ? null : ReadMutation(row, scope, actorId, requestId);
    }

    private async ValueTask<TriggerMutationReceipt> WriteAsyncCore(TriggerScope scope, TriggerStoreWrite write,
        CancellationToken cancellationToken = default)
    {
        CheckScope(scope, cancellationToken);
        ArgumentNullException.ThrowIfNull(write);
        if (context.Database.CurrentTransaction is not null || context.ChangeTracker.HasChanges())
            throw new InvalidOperationException("Resource mutation requires a clean owned Runtime context.");
        for (var attempt = 0; ; attempt++)
        {
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                var receipt = await StageWriteAsync(scope, write, cancellationToken);
                await context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return receipt;
            }
            catch (Exception exception) when (EfRelationalExceptionClassifier.IsSaveConflict(exception,
                EfWriteConflict.UniqueKey | EfWriteConflict.Concurrency | EfWriteConflict.Transient))
            {
                await context.RollbackAndClearAsync(transaction);
                await transaction.DisposeAsync();
                var replay = await FindMutationAsync(scope, write.ActorId, write.RequestId, cancellationToken);
                if (replay is not null) return VerifyReplay(replay, write);
                if (attempt < 2 && EfRelationalExceptionClassifier.IsSaveConflict(exception, EfWriteConflict.Transient))
                { await Task.Delay(TimeSpan.FromMilliseconds(20 * (attempt + 1)), cancellationToken); continue; }
                throw new TriggerOperationException("conflict", "The trigger or an association changed; reload and review the change.", innerException: exception);
            }
            finally { context.ChangeTracker.Clear(); }
        }
    }

    /// <summary>Stages the same mutation inside a qualified resource/route/schedule owner's transaction.</summary>
    internal async ValueTask<TriggerMutationReceipt> StageWriteAsync(TriggerScope scope, TriggerStoreWrite write,
        CancellationToken cancellationToken)
    {
        CheckScope(scope, cancellationToken);
        if (context.Database.CurrentTransaction is null) throw new InvalidOperationException("A staged trigger change requires its owner's transaction.");
        ArgumentException.ThrowIfNullOrWhiteSpace(write.RequestId);
        ArgumentException.ThrowIfNullOrWhiteSpace(write.RequestFingerprint);
        ArgumentException.ThrowIfNullOrWhiteSpace(write.ActorId);
        ArgumentNullException.ThrowIfNull(write.Trigger);
        ArgumentNullException.ThrowIfNull(write.Subscriptions);
        var replay = await FindMutationAsync(scope, write.ActorId, write.RequestId, cancellationToken);
        if (replay is not null) return VerifyReplay(replay, write);
        var id = ResourceId(scope, write.Trigger.Id);
        var oldRow = await context.StandaloneTriggers.AsNoTracking().SingleOrDefaultAsync(row => row.Id == id, cancellationToken);
        var previous = oldRow is null ? null : ReadResource(oldRow, scope);
        var subscriptions = write.Subscriptions.ToArray();
        ValidateReplacement(scope, write, subscriptions, previous);
        if (previous is not null && !await LockResourceAsync(context, scope, previous.Id, previous.Revision, cancellationToken))
            throw new TriggerOperationException("conflict", "The trigger changed; reload it.");
        var oldSubscriptions = await context.TriggerSubscriptions.AsNoTracking().Where(row => row.TriggerRowId == id).ToArrayAsync(cancellationToken);
        var existing = oldSubscriptions.ToDictionary(row => ReadSubscription(row, scope).Subscription.Id, StringComparer.Ordinal);
        foreach (var subscription in subscriptions)
        {
            var subscriptionId = SubscriptionId(scope, subscription.Id);
            if (existing.TryGetValue(subscription.Id, out var old) && old.IsDeleted ||
                !existing.ContainsKey(subscription.Id) && await context.TriggerSubscriptions.AsNoTracking().AnyAsync(row => row.Id == subscriptionId, cancellationToken))
                throw new TriggerOperationException("conflict", "The association identity belongs to another or removed association.");
        }
        var replacement = ResourceRow(scope, write.Trigger);
        context.StandaloneTriggers.Attach(replacement);
        context.Entry(replacement).State = oldRow is null ? EntityState.Added : EntityState.Modified;
        if (oldRow is not null) context.Entry(replacement).Property(row => row.Revision).OriginalValue = oldRow.Revision;
        var retained = subscriptions.Select(subscription => subscription.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var subscription in subscriptions)
        {
            var row = SubscriptionRow(scope, subscription, isDeleted: false);
            context.TriggerSubscriptions.Attach(row);
            context.Entry(row).State = existing.TryGetValue(subscription.Id, out var old) ? EntityState.Modified : EntityState.Added;
            if (old is not null) context.Entry(row).Property(item => item.Revision).OriginalValue = old.Revision;
        }
        foreach (var old in oldSubscriptions.Where(row => !row.IsDeleted && !retained.Contains(ReadSubscription(row, scope).Subscription.Id)))
        {
            var value = ReadSubscription(old, scope).Subscription;
            var tombstone = SubscriptionRow(scope, value, isDeleted: true);
            context.TriggerSubscriptions.Attach(tombstone);
            context.Entry(tombstone).State = EntityState.Modified;
            context.Entry(tombstone).Property(row => row.Revision).OriginalValue = old.Revision;
        }
        var receipt = new TriggerMutationReceipt(write.RequestId, write.RequestFingerprint, write.ActorId,
            write.Trigger.Id, write.Trigger.Revision, time.GetUtcNow());
        context.TriggerMutations.Add(MutationRow(scope, receipt));
        return receipt;
    }

    internal static async ValueTask<bool> LockResourceAsync(RuntimeDbContext db, TriggerScope scope, string triggerId,
        long expectedRevision, CancellationToken cancellationToken)
    {
        var id = ResourceId(scope, triggerId); var scopeKey = Encode(scope.Persistence.Value); var tenantKey = Encode(TenantTag(scope));
        return await db.StandaloneTriggers.Where(row => row.Id == id && row.ScopeKey == scopeKey && row.TenantKey == tenantKey && row.Revision == expectedRevision)
            .ExecuteUpdateAsync(update => update.SetProperty(row => row.Revision, expectedRevision), cancellationToken) == 1;
    }

    private void CheckScope(TriggerScope scope, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(scope); ArgumentNullException.ThrowIfNull(scope.Persistence);
        access.Current.RequireScope(); access.Current.EnsureScope(scope.Persistence);
        if (scope.TenantId is not null) ArgumentException.ThrowIfNullOrWhiteSpace(scope.TenantId);
    }

    private static void ValidateReplacement(TriggerScope scope, TriggerStoreWrite write, TriggerSubscription[] subscriptions,
        StandaloneTrigger? previous)
    {
        if (write.Trigger.TenantId != scope.TenantId) throw new TriggerOperationException("validation", "The prepared resource differs from its trusted scope.");
        if (write.ExpectedRevision != previous?.Revision || write.Trigger.Revision != (write.ExpectedRevision ?? 0) + 1)
            throw new TriggerOperationException("conflict", "The trigger changed; reload and review the change.");
        if (previous is not null && (previous.TypeKey != write.Trigger.TypeKey || previous.CreatedAt != write.Trigger.CreatedAt ||
            previous.CreatedBy != write.Trigger.CreatedBy || previous.Origin.Kind != write.Trigger.Origin.Kind || previous.Origin.SourceId != write.Trigger.Origin.SourceId ||
            previous.DesiredState == TriggerDesiredState.Deleted))
            throw new TriggerOperationException("validation", "A resource update cannot replace its identity, origin or deleted lifecycle.");
        if (subscriptions.Any(subscription => subscription is null || subscription.TriggerId != write.Trigger.Id ||
            subscription.Grant is { } grant && grant.SourceTypeKey != write.Trigger.TypeKey) ||
            subscriptions.Select(subscription => subscription.Id).Distinct(StringComparer.Ordinal).Count() != subscriptions.Length ||
            subscriptions.Select(subscription => (subscription.DefinitionId, subscription.SlotName)).Distinct().Count() != subscriptions.Length)
            throw new TriggerOperationException("validation", "Associations must belong to the trigger and have distinct identities and target slots.");
        if (subscriptions.Length == 0 && write.Trigger.DesiredState == TriggerDesiredState.Enabled)
            throw new TriggerOperationException("validation", "A trigger without associations must remain paused.");
    }

    private static TriggerMutationReceipt VerifyReplay(TriggerMutationReceipt replay, TriggerStoreWrite write)
    {
        if (replay.RequestFingerprint != write.RequestFingerprint || write.ExpectedRevision is not null && replay.TriggerId != write.Trigger.Id)
            throw new TriggerOperationException("conflict", "The mutation identity already names different content.");
        return replay;
    }

    private static StandaloneTriggerEntity ResourceRow(TriggerScope scope, StandaloneTrigger trigger) => new()
    {
        Id = ResourceId(scope, trigger.Id), ScopeKey = Encode(scope.Persistence.Value), ScopeHash = Hash(scope.Persistence.Value),
        TenantKey = Encode(TenantTag(scope)), TenantHash = Hash(TenantTag(scope)), TriggerKey = Encode(trigger.Id), TriggerOrderKey = Order(trigger.Id),
        TypeKey = Encode(trigger.TypeKey), SearchName = trigger.Name.ToUpperInvariant(), DesiredState = (int)trigger.DesiredState,
        Revision = trigger.Revision, ContentJson = JsonSerializer.Serialize(trigger, JsonOptions)
    };

    private static StandaloneTrigger ReadResource(StandaloneTriggerEntity row, TriggerScope scope)
    {
        var content = StandaloneTriggersEfModule.Chain.Upcast<StandaloneTriggerEntity>(row.SchemaVersion,
            (nameof(row.ContentJson), row.ContentJson))[nameof(row.ContentJson)]!;
        var value = Deserialize<StandaloneTrigger>(content) ?? throw new InvalidDataException("Invalid trigger content.");
        var expected = ResourceRow(scope, value);
        if (value.TenantId != scope.TenantId || row.Id != expected.Id || row.ScopeKey != expected.ScopeKey || row.ScopeHash != expected.ScopeHash ||
            row.TenantKey != expected.TenantKey || row.TenantHash != expected.TenantHash || row.TriggerKey != expected.TriggerKey ||
            row.TriggerOrderKey != expected.TriggerOrderKey || row.TypeKey != expected.TypeKey || row.SearchName != expected.SearchName ||
            row.DesiredState != expected.DesiredState || row.Revision != expected.Revision)
            throw new InvalidDataException("The trigger's stored identity or projection differs.");
        return value;
    }

    private static TriggerSubscriptionEntity SubscriptionRow(TriggerScope scope, TriggerSubscription subscription, bool isDeleted) => new()
    {
        Id = SubscriptionId(scope, subscription.Id), ScopeKey = Encode(scope.Persistence.Value), ScopeHash = Hash(scope.Persistence.Value),
        TenantKey = Encode(TenantTag(scope)), TenantHash = Hash(TenantTag(scope)), TriggerRowId = ResourceId(scope, subscription.TriggerId),
        SubscriptionKey = Encode(subscription.Id), SubscriptionOrderKey = Order(subscription.Id), DefinitionHash = Hash(subscription.DefinitionId),
        DefinitionKey = Encode(subscription.DefinitionId), SlotKey = Encode(subscription.SlotName), Revision = subscription.Revision,
        IsDeleted = isDeleted, ContentJson = JsonSerializer.Serialize(new SubscriptionJournal(subscription, isDeleted), JsonOptions)
    };

    private static SubscriptionJournal ReadSubscription(TriggerSubscriptionEntity row, TriggerScope scope)
    {
        var content = StandaloneTriggersEfModule.Chain.Upcast<TriggerSubscriptionEntity>(row.SchemaVersion,
            (nameof(row.ContentJson), row.ContentJson))[nameof(row.ContentJson)]!;
        var value = Deserialize<SubscriptionJournal>(content) ?? throw new InvalidDataException("Invalid association content.");
        if (value.Subscription is null) throw new InvalidDataException("An association has no content.");
        var expected = SubscriptionRow(scope, value.Subscription, value.IsDeleted);
        if (row.Id != expected.Id || row.ScopeKey != expected.ScopeKey || row.ScopeHash != expected.ScopeHash ||
            row.TenantKey != expected.TenantKey || row.TenantHash != expected.TenantHash || row.TriggerRowId != expected.TriggerRowId ||
            row.SubscriptionKey != expected.SubscriptionKey || row.SubscriptionOrderKey != expected.SubscriptionOrderKey ||
            row.DefinitionHash != expected.DefinitionHash || row.DefinitionKey != expected.DefinitionKey || row.SlotKey != expected.SlotKey ||
            row.IsDeleted != expected.IsDeleted || row.Revision != expected.Revision)
            throw new InvalidDataException("The association's stored identity or projection differs.");
        return value;
    }

    private static TriggerMutationEntity MutationRow(TriggerScope scope, TriggerMutationReceipt receipt) => new()
    {
        Id = MutationId(scope, receipt.ActorId, receipt.RequestId), ScopeKey = Encode(scope.Persistence.Value), TenantKey = Encode(TenantTag(scope)),
        ActorKey = Encode(receipt.ActorId), RequestKey = Encode(receipt.RequestId), TriggerRowId = ResourceId(scope, receipt.TriggerId),
        ContentJson = JsonSerializer.Serialize(receipt, JsonOptions)
    };

    private static TriggerMutationReceipt ReadMutation(TriggerMutationEntity row, TriggerScope scope, string actor, string request)
    {
        var content = StandaloneTriggersEfModule.Chain.Upcast<TriggerMutationEntity>(row.SchemaVersion,
            (nameof(row.ContentJson), row.ContentJson))[nameof(row.ContentJson)]!;
        var value = Deserialize<TriggerMutationReceipt>(content) ?? throw new InvalidDataException("Invalid mutation receipt.");
        var expected = MutationRow(scope, value);
        if (value.ActorId != actor || value.RequestId != request || row.Id != expected.Id || row.ScopeKey != expected.ScopeKey ||
            row.TenantKey != expected.TenantKey || row.ActorKey != expected.ActorKey || row.RequestKey != expected.RequestKey || row.TriggerRowId != expected.TriggerRowId)
            throw new InvalidDataException("The mutation receipt differs from its scoped key.");
        return value;
    }

    private static T? Deserialize<T>(string content)
    {
        try { return JsonSerializer.Deserialize<T>(content, JsonOptions); }
        catch (ArgumentException exception) { throw new InvalidDataException("Stored trigger content violates its contract.", exception); }
    }

    private static string TenantTag(TriggerScope scope) => scope.TenantId is null ? "null" : "value:" + scope.TenantId;
    private static string ResourceId(TriggerScope scope, string id) => CompositeId(scope.Persistence.Value, TenantTag(scope), "trigger", id);
    private static string SubscriptionId(TriggerScope scope, string id) => CompositeId(scope.Persistence.Value, TenantTag(scope), "subscription", id);
    private static string MutationId(TriggerScope scope, string actor, string request) => CompositeId(scope.Persistence.Value, TenantTag(scope), "mutation", actor, request);
    private static void CheckLimit(int limit) { if (limit is < 1 or > 100) throw new TriggerOperationException("validation", "Page size must be between 1 and 100."); }
    private sealed record SubscriptionJournal(TriggerSubscription Subscription, bool IsDeleted);
}
