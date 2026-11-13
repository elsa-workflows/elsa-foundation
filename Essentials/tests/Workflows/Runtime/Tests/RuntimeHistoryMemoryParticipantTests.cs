using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Bookmarks;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Elsa.Workflows.Runtime.Services.Coalescing;
using Elsa.Workflows.Runtime.Services.Executions;
using Elsa.Workflows.Runtime.Services.History;
using Elsa.Workflows.Runtime.Services.Recovery;
using Elsa.Workflows.Runtime.Services.Scheduler;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

/// <summary>Physical in-memory participant tests with supplied logical groups; not native owner/EF/host qualification.</summary>
public sealed class RuntimeHistoryMemoryParticipantTests
{
    private static readonly DateTimeOffset At = DateTimeOffset.UnixEpoch;
    private static readonly RuntimeHistoryCapturePolicy Policy = new("runtime-history-v3", 3, true, ["Transition", "BookmarkCreated", "BookmarkRemoved"]);
    private static readonly RuntimeCheckpointPersistenceDecision Immediate = new(RuntimeCheckpointPersistenceMode.Immediate);

    [Fact]
    public async Task Logical_groups_share_one_physical_allocation_and_replay_without_new_positions()
    {
        var f = await Fixture.Create();
        var commit = Commit("physical", [Batch("first", starts: true), Batch("second")]);
        await f.Store.CommitAsync(commit, Immediate);
        var replay = await f.Store.CommitAsync(commit, Immediate);
        var page = await f.Reader.ReadPageAsync(new("run"));
        Assert.Equal(new long[] { 1, 2 }, page.Items.Select(e => e.Sequence));
        Assert.Equal(new[] { "first", "second" }, page.Items.Select(e => e.CheckpointId));
        Assert.All(page.Items, e => { Assert.Equal("physical", e.PhysicalCommitId); Assert.Equal(At.AddHours(1), e.RecordedAt); Assert.Equal(0, e.CheckpointOrdinal); });
        Assert.True(page.Head!.CapturedFromStart);
        Assert.Single(f.Store.ListCommits());
        Assert.Empty(replay.PendingPostCommitWorkIds);
    }

    [Fact]
    public async Task Concurrent_physical_groups_allocate_serial_positions_and_deduplicate_original_semantics()
    {
        var f = await Fixture.Create();
        var shared = Batch("shared");
        await Task.WhenAll(Enumerable.Range(0, 8).Select(async i => await f.Store.CommitAsync(Commit("commit-" + i, [shared, Batch("unique-" + i)]), Immediate)));
        var page = await f.Reader.ReadPageAsync(new("run"));
        Assert.Equal(Enumerable.Range(1, 9).Select(i => (long)i), page.Items.Select(e => e.Sequence));
        Assert.Single(page.Items.Where(e => e.CheckpointId == "shared"));
        Assert.Equal(8, f.Store.ListCommits().Count);
    }

    [Fact]
    public async Task Conflicting_marker_or_candidate_refuses_before_native_mutation_or_allocation()
    {
        var f = await Fixture.Create();
        var first = Commit("first", [Batch("logical")]);
        await f.Store.CommitAsync(first, Immediate);
        var head = await f.Reader.FindHeadAsync("run");
        await Assert.ThrowsAsync<RuntimeCheckpointReplayConflictException>(async () => await f.Store.CommitAsync(first with
        { Metadata = new Dictionary<string, string> { ["changed"] = "marker" } }, Immediate));
        var changed = Batch("logical", changed: true);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await f.Store.CommitAsync(Commit("conflict", [changed], completed: true), Immediate));
        Assert.Equal(head, await f.Reader.FindHeadAsync("run"));
        Assert.Equal(WorkflowExecutionStatus.Running, (await f.Executions.FindAsync("run"))!.Status);
        Assert.Single(f.Store.ListCommits());
    }

    [Fact]
    public async Task Missing_required_contribution_is_refused_even_without_capture_registration()
    {
        var f = await Fixture.Create();
        await f.Store.CommitAsync(Commit("start", [Batch("logical", starts: true)]), Immediate);
        var head = await f.Reader.FindHeadAsync("run");
        await Assert.ThrowsAsync<RuntimeSchedulerWorkUnavailableException>(async () => await f.Store.CommitAsync(Commit("missing", null, completed: true), Immediate));
        // No incoming workflow state either: the retained required index still protects this physical boundary.
        await Assert.ThrowsAsync<RuntimeSchedulerWorkUnavailableException>(async () => await f.Store.CommitAsync(Commit("missing-state", null), Immediate));
        Assert.Equal(head, await f.Reader.FindHeadAsync("run"));
        Assert.Equal(WorkflowExecutionStatus.Running, (await f.Executions.FindAsync("run"))!.Status);
        Assert.Single(f.Store.ListCommits());
    }

    [Fact]
    public async Task Native_legacy_absence_prevents_retroactive_history_even_with_a_supplied_batch()
    {
        var f = await Fixture.Create(policy: null);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await f.Store.CommitAsync(Commit("retroactive", [Batch("logical")], completed: true), Immediate));
        Assert.Null(await f.Reader.FindHeadAsync("run"));
        Assert.Empty(f.Store.ListCommits());
        Assert.Null((await f.Executions.FindAsync("run"))!.HistoryCapturePolicy);
    }

    [Fact]
    public async Task Writer_qualification_requires_exact_state_native_owner_and_access_accessor()
    {
        var f = await Fixture.Create();
        IRuntimeCheckpointCommitStore writer = f.Store;
        Assert.True(writer.SupportsHistory);
        Assert.True(writer.CanCommitHistory(f.Reader));
        Assert.False(writer.CanCommitHistory(new InMemoryRuntimeHistoryStore(new(), f.Access, Codec())));
        Assert.False(writer.CanCommitHistory(new InMemoryRuntimeHistoryStore(f.State, new Access(), Codec())));
        IRuntimeCheckpointCommitStore incoherent = new InMemoryRuntimeCheckpointCommitStore(workflowExecutionStateStore: new InMemoryWorkflowExecutionStateStore(), state: f.State, persistenceAccessContextAccessor: f.Access);
        Assert.False(incoherent.SupportsHistory);
        Assert.False(incoherent.CanCommitHistory(f.Reader));
        await Assert.ThrowsAsync<RuntimeSchedulerWorkUnavailableException>(async () => await incoherent.CommitAsync(Commit("wrong-native", [Batch("logical")]), Immediate));
        Assert.Empty(f.Store.ListCommits());
    }

    [Fact]
    public async Task Captured_replay_and_cursors_cannot_cross_scopes_but_ordinary_legacy_markers_stay_plain()
    {
        var f = await Fixture.Create();
        var captured = Commit("physical", [Batch("first"), Batch("second")]);
        await f.Store.CommitAsync(captured, Immediate);
        var page = await f.Reader.ReadPageAsync(new("run", limit: 1));
        f.Access.Current = PersistenceAccessContext.Scoped(new("scope-b"));
        Assert.Null(await f.Reader.FindHeadAsync("run"));
        Assert.Null(await f.Reader.FindEventAsync("run", "event-first"));
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await f.Store.CommitAsync(captured, Immediate));
        await Assert.ThrowsAsync<ArgumentException>(async () => await f.Reader.ReadPageAsync(new("run", limit: 1, continuationToken: page.NextContinuationToken)));
        f.Access.Current = PersistenceAccessContext.Global;
        await Assert.ThrowsAsync<RuntimeSchedulerWorkUnavailableException>(async () => await f.Store.CommitAsync(Commit("unscoped-required", null), Immediate));
        var legacy = Commit("legacy", null) with { Checkpoint = new("legacy-checkpoint", "test", "other-run", At, [], new Dictionary<string, string>()) };
        await f.Store.CommitAsync(legacy, Immediate);
        await f.Store.CommitAsync(legacy, Immediate);
        Assert.Equal(2, f.Store.ListCommits().Count);
    }

    [Fact]
    public async Task Missing_unpurged_content_is_not_silently_recreated_from_a_semantic_receipt()
    {
        var f = await Fixture.Create();
        var batch = Batch("logical");
        await f.Store.CommitAsync(Commit("first", [batch]), Immediate);
        f.State.HistoryEventsById.Remove(("scope-a", "run", "event-logical"));
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await f.Store.CommitAsync(Commit("second", [batch], completed: true), Immediate));
        Assert.Single(f.Store.ListCommits());
        Assert.Equal(WorkflowExecutionStatus.Running, (await f.Executions.FindAsync("run"))!.Status);
    }

    [Fact]
    public async Task Exact_cleanup_inventory_must_still_match_the_native_bookmark_before_any_write()
    {
        var f = await Fixture.Create();
        var summary = new ActivityExecutionBookmarkSummary(new("bookmark", "run", "activity", "resume"), "task", "hash", At, null);
        var e = new RuntimeHistoryEvent("removed", "run", 0, RuntimeHistoryEventCategory.BookmarkLifecycle, "BookmarkRemoved", At,
            "activity", "node", "cleanup") { Authority = RuntimeHistoryAuthority.CommittedTransition, BookmarkId = "bookmark" };
        var batch = new RuntimeHistoryBatch(Policy, "cleanup", At, false, [new(e, 0, new(summary, RuntimeHistoryBookmarkStatus.Removed))]);
        var raw = Commit("cleanup-physical", [batch], completed: true);
        raw = raw with { StateChanges = new(raw.StateChanges.WorkflowExecution, null, [], [], [], [], [],
            activityScopeCleanups: [new("run", "activity", ["activity"], ["bookmark"], [], []) { HistoryBookmarks = [summary] }]) };
        // There is no native bookmark and no earlier creation in this physical group.
        await Assert.ThrowsAsync<RuntimeSchedulerWorkUnavailableException>(async () => await f.Store.CommitAsync(raw, Immediate));
        Assert.Null(await f.Reader.FindHeadAsync("run"));
        Assert.Equal(WorkflowExecutionStatus.Running, (await f.Executions.FindAsync("run"))!.Status);
        Assert.Empty(f.Store.ListCommits());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Real_coalescing_boundary_or_quiescence_preserves_logical_groups_in_one_receipt(bool quiescence)
    {
        var f = await Fixture.Create();
        var sessions = new AsyncLocalRuntimeCoalescingSessionAccessor();
        var decorated = new CoalescingRuntimeCheckpointCommitStore(new(f.Store), sessions);
        var committer = new RuntimeCheckpointCommitter(new ImmediateRuntimeCheckpointPersistencePolicy(), decorated,
            new AsyncLocalRuntimeExecutionOwnershipContextAccessor(), [], []) { HistoryStore = f.Reader };
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var factory = new RuntimeCoalescingDrainScopeFactory(sessions, committer, new(queue), new(f.Store), new PersistenceModeOptions(), f.Clock);
        await using var scope = factory.Begin("run", new(PersistenceMode.Balanced, true));
        await decorated.CommitAsync(Commit("first", [Batch("first")]), new(RuntimeCheckpointPersistenceMode.Deferred));
        Assert.Empty(f.Store.ListCommits());
        await decorated.CommitAsync(Commit("second", [Batch("second")]), new(quiescence ? RuntimeCheckpointPersistenceMode.Deferred : RuntimeCheckpointPersistenceMode.Immediate));
        if (quiescence) await scope.FlushAtQuiescenceAsync();
        var physical = Assert.Single(f.Store.ListCommits()).Commit;
        Assert.Equal(new[] { "first", "second" }, physical.HistoryBatches!.Select(b => b.LogicalCheckpointId));
        var page = await f.Reader.ReadPageAsync(new("run"));
        Assert.Equal(new long[] { 1, 2 }, page.Items.Select(e => e.Sequence));
        Assert.All(page.Items, e => Assert.Equal(physical.CommitId, e.PhysicalCommitId));
    }

    [Fact]
    public async Task Unknown_occurrence_and_bookmark_disposition_remain_separate_from_recorded_time()
    {
        var f = await Fixture.Create();
        var summary = new ActivityExecutionBookmarkSummary(new("bookmark", "run", "activity", "resume"), "task", "hash", At, null);
        RuntimeHistoryBatch Bookmark(string id, RuntimeHistoryBookmarkStatus status) => new(Policy, id, At, false,
            [new(new RuntimeHistoryEvent("event-" + id, "run", 0, RuntimeHistoryEventCategory.BookmarkLifecycle,
                status == RuntimeHistoryBookmarkStatus.Active ? "BookmarkCreated" : "BookmarkRemoved", At, "activity", "node", id)
                { Authority = RuntimeHistoryAuthority.CommittedTransition, BookmarkId = "bookmark", OccurredAtKnown = false }, 0, new(summary, status))]);
        await f.Store.CommitAsync(Commit("physical", [Bookmark("created", RuntimeHistoryBookmarkStatus.Active), Bookmark("removed", RuntimeHistoryBookmarkStatus.Removed)]), Immediate);
        var bookmark = await f.Reader.FindBookmarkAsync("run", "bookmark");
        Assert.Equal(RuntimeHistoryBookmarkStatus.Removed, bookmark!.Status);
        Assert.Equal("event-created", bookmark.CreatedHistoryEventId);
        Assert.Equal("event-removed", bookmark.EndedHistoryEventId);
        Assert.Equal(RuntimeHistoryCreationEvidence.CreationEvent, bookmark.PriorState);
        Assert.Null(bookmark.EndedAt);
        Assert.All((await f.Reader.ReadPageAsync(new("run"))).Items, e => { Assert.False(e.OccurredAtKnown); Assert.Equal(f.Clock.GetUtcNow(), e.RecordedAt); });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Historical_bookmark_cannot_change_its_definition_or_reactivate_a_disposed_handle(bool revive)
    {
        var f = await Fixture.Create();
        var summary = new ActivityExecutionBookmarkSummary(new("bookmark", "run", "activity", "resume"), "task", "hash", At, null);
        RuntimeHistoryBatch Bookmark(string id, RuntimeHistoryBookmarkStatus status, ActivityExecutionBookmarkSummary value) => new(Policy, id, At, false,
            [new(new RuntimeHistoryEvent("event-" + id, "run", 0, RuntimeHistoryEventCategory.BookmarkLifecycle,
                status == RuntimeHistoryBookmarkStatus.Active ? "BookmarkCreated" : "BookmarkRemoved", At, "activity", "node", id)
                { Authority = RuntimeHistoryAuthority.CommittedTransition, BookmarkId = "bookmark" }, 0, new(value, status))]);
        await f.Store.CommitAsync(Commit("initial", [Bookmark("created", RuntimeHistoryBookmarkStatus.Active, summary),
            Bookmark("removed", RuntimeHistoryBookmarkStatus.Removed, summary)]), Immediate);
        var before = await f.Reader.FindHeadAsync("run");
        var altered = revive ? summary : summary with { StimulusHash = "different-definition" };
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await f.Store.CommitAsync(Commit("invalid", [Bookmark("later",
            revive ? RuntimeHistoryBookmarkStatus.Active : RuntimeHistoryBookmarkStatus.Removed, altered)], completed: true), Immediate));
        Assert.Equal(before, await f.Reader.FindHeadAsync("run"));
        Assert.Equal(RuntimeHistoryBookmarkStatus.Removed, (await f.Reader.FindBookmarkAsync("run", "bookmark"))!.Status);
        Assert.Equal(WorkflowExecutionStatus.Running, (await f.Executions.FindAsync("run"))!.Status);
        Assert.Single(f.Store.ListCommits());
    }

    [Fact]
    public async Task Plain_commit_keeps_the_existing_read_path_and_does_not_resolve_native_policy()
    {
        var reader = new NoReads();
        var writer = new InMemoryRuntimeCheckpointCommitStore(workflowExecutionStateStore: reader);
        var plain = Commit("plain", null);
        await writer.CommitAsync(plain, Immediate);
        await writer.CommitAsync(plain, Immediate);
        Assert.Equal(0, reader.Reads);
        Assert.Single(writer.ListCommits());
    }

    private static RuntimeHistoryBatch Batch(string id, bool starts = false, bool changed = false) => new(Policy, id, At, starts,
        [new(new RuntimeHistoryEvent("event-" + id, "run", 0, RuntimeHistoryEventCategory.ActivityLifecycle, "Transition", At, checkpointId: id,
            metadata: changed ? new Dictionary<string, string> { ["history.status"] = "changed" } : null)
            { Authority = RuntimeHistoryAuthority.CommittedTransition }, 0)]);
    private static WorkflowExecutionState Workflow(RuntimeHistoryCapturePolicy? policy) => new("run", new("artifact", "definition", "version", "1", "hash"),
        WorkflowExecutionStatus.Running, null, At, At, At, null, null, null, null, new Dictionary<string, string>()) { HistoryCapturePolicy = policy };
    private static RuntimeCheckpointCommit Commit(string id, IReadOnlyList<RuntimeHistoryBatch>? batches, bool completed = false) => new(id,
        new("checkpoint-" + id, "test", "run", At, [], new Dictionary<string, string>()),
        new(completed ? new("run", RuntimeStateChangeOperation.Upsert, Workflow(Policy) with { Status = WorkflowExecutionStatus.Completed, CompletedAt = At }, new Dictionary<string, string>()) : null,
            null, [], [], [], [], []), [], new Dictionary<string, string>()) { HistoryBatches = batches };
    private static HmacRuntimeRecoveryContinuationCodec Codec() => new(Options.Create(new RuntimeRecoveryContinuationOptions { SigningKey = "memory-history-tests-signing-key-at-least-32-bytes" }));
    private sealed class Access : IPersistenceAccessContextAccessor
    { public PersistenceAccessContext Current { get; set; } = PersistenceAccessContext.Scoped(new("scope-a")); }
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
        private Fixture()
        {
            Executions = new(State, null);
            Store = new(workflowExecutionStateStore: Executions, state: State, persistenceAccessContextAccessor: Access, timeProvider: Clock, rootWriteLeaseManager: new Lease());
            Reader = new(State, Access, Codec());
        }
        internal static Task<Fixture> Create() => Create(Policy);
        internal static async Task<Fixture> Create(RuntimeHistoryCapturePolicy? policy)
        { var f = new Fixture(); await f.Executions.SaveAsync(Workflow(policy)); return f; }
    }
    private sealed class NoReads : IWorkflowExecutionStateStore
    {
        internal int Reads;
        public ValueTask<WorkflowExecutionState?> FindAsync(string id, CancellationToken cancellationToken = default) { Reads++; throw new InvalidOperationException("Unexpected history-free read."); }
        public ValueTask<WorkflowExecutionState> SaveAsync(WorkflowExecutionState state, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<IReadOnlyCollection<WorkflowExecutionState>> ListAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<WorkflowExecutionStatePage> QueryPageAsync(WorkflowExecutionStatePageQuery query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<IReadOnlyCollection<string>> ListPinnedExecutableArtifactIdsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<bool> DeleteAsync(string id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
