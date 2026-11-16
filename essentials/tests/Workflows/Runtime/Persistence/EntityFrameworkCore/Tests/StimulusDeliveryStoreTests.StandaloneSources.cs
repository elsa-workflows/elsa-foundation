using System.Reflection;
using System.Text.Json;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed partial class StimulusDeliveryStoreTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Generic_pending_producers_cannot_mint_accepted_standalone_authority(bool ef)
    {
        await using var harness = await Harness.Create(ef);
        var source = StandaloneProviderFixture();
        await Assert.ThrowsAsync<NotSupportedException>(() => harness.SavePending(source).AsTask());
        Assert.Null(await harness.Lookup.FindAsync(source.OutboxItemId));
        Assert.Empty(await harness.Outbox.GetDeliverableAsync(new(Now, 10)));
        if (ef) Assert.Empty(await harness.Context!.RuntimePostCommitOutbox.AsNoTracking().ToArrayAsync());
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Provider_fixture_roundtrip_queries_claims_and_retries_preserve_actual_standalone_origin(bool ef)
    {
        await using var harness = await Harness.Create(ef);
        var pending = StandaloneProviderFixture(sealedDispatch: true);
        await harness.PutStandaloneProviderFixture(pending);
        var reopened = harness.Reopen();
        var lookup = (IPostCommitOutboxLookupStore)reopened;
        var outbox = (IRuntimePostCommitOutboxStore)reopened;
        var claims = (IRuntimePostCommitOutboxClaimStore)reopened;
        var current = (await lookup.FindDurableAsync(pending.OutboxItemId))!;
        Assert.True(pending.IsEquivalentTo(current));
        Assert.Null(current.Intent); Assert.Null(current.WorkflowExecutionId);
        Assert.Equal(Scope, current.SourceScope);
        Assert.Single(await outbox.GetDeliverableAsync(new(Now, 10, intentKind: current.DeliveryKind)));
        Assert.Empty(await outbox.GetDeliverableAsync(new(Now, 10, workflowExecutionId: "publisher")));
        var foreign = (IRuntimePostCommitOutboxStore)harness.Reopen(new("foreign"));
        Assert.Empty(await foreign.GetDeliverableAsync(new(Now, 10)));
        Assert.Empty(await ((IRuntimePostCommitOutboxClaimStore)foreign).ClaimAsync(new("foreign", Now, TimeSpan.FromMinutes(1), 10)));
        var first = Assert.Single(await claims.ClaimAsync(new("first", Now, TimeSpan.FromMinutes(1), 10)));
        var seal = Assert.Single(first.Item.SubscriptionDelivery!.Progress).Seal!;
        var renewed = (await claims.RenewClaimAsync(first, Now.AddSeconds(1), TimeSpan.FromMinutes(2)))!;
        Assert.Equal(seal.Fingerprint, Assert.Single(renewed.Item.SubscriptionDelivery!.Progress).Seal!.Fingerprint);
        await claims.RecordDeliveryResultAsync(first, new(first.OutboxItemId, RuntimePostCommitOutboxStatus.FailedRetryable, Now.AddSeconds(2), "retry"));
        var retry = Assert.Single(await claims.ClaimAsync(new("second", Now.AddSeconds(4), TimeSpan.FromMinutes(1), 10)));
        Assert.Equal(seal.Fingerprint, Assert.Single(retry.Item.SubscriptionDelivery!.Progress).Seal!.Fingerprint);
        await Assert.ThrowsAsync<RuntimePostCommitOutboxStaleClaimException>(() => claims.RecordDeliveryResultAsync(first,
            new(first.OutboxItemId, RuntimePostCommitOutboxStatus.FailedFinal, Now.AddSeconds(5), "old-owner")).AsTask());
        await claims.RecordDeliveryResultAsync(retry, new(retry.OutboxItemId, RuntimePostCommitOutboxStatus.FailedFinal, Now.AddSeconds(5), "blocked"));
        var final = (await lookup.FindDurableAsync(pending.OutboxItemId))!;
        Assert.Equal(RuntimePostCommitOutboxStatus.FailedFinal, final.Status);
        Assert.Equal(pending.SourceFingerprint, final.SourceFingerprint);
        Assert.Equal(seal.Fingerprint, Assert.Single(final.SubscriptionDelivery!.Progress).Seal!.Fingerprint);
        Assert.Equal([Pin.ArtifactId], await reopened.ListPinnedExecutableArtifactIdsAsync());
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Provider_fixtures_in_null_and_named_tenant_have_distinct_physical_source_receipts(bool ef)
    {
        await using var harness = await Harness.Create(ef);
        var absent = StandaloneProviderFixture(tenantId: null);
        var named = StandaloneProviderFixture(tenantId: Scope.Value);
        await harness.PutStandaloneProviderFixture(absent);
        await harness.PutStandaloneProviderFixture(named);
        var reopened = harness.Reopen();
        var lookup = (IPostCommitOutboxLookupStore)reopened;
        var absentReceipt = (await lookup.FindDurableAsync(absent.OutboxItemId))!;
        var namedReceipt = (await lookup.FindDurableAsync(named.OutboxItemId))!;
        Assert.NotEqual(absent.OutboxItemId, named.OutboxItemId);
        Assert.NotEqual(absent.StandaloneIngress!.RequestFingerprint, named.StandaloneIngress!.RequestFingerprint);
        Assert.Null(absentReceipt.StandaloneIngress!.TenantId);
        Assert.Equal(Scope.Value, namedReceipt.StandaloneIngress!.TenantId);
        Assert.True(absent.IsEquivalentTo(absentReceipt));
        Assert.True(named.IsEquivalentTo(namedReceipt));
        Assert.NotEqual(Assert.Single(absentReceipt.SubscriptionDelivery!.Recipients).StartIdentity.WorkflowExecutionId,
            Assert.Single(namedReceipt.SubscriptionDelivery!.Recipients).StartIdentity.WorkflowExecutionId);
        var claims = await ((IRuntimePostCommitOutboxClaimStore)reopened).ClaimAsync(new("owner", Now, TimeSpan.FromMinutes(1), 10));
        Assert.Equal(2, claims.Count);
        Assert.Equal(2, claims.Select(claim => claim.OutboxItemId).Distinct(StringComparer.Ordinal).Count());
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Provider_stale_claim_cannot_erase_physically_cancelled_recipient_or_misreport_unsettled_success(bool ef)
    {
        await using var harness = await Harness.Create(ef);
        var pending = StandaloneProviderFixture(); await harness.PutStandaloneProviderFixture(pending);
        var claim = Assert.Single(await harness.Claims.ClaimAsync(new("owner", Now, TimeSpan.FromMinutes(1), 1)));
        await Assert.ThrowsAsync<ArgumentException>(() => harness.Completion.CompleteClaimAsync(new(claim,
            new(claim.OutboxItemId, RuntimePostCommitOutboxStatus.Delivered, Now.AddSeconds(1)))).AsTask());
        if (ef) harness.Context!.ChangeTracker.Clear();
        var current = (await harness.Lookup.FindDurableAsync(claim.OutboxItemId))!;
        var delivery = current.SubscriptionDelivery!;
        var cancelled = current.WithSubscriptionDelivery(delivery.WithProgress(Assert.Single(delivery.Progress).CancelPending()));
        await harness.PutStandaloneProviderFixture(cancelled);
        var renewed = (await harness.Claims.RenewClaimAsync(claim, Now.AddSeconds(2), TimeSpan.FromMinutes(2)))!;
        Assert.Equal(RuntimeRecipientPhase.CancelledBeforeDispatch, Assert.Single(renewed.Item.SubscriptionDelivery!.Progress).Phase);
        await harness.Completion.CompleteClaimAsync(new(claim, new(claim.OutboxItemId, RuntimePostCommitOutboxStatus.Delivered, Now.AddSeconds(3))));
        var settled = (await harness.Lookup.FindDurableAsync(claim.OutboxItemId))!;
        Assert.Equal(RuntimePostCommitOutboxStatus.Delivered, settled.Status);
        Assert.Equal(RuntimeRecipientPhase.CancelledBeforeDispatch, Assert.Single(settled.SubscriptionDelivery!.Progress).Phase);
        Assert.Empty(await harness.Delivery.ListPinnedExecutableArtifactIdsAsync());
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Accepted_subscription_roots_and_execution_dedup_are_retained_without_a_publisher(bool ef)
    {
        await using var harness = await Harness.Create(ef);
        await harness.EnsureMaterial();
        var source = StandaloneProviderFixture(sealedDispatch: true); await harness.PutStandaloneProviderFixture(source);
        Assert.Null(await harness.FindPublisher());
        Assert.Equal([Pin.ArtifactId], await harness.Delivery.ListPinnedExecutableArtifactIdsAsync(harness.Artifacts, harness.References, harness.Executions));
        var recipient = Assert.Single(source.SubscriptionDelivery!.Recipients);
        await harness.Executions.SaveAsync(Publisher with { WorkflowExecutionId = recipient.StartIdentity.WorkflowExecutionId });
        Assert.False(await harness.Executions.DeleteAsync(recipient.StartIdentity.WorkflowExecutionId));
        await Assert.ThrowsAsync<StimulusDeliveryRetentionException>(() => harness.Delivery.EnsureOrdinaryCollectionAllowedAsync(
            harness.Outbox, harness.Artifacts, harness.References, harness.Executions).AsTask());
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task A_workflow_subscription_source_retains_its_actual_publisher_before_first_selection(bool ef)
    {
        await using var harness = await Harness.Create(ef);
        await harness.EnsureMaterial(); await harness.SavePublisher(Publisher);
        var ordinary = Pending("subscription-source", false);
        var declared = new RuntimePostCommitOutboxItem(ordinary.OutboxItemId, ordinary.Intent, ordinary.Status,
            ordinary.RecordedAt, ordinary.AvailableAt, ordinary.RetryPolicy, 0, null, null, null, null, null, 0, null,
            null, Scope, null, null, RuntimeSubscriptionDelivery.CurrentVersion, null);
        await harness.SavePending(declared);
        Assert.False(await harness.Executions.DeleteAsync(Publisher.WorkflowExecutionId));
        Assert.Null((await harness.Lookup.FindDurableAsync(declared.OutboxItemId))!.SubscriptionDelivery);
    }

    [Theory]
    [InlineData("source-id")] [InlineData("source-kind")] [InlineData("trigger-key")]
    [InlineData("source-key")] [InlineData("workflow-owner")] [InlineData("v2-format")]
    public async Task SQLite_standalone_projection_or_predecessor_format_corruption_refuses(string defect)
    {
        await using var harness = await Harness.Create(true);
        var source = StandaloneProviderFixture(); await harness.PutStandaloneProviderFixture(source);
        var row = await harness.Context!.RuntimePostCommitOutbox.SingleAsync();
        switch (defect)
        {
            case "source-id": row.SourceId = "different"; break;
            case "source-kind": row.SourceKind = null; break;
            case "trigger-key": row.TriggerIdHash = "different"; break;
            case "source-key": row.SourceKeyHash = "different"; break;
            case "workflow-owner": row.WorkflowExecutionId = "fabricated"; break;
            case "v2-format": row.SchemaVersion = "2.0.0"; break;
        }
        await harness.Context.SaveChangesAsync(); harness.Context.ChangeTracker.Clear();
        await Assert.ThrowsAsync<InvalidDataException>(() => harness.Lookup.FindDurableAsync(source.OutboxItemId).AsTask());
    }

    private static RuntimePostCommitOutboxItem StandaloneProviderFixture(bool sealedDispatch = false, string? tenantId = "tenant")
    {
        const string fingerprint = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        var schema = new ValueTypeDescriptor("Test.Payload", schemaVersion: 1);
        var payload = JsonSerializer.SerializeToElement(new { value = 42 });
        var actor = new WorkflowExecutionAuthoritySnapshot("standalone-trigger", "fixture-actor");
        var requestFingerprint = RuntimeStandaloneIngressSource.ComputeRequestFingerprint(Scope, tenantId,
            "fixture-trigger", "Test.Source", "occurrence", schema, payload, actor);
        var source = new RuntimeStandaloneIngressSource(1, "fixture-admission", "dispatch", Now, Scope, tenantId,
            new("partition"), "fixture-trigger", 1, 1, "Test.Source", "occurrence", requestFingerprint,
            schema, payload, RuntimeIngressAdmissionPolicy.Independent, actor);
        var recipient = new RuntimeSubscriptionRecipient("subscription", "subscription", 1, 1, "selected-definition", "native", "slot", 1,
            "activation", Pin, SelectedSource(), fingerprint, 1, 1, new Dictionary<string, JsonElement>(), new("standalone-trigger", "fixture-actor"),
            tenantId, new("partition"), new(1, Scope, source.OutboxItemId, "subscription"));
        var delivery = new RuntimeSubscriptionDelivery(1, source.OutboxItemId, source.Fingerprint, Scope, [recipient], [],
            [new(recipient.RecipientId, RuntimeRecipientPhase.Pending)]);
        var pending = RuntimePostCommitOutboxItem.CreateStandaloneIngress(source, delivery, RuntimePostCommitRetryPolicy.UntilAcknowledged(TimeSpan.FromSeconds(1)));
        if (!sealedDispatch) return pending;
        var seal = new RuntimeDispatchSeal(1, pending.OutboxItemId, pending.SourceFingerprint, delivery.SelectionFingerprint,
            recipient.RecipientId, recipient.StartIdentity, Now, "fixture-command", 1, JsonSerializer.SerializeToElement(new { value = 42 }));
        return pending.WithSubscriptionDelivery(delivery.WithProgress(Assert.Single(delivery.Progress).CommitDispatch(seal)));
    }

    private sealed partial class Harness
    {
        /// <summary>Direct physical row fixture only: bypasses authority admission to test reader/CAS/retention compatibility.</summary>
        public async Task PutStandaloneProviderFixture(RuntimePostCommitOutboxItem item)
        {
            if (Context is null)
            {
                var property = typeof(InMemoryRuntimeCheckpointStoreState).GetProperty("OutboxItems", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var rows = (Dictionary<string, RuntimePostCommitOutboxItem>)property.GetValue(memory)!;
                rows[item.OutboxItemId] = JsonSerializer.Deserialize<RuntimePostCommitOutboxItem>(JsonSerializer.Serialize(item))!;
                return;
            }
            var storeType = typeof(EfRuntimePostCommitOutboxStore);
            var rowId = (string)storeType.GetMethod("RowId", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [Scope.Value, item.OutboxItemId])!;
            var existing = await Context.RuntimePostCommitOutbox.SingleOrDefaultAsync(row => row.Id == rowId);
            if (existing is null)
            {
                var row = (RuntimePostCommitOutboxEntity)storeType.GetMethod("ToEntity", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null,
                    [item, Scope.Value, rowId, 1L, RuntimePostCommitOutboxEfModule.SchemaVersion])!;
                Context.RuntimePostCommitOutbox.Add(row);
            }
            else storeType.GetMethod("Copy", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null,
                [existing, item, Scope.Value, checked(existing.Revision + 1), RuntimePostCommitOutboxEfModule.SchemaVersion]);
            await Context.SaveChangesAsync(); Context.ChangeTracker.Clear();
        }
    }
}
