using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Elsa.Workflows.Runtime.Services.Coalescing;
using Elsa.Workflows.Runtime.Services.Executions;
using Elsa.Workflows.Runtime.Services.Scheduler;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class RuntimeHistoryCheckpointTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 26, 0, 0, 0, TimeSpan.Zero);
    private static readonly RuntimeHistoryCapturePolicy Policy = new("test-history", 1, true, ["Transition"]);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void History_free_commits_keep_the_existing_golden_fingerprint(bool explicitEmpty)
    {
        // Same envelope and compiled-parent golden as StimulusDeliverySnapshotTests.
        var legacy = new RuntimeCheckpointCommit("commit", new("checkpoint", "name", "waiter", At, [], new Dictionary<string, string>()),
            new(null, null, [], [], [], [], []), [], new Dictionary<string, string>())
        {
            HistoryBatches = explicitEmpty ? [] : null
        };
        Assert.Equal("af976cb76bbef3f226bc15cbbdae5d8b330ec98ae9f287ff18e15aadc2df1f1a", RuntimeCheckpointCommitFingerprint.Compute(legacy));
        if (!explicitEmpty)
            Assert.False(JsonSerializer.SerializeToElement(legacy).TryGetProperty("HistoryBatches", out _));
    }

    [Fact]
    public void Sidecar_snapshots_preserve_all_state_clone_paths_and_roundtrip_identity()
    {
        var batch = Batch("source");
        var supplied = new List<RuntimeHistoryBatch> { batch };
        var commit = Commit("source") with { HistoryBatches = supplied };
        supplied.Clear();
        var changes = commit.StateChanges;
        var variants = new[]
        {
            changes.WithActivityExecutions([]), changes.WithPostCommitOutbox([]), changes.WithWorkflowDispatches([]),
            changes.WithWorkflowDispatchCancellations([]), changes.WithConsumedSchedulerWorkItems([]),
            changes.WithAlterationJobTerminalChange(null), changes.WithBackgroundActivityWork(BackgroundActivityWorkCheckpointChanges.Empty)
        };
        Assert.All(variants, variant => Assert.Same(batch, Assert.Single((commit with { StateChanges = variant }).HistoryBatches!)));
        var copy = JsonSerializer.Deserialize<RuntimeHistoryBatch>(JsonSerializer.Serialize(batch))!;
        Assert.Equal(RuntimeCheckpointCommitFingerprint.Compute(commit), RuntimeCheckpointCommitFingerprint.Compute(commit with { HistoryBatches = [copy] }));
        Assert.NotEqual(RuntimeCheckpointCommitFingerprint.Compute(commit), RuntimeCheckpointCommitFingerprint.Compute(commit with { HistoryBatches = null }));
    }

    [Fact]
    public void Ordered_logical_groups_participate_in_replay_identity()
    {
        var commit = Commit("physical");
        var first = Batch("first");
        var second = Batch("second");
        var ordered = commit with { HistoryBatches = [first, second] };
        Assert.NotEqual(RuntimeCheckpointCommitFingerprint.Compute(ordered), RuntimeCheckpointCommitFingerprint.Compute(commit with { HistoryBatches = [second, first] }));
        Assert.NotEqual(RuntimeCheckpointCommitFingerprint.Compute(ordered), RuntimeCheckpointCommitFingerprint.Compute(commit with { HistoryBatches = [first] }));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Actual_boundary_and_quiescence_flush_preserve_every_ordered_group(bool quiescence)
    {
        var reader = new UnusedReader();
        var writer = new RecordingWriter(reader);
        var sessions = new AsyncLocalRuntimeCoalescingSessionAccessor();
        var decorated = new CoalescingRuntimeCheckpointCommitStore(new(writer), sessions);
        var committer = Committer(decorated, reader);
        var factory = Factory(sessions, committer);
        await using var scope = factory.Begin("run", new(PersistenceMode.Balanced, true));
        await decorated.CommitAsync(Captured("first"), new(RuntimeCheckpointPersistenceMode.Deferred));
        Assert.Empty(writer.Commits);
        await decorated.CommitAsync(Captured("second"), new(quiescence ? RuntimeCheckpointPersistenceMode.Deferred : RuntimeCheckpointPersistenceMode.Immediate));
        if (quiescence) await scope.FlushAtQuiescenceAsync();

        var physical = Assert.Single(writer.Commits);
        Assert.Equal(new[] { "checkpoint-first", "checkpoint-second" }, physical.HistoryBatches!.Select(batch => batch.LogicalCheckpointId));
        Assert.Equal(new[] { "event-first", "event-second" }, physical.HistoryBatches.SelectMany(batch => batch.Candidates).Select(candidate => candidate.Event.HistoryEventId));
        Assert.Null(scope.Session.FoldHistoryBatchesWith());
        Assert.Null(typeof(RuntimeCheckpointStateChangeSet).GetProperty("HistoryBatches"));
    }

    [Fact]
    public async Task Never_recorded_ephemeral_completion_can_discard_history_without_an_extra_write()
    {
        var reader = new UnusedReader();
        var writer = new RecordingWriter(reader);
        var sessions = new AsyncLocalRuntimeCoalescingSessionAccessor();
        var decorated = new CoalescingRuntimeCheckpointCommitStore(new(writer), sessions);
        var factory = Factory(sessions, Committer(decorated, reader), recordCompletion: false);
        await using var scope = factory.Begin("run", new(PersistenceMode.Ephemeral, false));
        await decorated.CommitAsync(Captured("first"), new(RuntimeCheckpointPersistenceMode.Deferred));
        var completion = Captured("completion") with
        {
            Checkpoint = Commit("completion").Checkpoint with { Name = RuntimeCheckpointNames.WorkflowCompleted },
            StateChanges = new(new("run", RuntimeStateChangeOperation.Upsert, Completed(), new Dictionary<string, string>()), null, [], [], [], [], [])
        };
        await decorated.CommitAsync(completion, new(RuntimeCheckpointPersistenceMode.Immediate));

        Assert.Empty(writer.Commits);
        Assert.False(scope.Session.IsActive);
        Assert.Null(scope.Session.FoldHistoryBatchesWith());
    }

    [Fact]
    public void Capability_defaults_refuse_legacy_writers_and_decorator_preserves_exact_pairing()
    {
        IRuntimeCheckpointCommitStore legacy = new LegacyWriter();
        var selected = new UnusedReader();
        Assert.False(legacy.SupportsHistory);
        Assert.False(legacy.CanCommitHistory(selected));
        var writer = new RecordingWriter(selected);
        var decorated = new CoalescingRuntimeCheckpointCommitStore(new(writer), new AsyncLocalRuntimeCoalescingSessionAccessor());
        Assert.True(decorated.SupportsHistory);
        Assert.True(decorated.CanCommitHistory(selected));
        Assert.False(decorated.CanCommitHistory(new UnusedReader()));
    }

    [Theory]
    [InlineData("legacy-writer")]
    [InlineData("missing-reader")]
    [InlineData("unrelated-reader")]
    public async Task Unqualified_capture_is_refused_before_the_writer_sees_effects(string missing)
    {
        var selected = new UnusedReader();
        var writer = new RecordingWriter(selected);
        IRuntimeCheckpointCommitStore target = missing == "legacy-writer" ? new LegacyWriter() : writer;
        var committer = Committer(target, missing == "missing-reader" ? null : missing == "unrelated-reader" ? new UnusedReader() : selected);

        var error = await Assert.ThrowsAsync<RuntimeSchedulerWorkUnavailableException>(async () => await committer.CommitAsync(Captured("source")));
        Assert.Equal("runtime.history.capture-unavailable", error.Code);
        Assert.Empty(writer.Commits);
        if (target is LegacyWriter legacy) Assert.Equal(0, legacy.Calls);
    }

    [Fact]
    public async Task History_free_commit_never_resolves_the_optional_history_reader()
    {
        var writer = new LegacyWriter();
        var committer = new RuntimeCheckpointCommitter(new ImmediateRuntimeCheckpointPersistencePolicy(), writer,
            new AsyncLocalRuntimeExecutionOwnershipContextAccessor(), [], [])
        {
            HistoryStoreResolver = () => throw new InvalidOperationException("History resolution must remain lazy")
        };
        Assert.True((await committer.CommitAsync(Commit("legacy"))).Succeeded);
        Assert.Equal(1, writer.Calls);
    }

    private static RuntimeCheckpointCommit Commit(string id) => new(id,
        new("checkpoint-" + id, "test", "run", At, [], new Dictionary<string, string>()),
        new(null, null, [], [], [], [], []), [], new Dictionary<string, string>());

    private static RuntimeHistoryBatch Batch(string id) => new(Policy, "checkpoint-" + id, At, false,
        [new(new RuntimeHistoryEvent("event-" + id, "run", 0, RuntimeHistoryEventCategory.ActivityLifecycle,
            "Transition", At, checkpointId: "checkpoint-" + id) { Authority = RuntimeHistoryAuthority.CommittedTransition }, 0)]);

    private static RuntimeCheckpointCommit Captured(string id) => Commit(id) with { HistoryBatches = [Batch(id)] };

    private static WorkflowExecutionState Completed() => new("run", new("artifact", "definition", "version", "1", "hash"),
        WorkflowExecutionStatus.Completed, null, At, At, At, At, null, null, null, new Dictionary<string, string>());

    private static RuntimeCheckpointCommitter Committer(IRuntimeCheckpointCommitStore writer, IRuntimeHistoryStore? reader) =>
        new(new ImmediateRuntimeCheckpointPersistencePolicy(), writer, new AsyncLocalRuntimeExecutionOwnershipContextAccessor(), [], []) { HistoryStore = reader };

    private static RuntimeCoalescingDrainScopeFactory Factory(AsyncLocalRuntimeCoalescingSessionAccessor sessions,
        RuntimeCheckpointCommitter committer, bool recordCompletion = true) =>
        new(sessions, committer, new(new InMemoryWorkflowSchedulerWorkQueue()), new(new InMemoryRuntimeCheckpointCommitStore()),
            new PersistenceModeOptions { RecordCompletion = recordCompletion }, new FakeTimeProvider(At));

    private sealed class RecordingWriter(IRuntimeHistoryStore reader) : IRuntimeCheckpointCommitStore
    {
        public List<RuntimeCheckpointCommit> Commits { get; } = [];
        public bool SupportsHistory => true;
        public bool CanCommitHistory(IRuntimeHistoryStore selectedStore) => ReferenceEquals(reader, selectedStore);
        public ValueTask<RuntimeCheckpointCommitStoreResult> CommitAsync(RuntimeCheckpointCommit commit, RuntimeCheckpointPersistenceDecision decision, CancellationToken cancellationToken = default)
        {
            Commits.Add(commit);
            return ValueTask.FromResult(new RuntimeCheckpointCommitStoreResult([]));
        }
    }

    private sealed class LegacyWriter : IRuntimeCheckpointCommitStore
    {
        public int Calls { get; private set; }
        public ValueTask<RuntimeCheckpointCommitStoreResult> CommitAsync(RuntimeCheckpointCommit commit, RuntimeCheckpointPersistenceDecision decision, CancellationToken cancellationToken = default)
        {
            Calls++;
            return ValueTask.FromResult(new RuntimeCheckpointCommitStoreResult([]));
        }
    }

    // Reader methods must not be used by these write-side contract tests. Pairing is established by the writer.
    private sealed class UnusedReader : IRuntimeHistoryStore
    {
        public ValueTask<RuntimeHistoryHead?> FindHeadAsync(string workflowExecutionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<RuntimeHistoryPage> ReadPageAsync(RuntimeHistoryQuery query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<RuntimeHistoryEvent?> FindEventAsync(string workflowExecutionId, string historyEventId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<RuntimeHistoryBookmarkPage> ListBookmarksAsync(RuntimeHistoryBookmarkQuery query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<RuntimeHistoryBookmark?> FindBookmarkAsync(string workflowExecutionId, string bookmarkId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
