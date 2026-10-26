using Elsa.Workflows.Runtime.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Services.Coalescing;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Elsa.Workflows.Runtime.Services.Incidents;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

/// <summary>Generic source-context boundary only; no publisher or recipient execution is enabled by these controls.</summary>
public sealed class RuntimePostCommitDurableSourceTests
{
    private const string Kind = "Test.RequiredSource";
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 2, 0, 0, TimeSpan.Zero);
    private static readonly PersistenceScope Scope = new("tenant");

    [Fact]
    public async Task Feature_registered_processor_passes_actual_committed_claim_source_and_scope()
    {
        using var provider = Services().BuildServiceProvider(); using var scope = provider.CreateScope();
        var services = scope.ServiceProvider;
        var store = services.GetRequiredService<InMemoryRuntimeCheckpointCommitStore>();
        await store.AddPendingForTestingAsync(Pending("original"));
        var result = await services.GetRequiredService<IRuntimePostCommitOutboxProcessor>()
            .ProcessAsync(new(10, workflowExecutionId: "publisher"));
        Assert.Single(result.Items);
        var context = Assert.Single(services.GetRequiredService<Recorder>().Contexts);
        Assert.Equal("original:send", context.SourceOutboxItemId); Assert.Equal("original", context.SourceCommitId);
        Assert.Equal(Scope, context.Scope); Assert.Equal(1, context.Claim.FencingToken);
        Assert.Equal(RuntimePostCommitOutboxStatus.Delivering, context.Claim.Item.Status);
        Assert.Equal(RuntimePostCommitOutboxStatus.Delivered, (await store.FindAsync(context.SourceOutboxItemId))!.Status);
    }

    [Fact]
    public async Task Intent_only_dispatch_and_handler_default_refuse_required_source_before_calling_handler()
    {
        using var provider = Services().BuildServiceProvider(); using var scope = provider.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IRuntimePostCommitIntentDispatcher>();
        var item = Pending("source");
        Assert.True(dispatcher.RequiresDurableSource(Kind));
        await Assert.ThrowsAsync<RuntimePostCommitSourceBoundaryException>(() => dispatcher.DispatchAsync(item.Intent).AsTask());
        IRuntimePostCommitIntentHandler handler = scope.ServiceProvider.GetRequiredService<SourceHandler>();
        await Assert.ThrowsAsync<RuntimePostCommitSourceBoundaryException>(() => handler.HandleAsync(item.Intent).AsTask());
        Assert.Empty(scope.ServiceProvider.GetRequiredService<Recorder>().Contexts);
    }

    [Fact]
    public async Task Legacy_dispatcher_default_keeps_ordinary_behavior_and_refuses_an_unimplemented_required_context()
    {
        var context = Context();
        var ordinary = new LegacyDispatcher();
        await ordinary.DispatchAsync(context.Intent);
        Assert.Same(context.Intent, ordinary.Intent);
        await Assert.ThrowsAsync<RuntimePostCommitSourceBoundaryException>(() =>
            ((IRuntimePostCommitIntentDispatcher)ordinary).DispatchAsync(context).AsTask());
        IRuntimePostCommitIntentDispatcher required = new MissingContextDispatcher();
        await Assert.ThrowsAsync<RuntimePostCommitSourceBoundaryException>(() => required.DispatchAsync(context).AsTask());
    }

    [Fact]
    public void Contributions_are_keyed_and_detect_conflicting_source_requirements_without_partial_registration()
    {
        var services = Services();
        services.AddRuntimePostCommitIntentHandler<SourceHandler>(Kind, RuntimePostCommitRetryPolicy.None, true);
        Assert.Single(services.Where(entry => entry.ServiceType == typeof(RuntimePostCommitIntentHandlerContribution) &&
            entry.ImplementationInstance is RuntimePostCommitIntentHandlerContribution item && item.IntentKind == Kind));
        var before = services.ToArray();
        Assert.Throws<InvalidOperationException>(() => services.AddRuntimePostCommitIntentHandler<SourceHandler>(Kind));
        Assert.Equal(before, services);
        Assert.Throws<ArgumentException>(() => new RuntimePostCommitIntentHandlerContribution(Kind, typeof(OrdinaryHandler), RuntimePostCommitRetryPolicy.None, true));
    }

    [Theory]
    [InlineData("scope")] [InlineData("provider")] [InlineData("intent")]
    public async Task Missing_or_contradictory_source_support_holds_claim_without_handler_or_failure_projection(string defect)
    {
        using var provider = Services().BuildServiceProvider(); using var scope = provider.CreateScope();
        var services = scope.ServiceProvider; var store = services.GetRequiredService<InMemoryRuntimeCheckpointCommitStore>();
        await store.AddPendingForTestingAsync(Pending("source"));
        IRuntimePostCommitOutboxStore selected = defect == "provider" ? new ForwardingOutboxStore(store) : store;
        if (defect == "intent") selected = new LookupOverride(store, async id =>
        {
            var current = (await store.FindAsync(id))!;
            var changed = new RuntimePostCommitIntent(current.Intent.IntentId, "other-publisher", Kind, Now, null, "key", null);
            return new(current.OutboxItemId, changed, current.Status, current.RecordedAt, current.AvailableAt, current.RetryPolicy,
                current.DeliveryAttemptCount, current.DeliveringOwnerId, current.DeliveryStartedAt, null, null, current.Metadata,
                current.DeliveryFencingToken, current.DeliveryVisibleAfter, current.StimulusDeliveryProtocolVersion, current.StimulusDeliverySourceScope, null);
        });
        var processor = Processor(selected, services, defect == "scope" ? null : new Access());
        var refusal = await Assert.ThrowsAsync<RuntimePostCommitSourceBoundaryException>(() => processor.ProcessAsync(new(10)).AsTask());
        Assert.False(refusal.IsUnavailable); Assert.NotNull(refusal.InnerException);
        Assert.Empty(services.GetRequiredService<Recorder>().Contexts);
        var retained = (await store.FindAsync("source:send"))!;
        Assert.Equal(RuntimePostCommitOutboxStatus.Delivering, retained.Status); Assert.Equal(0, retained.DeliveryAttemptCount);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Unavailable_durable_read_preserves_cause_and_never_uses_ordinary_lookup(bool cancelled)
    {
        using var provider = Services().BuildServiceProvider(); using var scope = provider.CreateScope();
        var services = scope.ServiceProvider; var store = services.GetRequiredService<InMemoryRuntimeCheckpointCommitStore>();
        await store.AddPendingForTestingAsync(Pending("source"));
        Exception cause = cancelled ? new OperationCanceledException() : new IOException();
        var selected = new LookupOverride(store, _ => ValueTask.FromException<RuntimePostCommitOutboxItem?>(cause));
        var refusal = await Assert.ThrowsAsync<RuntimePostCommitSourceBoundaryException>(() =>
            Processor(selected, services, new Access()).ProcessAsync(new(10)).AsTask());
        Assert.True(refusal.IsUnavailable); Assert.Same(cause, refusal.InnerException);
        Assert.Empty(services.GetRequiredService<Recorder>().Contexts);
        Assert.Equal(0, (await store.FindAsync("source:send"))!.DeliveryAttemptCount);
    }

    [Fact]
    public async Task Claimless_legacy_provider_refuses_before_dispatch_or_outcome_write()
    {
        using var provider = Services().BuildServiceProvider(); using var scope = provider.CreateScope();
        var services = scope.ServiceProvider; var store = services.GetRequiredService<InMemoryRuntimeCheckpointCommitStore>();
        await store.AddPendingForTestingAsync(Pending("source"));
        var legacy = new Claimless(store);
        await Assert.ThrowsAsync<RuntimePostCommitSourceBoundaryException>(() =>
            Processor(legacy, services, new Access()).ProcessAsync(new(10)).AsTask());
        Assert.Empty(services.GetRequiredService<Recorder>().Contexts);
        Assert.Equal(RuntimePostCommitOutboxStatus.Pending, (await store.FindAsync("source:send"))!.Status);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Expiry_or_ownership_loss_during_durable_read_cannot_invoke_or_complete_the_handler(bool takeover)
    {
        using var provider = Services().BuildServiceProvider(); using var scope = provider.CreateScope();
        var services = scope.ServiceProvider; var store = services.GetRequiredService<InMemoryRuntimeCheckpointCommitStore>();
        await store.AddPendingForTestingAsync(Pending("source"));
        var clock = (Clock)services.GetRequiredService<TimeProvider>();
        var selected = new LookupOverride(store, async id =>
        {
            var current = (await store.FindAsync(id))!;
            clock.Now = current.DeliveryVisibleAfter!.Value.AddSeconds(1);
            if (takeover) Assert.Single(await store.ClaimAsync(new("peer", clock.Now, TimeSpan.FromMinutes(1), 1)));
            return await store.FindAsync(id);
        });
        var result = await Processor(selected, services, new Access()).ProcessAsync(new(10));
        Assert.Equal(RuntimePostCommitOutboxClaimCompletionOutcome.SupersededByOtherOwner, Assert.Single(result.Items).Outcome);
        Assert.Empty(services.GetRequiredService<Recorder>().Contexts);
        var retained = (await store.FindAsync("source:send"))!;
        if (takeover) Assert.Equal("peer", retained.DeliveringOwnerId);
        else Assert.NotEqual("peer", retained.DeliveringOwnerId);
        Assert.Equal(0, retained.DeliveryAttemptCount);
    }

    [Fact]
    public async Task Required_source_waits_for_real_quiescence_flush_and_uses_its_canonical_row_once()
    {
        var registrations = Services(); registrations.AddPersistenceModes();
        using var provider = registrations.BuildServiceProvider(); using var serviceScope = provider.CreateScope();
        var services = serviceScope.ServiceProvider;
        var inner = services.GetRequiredService<InMemoryRuntimeCheckpointCommitStore>();
        var store = services.GetRequiredService<IRuntimeCheckpointCommitStore>();
        var lookup = services.GetRequiredService<IPostCommitOutboxLookupStore>();
        var processor = services.GetRequiredService<IRuntimePostCommitOutboxProcessor>();
        await using var drain = services.GetRequiredService<IRuntimeCoalescingDrainScopeFactory>()
            .Begin("publisher", new(PersistenceMode.Balanced, RunHasRecord: false));
        // Generic required-source fixture, not an enabled publisher protocol. T050 owns publisher stamping.
        var original = Pending("overlay", protocol: false);
        await store.CommitAsync(Envelope("overlay", 1, original), new(RuntimeCheckpointPersistenceMode.Deferred));
        Assert.NotNull(await lookup.FindAsync(original.OutboxItemId));
        Assert.Null(await lookup.FindDurableAsync(original.OutboxItemId));
        Assert.Empty((await processor.ProcessAsync(new(10, "publisher", Kind))).Items);
        Assert.Empty(services.GetRequiredService<Recorder>().Contexts);
        Assert.Equal(RuntimePostCommitOutboxStatus.Pending, (await lookup.FindAsync(original.OutboxItemId))!.Status);
        await drain.FlushAtQuiescenceAsync();
        var flush = Assert.Single(inner.ListCommits());
        var canonical = Assert.Single(flush.Commit.StateChanges.PostCommitOutbox).State;
        Assert.NotEqual(original.OutboxItemId, canonical.OutboxItemId);
        Assert.Null(await inner.FindAsync(original.OutboxItemId));
        Assert.Equal(1, (await processor.ProcessAsync(new(10, "publisher", Kind))).DeliveredCount);
        var context = Assert.Single(services.GetRequiredService<Recorder>().Contexts);
        Assert.Equal(canonical.OutboxItemId, context.SourceOutboxItemId);
        Assert.Equal(flush.Commit.CommitId, context.SourceCommitId);
        Assert.Equal(Scope, context.Scope);
        Assert.Empty((await processor.ProcessAsync(new(10, "publisher", Kind))).Items);
        Assert.Single(services.GetRequiredService<Recorder>().Contexts);
    }

    [Fact]
    public async Task Repeated_cap_flush_never_rewrites_a_required_source_that_a_peer_selected_and_completed()
    {
        var registrations = Services(); registrations.AddPersistenceModes(options => options.MaxActivitiesPerCommit = 1);
        using var provider = registrations.BuildServiceProvider(); using var serviceScope = provider.CreateScope();
        var services = serviceScope.ServiceProvider; var inner = services.GetRequiredService<InMemoryRuntimeCheckpointCommitStore>();
        var publisher = new WorkflowExecutionState("publisher", new("artifact", "definition", "version", "1.0.0", "hash"),
            WorkflowExecutionStatus.Running, null, Now, Now, null, null, null, null, Scope.Value, new Dictionary<string, string>());
        await services.GetRequiredService<CoalescingInner<IWorkflowExecutionStateStore>>().Value.SaveAsync(publisher);
        await using var drain = services.GetRequiredService<IRuntimeCoalescingDrainScopeFactory>()
            .Begin("publisher", new(PersistenceMode.Balanced, RunHasRecord: false));
        var store = services.GetRequiredService<IRuntimeCheckpointCommitStore>();
        var original = Pending("cap-source", protocol: true);
        await store.CommitAsync(Envelope("one", 1, original), new(RuntimeCheckpointPersistenceMode.Deferred));
        await store.CommitAsync(Envelope("two", 2), new(RuntimeCheckpointPersistenceMode.Deferred));
        Assert.True(drain.Session.IsActive);
        Assert.NotNull(await inner.FindDurableAsync(original.OutboxItemId));
        var peer = Assert.Single(await inner.ClaimAsync(new("peer", Now, TimeSpan.FromMinutes(1), 1)));
        var source = new PostCommitDeliveryContext(peer, Scope);
        var selected = new StimulusDeliverySnapshot(1, source.SourceOutboxItemId, source.IntentFingerprint,
            StimulusDeliveryScope.From(publisher, Scope), Now, "event", "named", [], []);
        await inner.InstallAsync(source, selected);
        await inner.RecordDeliveryResultAsync(peer, new(peer.OutboxItemId, RuntimePostCommitOutboxStatus.Delivered, Now));
        await store.CommitAsync(Envelope("three", 3), new(RuntimeCheckpointPersistenceMode.Deferred));
        await store.CommitAsync(Envelope("four", 4), new(RuntimeCheckpointPersistenceMode.Deferred));
        await drain.FlushAtQuiescenceAsync();
        var retained = (await inner.FindDurableAsync(original.OutboxItemId))!;
        Assert.Equal(RuntimePostCommitOutboxStatus.Delivered, retained.Status);
        Assert.True(selected.IsEquivalentTo(retained.RecipientSnapshot));
        Assert.Empty(services.GetRequiredService<Recorder>().Contexts);
        Assert.Equal(2, inner.ListCommits().Count);
        Assert.Single(inner.ListCommits().SelectMany(commit => commit.Commit.StateChanges.PostCommitOutbox));
    }

    [Fact]
    public async Task Legacy_in_memory_lookup_stays_ordinary_but_unknown_scope_cannot_authorize_required_delivery()
    {
        var state = new InMemoryRuntimeCheckpointStoreState();
        var first = new InMemoryRuntimeCheckpointCommitStore(state: state, persistenceAccessContextAccessor: new Access());
        var second = new InMemoryRuntimeCheckpointCommitStore(state: state, persistenceAccessContextAccessor: new OtherAccess());
        var legacy = new RuntimePostCommitOutboxItem("legacy:send", Pending("legacy").Intent,
            RuntimePostCommitOutboxStatus.Pending, Now, Now);
        await first.AddPendingForTestingAsync(legacy);
        Assert.NotNull(await second.FindAsync(legacy.OutboxItemId));
        await Assert.ThrowsAsync<InvalidDataException>(() => first.FindDurableAsync(legacy.OutboxItemId).AsTask());
        await Assert.ThrowsAsync<InvalidDataException>(() => second.FindDurableAsync(legacy.OutboxItemId).AsTask());
        using var provider = Services().BuildServiceProvider(); using var scope = provider.CreateScope();
        await Assert.ThrowsAsync<RuntimePostCommitSourceBoundaryException>(() =>
            Processor(second, scope.ServiceProvider, new OtherAccess()).ProcessAsync(new(10)).AsTask());
        Assert.Empty(scope.ServiceProvider.GetRequiredService<Recorder>().Contexts);
    }

    [Fact]
    public async Task Required_source_scope_is_captured_by_real_committer_and_old_scope_free_entry_refuses()
    {
        var envelope = Envelope("scoped", 1) with { PostCommitIntents = [Pending("ignored").Intent] };
        var contribution = new RuntimePostCommitIntentHandlerContribution(Kind, typeof(SourceHandler), RuntimePostCommitRetryPolicy.None, true);
        Assert.Throws<RuntimePostCommitSourceBoundaryException>(() => RuntimePostCommitOutboxItems.CreatePendingChanges(envelope, [contribution]));
        using var provider = Services().BuildServiceProvider(); using var scope = provider.CreateScope();
        await scope.ServiceProvider.GetRequiredService<RuntimeCheckpointCommitter>().CommitAsync(envelope);
        var row = (await scope.ServiceProvider.GetRequiredService<InMemoryRuntimeCheckpointCommitStore>().FindDurableAsync("scoped:send"))!;
        Assert.Equal(Scope, row.StimulusDeliverySourceScope);
        Assert.Null(row.StimulusDeliveryProtocolVersion); Assert.Null(row.RecipientSnapshot);
        var missing = Services(); missing.RemoveAll<IPersistenceAccessContextAccessor>();
        using var missingProvider = missing.BuildServiceProvider(); using var missingScope = missingProvider.CreateScope();
        await Assert.ThrowsAsync<RuntimePostCommitSourceBoundaryException>(() =>
            missingScope.ServiceProvider.GetRequiredService<RuntimeCheckpointCommitter>().CommitAsync(envelope).AsTask());
        Assert.Empty(missingScope.ServiceProvider.GetRequiredService<InMemoryRuntimeCheckpointCommitStore>().ListCommits());
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Registered_requirement_cannot_be_erased_by_legacy_dispatcher_replacement(bool retainedWitness)
    {
        var registrations = Services();
        var legacy = new LegacyDispatcher();
        registrations.Replace(ServiceDescriptor.Singleton<IRuntimePostCommitIntentDispatcher>(legacy));
        using var provider = registrations.BuildServiceProvider(); using var scope = provider.CreateScope();
        var services = scope.ServiceProvider; var store = services.GetRequiredService<InMemoryRuntimeCheckpointCommitStore>();
        var pending = Pending("source");
        if (!retainedWitness) pending = new(pending.OutboxItemId, pending.Intent, pending.Status, Now, Now);
        await store.AddPendingForTestingAsync(pending);
        await Assert.ThrowsAsync<RuntimePostCommitSourceBoundaryException>(() =>
            services.GetRequiredService<IRuntimePostCommitOutboxProcessor>().ProcessAsync(new(10)).AsTask());
        Assert.Null(legacy.Intent);
        Assert.Empty(services.GetRequiredService<Recorder>().Contexts);
        Assert.Equal(0, (await store.FindAsync("source:send"))!.DeliveryAttemptCount);
    }

    [Fact]
    public async Task Active_required_source_does_not_consume_claim_limit_ahead_of_real_scheduler_continuation()
    {
        var registrations = Services(); registrations.AddPersistenceModes();
        using var provider = registrations.BuildServiceProvider(); using var serviceScope = provider.CreateScope();
        var services = serviceScope.ServiceProvider;
        await using var drain = services.GetRequiredService<IRuntimeCoalescingDrainScopeFactory>()
            .Begin("publisher", new(PersistenceMode.Balanced, RunHasRecord: false));
        var required = Pending("first");
        var work = new RuntimeSchedulerWorkItem("work", "publisher", "command", WorkflowExecutionCommandKind.InvokeActivity,
            "envelope", "work-key", Now, Now);
        var intent = new RuntimePostCommitIntent("continue", "publisher", RuntimePostCommitIntentKinds.EnqueueSchedulerWork,
            Now, null, "continue-key", System.Text.Json.JsonSerializer.SerializeToElement(work));
        var continuation = new RuntimePostCommitOutboxItem("continuation:continue", intent, RuntimePostCommitOutboxStatus.Pending, Now, Now);
        var envelope = Envelope("both", 1, required);
        envelope = envelope with { StateChanges = envelope.StateChanges.WithPostCommitOutbox([
            new(required.OutboxItemId, RuntimeStateChangeOperation.Upsert, required, new Dictionary<string, string>()),
            new(continuation.OutboxItemId, RuntimeStateChangeOperation.Upsert, continuation, new Dictionary<string, string>())]) };
        await services.GetRequiredService<IRuntimeCheckpointCommitStore>().CommitAsync(envelope, new(RuntimeCheckpointPersistenceMode.Deferred));
        var processed = await services.GetRequiredService<IRuntimePostCommitOutboxProcessor>().ProcessAsync(new(1, "publisher"));
        Assert.Equal(continuation.OutboxItemId, Assert.Single(processed.Items).OutboxItemId);
        Assert.Equal(1, processed.DeliveredCount);
        Assert.Equal("work", Assert.Single((await services.GetRequiredService<IWorkflowSchedulerWorkQueue>().ListAsync(new("publisher"))).Items).WorkItemId);
        Assert.Empty(services.GetRequiredService<Recorder>().Contexts);
        Assert.Equal(RuntimePostCommitOutboxStatus.Pending, (await services.GetRequiredService<IPostCommitOutboxLookupStore>().FindAsync(required.OutboxItemId))!.Status);
        await drain.FlushAtQuiescenceAsync();
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Protected_row_refuses_intent_only_delivery_through_preserved_processor_constructor(bool claimless)
    {
        using var provider = Services().BuildServiceProvider(); using var scope = provider.CreateScope();
        var services = scope.ServiceProvider; var store = services.GetRequiredService<InMemoryRuntimeCheckpointCommitStore>();
        await store.AddPendingForTestingAsync(Pending("source"));
        var legacy = new LegacyDispatcher();
        IRuntimePostCommitOutboxStore selected = claimless ? new Claimless(store) : store;
        var processor = new RuntimePostCommitOutboxProcessor(selected, legacy, services.GetRequiredService<TimeProvider>());
        await Assert.ThrowsAsync<RuntimePostCommitSourceBoundaryException>(() => processor.ProcessAsync(new(10)).AsTask());
        Assert.Null(legacy.Intent);
        var retained = (await store.FindAsync("source:send"))!;
        Assert.NotEqual(RuntimePostCommitOutboxStatus.Delivered, retained.Status);
        Assert.Equal(0, retained.DeliveryAttemptCount);
    }

    [Fact]
    public async Task Uncertain_quiescence_retry_keeps_one_source_after_peer_delivery_and_refuses_new_decisions()
    {
        var registrations = Services(); LostFlushAcknowledgement? lost = null;
        registrations.Replace(ServiceDescriptor.Scoped<IRuntimeCheckpointCommitStore>(services =>
            lost = new(services.GetRequiredService<InMemoryRuntimeCheckpointCommitStore>())));
        registrations.AddPersistenceModes();
        using var provider = registrations.BuildServiceProvider(); using var scope = provider.CreateScope();
        var services = scope.ServiceProvider; var inner = services.GetRequiredService<InMemoryRuntimeCheckpointCommitStore>();
        var store = services.GetRequiredService<IRuntimeCheckpointCommitStore>();
        await using var drain = services.GetRequiredService<IRuntimeCoalescingDrainScopeFactory>()
            .Begin("publisher", new(PersistenceMode.Balanced, RunHasRecord: false));
        await store.CommitAsync(Envelope("overlay", 1, Pending("overlay")), new(RuntimeCheckpointPersistenceMode.Deferred));
        await Assert.ThrowsAsync<IOException>(() => drain.FlushAtQuiescenceAsync().AsTask());
        var first = Assert.Single(inner.ListCommits());
        var canonical = Assert.Single(first.Commit.StateChanges.PostCommitOutbox).State;
        Assert.Equal(1, (await Processor(inner, services, new Access()).ProcessAsync(new(10))).DeliveredCount);
        Assert.Single(services.GetRequiredService<Recorder>().Contexts);
        var later = Envelope("later", 2);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.CommitAsync(later, new(RuntimeCheckpointPersistenceMode.Deferred)).AsTask());
        ((Clock)services.GetRequiredService<TimeProvider>()).Now = Now.AddSeconds(2);
        await drain.FlushAtQuiescenceAsync();
        Assert.Equal([first.Commit.CommitId, first.Commit.CommitId], lost!.FlushIds);
        Assert.Single(inner.ListCommits());
        Assert.Equal(RuntimePostCommitOutboxStatus.Delivered, (await inner.FindDurableAsync(canonical.OutboxItemId))!.Status);
        Assert.Empty((await services.GetRequiredService<IRuntimePostCommitOutboxProcessor>().ProcessAsync(new(10))).Items);
        Assert.Single(services.GetRequiredService<Recorder>().Contexts);
        await store.CommitAsync(later, new(RuntimeCheckpointPersistenceMode.Deferred));
        Assert.Equal(2, inner.ListCommits().Count);
    }

    private static RuntimeCheckpointCommit Envelope(string commitId, int activity, RuntimePostCommitOutboxItem? item = null) =>
        new(commitId, new("checkpoint-" + commitId, RuntimeCheckpointNames.ActivityCompleted, "publisher", Now,
            ["activity-" + activity], new Dictionary<string, string>()),
            new(null, null, [], [], [], [], [], postCommitOutbox: item is null ? [] :
                [new(item.OutboxItemId, RuntimeStateChangeOperation.Upsert, item, new Dictionary<string, string>())]),
            [], new Dictionary<string, string>());

    private static ServiceCollection Services()
    {
        var services = new ServiceCollection(); services.AddWorkflowRuntime();
        services.Replace(ServiceDescriptor.Singleton<TimeProvider>(new Clock()));
        services.AddSingleton<IPersistenceAccessContextAccessor>(new Access()); services.AddSingleton<Recorder>();
        services.AddRuntimePostCommitIntentHandler<SourceHandler>(Kind, RuntimePostCommitRetryPolicy.None, true);
        return services;
    }
    private static RuntimePostCommitOutboxProcessor Processor(IRuntimePostCommitOutboxStore store, IServiceProvider services,
        IPersistenceAccessContextAccessor? access) => new(store, services.GetRequiredService<IRuntimePostCommitIntentDispatcher>(),
        services.GetRequiredService<TimeProvider>(), DefaultRuntimeFaultCapturePolicy.CreateDefault(), null, [], null, null, null, access, services.GetServices<RuntimePostCommitIntentHandlerContribution>());
    private static RuntimePostCommitOutboxItem Pending(string commit, bool protocol = false) => new(commit + ":send",
        new("send", "publisher", Kind, Now, null, "key", null), RuntimePostCommitOutboxStatus.Pending, Now, Now,
        null, 0, null, null, null, null, null, 0, null, protocol ? 1 : null, Scope, null);
    private static PostCommitDeliveryContext Context() => new(RuntimePostCommitOutboxClaimTransitions.Claim(Pending("source"),
        new("owner", Now, TimeSpan.FromMinutes(1), 1)), Scope);
    private sealed class Access : IPersistenceAccessContextAccessor
    { public PersistenceAccessContext Current => PersistenceAccessContext.Scoped(Scope); }
    private sealed class OtherAccess : IPersistenceAccessContextAccessor
    { public PersistenceAccessContext Current => PersistenceAccessContext.Scoped(new("other")); }
    private sealed class Clock : TimeProvider { public DateTimeOffset Now { get; set; } = RuntimePostCommitDurableSourceTests.Now; public override DateTimeOffset GetUtcNow() => Now; }
    private sealed class Recorder { public List<PostCommitDeliveryContext> Contexts { get; } = []; }
    private sealed class SourceHandler(Recorder recorder) : IRuntimePostCommitDurableSourceIntentHandler
    { public ValueTask HandleAsync(PostCommitDeliveryContext source, CancellationToken cancellationToken = default) { recorder.Contexts.Add(source); return ValueTask.CompletedTask; } }
    private sealed class OrdinaryHandler : IRuntimePostCommitIntentHandler
    { public ValueTask HandleAsync(RuntimePostCommitIntent intent, CancellationToken cancellationToken = default) => ValueTask.CompletedTask; }
    private class LegacyDispatcher : IRuntimePostCommitIntentDispatcher
    { public RuntimePostCommitIntent? Intent { get; private set; } public ValueTask DispatchAsync(RuntimePostCommitIntent intent, CancellationToken cancellationToken = default) { Intent = intent; return ValueTask.CompletedTask; } }
    private sealed class MissingContextDispatcher : LegacyDispatcher, IRuntimePostCommitIntentDispatcher
    { public bool RequiresDurableSource(string intentKind) => true; }
    private sealed class LostFlushAcknowledgement(IRuntimeCheckpointCommitStore inner) : IRuntimeCheckpointCommitStore
    {
        private bool armed = true;
        public List<string> FlushIds { get; } = [];
        public async ValueTask<RuntimeCheckpointCommitStoreResult> CommitAsync(RuntimeCheckpointCommit commit,
            RuntimeCheckpointPersistenceDecision decision, CancellationToken cancellationToken = default)
        {
            var flush = commit.Checkpoint.Metadata.ContainsKey(RuntimeCoalescingMetadataKeys.CoalescedFlush);
            if (flush) FlushIds.Add(commit.CommitId);
            var result = await inner.CommitAsync(commit, decision, cancellationToken);
            if (flush && armed) { armed = false; throw new IOException("Constructed acknowledgement loss after actual commit."); }
            return result;
        }
    }
    private sealed class LookupOverride(IRuntimePostCommitOutboxStore inner, Func<string, ValueTask<RuntimePostCommitOutboxItem?>> read)
        : ForwardingOutboxStore(inner), IPostCommitOutboxLookupStore
    { public ValueTask<RuntimePostCommitOutboxItem?> FindDurableAsync(string outboxItemId, CancellationToken cancellationToken = default) => read(outboxItemId); }
    private sealed class Claimless(IRuntimePostCommitOutboxStore inner) : IRuntimePostCommitOutboxStore
    {
        public ValueTask<IReadOnlyCollection<RuntimePostCommitOutboxItem>> GetDeliverableAsync(RuntimePostCommitOutboxQuery query, CancellationToken cancellationToken = default) => inner.GetDeliverableAsync(query, cancellationToken);
        public ValueTask<RuntimePostCommitOutboxClaimCompletionOutcome> RecordDeliveryResultAsync(RuntimePostCommitOutboxDeliveryResult result, CancellationToken cancellationToken = default) => inner.RecordDeliveryResultAsync(result, cancellationToken);
    }
}
