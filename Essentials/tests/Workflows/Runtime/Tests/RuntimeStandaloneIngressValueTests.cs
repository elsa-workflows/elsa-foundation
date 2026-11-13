using System.Text.Json;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class RuntimeStandaloneIngressValueTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 6, 0, 0, 0, TimeSpan.Zero);
    private static readonly PersistenceScope Scope = new("tenant");
    private static readonly WorkflowExecutionPartition Partition = new("partition");
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private const string Hash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Fact]
    public void WorkflowWire_RemainsTheHistoricalByteSequence()
    {
        var item = new RuntimePostCommitOutboxItem("commit:intent", new("intent", "workflow", "Test.Kind", Now, null, "key", null),
            RuntimePostCommitOutboxStatus.Pending, Now, Now);
        const string expected = """
            {"outboxItemId":"commit:intent","intent":{"intentId":"intent","workflowExecutionId":"workflow","kind":"Test.Kind","recordedAt":"2026-11-06T00:00:00+00:00","activityExecutionId":null,"idempotencyKey":"key","payload":null,"metadata":{},"dependsOnWaitRegistrationId":null,"waitFailurePolicy":null,"isWaitDependent":false,"notBefore":null},"status":0,"recordedAt":"2026-11-06T00:00:00+00:00","availableAt":"2026-11-06T00:00:00+00:00","retryPolicy":{"maxAttempts":0,"delay":null,"retryUntilAcknowledged":false,"metadata":{}},"deliveryAttemptCount":0,"deliveringOwnerId":null,"deliveryStartedAt":null,"deliveredAt":null,"lastFailureMessage":null,"metadata":{},"deliveryFencingToken":0,"deliveryVisibleAfter":null,"isTerminal":false}
            """;
        Assert.Equal(expected, JsonSerializer.Serialize(item, Json));
        Assert.True(item.IsEquivalentTo(JsonSerializer.Deserialize<RuntimePostCommitOutboxItem>(expected, Json)));
    }

    [Fact]
    public void ExternalSource_RoundTripsWithoutAnyWorkflowIntentOrCheckpoint()
    {
        var item = Pending();
        var wire = JsonSerializer.Serialize(item, Json);
        using var document = JsonDocument.Parse(wire);
        Assert.False(document.RootElement.TryGetProperty("intent", out _));
        Assert.True(document.RootElement.TryGetProperty("standaloneIngress", out _));
        var restored = JsonSerializer.Deserialize<RuntimePostCommitOutboxItem>(wire, Json)!;
        Assert.Null(restored.Intent);
        Assert.Null(restored.WorkflowExecutionId);
        Assert.Equal(RuntimeDeliverySourceKind.StandaloneIngress, restored.SourceKind);
        Assert.True(item.IsEquivalentTo(restored));
        Assert.Throws<InvalidOperationException>(() => restored.RequireWorkflowIntent());
    }

    [Fact]
    public void Admission_RejectsAnEmptyAcceptedSetOrFalseRequireAllPartialSuccess()
    {
        var source = Source();
        var empty = new RuntimeSubscriptionDelivery(1, source.OutboxItemId, source.Fingerprint, Scope, [], [], []);
        Assert.Throws<ArgumentException>(() => RuntimePostCommitOutboxItem.CreateStandaloneIngress(source, empty, Retry()));
        var partial = Delivery(source, [new("other", 1, "input", "missing-required-input")]);
        Assert.Throws<ArgumentException>(() => RuntimePostCommitOutboxItem.CreateStandaloneIngress(source, partial, Retry()));
        var accepting = Source(policy: RuntimeIngressAdmissionPolicy.AcceptAvailable);
        var accepted = RuntimePostCommitOutboxItem.CreateStandaloneIngress(accepting,
            Delivery(accepting, [new("other", 1, "input", "missing-required-input")]), Retry());
        Assert.Single(accepted.SubscriptionDelivery!.Refusals);
        var independent = Source(policy: RuntimeIngressAdmissionPolicy.Independent);
        var independentAdmission = RuntimePostCommitOutboxItem.CreateStandaloneIngress(independent,
            Delivery(independent, [new("other", 1, "publication", "unpublished")]), Retry());
        Assert.Single(independentAdmission.SubscriptionDelivery!.Recipients);
        Assert.Single(independentAdmission.SubscriptionDelivery.Refusals);
    }

    [Fact]
    public void SubscriptionIdentities_DoNotCollapseWhenTargetsShareMaterial()
    {
        var source = Source();
        var first = Recipient(source, "one");
        var second = Recipient(source, "two");
        Assert.Equal(first.Material, second.Material);
        Assert.NotEqual(first.StartIdentity.WorkflowExecutionId, second.StartIdentity.WorkflowExecutionId);
        Assert.NotEqual(first.StartIdentity.EnvelopeId, second.StartIdentity.EnvelopeId);
        Assert.Equal(first.StartIdentity.WorkflowExecutionId, Recipient(source, "one").StartIdentity.WorkflowExecutionId);
    }

    [Fact]
    public void Exact_tenant_and_scope_isolate_source_keys_start_identity_and_acceptance_receipts()
    {
        var absent = Source(tenantId: null);
        var named = Source(tenantId: "tenant");
        var literalNull = Source(tenantId: "null");
        var anotherScope = Source(tenantId: null, scope: new("another-partition"));
        var sources = new[] { absent, named, literalNull, anotherScope };
        Assert.Equal(4, sources.Select(source => source.OutboxItemId).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(4, sources.Select(source => source.RequestFingerprint).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(4, sources.Select(source => Recipient(source).StartIdentity.WorkflowExecutionId).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(absent.OutboxItemId, Source(tenantId: null).OutboxItemId);
        Assert.Equal(absent.RequestFingerprint, Source(tenantId: null).RequestFingerprint);

        var absentItem = RuntimePostCommitOutboxItem.CreateStandaloneIngress(absent, Delivery(absent), Retry());
        var namedItem = RuntimePostCommitOutboxItem.CreateStandaloneIngress(named, Delivery(named), Retry());
        var absentRecipient = Assert.Single(absentItem.SubscriptionDelivery!.Recipients);
        var namedRecipient = Assert.Single(namedItem.SubscriptionDelivery!.Recipients);
        var absentSeal = Seal(absentItem, absentRecipient);
        var namedSeal = Seal(namedItem, namedRecipient);
        var namedReceipt = new RuntimeTargetAcceptanceReceipt(namedRecipient.RecipientId, namedSeal.Fingerprint,
            namedRecipient.StartIdentity, namedRecipient.Material, RuntimeTargetAcceptanceKind.SchedulerCommand,
            namedRecipient.StartIdentity.CommandId, Now);
        var absentProgress = Assert.Single(absentItem.SubscriptionDelivery.Progress).CommitDispatch(absentSeal);
        Assert.Throws<ArgumentException>(() => absentProgress.AcceptTarget(namedReceipt));
    }

    [Fact]
    public void Submitted_metadata_is_always_structurally_framed_and_bound_to_the_request_fingerprint()
    {
        var payload = JsonSerializer.SerializeToElement(new { value = 42 });
        var empty = JsonSerializer.SerializeToElement(new { });
        var post = JsonSerializer.SerializeToElement(new { method = "POST", path = "/orders" });
        var get = JsonSerializer.SerializeToElement(new { method = "GET", path = "/orders" });
        var schema = new ValueTypeDescriptor("Test.Payload", schemaVersion: 1);
        var actor = new WorkflowExecutionAuthoritySnapshot("source", "actor");
        string Fingerprint(JsonElement body, JsonElement metadata) => RuntimeStandaloneIngressSource.ComputeRequestFingerprint(Scope, "tenant",
            "trigger", "source", "key", schema, RuntimeIngressAdmission.ComputeSubmittedIntent(body, metadata), actor);
        Assert.NotEqual(Fingerprint(payload, post), Fingerprint(payload, get));
        Assert.NotEqual(Fingerprint(payload, empty), Fingerprint(RuntimeIngressAdmission.ComputeSubmittedIntent(payload, empty), empty));
        Assert.Equal(Fingerprint(payload, post), Fingerprint(payload, post));
        Assert.Throws<ArgumentException>(() => RuntimeIngressAdmission.ComputeSubmittedIntent(payload, JsonSerializer.SerializeToElement("not-an-object")));
    }

    [Fact]
    public void CancelAndSeal_HaveOneIrreversibleWinner_AndClaimsPreserveIt()
    {
        var item = Pending();
        var delivery = item.SubscriptionDelivery!;
        var recipient = Assert.Single(delivery.Recipients);
        var pending = Assert.Single(delivery.Progress);
        var seal = Seal(item, recipient);
        Assert.Throws<InvalidOperationException>(() => pending.CancelPending().CommitDispatch(seal));
        var committed = pending.CommitDispatch(seal);
        Assert.Same(committed, committed.CancelPending());
        var sealedItem = item.WithSubscriptionDelivery(delivery.WithProgress(committed));
        var claim = RuntimePostCommitOutboxClaimTransitions.Claim(sealedItem, new("owner", Now, TimeSpan.FromMinutes(1), 1));
        var renewed = RuntimePostCommitOutboxClaimTransitions.Renew(claim.Item, claim, Now.AddSeconds(30), TimeSpan.FromMinutes(1))!;
        Assert.Equal(seal.Fingerprint, Assert.Single(renewed.Item.SubscriptionDelivery!.Progress).Seal!.Fingerprint);
        Assert.Throws<ArgumentException>(() => RuntimePostCommitOutboxClaimTransitions.Complete(renewed.Item, renewed,
            new(item.OutboxItemId, RuntimePostCommitOutboxStatus.Delivered, Now.AddSeconds(31))));
        Assert.Throws<InvalidOperationException>(() => sealedItem.WithSubscriptionDelivery(delivery));
    }

    [Fact]
    public void SealedAuthority_SurvivesClaimExpiry_WithoutCarryingAClaimToken()
    {
        var item = Pending();
        var recipient = Assert.Single(item.SubscriptionDelivery!.Recipients);
        var seal = Seal(item, recipient);
        var original = item.WithSubscriptionDelivery(item.SubscriptionDelivery.WithProgress(new(recipient.RecipientId, RuntimeRecipientPhase.DispatchCommitted, seal)));
        var first = RuntimePostCommitOutboxClaimTransitions.Claim(original, new("first", Now, TimeSpan.FromSeconds(1), 1));
        var next = RuntimePostCommitOutboxClaimTransitions.Claim(first.Item, new("peer", Now.AddSeconds(2), TimeSpan.FromMinutes(1), 1));
        Assert.NotEqual(first.FencingToken, next.FencingToken);
        Assert.Equal(seal.Fingerprint, Assert.Single(next.Item.SubscriptionDelivery!.Progress).Seal!.Fingerprint);
        Assert.Throws<RuntimePostCommitOutboxStaleClaimException>(() => RuntimePostCommitOutboxClaimTransitions.Complete(next.Item, first,
            new(item.OutboxItemId, RuntimePostCommitOutboxStatus.FailedFinal, Now.AddSeconds(3), "failure")));
    }

    private static RuntimeStandaloneIngressSource Source(RuntimeIngressAdmissionPolicy policy = RuntimeIngressAdmissionPolicy.RequireAll,
        string? tenantId = "tenant", PersistenceScope? scope = null)
    {
        scope ??= Scope;
        var schema = new ValueTypeDescriptor("Test.Payload", schemaVersion: 1);
        var payload = JsonSerializer.SerializeToElement(new { value = 42 });
        var actor = new WorkflowExecutionAuthoritySnapshot("standalone-trigger", "source-actor");
        var fingerprint = RuntimeStandaloneIngressSource.ComputeRequestFingerprint(scope, tenantId, "trigger", "Test.Source", "request", schema, payload, actor);
        return new(1, "admission", "dispatch", Now, scope, tenantId, Partition, "trigger", 1, 1, "Test.Source", "request", fingerprint,
            schema, payload, policy, actor);
    }

    private static RuntimeSubscriptionRecipient Recipient(RuntimeStandaloneIngressSource source, string id = "subscription") =>
        new(id, id, 1, 1, "definition", "native", "slot", 1, "activation",
            new("artifact", "material-producer", "material-version", "1", Hash),
            new("reference", "Published", "publication", "1", "definition", "version", "1", "activation", "slot"),
            Hash, 1, 1, new Dictionary<string, JsonElement> { ["value"] = JsonSerializer.SerializeToElement(42) },
            new("standalone-trigger", "source-actor"), source.TenantId, source.Partition, new(1, source.Scope, source.OutboxItemId, id));

    private static RuntimeSubscriptionDelivery Delivery(RuntimeStandaloneIngressSource source, IReadOnlyList<RuntimeSubscriptionRefusal>? refusals = null)
    {
        var recipient = Recipient(source);
        return new(1, source.OutboxItemId, source.Fingerprint, source.Scope, [recipient], refusals ?? [], [new(recipient.RecipientId, RuntimeRecipientPhase.Pending)]);
    }
    private static RuntimePostCommitOutboxItem Pending() { var source = Source(); return RuntimePostCommitOutboxItem.CreateStandaloneIngress(source, Delivery(source), Retry()); }
    private static RuntimePostCommitRetryPolicy Retry() => RuntimePostCommitRetryPolicy.UntilAcknowledged(TimeSpan.FromSeconds(1));
    private static RuntimeDispatchSeal Seal(RuntimePostCommitOutboxItem item, RuntimeSubscriptionRecipient recipient) =>
        new(1, item.OutboxItemId, item.SourceFingerprint, item.SubscriptionDelivery!.SelectionFingerprint,
            recipient.RecipientId, recipient.StartIdentity, Now, "native-start", 1, JsonSerializer.SerializeToElement(new { value = 42 }));

}
