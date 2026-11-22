using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Elsa.Workflows.Runtime.Services.Triggers;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class StandaloneTriggerStoreTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);
    private static readonly TriggerScope Scope = new(new("tenant-a"), "tenant-a");
    private const string TypeKey = "Test.Http";

    [Fact]
    public async Task Reload_and_both_management_views_read_the_same_audited_association()
    {
        var state = new InMemoryRuntimeCheckpointStoreState();
        var store = Store(state);
        var resource = Resource("http");
        var subscription = Subscription("subscription", resource.Id);
        var receipt = await store.WriteAsync(Scope, Write(resource, [subscription]));

        var reloaded = Store(state);
        var aggregate = (await reloaded.FindAsync(Scope, resource.Id))!;
        Assert.Equal(JsonSerializer.Serialize(resource), JsonSerializer.Serialize(aggregate.Trigger));
        var byTrigger = Assert.Single((await reloaded.ListSubscriptionsAsync(Scope, new(TriggerId: resource.Id))).Items);
        var byDefinition = Assert.Single((await reloaded.ListSubscriptionsAsync(Scope, new(DefinitionId: subscription.DefinitionId, SlotName: "Published"))).Items);
        Assert.Equal(JsonSerializer.Serialize(subscription), JsonSerializer.Serialize(Assert.Single(aggregate.Subscriptions)));
        Assert.Equal(JsonSerializer.Serialize(byTrigger), JsonSerializer.Serialize(byDefinition));
        Assert.Equal(JsonSerializer.Serialize(byTrigger), JsonSerializer.Serialize(await reloaded.FindSubscriptionAsync(Scope, subscription.Id)));
        Assert.Equal("provider/http.json", aggregate.Trigger.Origin.SourceId);
        Assert.True(aggregate.Trigger.Origin.ConfigurationReadOnly);
        Assert.Equal("source-publisher", aggregate.Trigger.CreatedBy);
        Assert.Equal("retired-author", byDefinition.Grant!.GrantedBy);
        Assert.Equal(Now, byDefinition.Grant.GrantedAt);
        Assert.Equal(receipt, await reloaded.FindMutationAsync(Scope, "operator", "request"));
        Assert.Equal(Now, receipt.CommittedAt);
    }

    [Fact]
    public async Task Retained_associations_are_detached_from_the_writers_collection_and_read_only_to_readers()
    {
        var state = new InMemoryRuntimeCheckpointStoreState();
        var store = Store(state);
        var association = Subscription("subscription", "http");
        var submitted = new List<TriggerSubscription> { association };
        await store.WriteAsync(Scope, Write(Resource("http"), submitted));
        submitted.Clear();

        var captured = (await Store(state).FindAsync(Scope, "http"))!;
        Assert.Same(association, Assert.Single(captured.Subscriptions));
        var exposed = Assert.IsAssignableFrom<IList<TriggerSubscription>>(captured.Subscriptions);
        Assert.Throws<NotSupportedException>(() => exposed.Clear());
        Assert.Same(association, Assert.Single((await store.ListSubscriptionsAsync(Scope, new(DefinitionId: "definition"))).Items));
    }

    [Fact]
    public async Task Replay_precedes_CAS_and_an_update_does_not_overwrite_the_first_receipt()
    {
        var store = Store(new());
        var create = Write(Resource("http"), [Subscription("subscription", "http")]);
        var first = await store.WriteAsync(Scope, create);
        await store.WriteAsync(Scope, Write(Resource("http", revision: 2, name: "Changed"), [], "update", "update-fingerprint", 1));

        Assert.Equal(first, await store.WriteAsync(Scope, create));
        Assert.Equal(2, (await store.FindAsync(Scope, "http"))!.Trigger.Revision);
        Assert.Equal(first, await store.FindMutationAsync(Scope, "operator", "request"));
        var changed = await Assert.ThrowsAsync<TriggerOperationException>(() => store.WriteAsync(Scope,
            create with { RequestFingerprint = "different-content" }).AsTask());
        Assert.Equal("conflict", changed.Code);
        Assert.Equal(2, (await store.FindAsync(Scope, "http"))!.Trigger.Revision);
    }

    [Fact]
    public async Task Concurrent_generated_creates_share_the_native_write_gate_and_one_winning_receipt()
    {
        var state = new InMemoryRuntimeCheckpointStoreState();
        var firstStore = Store(state);
        var secondStore = Store(state);
        Task<TriggerMutationReceipt> first;
        Task<TriggerMutationReceipt> second;
        await state.WriteGate.WaitAsync();
        try
        {
            first = firstStore.WriteAsync(Scope, Write(Resource("generated-one"), [Subscription("one", "generated-one")])).AsTask();
            second = secondStore.WriteAsync(Scope, Write(Resource("generated-two"), [Subscription("two", "generated-two")])).AsTask();
            Assert.False(first.IsCompleted);
            Assert.False(second.IsCompleted);
            Assert.Empty((await firstStore.ListAsync(Scope, new())).Items);
            Assert.Null(await firstStore.FindMutationAsync(Scope, "operator", "request"));
        }
        finally { state.WriteGate.Release(); }

        var receipts = await Task.WhenAll(first, second);
        Assert.Equal(receipts[0], receipts[1]);
        var winner = Assert.Single((await firstStore.ListAsync(Scope, new())).Items);
        Assert.Equal(receipts[0].TriggerId, winner.Id);
        Assert.Single((await firstStore.FindAsync(Scope, winner.Id))!.Subscriptions);
        Assert.Null(await firstStore.FindAsync(Scope, winner.Id == "generated-one" ? "generated-two" : "generated-one"));
        Assert.Null(await firstStore.FindSubscriptionAsync(Scope, winner.Id == "generated-one" ? "two" : "one"));
    }

    [Theory]
    [InlineData("stale")]
    [InlineData("revision")]
    [InlineData("foreign-association")]
    [InlineData("duplicate-id")]
    [InlineData("duplicate-target")]
    [InlineData("creator")]
    [InlineData("source-type")]
    [InlineData("tenant")]
    [InlineData("enabled-empty")]
    public async Task Refused_replacement_preserves_the_entire_previous_aggregate_and_no_receipt(string invalid)
    {
        var store = Store(new());
        var previous = Write(Resource("http", state: TriggerDesiredState.Enabled), [Subscription("original", "http")]);
        await store.WriteAsync(Scope, previous);
        var before = JsonSerializer.Serialize(await store.FindAsync(Scope, "http"));
        var replacement = Resource("http", revision: 2, name: "Replacement");
        TriggerSubscription[] subscriptions = [Subscription("replacement", "http", "other-definition")];
        long expected = 1;
        switch (invalid)
        {
            case "stale": expected = 0; replacement = Resource("http", revision: 1, name: "Replacement"); break;
            case "revision": replacement = Resource("http", revision: 3, name: "Replacement"); break;
            case "foreign-association": subscriptions = [Subscription("replacement", "another-trigger")]; break;
            case "duplicate-id": subscriptions = [Subscription("duplicate", "http"), Subscription("duplicate", "http", "other")]; break;
            case "duplicate-target": subscriptions = [Subscription("one", "http"), Subscription("two", "http")]; break;
            case "creator": replacement = Resource("http", revision: 2, creator: "replacement-creator"); break;
            case "source-type": subscriptions = [Subscription("replacement", "http", sourceType: "Other.Source")]; break;
            case "tenant": replacement = Resource("http", revision: 2, tenant: "other-tenant"); break;
            case "enabled-empty": replacement = Resource("http", revision: 2, state: TriggerDesiredState.Enabled); subscriptions = []; break;
        }

        await Assert.ThrowsAsync<TriggerOperationException>(() => store.WriteAsync(Scope,
            Write(replacement, subscriptions, "refused", "different-fingerprint", expected)).AsTask());
        Assert.Equal(before, JsonSerializer.Serialize(await store.FindAsync(Scope, "http")));
        Assert.Null(await store.FindMutationAsync(Scope, "operator", "refused"));
        Assert.Null(await store.FindSubscriptionAsync(Scope, "replacement"));
        Assert.Single((await store.ListSubscriptionsAsync(Scope, new(DefinitionId: "definition"))).Items);
    }

    [Fact]
    public async Task Removing_the_last_association_pauses_and_retains_an_unreusable_identity_tombstone()
    {
        var state = new InMemoryRuntimeCheckpointStoreState();
        var store = Store(state);
        await store.WriteAsync(Scope, Write(Resource("http", state: TriggerDesiredState.Enabled), [Subscription("removed", "http")]));
        await store.WriteAsync(Scope, Write(Resource("http", revision: 2), [], "remove", "remove", 1));
        var reloaded = Store(state);
        Assert.Equal(TriggerDesiredState.Paused, (await reloaded.FindAsync(Scope, "http"))!.Trigger.DesiredState);
        Assert.Null(await reloaded.FindSubscriptionAsync(Scope, "removed"));
        Assert.Empty((await reloaded.ListSubscriptionsAsync(Scope, new(DefinitionId: "definition"))).Items);
        foreach (var candidate in new[]
        {
            Write(Resource("http", revision: 3), [Subscription("removed", "http")], "reuse-own", "reuse-own", 2),
            Write(Resource("other"), [Subscription("removed", "other")], "reuse-other", "reuse-other")
        })
        {
            var refusal = await Assert.ThrowsAsync<TriggerOperationException>(() => reloaded.WriteAsync(Scope, candidate).AsTask());
            Assert.Equal("conflict", refusal.Code);
            Assert.Null(await reloaded.FindMutationAsync(Scope, "operator", candidate.RequestId));
        }
        Assert.Null(await reloaded.FindAsync(Scope, "other"));
        Assert.Equal(2, (await reloaded.FindAsync(Scope, "http"))!.Trigger.Revision);
    }

    [Fact]
    public async Task Existing_association_identity_cannot_be_taken_by_another_resource()
    {
        var store = Store(new());
        await store.WriteAsync(Scope, Write(Resource("owner"), [Subscription("reserved", "owner")]));
        var refusal = await Assert.ThrowsAsync<TriggerOperationException>(() => store.WriteAsync(Scope,
            Write(Resource("intruder"), [Subscription("reserved", "intruder")], "intrude", "intrude")).AsTask());
        Assert.Equal("conflict", refusal.Code);
        Assert.Equal("owner", (await store.FindSubscriptionAsync(Scope, "reserved"))!.TriggerId);
        Assert.Null(await store.FindAsync(Scope, "intruder"));
        Assert.Null(await store.FindMutationAsync(Scope, "operator", "intrude"));
    }

    [Fact]
    public async Task Operational_update_retains_creator_source_origin_and_grant_revocation_audit()
    {
        var state = new InMemoryRuntimeCheckpointStoreState();
        var store = Store(state);
        var original = Subscription("subscription", "http");
        await store.WriteAsync(Scope, Write(Resource("http"), [original]));
        var grant = original.Grant!;
        var revoked = new TriggerInvocationGrant(grant.Id, grant.TriggerId, grant.SubscriptionId, grant.DefinitionId,
            grant.SlotName, grant.ModelKey, grant.SourceTypeKey, 2, TriggerGrantStatus.Revoked,
            grant.GrantedBy, grant.GrantedAt, "security-operator", Now.AddMinutes(1));
        var replacement = new TriggerSubscription(original.Id, original.TriggerId, original.DefinitionId, original.SlotName,
            original.ModelKey, 2, original.MappingRevision, false, original.Mappings, original.Origin, revoked);
        await store.WriteAsync(Scope, Write(Resource("http", revision: 2), [replacement], "revoke", "revoke", 1));

        var reloaded = (await Store(state).FindAsync(Scope, "http"))!;
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
    public async Task Deleted_resource_is_hidden_from_collections_and_cannot_be_recreated()
    {
        var store = Store(new());
        await store.WriteAsync(Scope, Write(Resource("http"), [Subscription("subscription", "http")]));
        await store.WriteAsync(Scope, Write(Resource("http", revision: 2, state: TriggerDesiredState.Deleted), [], "delete", "delete", 1));
        Assert.Empty((await store.ListAsync(Scope, new())).Items);
        Assert.Null(await store.FindSubscriptionAsync(Scope, "subscription"));
        await Assert.ThrowsAsync<TriggerOperationException>(() => store.WriteAsync(Scope,
            Write(Resource("http", revision: 3), [], "resurrect", "resurrect", 2)).AsTask());
        await Assert.ThrowsAsync<TriggerOperationException>(() => store.WriteAsync(Scope,
            Write(Resource("http"), [], "recreate", "recreate")).AsTask());
        Assert.Equal(TriggerDesiredState.Deleted, (await store.FindAsync(Scope, "http"))!.Trigger.DesiredState);
    }

    [Fact]
    public async Task Tenant_scope_and_actor_mutation_keys_are_exact_including_null_tenant()
    {
        var state = new InMemoryRuntimeCheckpointStoreState();
        foreach (var scope in new[] { Scope, new TriggerScope(Scope.Persistence, null), new TriggerScope(new("tenant-b"), "tenant-b"),
                     new TriggerScope(new("shell:opaque-namespace"), "independent-tenant") })
        {
            var store = Store(state, scope);
            await store.WriteAsync(scope, Write(Resource("same", tenant: scope.TenantId), [Subscription("same", "same")]));
            Assert.Single((await store.ListAsync(scope, new())).Items);
            Assert.Single((await store.ListSubscriptionsAsync(scope, new(DefinitionId: "definition"))).Items);
            Assert.Equal(scope.TenantId, (await store.FindAsync(scope, "same"))!.Trigger.TenantId);
        }
        var original = Store(state);
        await original.WriteAsync(Scope, Write(Resource("actor-owned"), [], actor: "different-operator"));
        Assert.Equal("same", (await original.FindMutationAsync(Scope, "operator", "request"))!.TriggerId);
        Assert.Equal("actor-owned", (await original.FindMutationAsync(Scope, "different-operator", "request"))!.TriggerId);
        await Assert.ThrowsAsync<InvalidOperationException>(() => original.FindAsync(new(new("tenant-b"), "tenant-b"), "same").AsTask());
        Assert.Null(await original.FindAsync(new(Scope.Persistence, "tenant-b"), "same"));
    }

    [Fact]
    public async Task Collections_filter_before_paging_and_treat_search_as_literal_text()
    {
        var store = Store(new());
        foreach (var id in new[] { "d", "b", "a", "c" })
            await store.WriteAsync(Scope, Write(Resource(id, name: id == "a" ? "Other" : "Sales %_ ' route"),
                [Subscription(id, id)], id, id));
        var first = await store.ListAsync(Scope, new(1, Search: "%_ '"));
        Assert.Equal("b", Assert.Single(first.Items).Id);
        Assert.Equal("b", first.Next);
        var second = await store.ListAsync(Scope, new(2, first.Next, "%_ '"));
        Assert.Equal(["c", "d"], second.Items.Select(item => item.Id));
        Assert.Null(second.Next);
        Assert.Empty((await store.ListAsync(Scope, new(Search: "' OR 1=1 --"))).Items);
        Assert.Empty((await store.ListAsync(Scope, new(TypeKey: "Other.Type"))).Items);
        var associations = await store.ListSubscriptionsAsync(Scope, new(DefinitionId: "definition", Limit: 2));
        Assert.Equal(["a", "b"], associations.Items.Select(item => item.Id));
        Assert.Equal(["c", "d"], (await store.ListSubscriptionsAsync(Scope,
            new(DefinitionId: "definition", Limit: 2, After: associations.Next))).Items.Select(item => item.Id));
        await Assert.ThrowsAsync<TriggerOperationException>(() => store.ListAsync(Scope, new(0)).AsTask());
        await Assert.ThrowsAsync<TriggerOperationException>(() => store.ListSubscriptionsAsync(Scope, new()).AsTask());
        await Assert.ThrowsAsync<TriggerOperationException>(() => store.ListSubscriptionsAsync(Scope,
            new(TriggerId: "a", DefinitionId: "definition")).AsTask());
    }

    private static InMemoryStandaloneTriggerStore Store(InMemoryRuntimeCheckpointStoreState state, TriggerScope? scope = null) =>
        new(state, new Access(scope ?? Scope), new FakeTimeProvider(Now));

    private static StandaloneTrigger Resource(string id, long revision = 1, string name = "HTTP endpoint",
        TriggerDesiredState state = TriggerDesiredState.Paused, string? tenant = "tenant-a", string creator = "source-publisher") =>
        new(id, name, TypeKey, 1, JsonSerializer.SerializeToElement(new { path = "/orders", method = "POST" }),
            revision, revision, revision, state, new(TriggerManagementKind.Provider, "provider/http.json", true),
            Now, Now.AddMinutes(revision - 1), creator, "Source-owned endpoint", hasActivated: true, tenantId: tenant);

    private static TriggerSubscription Subscription(string id, string trigger, string definition = "definition", string sourceType = TypeKey) =>
        new(id, trigger, definition, "Published", "workflow", 1, 1, true,
            [new("OrderId", new TriggerSourceBinding(["request", "id"])), new("Optional", new TriggerLiteralBinding(JsonSerializer.SerializeToElement<object?>(null)))],
            new(TriggerManagementKind.Provider, "provider/http.json", true),
            new("grant-" + id, trigger, id, definition, "Published", "workflow", sourceType, 1, TriggerGrantStatus.Granted, "retired-author", Now));

    private static TriggerStoreWrite Write(StandaloneTrigger resource, IReadOnlyList<TriggerSubscription> subscriptions,
        string request = "request", string fingerprint = "request-fingerprint", long? expected = null, string actor = "operator") =>
        new(request, fingerprint, actor, expected, resource, subscriptions);

    private sealed class Access(TriggerScope scope) : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = PersistenceAccessContext.Scoped(scope.Persistence);
    }
}
