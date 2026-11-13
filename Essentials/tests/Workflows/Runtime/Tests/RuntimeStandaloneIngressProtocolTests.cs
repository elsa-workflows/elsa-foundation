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
    public async Task ExistingProcessor_DispatchesAnActualStandaloneSourceThroughTheSameRegistry()
    {
        var store = new SerializedStore(Pending());
        var handled = new List<RuntimeDeliverySourceContext>();
        using var services = Services(new SourceExecutor(source =>
        {
            handled.Add(source);
            store.CancelPending(source);
            return ValueTask.CompletedTask;
        }));
        var processor = Processor(store, services);
        Assert.True(processor.SupportsSourceKind(RuntimeDeliverySourceKind.StandaloneIngress));
        Assert.True(processor.SupportsSubscriptionDelivery(RuntimeDeliverySourceKind.StandaloneIngress,
            RuntimeStandaloneIngressSource.StandaloneTriggerDeliveryKind, RuntimeSubscriptionDelivery.CurrentVersion));
        var result = await processor.ProcessAsync(new(10));
        Assert.Equal(1, result.DeliveredCount);
        Assert.Null(Assert.Single(handled).Claim.Item.Intent);
        Assert.Equal(RuntimePostCommitOutboxStatus.Delivered, store.Current.Status);
        Assert.Equal(RuntimeRecipientPhase.CancelledBeforeDispatch, Assert.Single(store.Current.SubscriptionDelivery!.Progress).Phase);
    }

    [Fact]
    public void Subscription_readiness_requires_the_exact_contribution_and_actual_claim_lookup_and_scope_pipeline()
    {
        var store = new SerializedStore(Pending());
        using var services = Services(new SourceExecutor(_ => throw new InvalidOperationException("Capability discovery cannot run a handler.")));
        var dispatcher = services.GetRequiredService<IRuntimePostCommitIntentDispatcher>();
        var kind = RuntimeStandaloneIngressSource.StandaloneTriggerDeliveryKind;
        var version = RuntimeSubscriptionDelivery.CurrentVersion;
        var processor = Processor(store, services);
        Assert.True(dispatcher.SupportsSubscriptionDelivery(RuntimeDeliverySourceKind.StandaloneIngress, kind, version));
        Assert.True(processor.SupportsSubscriptionDelivery(RuntimeDeliverySourceKind.StandaloneIngress, kind, version));
        Assert.False(processor.SupportsSubscriptionDelivery(RuntimeDeliverySourceKind.WorkflowCheckpoint, kind, version));
        Assert.False(dispatcher.SupportsSubscriptionDelivery(RuntimeDeliverySourceKind.StandaloneIngress, "different-kind", version));
        Assert.False(processor.SupportsSubscriptionDelivery(RuntimeDeliverySourceKind.StandaloneIngress, kind, version + 1));
        Assert.False(Processor(new ClaimlessStore(store), services).SupportsSubscriptionDelivery(RuntimeDeliverySourceKind.StandaloneIngress, kind, version));
        var noScope = new RuntimePostCommitOutboxProcessor(store, dispatcher, new Clock(), DefaultRuntimeFaultCapturePolicy.CreateDefault(),
            null, [], null, null, null, null, services.GetServices<RuntimePostCommitIntentHandlerContribution>());
        Assert.False(noScope.SupportsSubscriptionDelivery(RuntimeDeliverySourceKind.StandaloneIngress, kind, version));
        var noContribution = new RuntimePostCommitOutboxProcessor(store, dispatcher, new Clock(), DefaultRuntimeFaultCapturePolicy.CreateDefault(),
            null, [], null, null, null, new Access(), []);
        Assert.False(noContribution.SupportsSubscriptionDelivery(RuntimeDeliverySourceKind.StandaloneIngress, kind, version));
        Assert.Equal(RuntimePostCommitOutboxStatus.Pending, store.Current.Status);
    }

    [Fact]
    public async Task HandlerReturnWithoutTargetEvidence_CannotCompleteOwedWork()
    {
        var store = new SerializedStore(Pending());
        using var services = Services(new SourceExecutor(_ => ValueTask.CompletedTask));
        await Assert.ThrowsAsync<ArgumentException>(() => Processor(store, services).ProcessAsync(new(1)).AsTask());
        Assert.Equal(RuntimePostCommitOutboxStatus.Delivering, store.Current.Status);
        Assert.Equal(RuntimeRecipientPhase.Pending, Assert.Single(store.Current.SubscriptionDelivery!.Progress).Phase);
    }

    [Fact]
    public async Task ClaimFreeAndLegacyDispatchers_RefuseExternalSourcesBeforeEffects()
    {
        var store = new SerializedStore(Pending());
        using var services = Services(new SourceExecutor(_ => throw new Xunit.Sdk.XunitException("SourceExecutor must not run.")));
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
        services.AddRuntimeDeliverySourceExecutor<SourceExecutor>(RuntimeStandaloneIngressSource.StandaloneTriggerDeliveryKind, Retry());
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

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public async Task Subscription_bearing_workflow_sources_refuse_legacy_contributions_before_any_handler_effect(int path)
    {
        var handler = new LegacyDurableHandler();
        var services = new ServiceCollection();
        services.AddSingleton(handler);
        services.AddRuntimePostCommitIntentHandler<LegacyDurableHandler>("Test.Workflow", Retry(), true, StimulusDeliverySnapshot.CurrentVersion);
        services.AddScoped<IRuntimePostCommitIntentDispatcher, RuntimePostCommitIntentDispatcher>();
        using var provider = services.BuildServiceProvider();
        var store = new SerializedStore(WorkflowPending());
        var dispatcher = provider.GetRequiredService<IRuntimePostCommitIntentDispatcher>();
        Assert.False(dispatcher.SupportsSubscriptionDelivery(RuntimeDeliverySourceKind.WorkflowCheckpoint, "Test.Workflow", RuntimeSubscriptionDelivery.CurrentVersion));
        if (path == 0)
            await Assert.ThrowsAsync<RuntimePostCommitSourceBoundaryException>(() => Processor(store, provider).ProcessAsync(new(1)).AsTask());
        else
        {
            var claim = RuntimePostCommitOutboxClaimTransitions.Claim(store.Current, new("owner", Now, TimeSpan.FromMinutes(1), 1));
            if (path == 1)
                await Assert.ThrowsAsync<RuntimePostCommitSourceBoundaryException>(() => dispatcher.DispatchAsync(new RuntimeDeliverySourceContext(claim, Scope)).AsTask());
            else
                await Assert.ThrowsAsync<RuntimePostCommitSourceBoundaryException>(() => dispatcher.DispatchAsync(new PostCommitDeliveryContext(claim, Scope)).AsTask());
        }
        Assert.Equal(0, handler.Effects);
    }

    [Fact]
    public async Task Processor_requires_its_own_actual_protocol_contribution_before_a_capable_replacement_dispatcher_runs()
    {
        var dispatcher = new ProtocolClaimingDispatcher();
        var services = new ServiceCollection();
        services.AddSingleton<IRuntimePostCommitIntentDispatcher>(dispatcher);
        using var provider = services.BuildServiceProvider();
        var store = new SerializedStore(WorkflowPending());
        Assert.False(Processor(store, provider).SupportsSubscriptionDelivery(RuntimeDeliverySourceKind.WorkflowCheckpoint, "Test.Workflow", 1));
        await Assert.ThrowsAsync<RuntimePostCommitSourceBoundaryException>(() => Processor(store, provider).ProcessAsync(new(1)).AsTask());
        Assert.Equal(0, dispatcher.Effects);
    }

    private static RuntimePostCommitOutboxItem WorkflowPending() => new("commit:intent",
        new RuntimePostCommitIntent("intent", "workflow", "Test.Workflow", Now, null, "key", null),
        RuntimePostCommitOutboxStatus.Pending, Now, Now, Retry(), 0, null, null, null, null, null, 0, null,
        StimulusDeliverySnapshot.CurrentVersion, Scope, null, null, RuntimeSubscriptionDelivery.CurrentVersion, null);

    private sealed class LegacyDurableHandler : IRuntimePostCommitDurableSourceIntentHandler
    {
        internal int Effects { get; private set; }
        public ValueTask HandleAsync(RuntimePostCommitIntent intent, CancellationToken cancellationToken = default) { Effects++; return ValueTask.CompletedTask; }
        public ValueTask HandleAsync(PostCommitDeliveryContext source, CancellationToken cancellationToken = default) { Effects++; return ValueTask.CompletedTask; }
    }
    private sealed class ProtocolClaimingDispatcher : IRuntimePostCommitIntentDispatcher
    {
        internal int Effects { get; private set; }
        public bool SupportsSubscriptionDelivery(RuntimeDeliverySourceKind sourceKind, string deliveryKind, int protocolVersion) => true;
        public ValueTask DispatchAsync(RuntimePostCommitIntent intent, CancellationToken cancellationToken = default) { Effects++; return ValueTask.CompletedTask; }
        public ValueTask DispatchAsync(PostCommitDeliveryContext source, CancellationToken cancellationToken = default) { Effects++; return ValueTask.CompletedTask; }
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

    private static ServiceProvider Services(SourceExecutor handler)
    {
        var services = new ServiceCollection().AddSingleton(handler);
        services.AddRuntimeDeliverySourceExecutor<SourceExecutor>(RuntimeStandaloneIngressSource.StandaloneTriggerDeliveryKind, Retry());
        services.AddScoped<IRuntimePostCommitIntentDispatcher, RuntimePostCommitIntentDispatcher>();
        return services.BuildServiceProvider();
    }
    private static RuntimePostCommitOutboxProcessor Processor(IRuntimePostCommitOutboxStore store, IServiceProvider services) =>
        new(store, services.GetRequiredService<IRuntimePostCommitIntentDispatcher>(), new Clock(), DefaultRuntimeFaultCapturePolicy.CreateDefault(),
            null, [], null, null, null, new Access(), services.GetServices<RuntimePostCommitIntentHandlerContribution>());
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => Now; }
    private sealed class Access : IPersistenceAccessContextAccessor { public PersistenceAccessContext Current => PersistenceAccessContext.Scoped(Scope); }
    private sealed class SourceExecutor(Func<RuntimeDeliverySourceContext, ValueTask> run) : IRuntimeDeliverySourceExecutor
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
