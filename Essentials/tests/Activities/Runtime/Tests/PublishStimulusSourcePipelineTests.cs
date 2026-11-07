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
public sealed partial class PublishStimulusSourcePipelineTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 6, 0, 0, TimeSpan.Zero);
    private static readonly PersistenceScope Scope = new("tenant");
    private static readonly WorkflowExecutableIdentity Pin = new("artifact", "definition", "version", "1.0.0", "hash");
    private static readonly RuntimeHistoryCapturePolicy PublicationHistoryPolicy = new("runtime-history-v3", 3, true,
        ["StimulusPublicationCommitted", "StimulusRoutingAttemptObserved", "StimulusRoutingDispositionObserved"]);

    [Theory]
    [InlineData(false)] [InlineData(true)]
    [Trait("Feature", "RunHistory")]
    public async Task Publication_history_records_owned_commit_then_distinct_actual_routing_observations(bool coalescing)
    {
        await using var harness = new Harness(coalescing, history: true); await harness.SeedMaterial();
        await harness.Bind("one-start"); await harness.Wait("existing", "first"); await harness.Wait("existing", "second");
        var source = await harness.Commit();
        var committed = Assert.Single((await harness.History.ReadPageAsync(new("publisher"))).Items);
        Assert.Equal("StimulusPublicationCommitted", committed.Name); Assert.Equal(RuntimeHistoryAuthority.CommittedTransition, committed.Authority);
        Assert.Empty(harness.Actors.Accepted);
        Assert.Equal(committed.HistoryEventId, source.Intent.HistoryCausation!.HistoryEventId);
        harness.Clock.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(1, (await harness.Processor.ProcessAsync(new(10))).DeliveredCount);
        var events = (await harness.History.ReadPageAsync(new("publisher"))).Items;
        Assert.Equal(["StimulusPublicationCommitted", "StimulusRoutingAttemptObserved", "StimulusRoutingDispositionObserved"], events.Select(item => item.Name));
        Assert.All(events.Skip(1), item =>
        {
            Assert.Equal(harness.Clock.GetUtcNow(), item.OccurredAt); Assert.Equal(RuntimeHistoryAuthority.DeliveryObservation, item.Authority);
            Assert.Equal(committed.HistoryEventId, item.Causation!.HistoryEventId); Assert.Equal(source.OutboxItemId, item.Causation.DeliveryId);
            Assert.Equal("publisher", item.Causation.WorkflowExecutionId); Assert.Null(item.Payload);
        });
        Assert.Equal(3, harness.Actors.Accepted.Count);
        var resumed = harness.Actors.Accepted.Where(item => item.Command.Kind == WorkflowExecutionCommandKind.ResumeBookmark).ToArray();
        Assert.Equal(2, resumed.Length); Assert.All(resumed, item => Assert.Equal("existing", item.WorkflowExecutionId));
        Assert.Equal(2, resumed.Select(item => item.IdempotencyKey).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    [Trait("Feature", "RunHistory")]
    public async Task Same_source_claim_reinvocation_observes_two_attempts_but_one_saved_cohort_and_effect()
    {
        await using var harness = new Harness(history: true, observationAckLoss: true); await harness.SeedMaterial(); await harness.Bind("one-start");
        var item = await harness.Commit(); harness.Clock.Advance(TimeSpan.FromSeconds(1));
        var claim = Assert.Single(await ((IRuntimePostCommitOutboxClaimStore)harness.Store).ClaimAsync(new("history-owner", harness.Clock.GetUtcNow(), TimeSpan.FromMinutes(5), 1)));
        var source = new PostCommitDeliveryContext(claim, Scope);
        await harness.HandleSource(source);
        Assert.Single(harness.Actors.Accepted); Assert.Single(harness.Actors.Attempts);
        var repeatedWrite = harness.AckLoss!.Calls.Where(observation => observation.Event.Name == "StimulusRoutingDispositionObserved").ToArray();
        Assert.Equal(2, repeatedWrite.Length); Assert.Same(repeatedWrite[0], repeatedWrite[1]);
        var cohort = (await harness.Find(item.OutboxItemId))!.RecipientSnapshot;
        harness.Clock.Advance(TimeSpan.FromSeconds(1)); await harness.HandleSource(source);
        Assert.Single(harness.Actors.Accepted); Assert.Equal(2, harness.Actors.Attempts.Count);
        Assert.Equal(cohort!.SelectedAt, (await harness.Find(item.OutboxItemId))!.RecipientSnapshot!.SelectedAt);
        var events = (await harness.History.ReadPageAsync(new("publisher"))).Items;
        Assert.Equal(5, events.Count); Assert.Equal(2, events.Count(e => e.Name == "StimulusRoutingAttemptObserved"));
        Assert.Equal(2, events.Count(e => e.Name == "StimulusRoutingDispositionObserved"));
        Assert.Equal(5, events.Select(e => e.HistoryEventId).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    [Trait("Feature", "RunHistory")]
    public async Task Capable_routing_receiver_does_not_retrofit_observations_to_original_native_only_pin()
    {
        var pin = new RuntimeHistoryCapturePolicy("runtime-history-v3", 3, true, ["StimulusPublicationCommitted"]);
        await using var harness = new Harness(history: true, historyPolicy: pin); await harness.SeedMaterial(); await harness.Bind("one-start");
        Assert.True(harness.CanObservePublication());
        var source = await harness.Commit(); Assert.Null(source.Intent.HistoryCausation);
        Assert.Equal(1, (await harness.Processor.ProcessAsync(new(10))).DeliveredCount);
        harness.Clock.Advance(TimeSpan.FromSeconds(1)); await harness.ReplayRaw();
        Assert.Null((await harness.Find(source.OutboxItemId))!.Intent.HistoryCausation);
        Assert.Single((await harness.History.ReadPageAsync(new("publisher"))).Items, item => item.Name == "StimulusPublicationCommitted");
        Assert.Single(harness.Actors.Accepted);
    }

    [Fact]
    [Trait("Feature", "RunHistory")]
    public async Task Legacy_selected_routing_receiver_keeps_native_publication_and_legacy_delivery_without_observation_claims()
    {
        var pin = new RuntimeHistoryCapturePolicy("runtime-history-v3", 3, true, ["StimulusPublicationCommitted"]);
        await using var harness = new Harness(history: true, historyPolicy: pin, legacyReceiver: true); await harness.SeedMaterial(); await harness.Bind("one-start");
        Assert.False(harness.CanObservePublication());
        var source = await harness.Commit(); Assert.Null(source.Intent.HistoryCausation);
        Assert.Equal(1, (await harness.Processor.ProcessAsync(new(10))).DeliveredCount);
        Assert.Single(harness.Actors.Accepted);
        Assert.Single((await harness.History.ReadPageAsync(new("publisher"))).Items, item => item.Name == "StimulusPublicationCommitted");
    }

    [Fact]
    [Trait("Feature", "RunHistory")]
    public async Task Fresh_raw_publication_replay_preserves_original_source_fact_and_marker_after_delivery()
    {
        await using var harness = new Harness(history: true); await harness.SeedMaterial(); await harness.Bind("one-start");
        var source = await harness.Commit(); harness.Clock.Advance(TimeSpan.FromSeconds(1));
        await harness.Processor.ProcessAsync(new(10));
        var before = await harness.History.ReadPageAsync(new("publisher")); var commits = harness.Store.ListCommits().Count;
        harness.Clock.Advance(TimeSpan.FromSeconds(1)); await harness.ReplayRaw();
        var after = await harness.History.ReadPageAsync(new("publisher"));
        Assert.Equal(commits, harness.Store.ListCommits().Count); Assert.Equal(before.Head, after.Head);
        Assert.Equal(before.Items.Select(e => e.HistoryEventId), after.Items.Select(e => e.HistoryEventId));
        Assert.Single(harness.Actors.Accepted); Assert.Equal(RuntimePostCommitOutboxStatus.Delivered, (await harness.Find(source.OutboxItemId))!.Status);
    }

    [Theory]
    [InlineData("missing")] [InlineData("payload")] [InlineData("scope")]
    [InlineData("expired")] [InlineData("reclaimed")] [InlineData("delivered")]
    [Trait("Feature", "RunHistory")]
    public async Task Rejected_publication_source_context_adds_no_observation_and_routes_no_recipient(string scenario)
    {
        await using var harness = new Harness(history: true); await harness.SeedMaterial(); await harness.Bind("one-start");
        var item = await harness.Commit(); var claims = (IRuntimePostCommitOutboxClaimStore)harness.Store;
        var claim = Assert.Single(await claims.ClaimAsync(new("source-context-owner", harness.Clock.GetUtcNow(), TimeSpan.FromMinutes(5), 1)));
        var context = new PostCommitDeliveryContext(claim, Scope);
        if (scenario is "missing" or "payload" or "scope") context = TamperSource(context, scenario);
        if (scenario is "expired" or "reclaimed") harness.Clock.Advance(TimeSpan.FromMinutes(6));
        if (scenario == "reclaimed") Assert.Single(await claims.ClaimAsync(new("replacement-owner", harness.Clock.GetUtcNow(), TimeSpan.FromMinutes(5), 1)));
        if (scenario == "delivered") await claims.RecordDeliveryResultAsync(claim, new(claim.OutboxItemId, RuntimePostCommitOutboxStatus.Delivered, harness.Clock.GetUtcNow()));
        var beforeSource = (await harness.Find(item.OutboxItemId))!;
        var before = await harness.History.ReadPageAsync(new("publisher"));
        if (scenario is "expired" or "reclaimed" or "delivered")
            await Assert.ThrowsAsync<RuntimePostCommitOutboxStaleClaimException>(async () => { await harness.HandleSource(context); });
        else
            Assert.False((await Assert.ThrowsAsync<RuntimePostCommitSourceBoundaryException>(async () => { await harness.HandleSource(context); })).IsUnavailable);
        Assert.Empty(harness.Actors.Accepted); Assert.Empty(harness.Actors.Attempts); Assert.Equal(0, harness.Lookup.Reads);
        var after = await harness.History.ReadPageAsync(new("publisher"));
        Assert.Equal(before.Head, after.Head); Assert.Equal(before.Items.Select(e => e.HistoryEventId), after.Items.Select(e => e.HistoryEventId));
        Assert.True(beforeSource.IsEquivalentTo(await harness.Find(item.OutboxItemId)));
    }

    private static PostCommitDeliveryContext TamperSource(PostCommitDeliveryContext source, string scenario)
    {
        if (scenario == "scope") return new(source.Claim, new("another-tenant"));
        var item = source.Claim.Item; var id = scenario == "missing" ? "fabricated-commit:" + item.Intent.IntentId : item.OutboxItemId;
        var intent = new RuntimePostCommitIntent(item.Intent.IntentId, item.Intent.WorkflowExecutionId, item.Intent.Kind,
            item.Intent.RecordedAt, item.Intent.ActivityExecutionId, item.Intent.IdempotencyKey,
            scenario == "payload" ? JsonSerializer.SerializeToElement(new { forged = true }) : item.Intent.Payload,
            item.Intent.Metadata, item.Intent.DependsOnWaitRegistrationId, item.Intent.WaitFailurePolicy,
            item.Intent.MaterializedSchedulerWorkItem, item.Intent.NotBefore)
            { HistoryCausation = item.Intent.HistoryCausation! with { DeliveryId = id } };
        var forged = new RuntimePostCommitOutboxItem(id, intent, item.Status, item.RecordedAt, item.AvailableAt, item.RetryPolicy,
            item.DeliveryAttemptCount, item.DeliveringOwnerId, item.DeliveryStartedAt, item.DeliveredAt, item.LastFailureMessage,
            item.Metadata, item.DeliveryFencingToken, item.DeliveryVisibleAfter, item.StimulusDeliveryProtocolVersion,
            item.StimulusDeliverySourceScope, item.RecipientSnapshot);
        return new(new(forged, source.Claim.OwnerId, source.Claim.FencingToken, source.Claim.ClaimedAt, source.Claim.VisibleAfter), source.Scope);
    }

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
        private readonly bool history;
        private readonly RuntimeHistoryCapturePolicy historyPolicy;
        private readonly ServiceProvider provider; private readonly IServiceScope scope; private RuntimeCheckpointCommit? lateEnvelope;
        public RuntimeCheckpointCommitResult? CommitResult { get; private set; }
        private RuntimeCheckpointCommit? rawCommit;
        private IServiceProvider Services => scope.ServiceProvider;
        public FakeTimeProvider Clock { get; } = new(Now);
        public RecordingActors Actors { get; } = new();
        public IStimulusRouter Router => Services.GetRequiredService<IStimulusRouter>();
        public IBookmarkResumeDispatcher Resume => Services.GetRequiredService<IBookmarkResumeDispatcher>();
        public IWorkflowExecutionStateStore Executions => Services.GetRequiredService<IWorkflowExecutionStateStore>();
        public IRuntimeHistoryStore History => Services.GetRequiredService<IRuntimeHistoryStore>();
        public bool CanObservePublication() => Services.GetRequiredService<IRuntimePostCommitIntentDispatcher>().CanRecordHistory(
            PublishStimulusConstants.PublishStimulusIntentKind, Services.GetRequiredService<IRuntimeHistoryObservationWriter>(), History);
        public AckLossObservationWriter? AckLoss => Services.GetService<AckLossObservationWriter>();
        public ValueTask HandleSource(PostCommitDeliveryContext source) => ((IRuntimePostCommitDurableSourceIntentHandler)Services.GetRequiredService<PublishStimulusExecutor>()).HandleAsync(source);
        public ValueTask<RuntimeCheckpointCommitResult> ReplayRaw() => Services.GetRequiredService<RuntimeCheckpointCommitter>().CommitAsync(rawCommit!);
        public InMemoryRuntimeCheckpointCommitStore Store => Services.GetRequiredService<InMemoryRuntimeCheckpointCommitStore>();
        public IBookmarkStateStore Bookmarks => Services.GetRequiredService<IBookmarkStateStore>();
        public IWorkflowTriggerBindingStore Bindings => Services.GetRequiredService<IWorkflowTriggerBindingStore>();
        public IWorkflowExecutableSourceReferenceStore References => Services.GetRequiredService<IWorkflowExecutableSourceReferenceStore>();
        public InMemoryExecutionLivenessStateStore Ownership => (InMemoryExecutionLivenessStateStore)Services.GetRequiredService<IExecutionLivenessStateStore>();
        public CountingLookup Lookup => Services.GetRequiredService<CountingLookup>();
        public Harness(bool coalescing = false, string schema = "finalized", bool laterEnricher = false, bool skip = false, bool history = false, bool observationAckLoss = false, RuntimeHistoryCapturePolicy? historyPolicy = null, bool legacyReceiver = false,
            Action<IServiceCollection>? configureServices = null)
        {
            this.history = history; this.historyPolicy = historyPolicy ?? PublicationHistoryPolicy;
            var services = new ServiceCollection(); services.AddSingleton<TimeProvider>(Clock); services.AddSingleton<IPersistenceAccessContextAccessor, Access>();
            services.AddSingleton<IWorkflowExecutionActorProvider>(Actors);
            if (schema != "missing") services.AddSingleton<ISchemaDormancyCheck>(new SchemaDormancyCheck(observed: schema == "unobserved" ? null : new Observed(schema)));
            new ActivitiesPrimitivesFeature().ConfigureServices(services);
            if (skip) services.AddSingleton<IRuntimeCheckpointPersistencePolicy, SkipPolicy>();
            if (laterEnricher) services.AddSingleton<IRuntimeCheckpointCommitEnricher>(new LateEnricher(() => lateEnvelope));
            services.AddSingleton(new PersistenceModeOptions()); services.AddWorkflowRuntime(); if (coalescing) services.AddPersistenceModes();
            if (history) services.AddRuntimeHistory();
            if (legacyReceiver) services.AddScoped<PublishStimulusExecutor>(sp => new(sp.GetRequiredService<IStimulusRouter>()));
            if (observationAckLoss)
            {
                var original = services.Single(descriptor => descriptor.ServiceType == typeof(IRuntimeHistoryObservationWriter));
                services.Remove(original);
                services.AddScoped<AckLossObservationWriter>(sp => new((IRuntimeHistoryObservationWriter)ActivatorUtilities.CreateInstance(sp, original.ImplementationType!)));
                services.AddScoped<IRuntimeHistoryObservationWriter>(sp => sp.GetRequiredService<AckLossObservationWriter>());
            }
            services.AddSingleton<IWorkflowTriggerBindingStore, InMemoryWorkflowTriggerBindingStore>();
            services.AddScoped<CountingLookup>(sp => new(new GlobalBookmarkStimulusLookup((IBookmarkStimulusIndex)sp.GetRequiredService<IBookmarkStateStore>(), sp.GetRequiredService<IWorkflowExecutionStateStore>())));
            services.AddScoped<IGlobalBookmarkStimulusLookup>(sp => sp.GetRequiredService<CountingLookup>()); services.AddWorkflowStimulusRouter();
            configureServices?.Invoke(services);
            provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true }); scope = provider.CreateScope();
        }
        public async Task SeedMaterial()
        {
            _ = Store;
            await Services.GetRequiredService<IWorkflowExecutableStore>().SaveAsync(new(Pin,
                new ExecutableNode("node", "authored", "test/activity", "1.0.0", new RuntimeActivityDescriptor("test", RuntimeActivityDescriptor.InitialSchemaVersion, JsonSerializer.SerializeToElement(new { })), new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>()),
                new Dictionary<string, WorkflowExecutableResumeTarget> { ["resume"] = new("resume", "node", "test", new Dictionary<string, string>()) }, Now, new Dictionary<string, string>(), IncidentStrategyBuiltIns.FaultReference));
            if (history)
            {
                var publisher = Execution("publisher") with { Status = WorkflowExecutionStatus.Running,
                    HistoryCapturePolicy = historyPolicy, HistoryCaptureEnabledAtAdmission = true };
                var start = new RuntimeCheckpointCommit("publisher-start", new("publisher-start-checkpoint", RuntimeCheckpointNames.WorkflowStarted,
                    "publisher", Now, [], new Dictionary<string, string>()),
                    new(new("publisher", RuntimeStateChangeOperation.Upsert, publisher, new Dictionary<string, string>()), null, [], [], [], [], []),
                    [], new Dictionary<string, string>());
                var result = await Services.GetRequiredService<RuntimeCheckpointCommitter>().CommitAsync(start);
                Assert.True(result.Succeeded);
            }
            else await Services.GetRequiredService<IWorkflowExecutionStateStore>().SaveAsync(Execution("publisher"));
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
            rawCommit = commit;
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

    private sealed class AckLossObservationWriter(IRuntimeHistoryObservationWriter inner) : IRuntimeHistoryObservationWriter
    {
        private bool lose = true;
        public List<RuntimeHistoryObservation> Calls { get; } = [];
        public bool CanRecordHistory(IRuntimeHistoryStore selectedStore) => inner.CanRecordHistory(selectedStore);
        public async ValueTask<RuntimeHistoryObservationResult> RecordAsync(RuntimeHistoryObservation observation, CancellationToken cancellationToken = default)
        {
            Calls.Add(observation); var result = await inner.RecordAsync(observation, cancellationToken);
            if (lose && observation.Event.Name == "StimulusRoutingDispositionObserved") { lose = false; throw new IOException("Constructed observation acknowledgement loss."); }
            return result;
        }
    }
}
