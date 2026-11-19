using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Configuration;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.Services.Bookmarks;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Elsa.Workflows.Runtime.Services.Triggers;
using Elsa.Workflows.Runtime.Services.Executions;
using Elsa.Activities.Primitives;
using Elsa.Activities.Primitives.Activities;
using Elsa.Activities.Primitives.Services;
using Elsa.Cluster.Core.Contracts;
using Elsa.Cluster.Core.Models;
using Elsa.Cluster.InProcess;
using Elsa.Primitives.Exceptions;
using Elsa.Workflows.Runtime.Services.Executables;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

/// <summary>Real feature, staging, committer, native provider, processor, router and default dispatchers; recording mailbox only.</summary>
public sealed class PublishStimulusSourcePipelineTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 6, 0, 0, TimeSpan.Zero);
    private static readonly PersistenceScope Scope = new("tenant");
    private static readonly WorkflowExecutableIdentity Pin = new("artifact", "definition", "version", "1.0.0", "hash");

    [Theory]
    [InlineData(false, false)] [InlineData(false, true)] [InlineData(true, false)] [InlineData(true, true)]
    public async Task Actual_publisher_pipeline_commits_protocol_without_recipients_then_saves_complete_cohort_before_effect(bool coalescing, bool local)
    {
        await using var harness = new Harness(coalescing); await harness.SeedMaterial();
        await harness.Bind("binding-a"); await harness.Bind("binding-b"); await harness.Wait("publisher", "own"); await harness.Wait("existing", "wait");
        var pending = await harness.Commit(local);
        Assert.Equal(1, pending.StimulusDeliveryProtocolVersion); Assert.Equal(Scope, pending.StimulusDeliverySourceScope);
        Assert.Null(pending.RecipientSnapshot); Assert.Empty(harness.Actors.Attempts);
        harness.Clock.Advance(TimeSpan.FromSeconds(1)); await harness.Wait("delayed", "late");
        harness.Actors.BeforeEnqueue = async _ =>
        {
            var source = (await harness.Find(pending.OutboxItemId))!; var saved = source.RecipientSnapshot!;
            Assert.Equal(1, saved.RetentionAcceptanceVersion); Assert.Equal(local ? 0 : 2, saved.Starts.Count); Assert.Equal(local ? 1 : 3, saved.Resumes.Count);
        };
        var result = await harness.Processor.ProcessAsync(new(10)); Assert.Equal(1, result.DeliveredCount);
        var delivered = (await harness.Find(pending.OutboxItemId))!; Assert.Equal(RuntimePostCommitOutboxStatus.Delivered, delivered.Status);
        Assert.Equal(local ? 1 : 4, harness.Actors.Accepted.Count);
        Assert.All(harness.Actors.Accepted.Where(item => item.Command.Kind == WorkflowExecutionCommandKind.ResumeBookmark), item =>
        {
            var payload = item.Command.Payload!.Value.Deserialize<RuntimeResumeBookmarkCommandPayload>()!;
            Assert.NotNull(payload.ExpectedBookmarkResume); Assert.Equal("immutable-value", payload.Input!.Value.GetProperty("value").GetString());
        });
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Actual_source_boundary_failure_leaves_claim_and_attempt_history_owed_then_repairs(bool coalescing)
    {
        await using var harness = new Harness(coalescing); await harness.SeedMaterial(); await harness.Bind("missing", "missing-artifact");
        var original = await harness.Commit();
        var refusal = await Assert.ThrowsAsync<RuntimePostCommitSourceBoundaryException>(() => harness.Processor.ProcessAsync(new(10)).AsTask());
        Assert.True(refusal.IsUnavailable); Assert.IsType<StimulusDeliveryBoundaryException>(refusal.InnerException);
        var held = (await harness.Find(original.OutboxItemId))!;
        Assert.Equal(RuntimePostCommitOutboxStatus.Delivering, held.Status); Assert.Equal(0, held.DeliveryAttemptCount);
        Assert.Equal(original.LastFailureMessage, held.LastFailureMessage); Assert.Equal(original.Intent, held.Intent);
        Assert.Null(held.RecipientSnapshot); Assert.Empty(harness.Actors.Attempts);
        await harness.Bindings.DeleteByArtifactAsync("missing-artifact"); await harness.Bind("repaired");
        harness.Clock.Advance(TimeSpan.FromMinutes(10));
        Assert.Equal(1, (await harness.Processor.ProcessAsync(new(10))).DeliveredCount); Assert.Single(harness.Actors.Accepted);
        Assert.Equal(1, (await harness.Find(original.OutboxItemId))!.DeliveryAttemptCount);
    }

    [Theory]
    [InlineData("missing", false)] [InlineData("missing", true)]
    [InlineData("unobserved", false)] [InlineData("unobserved", true)]
    [InlineData("seed", false)] [InlineData("seed", true)]
    [InlineData("held", false)] [InlineData("held", true)]
    [InlineData("not-adopted", false)] [InlineData("not-adopted", true)]
    public async Task Actual_publish_checkpoint_refuses_shared_schema_dormancy_before_any_durable_write(string schema, bool materialized)
    {
        await using var harness = new Harness(schema: schema); await harness.SeedMaterial();
        await Assert.ThrowsAsync<SchemaDormancyRefusedException>(() => harness.Commit(materialized: materialized));
        Assert.Empty(harness.Store.ListCommits()); Assert.Empty(harness.Actors.Attempts);
        Assert.Empty((await harness.Processor.ProcessAsync(new(10))).Items);
    }

    [Theory]
    [InlineData("missing", false)] [InlineData("missing", true)]
    [InlineData("held", false)] [InlineData("held", true)]
    [InlineData("finalized", false)] [InlineData("finalized", true)]
    public async Task Late_enricher_publication_is_checked_on_final_envelope_before_acceptance(string schema, bool materialized)
    {
        await using var harness = new Harness(schema: schema, laterEnricher: true); await harness.SeedMaterial();
        if (schema == "finalized")
        {
            var source = await harness.Commit(materialized: materialized, late: true);
            Assert.Equal(1, source.StimulusDeliveryProtocolVersion); Assert.Null(source.RecipientSnapshot);
            Assert.Equal(1, (await harness.Processor.ProcessAsync(new(10))).DeliveredCount);
        }
        else
        {
            await Assert.ThrowsAsync<SchemaDormancyRefusedException>(() => harness.Commit(materialized: materialized, late: true));
            Assert.Empty(harness.Store.ListCommits()); Assert.Empty(harness.Actors.Attempts);
        }
    }

    [Fact]
    public async Task Materialized_publication_is_not_silently_dropped_by_skip_policy()
    {
        await using var harness = new Harness(skip: true); await harness.SeedMaterial();
        Assert.Null(await harness.Commit(materialized: true)); Assert.False(harness.CommitResult!.Succeeded);
        Assert.Equal(RuntimeCheckpointCommitFailureCodes.SkipHasPostCommitWork, harness.CommitResult.FailureCode);
        Assert.Empty(harness.Store.ListCommits()); Assert.Empty(harness.Actors.Attempts);
    }

    [Fact]
    public async Task Unrepresented_materialized_publication_refuses_instead_of_disappearing_from_mixed_envelope()
    {
        await using var harness = new Harness(); await harness.SeedMaterial();
        await Assert.ThrowsAsync<RuntimeCheckpointCommitValidationException>(() => harness.Commit(materialized: true, mixed: true));
        Assert.Empty(harness.Store.ListCommits()); Assert.Empty(harness.Actors.Attempts);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Duplicate_representations_are_checked_then_only_equivalent_work_is_folded(bool conflicting)
    {
        await using var harness = new Harness(); await harness.SeedMaterial();
        if (conflicting)
        {
            await Assert.ThrowsAsync<RuntimeCheckpointCommitValidationException>(() => harness.Commit(materialized: true, mixed: true, duplicate: true, conflicting: true));
            Assert.Empty(harness.Store.ListCommits()); Assert.Empty(harness.Actors.Attempts);
        }
        else
        {
            var source = await harness.Commit(materialized: true, mixed: true, duplicate: true);
            Assert.Single(harness.CommitResult!.PendingPostCommitWorkIds); Assert.Single(Assert.Single(harness.Store.ListCommits()).Commit.StateChanges.PostCommitOutbox);
            Assert.Equal(1, (await harness.Processor.ProcessAsync(new(10))).DeliveredCount);
            Assert.Equal(RuntimePostCommitOutboxStatus.Delivered, (await harness.Find(source.OutboxItemId))!.Status);
        }
    }

    private sealed class SkipPolicy : IRuntimeCheckpointPersistencePolicy
    {
        public ValueTask<RuntimeCheckpointPersistenceDecision> DecideAsync(RuntimeCheckpoint checkpoint, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new RuntimeCheckpointPersistenceDecision(RuntimeCheckpointPersistenceMode.Skip, "Constructed component skip policy."));
    }

    private sealed class LateEnricher(Func<RuntimeCheckpointCommit?> final) : IRuntimeCheckpointCommitEnricher
    {
        public int Order => int.MaxValue;
        public ValueTask<RuntimeCheckpointCommit> EnrichAsync(RuntimeCheckpointCommit commit, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(final() ?? commit);
    }

    internal sealed class Observed(string state) : IObservedSchemaFinalization
    {
        public SchemaFamilyObservation? Find(string family) => family != "RuntimePostCommitOutbox" ? null :
            new(family, "runtime", ["1.0.0", "2.0.0"], state is "held" or "seed" ? "1.0.0" : "2.0.0",
                state is "held" or "seed" or "not-adopted" ? "1.0.0" : "2.0.0", false, null,
                state == "held" ? [new("2.0.0", true, [new("2.0.0", "Constructed operator hold.", "component-owner", Now)], null)] : [], null, Now);
        public IReadOnlyList<SchemaFamilyObservation> Observe() => [Find("RuntimePostCommitOutbox")!];
        public ValueTask RefreshAsync(string family, TimeSpan maxAge, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask<IReadOnlyList<SchemaFamilyObservation>> ReadStatusAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(Observe());
    }
    private sealed class Access : IPersistenceAccessContextAccessor
    { public PersistenceAccessContext Current => PersistenceAccessContext.Scoped(Scope); }
    private sealed class CountingLookup(IGlobalBookmarkStimulusLookup inner) : IGlobalBookmarkStimulusLookup
    {
        public int Reads;
        public ValueTask<GlobalBookmarkStimulusLookupResult> FindWaitingAsync(GlobalBookmarkStimulusLookupRequest request, CancellationToken cancellationToken = default)
        { Reads++; return inner.FindWaitingAsync(request, cancellationToken); }
        public ValueTask<GlobalBookmarkStimulusLookupResult> FindWaitingByTypeAsync(GlobalBookmarkStimulusTypeLookupRequest request, CancellationToken cancellationToken = default) => inner.FindWaitingByTypeAsync(request, cancellationToken);
        public ValueTask<IReadOnlyCollection<string>> FindWaitingStimulusHashesByTypeAsync(GlobalBookmarkStimulusTypeLookupRequest request, CancellationToken cancellationToken = default) => inner.FindWaitingStimulusHashesByTypeAsync(request, cancellationToken);
    }
    private sealed class RecordingActors : IWorkflowExecutionActorProvider
    {
        private readonly HashSet<string> seen = new(StringComparer.Ordinal);
        public List<WorkflowExecutionCommandEnvelope> Attempts { get; } = [];
        public List<WorkflowExecutionCommandEnvelope> Accepted { get; } = [];
        public Func<WorkflowExecutionCommandEnvelope, Task>? BeforeEnqueue; public int ShedStarts; public bool LoseNextAck;
        public Func<WorkflowExecutionActorActivationRequest, Task>? BeforeAgent;
        public WorkflowExecutionCommandDispatchStatus? RefuseNextStart;
        public WorkflowExecutionActorCapabilities Capabilities => WorkflowExecutionActorCapabilities.InProcessMailbox;
        public async ValueTask<IWorkflowExecutionActor> GetAgentAsync(WorkflowExecutionActorActivationRequest request, CancellationToken cancellationToken = default)
        { if (BeforeAgent is { } before) { BeforeAgent = null; await before(request); } return new Actor(this, request.WorkflowExecutionId); }
        public ValueTask PassivateAsync(WorkflowExecutionActorPassivationRequest request, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        private sealed class Actor(RecordingActors owner, string executionId) : IWorkflowExecutionActor
        {
            public WorkflowExecutionActorDescriptor Descriptor { get; } = new(executionId, "recording:" + executionId, "recording-component-boundary", WorkflowExecutionActorStatus.Active, WorkflowExecutionActorCapabilities.InProcessMailbox, Now);
            public async ValueTask<WorkflowExecutionCommandDispatchResult> EnqueueAsync(WorkflowExecutionCommandEnvelope envelope, CancellationToken cancellationToken = default)
            {
                owner.Attempts.Add(envelope); if (owner.BeforeEnqueue is { } before) await before(envelope);
                if (envelope.Command.Kind == WorkflowExecutionCommandKind.Start && owner.RefuseNextStart is { } refusal)
                { owner.RefuseNextStart = null; return new(envelope.EnvelopeId, executionId, refusal, Now, "Constructed non-shed admission refusal."); }
                if (envelope.Command.Kind == WorkflowExecutionCommandKind.Start && owner.ShedStarts > 0)
                { owner.ShedStarts--; return new(envelope.EnvelopeId, executionId, WorkflowExecutionCommandDispatchStatus.Deferred, Now, "Constructed capacity refusal.", new Dictionary<string, string> { [RuntimeMetadataKeys.DispatchShed] = "true" }); }
                if (!owner.seen.Add(envelope.IdempotencyKey)) return new(envelope.EnvelopeId, executionId, WorkflowExecutionCommandDispatchStatus.Duplicate, Now);
                owner.Accepted.Add(envelope);
                if (owner.LoseNextAck) { owner.LoseNextAck = false; throw new IOException("Constructed acknowledgment loss after recorded mailbox acceptance."); }
                return new(envelope.EnvelopeId, executionId, WorkflowExecutionCommandDispatchStatus.Accepted, Now);
            }
        }
    }
    private sealed class Harness : IAsyncDisposable
    {
        private readonly ServiceProvider provider; private readonly IServiceScope scope; private RuntimeCheckpointCommit? lateEnvelope;
        public RuntimeCheckpointCommitResult? CommitResult { get; private set; }
        private IServiceProvider Services => scope.ServiceProvider;
        public FakeTimeProvider Clock { get; } = new(Now);
        public RecordingActors Actors { get; } = new();
        public IStimulusRouter Router => Services.GetRequiredService<IStimulusRouter>();
        public IBookmarkResumeDispatcher Resume => Services.GetRequiredService<IBookmarkResumeDispatcher>();
        public IWorkflowExecutionStateStore Executions => Services.GetRequiredService<IWorkflowExecutionStateStore>();
        public InMemoryRuntimeCheckpointCommitStore Store => Services.GetRequiredService<InMemoryRuntimeCheckpointCommitStore>();
        public IBookmarkStateStore Bookmarks => Services.GetRequiredService<IBookmarkStateStore>();
        public IWorkflowTriggerBindingStore Bindings => Services.GetRequiredService<IWorkflowTriggerBindingStore>();
        public IWorkflowExecutableSourceReferenceStore References => Services.GetRequiredService<IWorkflowExecutableSourceReferenceStore>();
        public InMemoryExecutionLivenessStateStore Ownership => (InMemoryExecutionLivenessStateStore)Services.GetRequiredService<IExecutionLivenessStateStore>();
        public CountingLookup Lookup => Services.GetRequiredService<CountingLookup>();
        public Harness(bool coalescing = false, string schema = "finalized", bool laterEnricher = false, bool skip = false)
        {
            var services = new ServiceCollection(); services.AddSingleton<TimeProvider>(Clock); services.AddSingleton<IPersistenceAccessContextAccessor, Access>();
            services.AddSingleton<IWorkflowExecutionActorProvider>(Actors);
            if (schema != "missing") services.AddSingleton<ISchemaDormancyCheck>(new SchemaDormancyCheck(observed: schema == "unobserved" ? null : new Observed(schema)));
            new ActivitiesPrimitivesFeature().ConfigureServices(services);
            if (skip) services.AddSingleton<IRuntimeCheckpointPersistencePolicy, SkipPolicy>();
            if (laterEnricher) services.AddSingleton<IRuntimeCheckpointCommitEnricher>(new LateEnricher(() => lateEnvelope));
            services.AddSingleton(new PersistenceModeOptions()); services.AddWorkflowRuntime(); if (coalescing) services.AddPersistenceModes();
            services.AddSingleton<IWorkflowTriggerBindingStore, InMemoryWorkflowTriggerBindingStore>();
            services.AddScoped<CountingLookup>(sp => new(new GlobalBookmarkStimulusLookup((IBookmarkStimulusIndex)sp.GetRequiredService<IBookmarkStateStore>(), sp.GetRequiredService<IWorkflowExecutionStateStore>())));
            services.AddScoped<IGlobalBookmarkStimulusLookup>(sp => sp.GetRequiredService<CountingLookup>()); services.AddWorkflowStimulusRouter();
            provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true }); scope = provider.CreateScope();
        }
        public async Task SeedMaterial()
        {
            _ = Store;
            await Services.GetRequiredService<IWorkflowExecutableStore>().SaveAsync(new(Pin,
                new ExecutableNode("node", "authored", "test/activity", "1.0.0", new RuntimeActivityDescriptor("test", RuntimeActivityDescriptor.InitialSchemaVersion, JsonSerializer.SerializeToElement(new { })), new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>()),
                new Dictionary<string, WorkflowExecutableResumeTarget> { ["resume"] = new("resume", "node", "test", new Dictionary<string, string>()) }, Now, new Dictionary<string, string>(), IncidentStrategyBuiltIns.FaultReference));
            await Services.GetRequiredService<IWorkflowExecutionStateStore>().SaveAsync(Execution("publisher"));
        }
        public async Task<RuntimePostCommitOutboxItem> Commit(bool local = false, bool materialized = false, bool late = false, bool mixed = false, bool duplicate = false, bool conflicting = false)
        {
            var buffer = Services.GetRequiredService<IPublishStimulusStagingAccessor>();
            Services.GetRequiredService<IPublishStimulusStager>().StagePublishStimulus(new("publisher", "publisher-activity", "named", null,
                JsonSerializer.SerializeToElement(new { value = "immutable-value" }), local));
            var intent = Assert.Single(buffer.TakePublishStimuli("publisher", "publisher-activity"));
            var commit = new RuntimeCheckpointCommit("publication", new("publication-checkpoint", RuntimeCheckpointNames.ActivityCompleted,
                "publisher", Now, ["publisher-activity"], new Dictionary<string, string>()), new(null, null, [], [], [], [], []),
                [intent], new Dictionary<string, string>());
            if (materialized)
            {
                var row = RuntimePostCommitOutboxItems.CreatePending(commit, intent, Services.GetServices<RuntimePostCommitIntentHandlerContribution>(), Scope);
                if (conflicting) row = new(row.OutboxItemId, row.Intent, row.Status, Now, Now, new(1, TimeSpan.FromSeconds(1)),
                    0, null, null, null, null, null, 0, null, 1, Scope, null);
                commit = commit with { PostCommitIntents = [], StateChanges = new(null, null, [], [], [], [], [],
                    postCommitOutbox: [new(row.OutboxItemId, RuntimeStateChangeOperation.Upsert, row, new Dictionary<string, string>())]) };
            }
            if (mixed)
                commit = commit with { PostCommitIntents = [duplicate ? intent : new RuntimePostCommitIntent("other", "publisher", "other-component-kind", Now, null, "other-key", null)] };
            if (late)
            {
                lateEnvelope = commit;
                commit = commit with { PostCommitIntents = [], StateChanges = new(null, null, [], [], [], [], []) };
            }
            CommitResult = await Services.GetRequiredService<RuntimeCheckpointCommitter>().CommitAsync(commit);
            return (await Store.FindAsync(RuntimePostCommitOutboxItems.OutboxItemId(commit.CommitId, intent)))!;
        }
        public IRuntimePostCommitOutboxProcessor Processor => Services.GetRequiredService<IRuntimePostCommitOutboxProcessor>();
        public async Task<RuntimePostCommitOutboxItem?> Find(string id) => await Store.FindAsync(id);
        public async Task Bind(string id, string artifact = "artifact", string? correlation = null) => await Bindings.SaveAsync(new(id, artifact, Pin.DefinitionId,
            Pin.ArtifactVersion, Pin.ArtifactHash, "node", EventStimulus.StimulusType, EventStimulus.Hash("named"), correlation, new Dictionary<string, string> { ["binding"] = id }, Now));
        public async Task Wait(string executionId, string id, string? correlation = null, bool testRun = false)
        {
            await Services.GetRequiredService<IWorkflowExecutionStateStore>().SaveAsync(Execution(executionId) with { RunKind = testRun ? WorkflowRunKind.TestRun : WorkflowRunKind.PublishedRun });
            await Services.GetRequiredService<IActivityExecutionStateStore>().SaveAsync(new(new(id + ":activity", executionId, "node", "authored", "test/activity", "1.0.0"),
                ActivityExecutionStatus.Suspended, null, 1, Now, Now, null, null, null, null, null,
                ActivitySchedulingProvenance.From(executionId, null, null, null, null, null, null, "fixture"), null, [id], [], 0, 0, new Dictionary<string, string>()));
            await Bookmarks.SaveAsync(new(id, executionId, id + ":activity", "node", "resume", EventStimulus.StimulusType, EventStimulus.Hash("named"), null,
                correlation is null ? new Dictionary<string, string>() : new Dictionary<string, string> { [RuntimeMetadataKeys.CorrelationId] = correlation }, Clock.GetUtcNow(), null));
        }
        private static WorkflowExecutionState Execution(string id) => new(id, Pin, WorkflowExecutionStatus.Suspended, null, Now, Now, Now,
            null, null, null, "tenant", new Dictionary<string, string>()) { RunKind = WorkflowRunKind.PublishedRun, Partition = new("partition") };
        public async ValueTask DisposeAsync() { scope.Dispose(); await provider.DisposeAsync(); }
    }
}
