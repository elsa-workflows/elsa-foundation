using System.Data.Common;
using System.Text.Json;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed partial class StandaloneTriggerStoreTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);
    private static readonly TriggerScope Scope = new(new("tenant-a"), "tenant-a");
    private const string TypeKey = "Test.Http";

    [Fact]
    public async Task New_context_reads_the_same_source_owned_resource_association_grant_and_receipt()
    {
        await using var database = await Database.CreateAsync();
        var resource = Resource("http");
        var subscription = Subscription("subscription", "http");
        TriggerMutationReceipt receipt;
        await using (var first = database.Open())
            receipt = await first.Store.WriteAsync(Scope, Write(resource, [subscription]));

        await using var restarted = database.Open();
        var aggregate = (await restarted.Store.FindAsync(Scope, "http"))!;
        Assert.Equal(JsonSerializer.Serialize(resource), JsonSerializer.Serialize(aggregate.Trigger));
        Assert.Equal(JsonSerializer.Serialize(subscription), JsonSerializer.Serialize(Assert.Single(aggregate.Subscriptions)));
        var byTrigger = Assert.Single((await restarted.Store.ListSubscriptionsAsync(Scope, new(TriggerId: "http"))).Items);
        var byDefinition = Assert.Single((await restarted.Store.ListSubscriptionsAsync(Scope,
            new(DefinitionId: "definition", SlotName: "Published"))).Items);
        Assert.Equal(JsonSerializer.Serialize(byTrigger), JsonSerializer.Serialize(byDefinition));
        Assert.Equal(JsonSerializer.Serialize(byTrigger), JsonSerializer.Serialize(await restarted.Store.FindSubscriptionAsync(Scope, subscription.Id)));
        Assert.Equal("provider/http.json", aggregate.Trigger.Origin.SourceId);
        Assert.True(aggregate.Trigger.Origin.ConfigurationReadOnly);
        Assert.Equal("source-publisher", aggregate.Trigger.CreatedBy);
        Assert.Equal("retired-author", byDefinition.Grant!.GrantedBy);
        Assert.Equal(Now, byDefinition.Grant.GrantedAt);
        Assert.Equal(receipt, await restarted.Store.FindMutationAsync(Scope, "operator", "request"));
        Assert.Equal(Now, receipt.CommittedAt);
    }

    [Fact]
    public async Task Identical_replay_precedes_CAS_after_restart_and_changed_content_conflicts()
    {
        await using var database = await Database.CreateAsync();
        var create = Write(Resource("http"), [Subscription("subscription", "http")]);
        TriggerMutationReceipt receipt;
        await using (var first = database.Open())
        {
            receipt = await first.Store.WriteAsync(Scope, create);
            await first.Store.WriteAsync(Scope, Write(Resource("http", revision: 2, name: "Updated"), [], "update", "update", 1));
        }

        await using var restarted = database.Open();
        Assert.Equal(receipt, await restarted.Store.WriteAsync(Scope, create));
        var conflict = await Assert.ThrowsAsync<TriggerOperationException>(() => restarted.Store.WriteAsync(Scope,
            create with { RequestFingerprint = "changed-content" }).AsTask());
        Assert.Equal("conflict", conflict.Code);
        Assert.Equal(2, (await restarted.Store.FindAsync(Scope, "http"))!.Trigger.Revision);
        Assert.Equal(receipt, await restarted.Store.FindMutationAsync(Scope, "operator", "request"));
        Assert.Equal(2, await restarted.Context.TriggerMutations.CountAsync());
    }

    [Fact]
    public async Task Concurrent_contexts_reconcile_generated_create_ids_to_one_committed_receipt()
    {
        await using var database = await Database.CreateAsync();
        var heldRead = new HoldMutationRead();
        await using var firstWriter = database.Open(interceptors: [heldRead]);
        await using var secondWriter = database.Open(interceptors: [new ReleaseBeforeTransaction(heldRead)]);
        var first = firstWriter.Store.WriteAsync(Scope,
            Write(Resource("generated-one"), [Subscription("subscription-generated-one", "generated-one")])).AsTask();
        TriggerMutationReceipt[] receipts;
        try
        {
            await heldRead.Entered.Task.WaitAsync(TimeSpan.FromSeconds(30));
            // The second writer reaches the actual transaction boundary while the first still owns its transaction.
            var second = Task.Run(async () => await secondWriter.Store.WriteAsync(Scope,
                Write(Resource("generated-two"), [Subscription("subscription-generated-two", "generated-two")])));
            receipts = await Task.WhenAll(first, second);
        }
        finally { heldRead.Release.TrySetResult(); }

        Assert.Equal(receipts[0], receipts[1]);
        await using var verification = database.Open();
        var winner = Assert.Single((await verification.Store.ListAsync(Scope, new())).Items);
        Assert.Equal(receipts[0].TriggerId, winner.Id);
        Assert.Single((await verification.Store.FindAsync(Scope, winner.Id))!.Subscriptions);
        Assert.Equal(1, await verification.Context.StandaloneTriggers.CountAsync());
        Assert.Equal(1, await verification.Context.TriggerSubscriptions.CountAsync());
        Assert.Equal(1, await verification.Context.TriggerMutations.CountAsync());
        Assert.Null(await verification.Store.FindAsync(Scope, winner.Id == "generated-one" ? "generated-two" : "generated-one"));
    }

    [Theory]
    [InlineData("stale")]
    [InlineData("foreign-association")]
    [InlineData("duplicate-id")]
    [InlineData("duplicate-target")]
    [InlineData("creator")]
    [InlineData("source-type")]
    [InlineData("tenant")]
    [InlineData("enabled-empty")]
    public async Task Refused_replacement_preserves_resource_associations_and_receipts_after_restart(string invalid)
    {
        await using var database = await Database.CreateAsync();
        await using var fixture = database.Open();
        await fixture.Store.WriteAsync(Scope, Write(Resource("http", state: TriggerDesiredState.Enabled), [Subscription("original", "http")]));
        var before = JsonSerializer.Serialize(await fixture.Store.FindAsync(Scope, "http"));
        var replacement = Resource("http", revision: 2, name: "Replacement");
        TriggerSubscription[] subscriptions = [Subscription("replacement", "http", "other-definition")];
        long expected = 1;
        switch (invalid)
        {
            case "stale": expected = 0; break;
            case "foreign-association": subscriptions = [Subscription("replacement", "another-trigger")]; break;
            case "duplicate-id": subscriptions = [Subscription("duplicate", "http"), Subscription("duplicate", "http", "other")]; break;
            case "duplicate-target": subscriptions = [Subscription("one", "http"), Subscription("two", "http")]; break;
            case "creator": replacement = Resource("http", revision: 2, creator: "replacement-creator"); break;
            case "source-type": subscriptions = [Subscription("replacement", "http", sourceType: "Other.Source")]; break;
            case "tenant": replacement = Resource("http", revision: 2, tenant: "other-tenant"); break;
            case "enabled-empty": replacement = Resource("http", revision: 2, state: TriggerDesiredState.Enabled); subscriptions = []; break;
        }
        await Assert.ThrowsAsync<TriggerOperationException>(() => fixture.Store.WriteAsync(Scope,
            Write(replacement, subscriptions, "refused", "refused", expected)).AsTask());

        await using var verification = database.Open();
        Assert.Equal(before, JsonSerializer.Serialize(await verification.Store.FindAsync(Scope, "http")));
        Assert.Null(await verification.Store.FindMutationAsync(Scope, "operator", "refused"));
        Assert.Null(await verification.Store.FindSubscriptionAsync(Scope, "replacement"));
        Assert.Equal(1, await verification.Context.StandaloneTriggers.CountAsync());
        Assert.Equal(1, await verification.Context.TriggerSubscriptions.CountAsync());
        Assert.Equal(1, await verification.Context.TriggerMutations.CountAsync());
        Assert.Empty(fixture.Context.ChangeTracker.Entries());
    }

    [Fact]
    public async Task Last_association_removal_is_paused_and_its_tombstone_prevents_reuse_in_any_resource()
    {
        await using var database = await Database.CreateAsync();
        await using (var first = database.Open())
        {
            await first.Store.WriteAsync(Scope, Write(Resource("http", state: TriggerDesiredState.Enabled), [Subscription("removed", "http")]));
            await first.Store.WriteAsync(Scope, Write(Resource("http", revision: 2), [], "remove", "remove", 1));
        }
        await using var restarted = database.Open();
        Assert.Equal(TriggerDesiredState.Paused, (await restarted.Store.FindAsync(Scope, "http"))!.Trigger.DesiredState);
        Assert.Null(await restarted.Store.FindSubscriptionAsync(Scope, "removed"));
        Assert.Empty((await restarted.Store.ListSubscriptionsAsync(Scope, new(DefinitionId: "definition"))).Items);
        Assert.True((await restarted.Context.TriggerSubscriptions.SingleAsync()).IsDeleted);
        foreach (var candidate in new[]
        {
            Write(Resource("http", revision: 3), [Subscription("removed", "http")], "reuse-own", "reuse-own", 2),
            Write(Resource("other"), [Subscription("removed", "other")], "reuse-other", "reuse-other")
        })
        {
            var refusal = await Assert.ThrowsAsync<TriggerOperationException>(() => restarted.Store.WriteAsync(Scope, candidate).AsTask());
            Assert.Equal("conflict", refusal.Code);
            Assert.Null(await restarted.Store.FindMutationAsync(Scope, "operator", candidate.RequestId));
        }
        Assert.Equal(2, (await restarted.Store.FindAsync(Scope, "http"))!.Trigger.Revision);
        Assert.Null(await restarted.Store.FindAsync(Scope, "other"));
        Assert.True((await restarted.Context.TriggerSubscriptions.SingleAsync()).IsDeleted);
    }

    [Fact]
    public async Task Existing_association_identity_cannot_be_taken_by_another_resource()
    {
        await using var database = await Database.CreateAsync();
        await using var fixture = database.Open();
        await fixture.Store.WriteAsync(Scope, Write(Resource("owner"), [Subscription("reserved", "owner")]));
        var refusal = await Assert.ThrowsAsync<TriggerOperationException>(() => fixture.Store.WriteAsync(Scope,
            Write(Resource("intruder"), [Subscription("reserved", "intruder")], "intrude", "intrude")).AsTask());
        Assert.Equal("conflict", refusal.Code);
        await using var verification = database.Open();
        Assert.Equal("owner", (await verification.Store.FindSubscriptionAsync(Scope, "reserved"))!.TriggerId);
        Assert.Null(await verification.Store.FindAsync(Scope, "intruder"));
        Assert.Null(await verification.Store.FindMutationAsync(Scope, "operator", "intrude"));
        Assert.Equal(1, await verification.Context.StandaloneTriggers.CountAsync());
        Assert.Equal(1, await verification.Context.TriggerSubscriptions.CountAsync());
    }

    [Fact]
    public async Task Operational_update_retains_creator_source_origin_and_grant_revocation_audit_after_restart()
    {
        await using var database = await Database.CreateAsync();
        var original = Subscription("subscription", "http");
        var grant = original.Grant!;
        var revoked = new TriggerInvocationGrant(grant.Id, grant.TriggerId, grant.SubscriptionId, grant.DefinitionId,
            grant.SlotName, grant.ModelKey, grant.SourceTypeKey, 2, TriggerGrantStatus.Revoked,
            grant.GrantedBy, grant.GrantedAt, "security-operator", Now.AddMinutes(1));
        var replacement = new TriggerSubscription(original.Id, original.TriggerId, original.DefinitionId, original.SlotName,
            original.ModelKey, 2, original.MappingRevision, false, original.Mappings, original.Origin, revoked);
        await using (var first = database.Open())
        {
            await first.Store.WriteAsync(Scope, Write(Resource("http"), [original]));
            await first.Store.WriteAsync(Scope, Write(Resource("http", revision: 2), [replacement], "revoke", "revoke", 1));
        }
        await using var restarted = database.Open();
        var reloaded = (await restarted.Store.FindAsync(Scope, "http"))!;
        Assert.Equal("source-publisher", reloaded.Trigger.CreatedBy);
        Assert.Equal(Now, reloaded.Trigger.CreatedAt);
        Assert.Equal("provider/http.json", reloaded.Trigger.Origin.SourceId);
        Assert.True(reloaded.Trigger.Origin.ConfigurationReadOnly);
        var association = Assert.Single(reloaded.Subscriptions);
        Assert.False(association.Enabled);
        Assert.Equal(JsonSerializer.Serialize(replacement), JsonSerializer.Serialize(association));
        Assert.Equal("retired-author", association.Grant!.GrantedBy);
        Assert.Equal("security-operator", association.Grant.RevokedBy);
        Assert.Equal(Now.AddMinutes(1), association.Grant.RevokedAt);
    }

    [Fact]
    public async Task Deleted_resource_is_hidden_but_retains_its_unreusable_identity()
    {
        await using var database = await Database.CreateAsync();
        await using var fixture = database.Open();
        await fixture.Store.WriteAsync(Scope, Write(Resource("http"), [Subscription("subscription", "http")]));
        await fixture.Store.WriteAsync(Scope, Write(Resource("http", revision: 2, state: TriggerDesiredState.Deleted), [], "delete", "delete", 1));
        Assert.Empty((await fixture.Store.ListAsync(Scope, new())).Items);
        Assert.Null(await fixture.Store.FindSubscriptionAsync(Scope, "subscription"));
        await Assert.ThrowsAsync<TriggerOperationException>(() => fixture.Store.WriteAsync(Scope,
            Write(Resource("http", revision: 3), [], "resurrect", "resurrect", 2)).AsTask());
        await Assert.ThrowsAsync<TriggerOperationException>(() => fixture.Store.WriteAsync(Scope,
            Write(Resource("http"), [], "recreate", "recreate")).AsTask());
        Assert.Equal(TriggerDesiredState.Deleted, (await fixture.Store.FindAsync(Scope, "http"))!.Trigger.DesiredState);
        Assert.Equal(2, await fixture.Context.TriggerMutations.CountAsync());
    }

    [Fact]
    public async Task Tenant_scope_and_actor_keys_remain_distinct_including_null_tenant()
    {
        await using var database = await Database.CreateAsync();
        foreach (var scope in new[] { Scope, new TriggerScope(Scope.Persistence, null), new TriggerScope(new("tenant-b"), "tenant-b"),
                     new TriggerScope(new("shell:opaque-namespace"), "independent-tenant") })
        {
            await using var fixture = database.Open(scope);
            await fixture.Store.WriteAsync(scope, Write(Resource("same", tenant: scope.TenantId), [Subscription("same", "same")]));
            Assert.Equal(scope.TenantId, (await fixture.Store.FindAsync(scope, "same"))!.Trigger.TenantId);
            Assert.Single((await fixture.Store.ListAsync(scope, new())).Items);
            Assert.Single((await fixture.Store.ListSubscriptionsAsync(scope, new(DefinitionId: "definition"))).Items);
        }
        await using var original = database.Open();
        await original.Store.WriteAsync(Scope, Write(Resource("actor-owned"), [], actor: "different-operator"));
        Assert.Equal("same", (await original.Store.FindMutationAsync(Scope, "operator", "request"))!.TriggerId);
        Assert.Equal("actor-owned", (await original.Store.FindMutationAsync(Scope, "different-operator", "request"))!.TriggerId);
        Assert.Equal(5, await original.Context.StandaloneTriggers.CountAsync());
        Assert.Equal(5, await original.Context.TriggerMutations.CountAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => original.Store.FindAsync(new(new("tenant-b"), "tenant-b"), "same").AsTask());
        Assert.Null(await original.Store.FindAsync(new(Scope.Persistence, "tenant-b"), "same"));
    }

    [Fact]
    public async Task Collection_filters_and_cursors_are_stable_and_search_is_literal()
    {
        await using var database = await Database.CreateAsync();
        await using var fixture = database.Open();
        foreach (var id in new[] { "d", "b", "a", "c" })
            await fixture.Store.WriteAsync(Scope, Write(Resource(id, name: id == "a" ? "Other" : "Sales %_ ' route"),
                [Subscription(id, id)], id, id));
        var first = await fixture.Store.ListAsync(Scope, new(1, Search: "%_ '"));
        Assert.Equal("b", Assert.Single(first.Items).Id);
        Assert.Equal("b", first.Next);
        var second = await fixture.Store.ListAsync(Scope, new(2, first.Next, "%_ '"));
        Assert.Equal(["c", "d"], second.Items.Select(item => item.Id));
        Assert.Null(second.Next);
        Assert.Empty((await fixture.Store.ListAsync(Scope, new(Search: "' OR 1=1 --"))).Items);
        Assert.Empty((await fixture.Store.ListAsync(Scope, new(TypeKey: "Other.Type"))).Items);
        var associations = await fixture.Store.ListSubscriptionsAsync(Scope, new(DefinitionId: "definition", Limit: 2));
        Assert.Equal(["a", "b"], associations.Items.Select(item => item.Id));
        Assert.Equal(["c", "d"], (await fixture.Store.ListSubscriptionsAsync(Scope,
            new(DefinitionId: "definition", Limit: 2, After: associations.Next))).Items.Select(item => item.Id));
        await Assert.ThrowsAsync<TriggerOperationException>(() => fixture.Store.ListAsync(Scope, new(101)).AsTask());
        await Assert.ThrowsAsync<TriggerOperationException>(() => fixture.Store.ListSubscriptionsAsync(Scope, new()).AsTask());
        await Assert.ThrowsAsync<TriggerOperationException>(() => fixture.Store.ListSubscriptionsAsync(Scope,
            new(TriggerId: "a", DefinitionId: "definition")).AsTask());
    }

    [Theory]
    [InlineData("resource")]
    [InlineData("subscription")]
    [InlineData("receipt")]
    [InlineData("type-hash")]
    [InlineData("slot-hash")]
    public async Task Corrupt_projections_refuse_instead_of_returning_detached_content(string family)
    {
        await using var database = await Database.CreateAsync();
        await using var fixture = database.Open();
        await fixture.Store.WriteAsync(Scope, Write(Resource("http"), [Subscription("subscription", "http")]));
        switch (family)
        {
            case "resource": await fixture.Context.StandaloneTriggers.ExecuteUpdateAsync(set => set.SetProperty(row => row.SearchName, "CORRUPT")); break;
            case "type-hash": await fixture.Context.StandaloneTriggers.ExecuteUpdateAsync(set => set.SetProperty(row => row.TypeHash, "CORRUPT")); break;
            case "slot-hash": await fixture.Context.TriggerSubscriptions.ExecuteUpdateAsync(set => set.SetProperty(row => row.SlotHash, "CORRUPT")); break;
            case "subscription": await fixture.Context.TriggerSubscriptions.ExecuteUpdateAsync(set => set.SetProperty(row => row.DefinitionKey, "CORRUPT")); break;
            case "receipt": await fixture.Context.TriggerMutations.ExecuteUpdateAsync(set => set.SetProperty(row => row.TriggerRowId, "CORRUPT")); break;
        }
        await using var restarted = database.Open();
        var error = await Assert.ThrowsAsync<TriggerOperationException>(() => ReadAsync(restarted.Store, family));
        Assert.Equal("conflict", error.Code);
        Assert.IsType<InvalidDataException>(error.InnerException);
        Assert.Equal(Scope.Persistence.Value, error.Fields!["scope"]);
    }

    [Theory]
    [InlineData("resource")]
    [InlineData("subscription")]
    [InlineData("receipt")]
    public async Task Unknown_schema_is_reported_as_skew_for_each_resource_family_row(string family)
    {
        await using var database = await Database.CreateAsync();
        await using var fixture = database.Open();
        await fixture.Store.WriteAsync(Scope, Write(Resource("http"), [Subscription("subscription", "http")]));
        switch (family)
        {
            case "resource": await fixture.Context.StandaloneTriggers.ExecuteUpdateAsync(set => set.SetProperty(row => row.SchemaVersion, "999.0.0")); break;
            case "subscription": await fixture.Context.TriggerSubscriptions.ExecuteUpdateAsync(set => set.SetProperty(row => row.SchemaVersion, "999.0.0")); break;
            case "receipt": await fixture.Context.TriggerMutations.ExecuteUpdateAsync(set => set.SetProperty(row => row.SchemaVersion, "999.0.0")); break;
        }
        await using var restarted = database.Open();
        var skew = await Assert.ThrowsAsync<EfSchemaVersionSkewException>(() => ReadAsync(restarted.Store, family));
        Assert.Equal(StandaloneTriggersEfModule.SchemaFamily, skew.Family);
        Assert.Equal([StandaloneTriggersEfModule.SchemaVersion], skew.ReadableVersions);
    }

    [Fact]
    public async Task Resource_write_refuses_to_flush_unrelated_changes_or_borrow_a_transaction()
    {
        await using var database = await Database.CreateAsync();
        await using var fixture = database.Open();
        await fixture.Store.WriteAsync(Scope, Write(Resource("http"), []));
        var row = await fixture.Context.StandaloneTriggers.SingleAsync();
        row.SearchName = "UNRELATED CHANGE";
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Store.WriteAsync(Scope,
            Write(Resource("other"), [], "other", "other")).AsTask());
        fixture.Context.ChangeTracker.Clear();
        await using (var transaction = await fixture.Context.Database.BeginTransactionAsync())
            await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Store.WriteAsync(Scope,
                Write(Resource("other"), [], "other", "other")).AsTask());
        await using var verification = database.Open();
        Assert.Equal("HTTP ENDPOINT", (await verification.Context.StandaloneTriggers.SingleAsync()).SearchName);
        Assert.Null(await verification.Store.FindAsync(Scope, "other"));
        Assert.Null(await verification.Store.FindMutationAsync(Scope, "operator", "other"));
    }

    private static async Task ReadAsync(IStandaloneTriggerStore store, string family)
    {
        if (family is "resource" or "type-hash") await store.FindAsync(Scope, "http");
        else if (family is "subscription" or "slot-hash") await store.FindSubscriptionAsync(Scope, "subscription");
        else await store.FindMutationAsync(Scope, "operator", "request");
    }

    private static StandaloneTrigger Resource(string id, long revision = 1, string name = "HTTP endpoint",
        TriggerDesiredState state = TriggerDesiredState.Paused, string? tenant = "tenant-a", string creator = "source-publisher", string typeKey = TypeKey) =>
        new(id, name, typeKey, 1, JsonSerializer.SerializeToElement(new { path = "/orders", method = "POST" }),
            revision, revision, revision, state, new(TriggerManagementKind.Provider, "provider/http.json", true),
            Now, Now.AddMinutes(revision - 1), creator, "Source-owned endpoint", hasActivated: true, tenantId: tenant);

    private static TriggerSubscription Subscription(string id, string trigger, string definition = "definition", string sourceType = TypeKey, string slot = "Published") =>
        new(id, trigger, definition, slot, "workflow", 1, 1, true,
            [new("OrderId", new TriggerSourceBinding(["request", "id"])), new("Optional", new TriggerLiteralBinding(JsonSerializer.SerializeToElement<object?>(null)))],
            new(TriggerManagementKind.Provider, "provider/http.json", true),
            new("grant-" + id, trigger, id, definition, slot, "workflow", sourceType, 1, TriggerGrantStatus.Granted, "retired-author", Now));

    private static TriggerStoreWrite Write(StandaloneTrigger resource, IReadOnlyList<TriggerSubscription> subscriptions,
        string request = "request", string fingerprint = "request-fingerprint", long? expected = null, string actor = "operator") =>
        new(request, fingerprint, actor, expected, resource, subscriptions);

    private sealed class Access(TriggerScope scope) : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = PersistenceAccessContext.Scoped(scope.Persistence);
    }

    private sealed class Database : IAsyncDisposable
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), "elsa-trigger-store-" + Guid.NewGuid().ToString("N") + ".sqlite");
        private DbContextOptions<RuntimeSqliteDbContext> Options => new DbContextOptionsBuilder<RuntimeSqliteDbContext>()
            .UseSqlite(new SqliteConnectionStringBuilder { DataSource = _path, Pooling = false, DefaultTimeout = 10 }.ToString()).Options;

        public static async Task<Database> CreateAsync(params IInterceptor[] interceptors)
        {
            var database = new Database();
            await using var context = new RuntimeSqliteDbContext(new DbContextOptionsBuilder<RuntimeSqliteDbContext>(database.Options).AddInterceptors(interceptors).Options);
            await context.Database.EnsureCreatedAsync();
            return database;
        }

        public Fixture Open(TriggerScope? scope = null, params IInterceptor[] interceptors) =>
            new(new RuntimeSqliteDbContext(new DbContextOptionsBuilder<RuntimeSqliteDbContext>(Options)
                .AddInterceptors(interceptors).Options), scope ?? Scope);

        public ValueTask DisposeAsync()
        {
            File.Delete(_path);
            File.Delete(_path + "-wal");
            File.Delete(_path + "-shm");
            return ValueTask.CompletedTask;
        }
    }

    private sealed class Fixture(RuntimeSqliteDbContext context, TriggerScope scope) : IAsyncDisposable
    {
        public RuntimeSqliteDbContext Context { get; } = context;
        public EfStandaloneTriggerStore Store { get; } = new(context, new Access(scope), new FakeTimeProvider(Now));
        public ValueTask DisposeAsync() => Context.DisposeAsync();
    }

    private sealed class HoldMutationRead : DbCommandInterceptor
    {
        private int _held;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("elsa_runtime_trigger_mutations", StringComparison.Ordinal) &&
                Interlocked.Exchange(ref _held, 1) == 0)
            {
                Entered.TrySetResult();
                await Release.Task.WaitAsync(cancellationToken);
            }
            return result;
        }
    }

    private sealed class ReleaseBeforeTransaction(HoldMutationRead first) : DbTransactionInterceptor
    {
        public override ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(DbConnection connection,
            TransactionStartingEventData eventData, InterceptionResult<DbTransaction> result,
            CancellationToken cancellationToken = default)
        {
            first.Release.TrySetResult();
            return ValueTask.FromResult(result);
        }
    }
}
