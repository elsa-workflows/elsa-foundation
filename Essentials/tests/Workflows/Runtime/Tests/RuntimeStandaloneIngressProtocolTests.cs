using System.Text.Json;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Elsa.Workflows.Runtime.Services.Incidents;
using Elsa.Workflows.Runtime.Services.Coalescing;
using Elsa.Workflows.Runtime.Services.Scheduler;
using Elsa.Workflows.Runtime.Core.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class RuntimeStandaloneIngressProtocolTests
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

    [Fact]
    public async Task ExistingProcessor_DispatchesAnActualStandaloneSourceThroughTheSameRegistry()
    {
        var store = new SerializedStore(Pending());
        var handled = new List<RuntimeDeliverySourceContext>();
        using var services = Services(new Handler(source =>
        {
            handled.Add(source);
            store.CancelPending(source);
            return ValueTask.CompletedTask;
        }));
        var processor = Processor(store, services);
        Assert.True(processor.SupportsSourceKind(RuntimeDeliverySourceKind.StandaloneIngress));
        var result = await processor.ProcessAsync(new(10));
        Assert.Equal(1, result.DeliveredCount);
        Assert.Null(Assert.Single(handled).Claim.Item.Intent);
        Assert.Equal(RuntimePostCommitOutboxStatus.Delivered, store.Current.Status);
        Assert.Equal(RuntimeRecipientPhase.CancelledBeforeDispatch, Assert.Single(store.Current.SubscriptionDelivery!.Progress).Phase);
    }

    [Fact]
    public async Task HandlerReturnWithoutTargetEvidence_CannotCompleteOwedWork()
    {
        var store = new SerializedStore(Pending());
        using var services = Services(new Handler(_ => ValueTask.CompletedTask));
        await Assert.ThrowsAsync<ArgumentException>(() => Processor(store, services).ProcessAsync(new(1)).AsTask());
        Assert.Equal(RuntimePostCommitOutboxStatus.Delivering, store.Current.Status);
        Assert.Equal(RuntimeRecipientPhase.Pending, Assert.Single(store.Current.SubscriptionDelivery!.Progress).Phase);
    }

    [Fact]
    public async Task ClaimFreeAndLegacyDispatchers_RefuseExternalSourcesBeforeEffects()
    {
        var store = new SerializedStore(Pending());
        using var services = Services(new Handler(_ => throw new Xunit.Sdk.XunitException("Handler must not run.")));
        var legacyStore = new ClaimlessStore(store);
        await Assert.ThrowsAsync<RuntimePostCommitSourceBoundaryException>(() => Processor(legacyStore, services).ProcessAsync(new(1)).AsTask());
        var claim = RuntimePostCommitOutboxClaimTransitions.Claim(store.Current, new("owner", Now, TimeSpan.FromMinutes(1), 1));
        IRuntimePostCommitIntentDispatcher legacy = new LegacyDispatcher();
        await Assert.ThrowsAsync<RuntimePostCommitSourceBoundaryException>(() => legacy.DispatchAsync(new RuntimeDeliverySourceContext(claim, Scope)).AsTask());
        Assert.Equal(RuntimePostCommitOutboxStatus.Pending, store.Current.Status);
    }

    [Fact]
    public void ConflictingSourceOwners_AndUnknownProtocolVersions_Refuse()
    {
        var services = new ServiceCollection();
        services.AddRuntimeDeliverySourceHandler<Handler>(RuntimeStandaloneIngressSource.StandaloneTriggerDeliveryKind, Retry());
        Assert.Throws<InvalidOperationException>(() => services.AddRuntimePostCommitIntentHandler<WorkflowHandler>(
            RuntimeStandaloneIngressSource.StandaloneTriggerDeliveryKind, Retry()));
        Assert.Throws<NotSupportedException>(() => new RuntimeSubscriptionDelivery(99, "source", Hash, Scope, [], [], []));
        Assert.Throws<NotSupportedException>(() => new RuntimeSubscriptionStartIdentity(99, Scope, "source", "subscription"));
    }

    [Fact]
    public void A_workflow_overlay_cannot_mint_standalone_admission_before_any_state_changes()
    {
        var item = Pending();
        var commit = new RuntimeCheckpointCommit("commit", new("checkpoint", "DeferredWork", "workflow", Now, [], new Dictionary<string, string>()),
            new(null, null, [], [], [], [], [], postCommitOutbox:
                [new(item.OutboxItemId, RuntimeStateChangeOperation.Upsert, item, new Dictionary<string, string>())]),
            [], new Dictionary<string, string>());
        var session = new RuntimeCoalescingSession("workflow", new InMemoryWorkflowSchedulerWorkQueue(), new PersistenceModeOptions());
        Assert.Throws<NotSupportedException>(() => session.BufferDeferred(commit));
        Assert.False(session.HasBufferedChanges);
        Assert.Empty(session.RemainingPendingOutboxChanges());
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

    private static ServiceProvider Services(Handler handler)
    {
        var services = new ServiceCollection().AddSingleton(handler);
        services.AddRuntimeDeliverySourceHandler<Handler>(RuntimeStandaloneIngressSource.StandaloneTriggerDeliveryKind, Retry());
        services.AddScoped<IRuntimePostCommitIntentDispatcher, RuntimePostCommitIntentDispatcher>();
        return services.BuildServiceProvider();
    }
    private static RuntimePostCommitOutboxProcessor Processor(IRuntimePostCommitOutboxStore store, IServiceProvider services) =>
        new(store, services.GetRequiredService<IRuntimePostCommitIntentDispatcher>(), new Clock(), DefaultRuntimeFaultCapturePolicy.CreateDefault(),
            null, [], null, null, null, new Access(), services.GetServices<RuntimePostCommitIntentHandlerContribution>());
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => Now; }
    private sealed class Access : IPersistenceAccessContextAccessor { public PersistenceAccessContext Current => PersistenceAccessContext.Scoped(Scope); }
    private sealed class Handler(Func<RuntimeDeliverySourceContext, ValueTask> run) : IRuntimeDeliverySourceHandler
    { public ValueTask HandleAsync(RuntimeDeliverySourceContext source, CancellationToken cancellationToken = default) => run(source); }
    private sealed class WorkflowHandler : IRuntimePostCommitIntentHandler
    { public ValueTask HandleAsync(RuntimePostCommitIntent intent, CancellationToken cancellationToken = default) => ValueTask.CompletedTask; }
    private sealed class LegacyDispatcher : IRuntimePostCommitIntentDispatcher
    { public ValueTask DispatchAsync(RuntimePostCommitIntent intent, CancellationToken cancellationToken = default) => throw new InvalidOperationException("No workflow fallback is allowed."); }

    // A serialized physical test boundary, not a persistence-provider or restart qualification witness.
    private sealed class SerializedStore : IRuntimePostCommitOutboxStore, IRuntimePostCommitOutboxClaimStore, IPostCommitOutboxLookupStore
    {
        private byte[] bytes;
        public SerializedStore(RuntimePostCommitOutboxItem item) => bytes = JsonSerializer.SerializeToUtf8Bytes(item, Json);
        public RuntimePostCommitOutboxItem Current => JsonSerializer.Deserialize<RuntimePostCommitOutboxItem>(bytes, Json)!;
        private void Write(RuntimePostCommitOutboxItem item) => bytes = JsonSerializer.SerializeToUtf8Bytes(item, Json);
        public void CancelPending(RuntimeDeliverySourceContext context)
        {
            var current = Current;
            Assert.Equal(context.Claim.FencingToken, current.DeliveryFencingToken);
            var delivery = current.SubscriptionDelivery!;
            foreach (var progress in delivery.Progress) delivery = delivery.WithProgress(progress.CancelPending());
            Write(current.WithSubscriptionDelivery(delivery));
        }
        public ValueTask<RuntimePostCommitOutboxItem?> FindAsync(string id, CancellationToken cancellationToken = default) => ValueTask.FromResult<RuntimePostCommitOutboxItem?>(Current.OutboxItemId == id ? Current : null);
        public ValueTask<RuntimePostCommitOutboxItem?> FindDurableAsync(string id, CancellationToken cancellationToken = default) => FindAsync(id, cancellationToken);
        public ValueTask<IReadOnlyCollection<RuntimePostCommitOutboxItem>> GetDeliverableAsync(RuntimePostCommitOutboxQuery query, CancellationToken cancellationToken = default) => ValueTask.FromResult<IReadOnlyCollection<RuntimePostCommitOutboxItem>>([Current]);
        public ValueTask<RuntimePostCommitOutboxClaimCompletionOutcome> RecordDeliveryResultAsync(RuntimePostCommitOutboxDeliveryResult result, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<IReadOnlyCollection<RuntimePostCommitOutboxClaim>> ClaimAsync(RuntimePostCommitOutboxClaimRequest request, CancellationToken cancellationToken = default)
        {
            var item = Current;
            if (!RuntimePostCommitOutboxClaimTransitions.CanClaim(item, request)) return ValueTask.FromResult<IReadOnlyCollection<RuntimePostCommitOutboxClaim>>([]);
            var claim = RuntimePostCommitOutboxClaimTransitions.Claim(item, request); Write(claim.Item);
            return ValueTask.FromResult<IReadOnlyCollection<RuntimePostCommitOutboxClaim>>([claim]);
        }
        public ValueTask<RuntimePostCommitOutboxClaim?> RenewClaimAsync(RuntimePostCommitOutboxClaim claim, DateTimeOffset now, TimeSpan visibilityTimeout, CancellationToken cancellationToken = default)
        {
            var renewed = RuntimePostCommitOutboxClaimTransitions.Renew(Current, claim, now, visibilityTimeout);
            if (renewed is not null) Write(renewed.Item);
            return ValueTask.FromResult(renewed);
        }
        public ValueTask RecordDeliveryResultAsync(RuntimePostCommitOutboxClaim claim, RuntimePostCommitOutboxDeliveryResult result, CancellationToken cancellationToken = default)
        { Write(RuntimePostCommitOutboxClaimTransitions.Complete(Current, claim, result)); return ValueTask.CompletedTask; }
        public ValueTask<IReadOnlyCollection<RuntimePostCommitOutboxItem>> ListClaimedAsync(RuntimePostCommitOutboxClaimedQuery query, CancellationToken cancellationToken = default) => ValueTask.FromResult<IReadOnlyCollection<RuntimePostCommitOutboxItem>>(query.Matches(Current) ? [Current] : []);
    }
    private sealed class ClaimlessStore(SerializedStore inner) : IRuntimePostCommitOutboxStore
    {
        public ValueTask<IReadOnlyCollection<RuntimePostCommitOutboxItem>> GetDeliverableAsync(RuntimePostCommitOutboxQuery query, CancellationToken cancellationToken = default) => inner.GetDeliverableAsync(query, cancellationToken);
        public ValueTask<RuntimePostCommitOutboxClaimCompletionOutcome> RecordDeliveryResultAsync(RuntimePostCommitOutboxDeliveryResult result, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
