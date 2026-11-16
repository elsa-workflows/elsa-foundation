using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Elsa.Workflows.Runtime.Services.Coalescing;
using Elsa.Workflows.Runtime.Services.Executions;
using Elsa.Workflows.Runtime.Services.History;
using Elsa.Workflows.Runtime.Services.Recovery;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

/// <summary>Memory-provider controls with supplied native groups/observations, not delivery-owner or durable-provider proof.</summary>
public sealed class RuntimeHistoryObservationParticipantTests
{
    private static readonly DateTimeOffset At = DateTimeOffset.UnixEpoch;
    private static readonly RuntimeHistoryCapturePolicy Policy = new("runtime-history-v3", 3, true,
        ["Transition", "ActivityCompleted", "DeliveryReceived", "DeliveryFaulted"]);
    private static readonly RuntimeCheckpointPersistenceDecision Immediate = new(RuntimeCheckpointPersistenceMode.Immediate);

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Supplied_publication_owner_observations_use_native_shared_order_and_replay_without_advancing_checkpoint_coverage(bool disposition)
    {
        var policy = new RuntimeHistoryCapturePolicy("runtime-history-v3", 3, true,
            ["Transition", "StimulusPublicationCommitted", "StimulusRoutingAttemptObserved", "StimulusRoutingDispositionObserved"]);
        var f = await Fixture.CreateAsync(policy: policy); var before = (await f.Reader.FindHeadAsync("run"))!;
        var sourceScope = new PersistenceScope("scope-a");
        var intent = new RuntimePostCommitIntent("send", "run", "test.publication", At, "activity", "key", null)
            { HistoryCausation = new("publication-event", "run", "source:send", "command", "work") };
        var pending = new RuntimePostCommitOutboxItem("source:send", intent, RuntimePostCommitOutboxStatus.Pending, At, At,
            null, 0, null, null, null, null, null, 0, null, null, sourceScope, null);
        var source = new PostCommitDeliveryContext(RuntimePostCommitOutboxClaimTransitions.Claim(pending, new("owner", At, TimeSpan.FromMinutes(1), 1)), sourceScope);
        var attempt = RuntimeHistorySourceFacts.PublicationObservation(policy, source, "receipt", "StimulusRoutingAttemptObserved", At, "handler-invocation-observed");
        var first = await f.Writer.RecordAsync(attempt); Assert.True(first.Recorded);
        var replay = await f.Writer.RecordAsync(attempt); Assert.Equal(first.Event!.Sequence, replay.Event!.Sequence);
        if (disposition)
        {
            var completed = RuntimeHistorySourceFacts.PublicationObservation(policy, source, "receipt", "StimulusRoutingDispositionObserved", At.AddSeconds(1), "saved-cohort-routing-acknowledged-observed");
            var result = await f.Writer.RecordAsync(completed); Assert.True(result.Recorded);
            Assert.Equal(first.Event.HistoryEventId, result.Event!.Causation!.HistoryEventId);
            Assert.Equal(first.Event.Sequence + 1, result.Event.Sequence);
        }
        var after = (await f.Reader.FindHeadAsync("run"))!;
        Assert.Equal(before.LastSequence + (disposition ? 2 : 1), after.LastSequence);
        Assert.Equal(before.LastPhysicalCommitId, after.LastPhysicalCommitId); Assert.Equal(before.LastCommittedAt, after.LastCommittedAt);
        Assert.Equal(before.CapturedFromStart, after.CapturedFromStart); Assert.Single(f.Store.ListCommits());
        Assert.Equal(disposition ? 3 : 2, (await f.Reader.ReadPageAsync(new("run"))).Items.Count);
        // Source/claim and seed group are supplied; this is the actual memory observer, not publication delivery/capture proof.
    }

    [Fact]
    public async Task Observation_advances_shared_order_without_advancing_the_last_covered_native_checkpoint()
    {
        var f = await Fixture.CreateAsync();
        var before = (await f.Reader.FindHeadAsync("run"))!;
        f.Clock.Advance(TimeSpan.FromMinutes(1));
        var observation = Observation();
        var result = await f.Writer.RecordAsync(observation);
        Assert.True(result.Recorded);
        var stored = result.Event!;
        Assert.Equal(2, stored.Sequence);
        Assert.Equal(At, stored.OccurredAt);
        Assert.Equal(f.Clock.GetUtcNow(), stored.RecordedAt);
        Assert.Equal(RuntimeHistoryAuthority.DeliveryObservation, stored.Authority);
        Assert.Null(stored.CheckpointId);
        Assert.Null(stored.CheckpointOrdinal);
        Assert.Equal("owner-observation:" + observation.Event.HistoryEventId, stored.PhysicalCommitId);
        var after = (await f.Reader.FindHeadAsync("run"))!;
        Assert.Equal(2, after.LastSequence);
        Assert.Equal(before.LastPhysicalCommitId, after.LastPhysicalCommitId);
        Assert.Equal(before.LastCommittedAt, after.LastCommittedAt);
        Assert.Equal(before.CapturedFromStart, after.CapturedFromStart);
        Assert.Equal(before.HasGaps, after.HasGaps);
        Assert.True(before.Policy.IsEquivalentTo(after.Policy));
        Assert.Single(f.Store.ListCommits());
        Assert.Same(stored, await f.Reader.FindEventAsync("run", stored.HistoryEventId));
    }

    [Fact]
    public async Task Concurrent_replays_allocate_one_observation_and_retain_the_original_recorded_instant()
    {
        var f = await Fixture.CreateAsync();
        var observation = Observation();
        await f.State.WriteGate.WaitAsync();
        Task<RuntimeHistoryObservationResult>[] writes;
        try
        {
            writes = Enumerable.Range(0, 8).Select(_ => f.Writer.RecordAsync(observation).AsTask()).ToArray();
            Assert.All(writes, write => Assert.False(write.IsCompleted));
        }
        finally { f.State.WriteGate.Release(); }
        var outcomes = await Task.WhenAll(writes);
        Assert.All(outcomes, result => { Assert.True(result.Recorded); Assert.Equal(2, result.Event!.Sequence); });
        f.Clock.Advance(TimeSpan.FromDays(1));
        var replay = await f.Writer.RecordAsync(observation);
        Assert.Equal(At.AddHours(1), replay.Event!.RecordedAt);
        var page = await f.Reader.ReadPageAsync(new("run"));
        Assert.Equal(new long[] { 1, 2 }, page.Items.Select(item => item.Sequence));
        Assert.Single(page.Items, item => item.Name == "DeliveryReceived");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Conflicting_content_or_occurrence_knowledge_refuses_without_another_position(bool unknownTime)
    {
        var f = await Fixture.CreateAsync();
        await f.Writer.RecordAsync(Observation());
        var before = await f.Reader.FindHeadAsync("run");
        var changed = Observation(Event(known: unknownTime ? false : null, metadata: unknownTime ? null :
            new Dictionary<string, string> { ["history.reasonCode"] = "different" }));
        await Assert.ThrowsAsync<RuntimeHistoryIntegrityException>(async () => await f.Writer.RecordAsync(changed));
        Assert.Equal(before, await f.Reader.FindHeadAsync("run"));
        Assert.Equal(2, (await f.Reader.ReadPageAsync(new("run"))).Items.Count);
    }

    [Theory]
    [InlineData("fingerprint")]
    [InlineData("position")]
    [InlineData("wrong-position")]
    [InlineData("missing-content")]
    [InlineData("wrong-content")]
    public async Task Contradictory_observation_receipts_fail_closed_without_reallocating_the_identity(string corrupt)
    {
        var f = await Fixture.CreateAsync();
        await f.Writer.RecordAsync(Observation());
        var before = await f.Reader.FindHeadAsync("run");
        var key = ("scope-a", "run", "observation");
        lock (f.State.SyncRoot)
        {
            if (corrupt == "fingerprint") f.State.HistoryCandidateFingerprints.Remove(key);
            else if (corrupt == "position") f.State.HistoryReceiptPositions.Remove(key);
            else if (corrupt == "wrong-position") f.State.HistoryReceiptPositions[key] = 1;
            else if (corrupt == "missing-content") f.State.HistoryEventsById.Remove(key);
            else f.State.HistoryEventsById[key] = RuntimeHistoryIdentity.Position(Event(metadata:
                new Dictionary<string, string> { ["history.reasonCode"] = "changed" }), 2, "owner-observation:observation", f.Clock.GetUtcNow());
        }
        await Assert.ThrowsAsync<RuntimeHistoryIntegrityException>(async () => await f.Writer.RecordAsync(Observation()));
        Assert.Equal(before, await f.Reader.FindHeadAsync("run"));
        Assert.Single(f.Store.ListCommits());
    }

    [Fact]
    public async Task An_acknowledged_purged_receipt_deduplicates_without_recreating_event_content()
    {
        var f = await Fixture.CreateAsync();
        await f.Writer.RecordAsync(Observation());
        // Supply a consistent acknowledged purge snapshot, not a running retention-provider implementation.
        lock (f.State.SyncRoot)
        {
            var owner = ("scope-a", "run");
            f.State.HistoryHeads[owner] = f.State.HistoryHeads[owner] with { PurgedThroughSequence = 2, FirstSequence = 3 };
            foreach (var item in f.State.HistoryEvents[owner]) f.State.HistoryEventsById.Remove(("scope-a", "run", item.HistoryEventId));
            f.State.HistoryEvents[owner].Clear();
            foreach (var name in f.State.HistoryNames[owner]) f.State.HistoryNameEvents[("scope-a", "run", name)].Clear();
            foreach (var category in f.State.HistoryCategories[owner]) f.State.HistoryCategoryEvents[("scope-a", "run", category)].Clear();
        }
        var before = await f.Reader.FindHeadAsync("run");
        var result = await f.Writer.RecordAsync(Observation());
        Assert.True(result.Recorded);
        Assert.Null(result.Event);
        Assert.Equal(before, await f.Reader.FindHeadAsync("run"));
        Assert.Empty((await f.Reader.ReadPageAsync(new("run"))).Items);
    }

    [Theory]
    [InlineData("payload")]
    [InlineData("sequence")]
    [InlineData("physical")]
    [InlineData("recorded")]
    [InlineData("checkpoint")]
    [InlineData("ordinal")]
    [InlineData("authority")]
    [InlineData("category")]
    [InlineData("schema")]
    [InlineData("native-kind")]
    [InlineData("policy-kind")]
    [InlineData("metadata")]
    public async Task Unsafe_or_native_fact_envelopes_are_rejected_before_any_allocation(string invalid)
    {
        var f = await Fixture.CreateAsync();
        var before = await f.Reader.FindHeadAsync("run");
        var e = new RuntimeHistoryEvent("observation", "run", invalid == "sequence" ? 1 : 0,
            invalid == "category" ? RuntimeHistoryEventCategory.ActivityLifecycle : RuntimeHistoryEventCategory.DeliveryObservation,
            invalid == "native-kind" ? "ActivityCompleted" : invalid == "policy-kind" ? "DeliveryDuplicate" : "DeliveryReceived", At,
            checkpointId: invalid == "checkpoint" ? "invented-checkpoint" : null,
            payload: invalid == "payload" ? JsonSerializer.SerializeToElement("private payload") : null,
            metadata: invalid == "metadata" ? new Dictionary<string, string> { ["private"] = "private" } : null)
        {
            Authority = invalid == "authority" ? RuntimeHistoryAuthority.CommittedTransition : RuntimeHistoryAuthority.DeliveryObservation,
            SchemaVersion = invalid == "schema" ? 2 : 1,
            PhysicalCommitId = invalid == "physical" ? "invented-commit" : null,
            RecordedAt = invalid == "recorded" ? At : null,
            CheckpointOrdinal = invalid == "ordinal" ? 0 : null
        };
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await f.Writer.RecordAsync(Observation(e)));
        Assert.Equal(before, await f.Reader.FindHeadAsync("run"));
        Assert.Single((await f.Reader.ReadPageAsync(new("run"))).Items);
        Assert.Single(f.Store.ListCommits());
    }

    [Fact]
    public async Task Observations_and_native_checkpoints_share_one_serial_allocation()
    {
        var f = await Fixture.CreateAsync();
        await f.State.WriteGate.WaitAsync();
        Task[] writes;
        try
        {
            writes = Enumerable.Range(0, 8).SelectMany(index => new Task[]
            {
                f.Writer.RecordAsync(Observation(Event("observation-" + index))).AsTask(),
                f.Store.CommitAsync(Commit("native-" + index), Immediate).AsTask()
            }).ToArray();
            Assert.All(writes, write => Assert.False(write.IsCompleted));
        }
        finally { f.State.WriteGate.Release(); }
        await Task.WhenAll(writes);
        var page = await f.Reader.ReadPageAsync(new("run"));
        Assert.Equal(Enumerable.Range(1, 17).Select(index => (long)index), page.Items.Select(item => item.Sequence));
        Assert.Equal(8, page.Items.Count(item => item.Authority == RuntimeHistoryAuthority.DeliveryObservation));
        Assert.Equal(9, f.Store.ListCommits().Count);
        Assert.StartsWith("native-", page.Head!.LastPhysicalCommitId);
        Assert.Equal(At.AddHours(1), page.Head.LastCommittedAt);
    }

    [Fact]
    public async Task Native_owner_reader_and_accessor_must_share_the_exact_memory_boundary()
    {
        var f = await Fixture.CreateAsync();
        Assert.True(f.Writer.CanRecordHistory(f.Reader));
        var foreign = new InMemoryRuntimeHistoryStore(new(), f.Access, Codec());
        Assert.False(f.Writer.CanRecordHistory(foreign));
        Assert.False(f.Writer.CanRecordHistory(new InMemoryRuntimeHistoryStore(f.State, new Access(), Codec())));
        var wrongOwner = new InMemoryRuntimeHistoryObservationWriter(f.State, f.Access, new InMemoryWorkflowExecutionStateStore(), f.Clock);
        Assert.False(wrongOwner.CanRecordHistory(f.Reader));
        var failure = await Assert.ThrowsAsync<RuntimeHistoryUnavailableException>(async () => await wrongOwner.RecordAsync(Observation()));
        Assert.Equal("run", failure.WorkflowExecutionId);
        Assert.Equal("record-observation", failure.Operation);
        Assert.IsType<NotSupportedException>(failure.InnerException);
        Assert.Single((await f.Reader.ReadPageAsync(new("run"))).Items);
    }

    [Theory]
    [InlineData("head-policy")]
    [InlineData("owner-policy")]
    [InlineData("owner-missing")]
    [InlineData("head-missing")]
    public async Task Contradictory_retained_head_and_owner_refuse_instead_of_claiming_pending_capture(string corrupt)
    {
        var f = await Fixture.CreateAsync();
        lock (f.State.SyncRoot)
        {
            if (corrupt == "head-policy") f.State.HistoryHeads[("scope-a", "run")] = f.State.HistoryHeads[("scope-a", "run")] with { Policy = null! };
            else if (corrupt == "owner-policy") f.State.HistoryRunOwners["run"] = ("scope-a", null!);
            else if (corrupt == "owner-missing") f.State.HistoryRunOwners.Remove("run");
            else f.State.HistoryHeads.Remove(("scope-a", "run"));
        }
        await Assert.ThrowsAsync<RuntimeHistoryIntegrityException>(async () => await f.Writer.RecordAsync(Observation()));
        Assert.DoesNotContain(("scope-a", "run", "observation"), f.State.HistoryCandidateFingerprints.Keys);
        Assert.Single(f.Store.ListCommits());
    }

    [Fact]
    public async Task Missing_native_owner_or_head_never_creates_capture_or_a_receipt()
    {
        var f = await Fixture.CreateAsync(seedHead: false);
        Assert.False((await f.Writer.RecordAsync(Observation())).Recorded);
        Assert.Null(await f.Reader.FindHeadAsync("run"));
        Assert.Empty(f.State.HistoryCandidateFingerprints);
        Assert.True(await f.Executions.DeleteAsync("run"));
        Assert.False((await f.Writer.RecordAsync(Observation())).Recorded);
        Assert.Empty(f.State.HistoryCandidateFingerprints);
    }

    [Fact]
    public async Task Changed_policy_or_scope_refuses_without_rewriting_the_captured_head()
    {
        var f = await Fixture.CreateAsync();
        var before = await f.Reader.FindHeadAsync("run");
        var changedPolicy = new RuntimeHistoryCapturePolicy("other-policy", 3, true, Policy.SupportedKinds);
        await Assert.ThrowsAsync<RuntimeHistoryIntegrityException>(async () => await f.Writer.RecordAsync(new(changedPolicy, Event())));
        f.Access.Current = PersistenceAccessContext.Scoped(new("scope-b"));
        await Assert.ThrowsAsync<RuntimeHistoryIntegrityException>(async () => await f.Writer.RecordAsync(Observation()));
        Assert.Null(await f.Reader.FindHeadAsync("run"));
        f.Access.Current = PersistenceAccessContext.Scoped(new("scope-a"));
        Assert.Equal(before, await f.Reader.FindHeadAsync("run"));
        Assert.Single((await f.Reader.ReadPageAsync(new("run"))).Items);
    }

    [Fact]
    public async Task Scope_drift_while_waiting_for_the_shared_gate_refuses_before_publication()
    {
        var f = await Fixture.CreateAsync();
        var before = await f.Reader.FindHeadAsync("run");
        await f.State.WriteGate.WaitAsync();
        Task<RuntimeHistoryObservationResult> recording;
        try
        {
            recording = f.Writer.RecordAsync(Observation()).AsTask();
            Assert.False(recording.IsCompleted);
            f.Access.Current = PersistenceAccessContext.Scoped(new("scope-b"));
        }
        finally { f.State.WriteGate.Release(); }
        await Assert.ThrowsAsync<InvalidOperationException>(() => recording);
        f.Access.Current = PersistenceAccessContext.Scoped(new("scope-a"));
        Assert.Equal(before, await f.Reader.FindHeadAsync("run"));
        Assert.Single((await f.Reader.ReadPageAsync(new("run"))).Items);
    }

    [Fact]
    public async Task Known_disabled_native_admission_cannot_be_overridden_by_a_retained_head()
    {
        var f = await Fixture.CreateAsync();
        var before = await f.Reader.FindHeadAsync("run");
        var native = (await f.Executions.FindAsync("run"))!;
        await f.Executions.SaveAsync(native with { HistoryCaptureEnabledAtAdmission = false });
        await Assert.ThrowsAsync<RuntimeHistoryIntegrityException>(async () => await f.Writer.RecordAsync(Observation()));
        Assert.Equal(before, await f.Reader.FindHeadAsync("run"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Deletion_and_observation_wait_for_the_same_gate_and_late_observations_never_resurrect_history(bool bookmarkLinked)
    {
        var f = await Fixture.CreateAsync();
        var native = (await f.Executions.FindAsync("run"))!;
        var observation = Observation(Event(bookmark: bookmarkLinked ? "native-only" : null));
        await f.State.WriteGate.WaitAsync();
        Task<RuntimeHistoryObservationResult> recording;
        Task<bool> deletion;
        try
        {
            recording = f.Writer.RecordAsync(observation).AsTask();
            deletion = f.Executions.DeleteAsync("run").AsTask();
            Assert.False(recording.IsCompleted);
            Assert.False(deletion.IsCompleted);
        }
        finally { f.State.WriteGate.Release(); }
        await recording;
        Assert.True(await deletion);
        Assert.Null(await f.Reader.FindHeadAsync("run"));
        Assert.False((await f.Writer.RecordAsync(observation)).Recorded);
        Assert.Empty((await f.Reader.ReadPageAsync(new("run"))).Items);
        Assert.Empty((await f.Reader.ReadPageAsync(new("run", bookmarkId: "native-only"))).Items);
        Assert.Empty(f.State.HistoryBookmarkEvents);
        Assert.Empty(f.State.HistoryBookmarkEventIds);
        Assert.Empty(f.State.HistoryCandidateFingerprints);
        // Even a deliberately permissive native fixture cannot restore the deleted diagnostic identity.
        await f.Executions.SaveAsync(native);
        Assert.False((await f.Writer.RecordAsync(observation)).Recorded);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await f.Store.CommitAsync(Commit("after-deletion"), Immediate));
        Assert.Null(await f.Reader.FindHeadAsync("run"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Owning_terminal_deletion_clears_observation_only_bookmark_indexes_and_tombstones_history(bool coalescing)
    {
        var services = new ServiceCollection();
        var access = new Access();
        var clock = new FakeTimeProvider(At.AddHours(1));
        services.AddLogging();
        services.AddSingleton<IPersistenceAccessContextAccessor>(access);
        services.AddSingleton<TimeProvider>(clock);
        services.AddSingleton<IWorkflowExecutableRootWriteLeaseManager>(new Lease());
        services.AddWorkflowRuntime();
        if (coalescing) services.AddPersistenceModes();
        using var provider = services.BuildServiceProvider(validateScopes: true);
        using var scope = provider.CreateScope();
        var selected = scope.ServiceProvider;
        var state = selected.GetRequiredService<InMemoryRuntimeCheckpointStoreState>();
        var executions = coalescing ? selected.GetRequiredService<CoalescingInner<IWorkflowExecutionStateStore>>().Value
            : selected.GetRequiredService<IWorkflowExecutionStateStore>();
        await executions.SaveAsync(new("run", new("artifact", "definition", "version", "1", "hash"), WorkflowExecutionStatus.Completed,
            null, At, At, At, At, null, null, null, new Dictionary<string, string>())
            { HistoryCapturePolicy = Policy, HistoryCaptureEnabledAtAdmission = true });
        await selected.GetRequiredService<InMemoryRuntimeCheckpointCommitStore>().CommitAsync(Commit("seed", starts: true), Immediate);
        var reader = new InMemoryRuntimeHistoryStore(state, access, Codec());
        var writer = new InMemoryRuntimeHistoryObservationWriter(state, access, executions, clock);
        Assert.True(writer.CanRecordHistory(reader));
        Assert.True((await writer.RecordAsync(Observation(Event(bookmark: "native-only")))).Recorded);
        Assert.Empty(state.HistoryBookmarks); // No lifecycle projection owns this bookmark-event index.
        Assert.Single(state.HistoryBookmarkEvents);
        var deletion = selected.GetRequiredService<IWorkflowExecutionDeletionStore>();
        Assert.True(deletion.IsSupported);
        var candidate = (await deletion.ReadAsync("run"))!;
        Assert.Equal(WorkflowExecutionDeletionResult.Deleted, await deletion.TryDeleteTerminalAsync(candidate));
        Assert.Null(await executions.FindAsync("run"));
        Assert.Null(await reader.FindHeadAsync("run"));
        Assert.Empty((await reader.ReadPageAsync(new("run", bookmarkId: "native-only"))).Items);
        Assert.Empty(state.HistoryBookmarkEvents);
        Assert.Empty(state.HistoryBookmarkEventIds);
        Assert.Empty(state.HistoryCandidateFingerprints);
        Assert.False(state.HistoryRunOwners.ContainsKey("run"));
        Assert.Contains(("scope-a", "run"), state.HistoryDeletedRuns);
        Assert.False((await writer.RecordAsync(Observation())).Recorded);
    }

    [Fact]
    public async Task Requested_cancellation_while_waiting_for_the_gate_propagates_without_append()
    {
        var f = await Fixture.CreateAsync();
        var before = await f.Reader.FindHeadAsync("run");
        using var cancellation = new CancellationTokenSource();
        await f.State.WriteGate.WaitAsync();
        try
        {
            var pending = f.Writer.RecordAsync(Observation(), cancellation.Token).AsTask();
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        }
        finally { f.State.WriteGate.Release(); }
        Assert.Equal(before, await f.Reader.FindHeadAsync("run"));
        Assert.Single((await f.Reader.ReadPageAsync(new("run"))).Items);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Localizes_provider_failure_and_malformed_content_without_losing_the_original_cause(bool malformed)
    {
        var f = await Fixture.CreateAsync();
        Exception cause = malformed ? new InvalidDataException("broken native content") : new IOException("provider unavailable");
        var writer = new InMemoryRuntimeHistoryObservationWriter(f.State, f.Access, f.Executions, new BrokenClock(cause));
        var before = await f.Reader.FindHeadAsync("run");
        var failure = await Record.ExceptionAsync(async () => await writer.RecordAsync(Observation()));
        if (malformed) Assert.IsType<RuntimeHistoryIntegrityException>(failure);
        else
        {
            var unavailable = Assert.IsType<RuntimeHistoryUnavailableException>(failure);
            Assert.Equal("record-observation", unavailable.Operation);
            Assert.Equal("run", unavailable.WorkflowExecutionId);
        }
        Assert.Same(cause, failure!.InnerException);
        Assert.Equal(before, await f.Reader.FindHeadAsync("run"));
        Assert.Single((await f.Reader.ReadPageAsync(new("run"))).Items);
    }

    private static RuntimeHistoryObservation Observation(RuntimeHistoryEvent? e = null) => new(Policy, e ?? Event());
    private static RuntimeHistoryEvent Event(string id = "observation", bool? known = null, IReadOnlyDictionary<string, string>? metadata = null, string? bookmark = null) =>
        new(id, "run", 0, RuntimeHistoryEventCategory.DeliveryObservation, "DeliveryReceived", At, metadata: metadata)
        { Authority = RuntimeHistoryAuthority.DeliveryObservation, Causation = new(DeliveryId: "delivery"), OccurredAtKnown = known, BookmarkId = bookmark };
    private static RuntimeCheckpointCommit Commit(string id, bool starts = false, RuntimeHistoryCapturePolicy? policy = null) => new(id,
        new("checkpoint-" + id, "test", "run", At, [], new Dictionary<string, string>()),
        new(null, null, [], [], [], [], []), [], new Dictionary<string, string>())
        { HistoryBatches = [new(policy ?? Policy, id, At, starts, [new(new RuntimeHistoryEvent("event-" + id, "run", 0,
            RuntimeHistoryEventCategory.ActivityLifecycle, "Transition", At, checkpointId: id)
            { Authority = RuntimeHistoryAuthority.CommittedTransition }, 0)])] };
    private static HmacRuntimeRecoveryContinuationCodec Codec() => new(Options.Create(new RuntimeRecoveryContinuationOptions
        { SigningKey = "observation-history-tests-signing-key-at-least-32-bytes" }));
    private sealed class Access : IPersistenceAccessContextAccessor
    { public PersistenceAccessContext Current { get; set; } = PersistenceAccessContext.Scoped(new("scope-a")); }
    private sealed class BrokenClock(Exception failure) : TimeProvider
    { public override DateTimeOffset GetUtcNow() => throw failure; }
    private sealed class Lease : IWorkflowExecutableRootWriteLeaseManager
    {
        public ValueTask ExecuteAsync(string artifactId, string leaseId, Func<CancellationToken, ValueTask> write, CancellationToken cancellationToken = default) => write(cancellationToken);
    }
    private sealed class Fixture
    {
        internal InMemoryRuntimeCheckpointStoreState State { get; } = new();
        internal Access Access { get; } = new();
        internal FakeTimeProvider Clock { get; } = new(At.AddHours(1));
        internal InMemoryWorkflowExecutionStateStore Executions { get; }
        internal InMemoryRuntimeCheckpointCommitStore Store { get; }
        internal InMemoryRuntimeHistoryStore Reader { get; }
        internal InMemoryRuntimeHistoryObservationWriter Writer { get; }
        private Fixture()
        {
            Executions = new(State, null);
            Store = new(workflowExecutionStateStore: Executions, state: State, persistenceAccessContextAccessor: Access, timeProvider: Clock, rootWriteLeaseManager: new Lease());
            Reader = new(State, Access, Codec());
            Writer = new(State, Access, Executions, Clock);
        }
        internal static async Task<Fixture> CreateAsync(bool seedHead = true, RuntimeHistoryCapturePolicy? policy = null)
        {
            var f = new Fixture();
            await f.Executions.SaveAsync(new("run", new("artifact", "definition", "version", "1", "hash"), WorkflowExecutionStatus.Running,
                null, At, At, At, null, null, null, null, new Dictionary<string, string>())
                { HistoryCapturePolicy = policy ?? Policy, HistoryCaptureEnabledAtAdmission = true });
            if (seedHead) await f.Store.CommitAsync(Commit("seed", starts: true, policy: policy), Immediate);
            return f;
        }
    }
}
