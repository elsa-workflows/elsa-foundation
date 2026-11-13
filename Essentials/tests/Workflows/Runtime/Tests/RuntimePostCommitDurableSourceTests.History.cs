using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

/// <summary>Supplied origins and controlled observation qualification; actual native source/dispatcher boundaries, no history events or semantic delivery.</summary>
public sealed partial class RuntimePostCommitDurableSourceTests
{
    [Fact]
    public async Task Origin_requires_context_even_when_the_original_contribution_was_ordinary()
    {
        using var provider = HistoryServices().BuildServiceProvider(); using var scope = provider.CreateScope();
        var services = scope.ServiceProvider;
        var dispatcher = services.GetRequiredService<IRuntimePostCommitIntentDispatcher>();
        Assert.False(dispatcher.RequiresDurableSource(Kind));
        await Assert.ThrowsAsync<RuntimePostCommitSourceBoundaryException>(() => dispatcher.DispatchAsync(HistoryItem().Intent).AsTask());
        Assert.Empty(services.GetRequiredService<HistoryOwner>().Contexts);
        Assert.Empty(services.GetRequiredService<HistoryOwner>().Intents);
    }

    [Fact]
    public async Task Actual_committer_binds_origin_source_scope_without_retrofitting_a_routing_protocol()
    {
        using var provider = HistoryServices().BuildServiceProvider(); using var scope = provider.CreateScope();
        var services = scope.ServiceProvider;
        var commit = Envelope("source", 1) with { PostCommitIntents = [HistoryItem().Intent] };
        Assert.Throws<RuntimePostCommitSourceBoundaryException>(() => RuntimePostCommitOutboxItems.CreatePendingChanges(commit));
        await services.GetRequiredService<RuntimeCheckpointCommitter>().CommitAsync(commit);
        var item = (await services.GetRequiredService<InMemoryRuntimeCheckpointCommitStore>().FindDurableAsync("source:send"))!;
        Assert.Equal(Scope, item.StimulusDeliverySourceScope);
        Assert.Equal(HistoryItem().Intent.HistoryCausation, item.Intent.HistoryCausation);
        Assert.Null(item.StimulusDeliveryProtocolVersion); Assert.Null(item.RecipientSnapshot);
        var ordinary = commit with { PostCommitIntents = [new("legacy", "publisher", Kind, Now, null, null, null)] };
        Assert.Null(Assert.Single(RuntimePostCommitOutboxItems.CreatePendingChanges(ordinary)).State.StimulusDeliverySourceScope);
    }

    [Fact]
    public async Task Missing_native_scope_refuses_the_origin_write_before_persistence()
    {
        var registrations = HistoryServices(); registrations.RemoveAll<IPersistenceAccessContextAccessor>();
        using var provider = registrations.BuildServiceProvider(); using var scope = provider.CreateScope();
        await Assert.ThrowsAsync<RuntimePostCommitSourceBoundaryException>(() => scope.ServiceProvider.GetRequiredService<RuntimeCheckpointCommitter>()
            .CommitAsync(Envelope("source", 1) with { PostCommitIntents = [HistoryItem().Intent] }).AsTask());
        Assert.Empty(scope.ServiceProvider.GetRequiredService<InMemoryRuntimeCheckpointCommitStore>().ListCommits());
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Native_processor_uses_exact_physical_context_and_the_same_qualified_owner_for_an_old_declaration(bool transient)
    {
        var registrations = HistoryServices();
        if (transient) registrations.Replace(ServiceDescriptor.Transient<HistoryHandler>(services =>
        {
            var owner = services.GetRequiredService<HistoryOwner>(); owner.Resolutions++;
            return new(owner);
        }));
        using var provider = registrations.BuildServiceProvider(); using var scope = provider.CreateScope();
        var services = scope.ServiceProvider; var store = services.GetRequiredService<InMemoryRuntimeCheckpointCommitStore>();
        await store.AddPendingForTestingAsync(HistoryItem());
        var result = await services.GetRequiredService<IRuntimePostCommitOutboxProcessor>().ProcessAsync(new(10));
        Assert.Equal(1, result.DeliveredCount);
        var source = Assert.Single(services.GetRequiredService<HistoryOwner>().Contexts);
        Assert.Equal("source:send", source.SourceOutboxItemId); Assert.Equal(Scope, source.Scope);
        Assert.Equal(HistoryItem().Intent.HistoryCausation, source.Intent.HistoryCausation);
        Assert.Empty(services.GetRequiredService<HistoryOwner>().Intents);
        Assert.Equal(0, services.GetRequiredService<HistoryReader>().Reads);
        Assert.Equal(0, services.GetRequiredService<HistoryWriter>().Writes);
        Assert.Equal(RuntimePostCommitOutboxStatus.Delivered, (await store.FindDurableAsync(source.SourceOutboxItemId))!.Status);
        if (transient) Assert.Equal(1, services.GetRequiredService<HistoryOwner>().Resolutions);
    }

    [Theory]
    [InlineData("writer")] [InlineData("reader")] [InlineData("pair")]
    [InlineData("default-owner")] [InlineData("ordinary-owner")] [InlineData("owner")]
    public async Task Missing_selected_recording_chain_keeps_the_actual_source_owed_without_effect_or_attempt_ack(string missing)
    {
        var registrations = HistoryServices();
        if (missing == "writer") registrations.RemoveAll<IRuntimeHistoryObservationWriter>();
        if (missing == "reader") registrations.RemoveAll<IRuntimeHistoryStore>();
        if (missing is "default-owner" or "ordinary-owner")
        {
            registrations.RemoveAll<RuntimePostCommitIntentHandlerContribution>();
            if (missing == "default-owner") registrations.AddRuntimePostCommitIntentHandler<SourceHandler>(Kind);
            else registrations.AddRuntimePostCommitIntentHandler<OrdinaryHandler>(Kind);
        }
        using var provider = registrations.BuildServiceProvider(); using var scope = provider.CreateScope();
        var services = scope.ServiceProvider; var store = services.GetRequiredService<InMemoryRuntimeCheckpointCommitStore>();
        if (missing == "pair") services.GetRequiredService<HistoryWriter>().Paired = false;
        if (missing == "owner") services.GetRequiredService<HistoryOwner>().Supported = false;
        await store.AddPendingForTestingAsync(HistoryItem());
        var refusal = await Assert.ThrowsAsync<RuntimePostCommitSourceBoundaryException>(() =>
            services.GetRequiredService<IRuntimePostCommitOutboxProcessor>().ProcessAsync(new(10)).AsTask());
        Assert.True(refusal.IsUnavailable);
        Assert.Empty(services.GetRequiredService<HistoryOwner>().Contexts); Assert.Empty(services.GetRequiredService<HistoryOwner>().Intents);
        Assert.Empty(services.GetRequiredService<Recorder>().Contexts);
        var retained = (await store.FindDurableAsync("source:send"))!;
        Assert.Equal(RuntimePostCommitOutboxStatus.Delivering, retained.Status); Assert.Equal(0, retained.DeliveryAttemptCount);
        Assert.Null(retained.DeliveredAt); Assert.Null(retained.LastFailureMessage);
    }

    [Fact]
    public void Qualification_requires_the_selected_pair_and_legacy_ports_default_to_false()
    {
        using var provider = HistoryServices().BuildServiceProvider(); using var scope = provider.CreateScope();
        var services = scope.ServiceProvider; var dispatcher = services.GetRequiredService<IRuntimePostCommitIntentDispatcher>();
        var writer = services.GetRequiredService<HistoryWriter>(); var reader = services.GetRequiredService<HistoryReader>();
        Assert.True(dispatcher.CanRecordHistory(Kind, writer, reader));
        Assert.False(dispatcher.CanRecordHistory(Kind, new HistoryWriter(reader), reader));
        Assert.False(dispatcher.CanRecordHistory(Kind, writer, new HistoryReader()));
        Assert.False(dispatcher.CanRecordHistory("another-kind", writer, reader));
        Assert.False(((IRuntimePostCommitDurableSourceIntentHandler)new SourceHandler(new Recorder())).CanRecordHistory(writer, reader));
        Assert.False(((IRuntimePostCommitIntentDispatcher)new LegacyDispatcher()).CanRecordHistory(Kind, writer, reader));
        Assert.Equal(0, reader.Reads); Assert.Equal(0, writer.Writes);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Origin_alone_prevents_legacy_constructor_or_claimless_dispatch_even_without_an_old_source_witness(bool claimless)
    {
        using var provider = HistoryServices().BuildServiceProvider(); using var scope = provider.CreateScope();
        var services = scope.ServiceProvider; var store = services.GetRequiredService<InMemoryRuntimeCheckpointCommitStore>();
        await store.AddPendingForTestingAsync(HistoryItem(withScope: false));
        var legacy = new LegacyDispatcher();
        IRuntimePostCommitOutboxStore selected = claimless ? new Claimless(store) : store;
        var processor = new RuntimePostCommitOutboxProcessor(selected, legacy, services.GetRequiredService<TimeProvider>());
        await Assert.ThrowsAsync<RuntimePostCommitSourceBoundaryException>(() => processor.ProcessAsync(new(10)).AsTask());
        Assert.Null(legacy.Intent);
        var retained = (await store.FindDurableAsync("source:send"))!;
        Assert.NotEqual(RuntimePostCommitOutboxStatus.Delivered, retained.Status); Assert.Equal(0, retained.DeliveryAttemptCount);
    }

    [Theory]
    [InlineData("event")] [InlineData("run")] [InlineData("delivery")] [InlineData("scope")]
    [InlineData("missing-scope")] [InlineData("blank-work")] [InlineData("oversized")]
    public async Task Diagnostic_origin_cannot_substitute_foreign_or_malformed_native_identity(string defect)
    {
        using var provider = HistoryServices().BuildServiceProvider(); using var scope = provider.CreateScope();
        var item = HistoryItem(); var cause = item.Intent.HistoryCausation!;
        cause = defect switch
        {
            "event" => cause with { HistoryEventId = null }, "run" => cause with { WorkflowExecutionId = "foreign" },
            "delivery" => cause with { DeliveryId = "foreign:send" }, "blank-work" => cause with { SchedulerWorkItemId = " " },
            "oversized" => cause with { CommandId = new string('x', 1025) }, _ => cause
        };
        var intent = new RuntimePostCommitIntent("send", "publisher", Kind, Now, null, "key", null) { HistoryCausation = cause };
        item = new("source:send", intent, RuntimePostCommitOutboxStatus.Pending, Now, Now, null, 0, null, null, null, null, null, 0,
            null, null, defect == "scope" ? new("foreign") : defect == "missing-scope" ? null : Scope, null);
        var source = new PostCommitDeliveryContext(RuntimePostCommitOutboxClaimTransitions.Claim(item, new("owner", Now, TimeSpan.FromMinutes(1), 1)), Scope);
        var refusal = await Assert.ThrowsAsync<RuntimePostCommitSourceBoundaryException>(() => scope.ServiceProvider
            .GetRequiredService<IRuntimePostCommitIntentDispatcher>().DispatchAsync(source).AsTask());
        Assert.False(refusal.IsUnavailable); Assert.IsType<InvalidDataException>(refusal.InnerException);
        Assert.Empty(scope.ServiceProvider.GetRequiredService<HistoryOwner>().Contexts);
    }

    [Theory]
    [InlineData("io", true)] [InlineData("hard", false)] [InlineData("unrequested-cancel", true)]
    public async Task Qualification_failures_keep_the_original_cause_and_do_not_reclassify_hard_inner_io(string kind, bool unavailable)
    {
        using var provider = HistoryServices().BuildServiceProvider(); using var scope = provider.CreateScope();
        var services = scope.ServiceProvider;
        Exception original = kind switch
        {
            "io" => new IOException("private provider detail"),
            "hard" => new InvalidDataException("private malformed data", new IOException()),
            _ => new OperationCanceledException()
        };
        services.GetRequiredService<HistoryWriter>().Failure = original;
        var source = new PostCommitDeliveryContext(RuntimePostCommitOutboxClaimTransitions.Claim(HistoryItem(), new("owner", Now, TimeSpan.FromMinutes(1), 1)), Scope);
        var refusal = await Assert.ThrowsAsync<RuntimePostCommitSourceBoundaryException>(() => services.GetRequiredService<IRuntimePostCommitIntentDispatcher>()
            .DispatchAsync(source).AsTask());
        Assert.Equal(unavailable, refusal.IsUnavailable); Assert.Same(original, refusal.InnerException);
        Assert.Empty(services.GetRequiredService<HistoryOwner>().Contexts);
    }

    [Fact]
    public async Task Requested_cancellation_propagates_before_origin_qualification_or_delivery()
    {
        using var provider = HistoryServices().BuildServiceProvider(); using var scope = provider.CreateScope();
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var source = new PostCommitDeliveryContext(RuntimePostCommitOutboxClaimTransitions.Claim(HistoryItem(), new("owner", Now, TimeSpan.FromMinutes(1), 1)), Scope);
        var failure = await Assert.ThrowsAsync<OperationCanceledException>(() => scope.ServiceProvider.GetRequiredService<IRuntimePostCommitIntentDispatcher>()
            .DispatchAsync(source, cancellation.Token).AsTask());
        Assert.Equal(cancellation.Token, failure.CancellationToken);
        Assert.Empty(scope.ServiceProvider.GetRequiredService<HistoryOwner>().Contexts);
    }

    private static ServiceCollection HistoryServices()
    {
        var services = Services(); services.RemoveAll<RuntimePostCommitIntentHandlerContribution>();
        services.AddSingleton<HistoryOwner>(); services.AddSingleton<HistoryReader>(); services.AddSingleton<HistoryWriter>();
        services.AddSingleton<IRuntimeHistoryStore>(provider => provider.GetRequiredService<HistoryReader>());
        services.AddSingleton<IRuntimeHistoryObservationWriter>(provider => provider.GetRequiredService<HistoryWriter>());
        // Intentionally legacy contribution: the retained origin, not today's declaration, requires context/qualification.
        services.AddRuntimePostCommitIntentHandler<HistoryHandler>(Kind);
        return services;
    }
    private static RuntimePostCommitOutboxItem HistoryItem(bool withScope = true) => new("source:send",
        new("send", "publisher", Kind, Now, null, "key", null) { HistoryCausation = new("event", "publisher", "source:send", "command", "work") },
        RuntimePostCommitOutboxStatus.Pending, Now, Now, null, 0, null, null, null, null, null, 0, null, null, withScope ? Scope : null, null);
    private sealed class HistoryOwner
    {
        internal bool Supported = true; internal int Resolutions;
        internal List<PostCommitDeliveryContext> Contexts { get; } = [];
        internal List<RuntimePostCommitIntent> Intents { get; } = [];
    }
    private sealed class HistoryHandler(HistoryOwner owner) : IRuntimePostCommitDurableSourceIntentHandler
    {
        public bool CanRecordHistory(IRuntimeHistoryObservationWriter writer, IRuntimeHistoryStore reader) => owner.Supported;
        public ValueTask HandleAsync(PostCommitDeliveryContext source, CancellationToken cancellationToken = default)
        { owner.Contexts.Add(source); return ValueTask.CompletedTask; }
        public ValueTask HandleAsync(RuntimePostCommitIntent intent, CancellationToken cancellationToken = default)
        { owner.Intents.Add(intent); return ValueTask.CompletedTask; }
    }
    private sealed class HistoryWriter(HistoryReader reader) : IRuntimeHistoryObservationWriter
    {
        internal bool Paired = true; internal Exception? Failure; internal int Writes;
        public bool CanRecordHistory(IRuntimeHistoryStore selectedStore)
        { if (Failure is { } failure) throw failure; return Paired && ReferenceEquals(selectedStore, reader); }
        public ValueTask<RuntimeHistoryObservationResult> RecordAsync(RuntimeHistoryObservation observation, CancellationToken cancellationToken = default)
        { Writes++; throw new InvalidOperationException("Qualification must not manufacture observations."); }
    }
    private sealed class HistoryReader : IRuntimeHistoryStore
    {
        internal int Reads;
        private ValueTask<T> Read<T>() { Reads++; throw new InvalidOperationException("Diagnostic history is not execution authority."); }
        public ValueTask<RuntimeHistoryHead?> FindHeadAsync(string workflowExecutionId, CancellationToken cancellationToken = default) => Read<RuntimeHistoryHead?>();
        public ValueTask<RuntimeHistoryPage> ReadPageAsync(RuntimeHistoryQuery query, CancellationToken cancellationToken = default) => Read<RuntimeHistoryPage>();
        public ValueTask<RuntimeHistoryEvent?> FindEventAsync(string workflowExecutionId, string historyEventId, CancellationToken cancellationToken = default) => Read<RuntimeHistoryEvent?>();
        public ValueTask<RuntimeHistoryBookmarkPage> ListBookmarksAsync(RuntimeHistoryBookmarkQuery query, CancellationToken cancellationToken = default) => Read<RuntimeHistoryBookmarkPage>();
        public ValueTask<RuntimeHistoryBookmark?> FindBookmarkAsync(string workflowExecutionId, string bookmarkId, CancellationToken cancellationToken = default) => Read<RuntimeHistoryBookmark?>();
    }
}
