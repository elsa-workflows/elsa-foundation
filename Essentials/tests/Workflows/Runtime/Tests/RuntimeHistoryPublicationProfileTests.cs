using System.Text.Json;
using Elsa.Activities.Primitives;
using Elsa.Activities.Primitives.Activities;
using Elsa.Activities.Primitives.Services;
using Elsa.Cluster.Core.Contracts;
using Elsa.Cluster.Core.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Contracts;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.Services.Bookmarks;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Elsa.Workflows.Runtime.Services.Executions;
using Elsa.Workflows.Runtime.Services.History;
using Elsa.Workflows.Runtime.Services.Triggers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

// Actual public publication feature plus internal native history; seeded publisher and empty cohort, not authored/recipient/host/provider proof.
public sealed class RuntimeHistoryPublicationProfileTests
{
    private static readonly DateTimeOffset At = DateTimeOffset.UnixEpoch.AddDays(1);
    private static readonly PersistenceScope Scope = new("publication-history");
    private static readonly WorkflowExecutableIdentity Pin = new("artifact", "definition", "version", "1", "hash");
    private const string Kind = PublishStimulusConstants.PublishStimulusIntentKind;
    private static readonly string[] PublicationKinds = ["StimulusPublicationCommitted", "StimulusRoutingAttemptObserved", "StimulusRoutingDispositionObserved"];

    [Theory]
    [InlineData(false, false)] [InlineData(false, true)] [InlineData(true, false)] [InlineData(true, true)]
    public async Task Selected_native_chain_records_original_publication_and_actual_empty_cohort_disposition(bool decorated, bool disableNewAdmissions)
    {
        using var f = new Fixture(decorated);
        var policy = RuntimeHistoryRegistration.ResolvePolicy(f.Services, "publisher")!;
        Assert.All(PublicationKinds, kind => Assert.Contains(kind, policy.SupportedKinds));
        Assert.Single(f.Services.GetServices<RuntimeHistorySourceSupport>().Where(owner => owner.SourceIntentKind == Kind));
        var source = f.Services.GetRequiredService<IRuntimeHistorySourceExtension>();
        Assert.Single(f.Services.GetServices<IRuntimeCheckpointCommitEnricher>().Where(owner => owner.CanSupplyHistorySource(Kind, source)));
        if (disableNewAdmissions)
        {
            f.Options.Enabled = false;
            var admission = RuntimeHistoryRegistration.ResolveAdmission(f.Services, "later");
            Assert.False(admission.Enabled); Assert.Null(admission.Policy);
        }
        await f.SeedAsync(policy);
        var pending = await f.PublishAsync();
        var published = Assert.Single((await f.Reader.ReadPageAsync(new("publisher"))).Items, item => item.Name == "StimulusPublicationCommitted");
        Assert.Equal(published.HistoryEventId, pending.Intent.HistoryCausation!.HistoryEventId);
        Assert.Equal("publisher", pending.Intent.HistoryCausation.WorkflowExecutionId);
        Assert.Equal(pending.OutboxItemId, pending.Intent.HistoryCausation.DeliveryId);
        Assert.Equal("publisher-command", pending.Intent.HistoryCausation.CommandId);
        Assert.Equal(Scope, pending.StimulusDeliverySourceScope);
        var processed = await f.Services.GetRequiredService<IRuntimePostCommitOutboxProcessor>().ProcessAsync(new(10));
        Assert.Equal(1, processed.DeliveredCount);
        var delivered = (await f.Writer.FindDurableAsync(pending.OutboxItemId))!;
        Assert.Equal(RuntimePostCommitOutboxStatus.Delivered, delivered.Status);
        Assert.Empty(delivered.RecipientSnapshot!.Starts); Assert.Empty(delivered.RecipientSnapshot.Resumes);
        var page = await f.Reader.ReadPageAsync(new("publisher"));
        var attempt = Assert.Single(page.Items, item => item.Name == "StimulusRoutingAttemptObserved");
        var disposition = Assert.Single(page.Items, item => item.Name == "StimulusRoutingDispositionObserved");
        Assert.Equal(published.HistoryEventId, attempt.Causation!.HistoryEventId);
        Assert.Equal(attempt.HistoryEventId, disposition.Causation!.HistoryEventId);
        Assert.Equal("saved-cohort-routing-acknowledged-observed", disposition.Metadata["history.reasonCode"]);
        Assert.True(published.Sequence < attempt.Sequence); Assert.True(attempt.Sequence < disposition.Sequence);
        Assert.All(new[] { attempt, disposition }, item => Assert.Equal(RuntimeHistoryAuthority.DeliveryObservation, item.Authority));
        Assert.DoesNotContain(page.Items, item => item.Name is "WorkflowStarted" or "DeliveryReceived" or "BookmarkConsumed");
        Assert.DoesNotContain("private-value", JsonSerializer.Serialize(page.Items));
        Assert.False(page.Head!.CapturedFromStart); Assert.True(policy.IsEquivalentTo(page.Head.Policy));
        Assert.Equal(0, f.Actors.Calls);
    }

    [Theory]
    [InlineData("input")] [InlineData("handler")] [InlineData("router")]
    [InlineData("start")] [InlineData("resume")] [InlineData("adapter")] [InlineData("native-source")]
    public async Task Declaration_alone_cannot_advertise_a_replaced_or_missing_actual_source_component(string missing)
    {
        using var f = new Fixture(change: services =>
        {
            switch (missing)
            {
                case "input":
                    var descriptor = Assert.Single(services.Where(item => item.ServiceType == typeof(IRuntimeCheckpointCommitEnricher) &&
                        item.ImplementationType?.Name == "PublishStimulusHistoryEnricher"));
                    services.Remove(descriptor); break;
                case "handler": services.AddScoped<PublishStimulusExecutor>(sp => new(sp.GetRequiredService<IStimulusRouter>())); break;
                case "router": services.AddSingleton<IStimulusRouter, LegacyRouter>(); break;
                case "start": services.AddSingleton<IWorkflowStartDispatcher, LegacyStart>(); break;
                case "resume": services.AddScoped<IBookmarkResumeDispatcher>(sp => new LegacySavedResume(BookmarkResumeDispatcher.Create(sp))); break;
                case "adapter": services.RemoveAll<IStimulusDeliveryIntentAdapter>(); break;
                case "native-source": services.AddSingleton<IStimulusDeliveryStore, LegacySource>(); break;
            }
        });
        var newPolicy = RuntimeHistoryRegistration.ResolvePolicy(f.Services, "new-publisher")!;
        Assert.All(PublicationKinds, kind => Assert.DoesNotContain(kind, newPolicy.SupportedKinds));
        var retained = new RuntimeHistoryCapturePolicy(newPolicy.PolicyId, newPolicy.Version, true, newPolicy.SupportedKinds.Concat(PublicationKinds).ToArray());
        Assert.False(f.Services.GetServices<IRuntimeCheckpointCommitEnricher>().OfType<RuntimeHistoryCapture>().Single().CanCapture(retained));
        await f.SeedAsync(retained);
        var failure = await Assert.ThrowsAsync<RuntimeSchedulerWorkUnavailableException>(() => f.PublishAsync());
        Assert.Equal("runtime.history.capture-unavailable", failure.Code);
        Assert.Empty(f.Writer.ListCommits()); Assert.Null(await f.Writer.FindDurableAsync(f.LastSourceId!));
        Assert.Equal(0, f.Actors.Calls);
    }

    [Fact]
    public async Task An_older_profile_keeps_its_exact_vocabulary_and_never_stamps_or_observes_publication()
    {
        using var f = new Fixture();
        var current = RuntimeHistoryRegistration.ResolvePolicy(f.Services, "publisher")!;
        var prior = new RuntimeHistoryCapturePolicy(current.PolicyId, current.Version, current.Required,
            current.SupportedKinds.Except(PublicationKinds, StringComparer.Ordinal).ToArray());
        await f.SeedAsync(prior);
        var pending = await f.PublishAsync();
        Assert.Null(pending.Intent.HistoryCausation);
        Assert.Equal(1, (await f.Services.GetRequiredService<IRuntimePostCommitOutboxProcessor>().ProcessAsync(new(10))).DeliveredCount);
        Assert.True(prior.IsEquivalentTo((await f.Services.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync("publisher"))!.HistoryCapturePolicy));
        Assert.DoesNotContain((await f.Reader.ReadPageAsync(new("publisher"))).Items, item => PublicationKinds.Contains(item.Name));
        Assert.Equal(0, f.Actors.Calls);
    }

    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)] [InlineData(6)]
    public async Task Every_nonempty_partial_publication_vocabulary_refuses_required_capture_before_persistence(int mask)
    {
        using var f = new Fixture();
        var current = RuntimeHistoryRegistration.ResolvePolicy(f.Services, "publisher")!;
        var partial = new RuntimeHistoryCapturePolicy(current.PolicyId, current.Version, true,
            current.SupportedKinds.Except(PublicationKinds, StringComparer.Ordinal).Concat(PublicationKinds.Where((_, index) => (mask & (1 << index)) != 0)).ToArray());
        Assert.False(f.Services.GetServices<IRuntimeCheckpointCommitEnricher>().OfType<RuntimeHistoryCapture>().Single().CanCapture(partial));
        await f.SeedAsync(partial);
        var refusal = await Assert.ThrowsAsync<RuntimeSchedulerWorkUnavailableException>(() => f.PublishAsync());
        Assert.Equal("runtime.history.capture-unavailable", refusal.Code);
        Assert.Empty(f.Writer.ListCommits()); Assert.Null(await f.Writer.FindDurableAsync(f.LastSourceId!)); Assert.Equal(0, f.Actors.Calls);
    }

    [Theory]
    [InlineData("logical")] [InlineData("raw")] [InlineData("remove-fact")]
    public async Task A_later_contributor_cannot_accept_publication_without_its_exact_required_fact_and_origin(string mode)
    {
        using var f = new Fixture(change: services => services.AddScoped<IRuntimeCheckpointCommitEnricher>(sp =>
            new LatePublication(mode, false, sp.GetServices<RuntimePostCommitIntentHandlerContribution>().ToArray())));
        await f.SeedAsync(RuntimeHistoryRegistration.ResolvePolicy(f.Services, "publisher")!);
        var refusal = await Assert.ThrowsAsync<RuntimeCheckpointCommitValidationException>(() => f.PublishAsync());
        Assert.Contains("required logical history", refusal.Message);
        Assert.Empty(f.Writer.ListCommits()); Assert.Null(await f.Writer.FindDurableAsync(f.LastSourceId!)); Assert.Equal(0, f.Actors.Calls);
    }

    [Fact]
    public async Task A_real_quiescence_flush_keeps_the_original_publication_group_and_raw_source()
    {
        using var f = new Fixture(decorated: true);
        await f.SeedAsync(RuntimeHistoryRegistration.ResolvePolicy(f.Services, "publisher")!);
        await using var drain = f.Services.GetRequiredService<IRuntimeCoalescingDrainScopeFactory>().Begin("publisher", new(PersistenceMode.Balanced, RunHasRecord: true));
        var pending = await f.PublishAsync();
        Assert.Null(await f.Writer.FindDurableAsync(pending.OutboxItemId));
        await drain.FlushAtQuiescenceAsync();
        var saved = (await f.Writer.FindDurableAsync(pending.OutboxItemId))!;
        Assert.Equal(pending.Intent.HistoryCausation, saved.Intent.HistoryCausation);
        var published = Assert.Single((await f.Reader.ReadPageAsync(new("publisher"))).Items, item => item.Name == "StimulusPublicationCommitted");
        Assert.Equal(published.HistoryEventId, saved.Intent.HistoryCausation!.HistoryEventId);
        Assert.Single(f.Writer.ListCommits()); Assert.Equal(0, f.Actors.Calls);
    }

    [Theory]
    [InlineData("logical")] [InlineData("raw")]
    public async Task A_physical_flush_cannot_introduce_a_new_publication_outside_its_captured_logical_groups(string mode)
    {
        using var f = new Fixture(decorated: true, change: services => services.AddScoped<IRuntimeCheckpointCommitEnricher>(sp =>
            new LatePublication(mode, true, sp.GetServices<RuntimePostCommitIntentHandlerContribution>().ToArray())));
        await f.SeedAsync(RuntimeHistoryRegistration.ResolvePolicy(f.Services, "publisher")!);
        await using var drain = f.Services.GetRequiredService<IRuntimeCoalescingDrainScopeFactory>().Begin("publisher", new(PersistenceMode.Balanced, RunHasRecord: true));
        var pending = await f.PublishAsync();
        var refusal = await Assert.ThrowsAsync<RuntimeCheckpointCommitValidationException>(() => drain.FlushAtQuiescenceAsync().AsTask());
        Assert.Contains("required logical history", refusal.Message);
        Assert.Empty(f.Writer.ListCommits()); Assert.Null(await f.Writer.FindDurableAsync(pending.OutboxItemId)); Assert.Equal(0, f.Actors.Calls);
    }

    [Theory]
    [InlineData("rebind")] [InlineData("rewrite")] [InlineData("remove-source")]
    public async Task Physical_flush_preserves_the_exact_original_send_inventory_even_when_an_existing_event_is_reused(string mode)
    {
        using var f = new Fixture(decorated: true, change: services => services.AddScoped<IRuntimeCheckpointCommitEnricher>(sp =>
            new LatePublication(mode, true, sp.GetServices<RuntimePostCommitIntentHandlerContribution>().ToArray())));
        await f.SeedAsync(RuntimeHistoryRegistration.ResolvePolicy(f.Services, "publisher")!);
        await using var drain = f.Services.GetRequiredService<IRuntimeCoalescingDrainScopeFactory>().Begin("publisher", new(PersistenceMode.Balanced, RunHasRecord: true));
        var pending = await f.PublishAsync();
        var refusal = await Assert.ThrowsAsync<RuntimeCheckpointCommitValidationException>(() => drain.FlushAtQuiescenceAsync().AsTask());
        Assert.Contains("original logical history sources", refusal.Message);
        Assert.Empty(f.Writer.ListCommits()); Assert.Null(await f.Writer.FindDurableAsync(pending.OutboxItemId)); Assert.Equal(0, f.Actors.Calls);
    }

    [Theory]
    [InlineData(false, "io")] [InlineData(true, "io")] [InlineData(false, "malformed-io")] [InlineData(true, "malformed-io")]
    [InlineData(false, "argument")] [InlineData(true, "argument")] [InlineData(false, "cancelled")] [InlineData(true, "cancelled")]
    public void Direct_public_qualification_normalizes_provider_failure_without_losing_kind_or_cause(bool router, string failureKind)
    {
        Exception cause = failureKind switch
        { "io" => new IOException("provider"), "malformed-io" => new InvalidDataException("malformed", new IOException("nested provider")), "argument" => new ArgumentException("invalid"), _ => new OperationCanceledException("cancelled") };
        var writer = new FailingWriter(cause);
        using var f = new Fixture(change: services => services.AddSingleton<IRuntimeHistoryObservationWriter>(writer));
        var failure = Assert.Throws<RuntimePostCommitSourceBoundaryException>(() =>
        {
            if (router) f.Services.GetRequiredService<IStimulusRouter>().CanRecordHistory(Kind, writer, f.Reader);
            else f.Services.GetRequiredService<IWorkflowStartDispatcher>().CanPreserveHistoryCausation(Kind, writer, f.Reader);
        });
        Assert.Same(cause, failure.InnerException); Assert.Equal(Kind, failure.IntentKind); Assert.Null(failure.SourceOutboxItemId);
        Assert.Equal(failureKind is "io" or "cancelled", failure.IsUnavailable); Assert.Empty(f.Writer.ListCommits()); Assert.Equal(0, f.Actors.Calls);
    }

    [Fact]
    public void Actual_router_and_start_transport_require_the_selected_pair_and_owning_adapter_without_activation_or_writes()
    {
        using var f = new Fixture(); using var other = new Fixture();
        var writer = f.Services.GetRequiredService<IRuntimeHistoryObservationWriter>();
        var router = f.Services.GetRequiredService<IStimulusRouter>(); var start = f.Services.GetRequiredService<IWorkflowStartDispatcher>();
        Assert.True(router.CanRecordHistory(Kind, writer, f.Reader)); Assert.True(start.CanPreserveHistoryCausation(Kind, writer, f.Reader));
        Assert.False(router.CanRecordHistory(Kind, writer, other.Reader)); Assert.False(start.CanPreserveHistoryCausation(Kind, writer, other.Reader));
        Assert.False(router.CanRecordHistory("another-owner", writer, f.Reader)); Assert.False(start.CanPreserveHistoryCausation("another-owner", writer, f.Reader));
        Assert.Empty(f.Writer.ListCommits()); Assert.Empty(other.Writer.ListCommits()); Assert.Equal(0, f.Actors.Calls);
    }

    [Fact]
    public void Original_public_constructors_do_not_qualify_history_transport_from_compatible_stores_alone()
    {
        using var f = new Fixture(); var sp = f.Services; var writer = sp.GetRequiredService<IRuntimeHistoryObservationWriter>();
        IWorkflowStartDispatcher start = new WorkflowStartDispatcher(sp.GetRequiredService<IWorkflowExecutableStore>(),
            sp.GetRequiredService<IWorkflowExecutableSourceReferenceStore>(), f.Actors, sp.GetRequiredService<IRuntimeExecutionIdGenerator>());
        IStimulusRouter router = new StimulusRouter(sp.GetRequiredService<IWorkflowTriggerBindingStore>(), sp.GetRequiredService<IGlobalBookmarkStimulusLookup>(),
            sp.GetRequiredService<IWorkflowStartDispatcher>(), sp.GetRequiredService<IBookmarkResumeDispatcher>());
        Assert.False(start.CanPreserveHistoryCausation(Kind, writer, f.Reader)); Assert.False(router.CanRecordHistory(Kind, writer, f.Reader));
        Assert.Equal(0, f.Actors.Calls); Assert.Empty(f.Writer.ListCommits());
    }

    private sealed class Fixture : IDisposable
    {
        private readonly ServiceProvider _provider; private readonly IServiceScope _scope;
        internal IServiceProvider Services => _scope.ServiceProvider;
        internal RuntimeHistoryOptions Options { get; } = new();
        internal string? LastSourceId { get; private set; }
        internal Actors Actors { get; } = new();
        internal InMemoryRuntimeCheckpointCommitStore Writer => Services.GetRequiredService<InMemoryRuntimeCheckpointCommitStore>();
        internal IRuntimeHistoryStore Reader => Services.GetRequiredService<IRuntimeHistoryStore>();
        internal Fixture(bool decorated = false, Action<ServiceCollection>? change = null)
        {
            var services = new ServiceCollection(); services.AddLogging(); services.AddSingleton<TimeProvider>(new FakeTimeProvider(At));
            services.AddSingleton<IPersistenceAccessContextAccessor, Access>(); services.AddSingleton<IWorkflowExecutionActorProvider>(Actors);
            services.AddSingleton<IWorkflowExecutableRootWriteLeaseManager>(PassThroughWorkflowExecutableRootWriteLeaseManager.Instance);
            services.AddSingleton<ISchemaDormancyCheck, ControlledSchema>();
            new ActivitiesPrimitivesFeature().ConfigureServices(services); new ActivitiesPrimitivesFeature().ConfigureServices(services);
            services.AddWorkflowRuntime(); services.AddWorkflowStimulusRouter(); if (decorated) services.AddPersistenceModes();
            RuntimeHistoryRegistration.AddServices(services, Options);
            services.Configure<RuntimeRecoveryContinuationOptions>(options => options.SigningKey = "publication-profile-tests-key-at-least-32-bytes");
            change?.Invoke(services);
            _provider = services.BuildServiceProvider(validateScopes: true); _scope = _provider.CreateScope();
        }
        internal async Task SeedAsync(RuntimeHistoryCapturePolicy policy)
        {
            await Services.GetRequiredService<IWorkflowExecutableStore>().SaveAsync(new(Pin,
                new("node", "authored", "test/activity", "1", "test", JsonSerializer.SerializeToElement(new { type = "test" }),
                    new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>()),
                new Dictionary<string, WorkflowExecutableResumeTarget>(), At, new Dictionary<string, string>(), IncidentStrategyBuiltIns.FaultReference));
            await Services.GetRequiredService<IWorkflowExecutionStateStore>().SaveAsync(new("publisher", Pin, WorkflowExecutionStatus.Suspended,
                null, At, At, At, null, null, null, Scope.Value, new Dictionary<string, string>())
                { HistoryCapturePolicy = policy, HistoryCaptureEnabledAtAdmission = true, Partition = new(Scope.Value) });
        }
        internal async Task<RuntimePostCommitOutboxItem> PublishAsync()
        {
            Services.GetRequiredService<IPublishStimulusStager>().StagePublishStimulus(new("publisher", "activity", "named", null,
                JsonSerializer.SerializeToElement(new { value = "private-value" }), false));
            var intent = Assert.Single(Services.GetRequiredService<IPublishStimulusStagingAccessor>().TakePublishStimuli("publisher", "activity"));
            var commit = new RuntimeCheckpointCommit("publication", new("checkpoint", RuntimeCheckpointNames.ActivityCompleted, "publisher", At, ["activity"],
                new Dictionary<string, string> { [RuntimeMetadataKeys.CommandId] = "publisher-command" }), new(null, null, [], [], [], [], []), [intent], new Dictionary<string, string>());
            LastSourceId = RuntimePostCommitOutboxItems.OutboxItemId(commit.CommitId, intent);
            var result = await Services.GetRequiredService<RuntimeCheckpointCommitter>().CommitAsync(commit);
            Assert.True(result.Succeeded);
            return (await Services.GetRequiredService<IPostCommitOutboxLookupStore>().FindAsync(LastSourceId))!;
        }
        public void Dispose() { _scope.Dispose(); _provider.Dispose(); }
    }
    private sealed class Access : IPersistenceAccessContextAccessor
    { public PersistenceAccessContext Current => PersistenceAccessContext.Scoped(Scope); }
    private sealed class Actors : IWorkflowExecutionActorProvider
    {
        internal int Calls;
        public WorkflowExecutionActorCapabilities Capabilities => WorkflowExecutionActorCapabilities.InProcessMailbox;
        public ValueTask<IWorkflowExecutionActor> GetAgentAsync(WorkflowExecutionActorActivationRequest request, CancellationToken cancellationToken = default)
        { Calls++; throw new InvalidOperationException("This empty-cohort qualification must not activate a recipient."); }
        public ValueTask PassivateAsync(WorkflowExecutionActorPassivationRequest request, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }
    private sealed class LegacyRouter : IStimulusRouter
    { public ValueTask<StimulusRoutingResult> RouteAsync(StimulusDispatchRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException(); }
    private sealed class LegacySource : IStimulusDeliveryStore
    {
        public ValueTask<PostCommitDeliveryContext> ReadSourceAsync(PostCommitDeliveryContext source, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<StimulusDeliverySnapshot> InstallAsync(PostCommitDeliveryContext source, StimulusDeliverySnapshot candidate, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
    private sealed class FailingWriter(Exception failure) : IRuntimeHistoryObservationWriter
    {
        public bool CanRecordHistory(IRuntimeHistoryStore selectedStore) => throw failure;
        public ValueTask<RuntimeHistoryObservationResult> RecordAsync(RuntimeHistoryObservation observation, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
    private sealed class LatePublication(string mode, bool onlyFlush, IReadOnlyCollection<RuntimePostCommitIntentHandlerContribution> contributions) : IRuntimeCheckpointCommitEnricher
    {
        public int Order => 1950;
        public ValueTask<RuntimeCheckpointCommit> EnrichAsync(RuntimeCheckpointCommit commit, CancellationToken cancellationToken = default)
        {
            if (commit.Checkpoint.Metadata.ContainsKey(RuntimeMetadataKeys.CoalescedFlush) != onlyFlush) return ValueTask.FromResult(commit);
            if (mode == "remove-fact") return ValueTask.FromResult(commit with { HistorySourceFacts = [] });
            if (mode == "remove-source") return ValueTask.FromResult(commit with
            { StateChanges = commit.StateChanges.WithPostCommitOutbox(commit.StateChanges.PostCommitOutbox.Where(change => change.State.Intent.Kind != Kind).ToArray()) });
            if (mode == "rewrite")
            {
                var row = Assert.Single(commit.StateChanges.PostCommitOutbox.Where(change => change.State.Intent.Kind == Kind)).State;
                var changed = new RuntimePostCommitIntent(row.Intent.IntentId, row.Intent.WorkflowExecutionId, Kind, row.Intent.RecordedAt,
                    row.Intent.ActivityExecutionId, "rewritten-key", row.Intent.Payload, row.Intent.Metadata) { HistoryCausation = row.Intent.HistoryCausation };
                var replacement = new RuntimePostCommitOutboxItem(row.OutboxItemId, changed, row.Status, row.RecordedAt, row.AvailableAt,
                    row.RetryPolicy, row.DeliveryAttemptCount, row.DeliveringOwnerId, row.DeliveryStartedAt, row.DeliveredAt, row.LastFailureMessage,
                    row.Metadata, row.DeliveryFencingToken, row.DeliveryVisibleAfter, row.StimulusDeliveryProtocolVersion, row.StimulusDeliverySourceScope, row.RecipientSnapshot);
                return ValueTask.FromResult(commit with { StateChanges = commit.StateChanges.WithPostCommitOutbox(commit.StateChanges.PostCommitOutbox
                    .Select(change => change.StateId == row.OutboxItemId ? change with { State = replacement } : change).ToArray()) });
            }
            var original = commit.PostCommitIntents.Concat(commit.StateChanges.PostCommitOutbox.Select(change => change.State.Intent)).First(intent => intent.Kind == Kind);
            var late = new RuntimePostCommitIntent("late", commit.WorkflowExecutionId, Kind, At, "activity", "late-key", original.Payload);
            if (mode == "rebind") late = late with { HistoryCausation = original.HistoryCausation! with
                { DeliveryId = RuntimePostCommitOutboxIdentity.CreateLogicalValue(commit.CommitId, late.IntentId) } };
            if (mode == "logical") return ValueTask.FromResult(commit with { PostCommitIntents = commit.PostCommitIntents.Append(late).ToArray() });
            var pending = commit.PostCommitIntents.Append(late).Select(intent => RuntimePostCommitOutboxItems.CreatePending(commit, intent, contributions, Scope));
            var changes = commit.StateChanges.PostCommitOutbox.Concat(pending.Select(item =>
                new RuntimeStateChange<RuntimePostCommitOutboxItem>(item.OutboxItemId, RuntimeStateChangeOperation.Upsert, item, new Dictionary<string, string>()))).ToArray();
            return ValueTask.FromResult(commit with { PostCommitIntents = [], StateChanges = commit.StateChanges.WithPostCommitOutbox(changes) });
        }
    }
    private sealed class LegacyStart : IWorkflowStartDispatcher
    {
        public ValueTask<WorkflowExecutionStartDispatchResult> DispatchAsync(WorkflowExecutionStartDispatchRequest request,
            WorkflowExecutableReferenceScope requiredScope = WorkflowExecutableReferenceScope.Published, WorkflowExecutionCommandDispatchOptions? dispatchOptions = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
    private sealed class LegacySavedResume(IBookmarkResumeDispatcher inner) : IBookmarkResumeDispatcher
    {
        public bool CanRecordHistory(IRuntimeHistoryObservationWriter writer, IRuntimeHistoryStore reader) => inner.CanRecordHistory(writer, reader);
        public ValueTask<BookmarkResumeDispatchResult> DispatchAsync(BookmarkResumeDispatchRequest request, WorkflowExecutionCommandDispatchOptions? options = null,
            CancellationToken cancellationToken = default) => inner.DispatchAsync(request, options, cancellationToken);
    }
    // Deliberately controlled schema availability for these component controls; no provider/migration/fleet qualification.
    private sealed class ControlledSchema : ISchemaDormancyCheck
    {
        public SchemaAvailability Evaluate(IEnumerable<SchemaVersionRequirement> requirements) => SchemaAvailability.Available;
        public ValueTask<SchemaAvailability> EvaluateAsync(IEnumerable<SchemaVersionRequirement> requirements, CancellationToken cancellationToken = default) => ValueTask.FromResult(Evaluate(requirements));
        public ValueTask EnsureAvailableAsync(IEnumerable<SchemaVersionRequirement> requirements, string? featureId = null, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public IReadOnlyList<SchemaFamilyObservation> Observe() => [];
        public ValueTask<IReadOnlyList<SchemaFamilyObservation>> ReadStatusAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult<IReadOnlyList<SchemaFamilyObservation>>([]);
    }
}
