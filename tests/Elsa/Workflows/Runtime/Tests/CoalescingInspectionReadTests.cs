using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Elsa.Workflows.Runtime.Core.Services.Coalescing;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

// Store-read coalescing for the inspection store (spec-110 family, re-aimed from the spec-130 KILL): the coalescing
// overlay memoizes the DURABLE BASELINE per activity execution — never the session's buffered state, which would flip
// the accumulator's FromState/Merge branch — so mid-segment BuildProjectionAsync calls stop paying a durable FindAsync
// per hop while every read, every flushed checkpoint commit, and every flushed inspection document stays byte-identical
// to the per-hop-read control (CoalesceInspectionReads = false).
public sealed class CoalescingInspectionReadTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 22, 12, 0, 0, TimeSpan.Zero);
    private const string WorkflowExecutionId = "wfexec-1";

    [Fact]
    public async Task MemoizedInspectionReads_AreByteIdenticalToPerHopReads_AndCollapseDurableReadCount()
    {
        var control = await RunScenarioAsync(coalesceInspectionReads: false);
        var memoized = await RunScenarioAsync(coalesceInspectionReads: true);

        // Byte-identical guardrails, OFF path as the control: every projection the accumulator built, every checkpoint
        // commit that reached the durable store, and the final durable inspection documents.
        Assert.Equal(control.BuiltProjections, memoized.BuiltProjections);
        Assert.Equal(control.FlushedCommits, memoized.FlushedCommits);
        Assert.Equal(control.FinalDurableProjections, memoized.FinalDurableProjections);

        // The control pays one durable read per build (7). The memo pays one per distinct activity per coalesced
        // window: segment 1 reads actexec-1/actexec-2/actexec-prior once each, the flush invalidates the baselines,
        // and segment 2 re-reads actexec-1 once (its second build hits the memo again).
        Assert.Equal(control.BuildCount, control.DurableReadCount);
        Assert.Equal(7, control.DurableReadCount);
        Assert.Equal(4, memoized.DurableReadCount);
    }

    [Fact]
    public async Task WithoutActiveSession_OverlayIsPassThrough_EvenWithMemoEnabled()
    {
        var durable = new InMemoryActivityExecutionInspectionStore();
        var options = new CoalescingRuntimeCheckpointPersistenceOptions { CoalesceInspectionReads = true };
        var session = new RuntimeCoalescingSession(WorkflowExecutionId, new InMemoryWorkflowSchedulerWorkQueue(), options);
        session.Deactivate();
        var counting = new CountingInspectionStore(durable);
        var overlay = new CoalescingActivityExecutionInspectionStore(
            new CoalescingInner<IActivityExecutionInspectionStore>(counting),
            new FixedCoalescingSessionAccessor(session),
            options);

        Assert.Null(await overlay.FindAsync(WorkflowExecutionId, "actexec-1"));
        Assert.Null(await overlay.FindAsync(WorkflowExecutionId, "actexec-1"));

        // A deactivated session never serves the memo, so out-of-drain readers (and post-terminal reads) keep hitting
        // the durable store directly.
        Assert.Equal(2, counting.FindCount);
    }

    private static async Task<ScenarioResult> RunScenarioAsync(bool coalesceInspectionReads)
    {
        var durableInspections = new InMemoryActivityExecutionInspectionStore();
        var countingInspections = new CountingInspectionStore(durableInspections);
        var innerCommits = new InMemoryRuntimeCheckpointCommitStore(
            activityExecutionStateStore: new InMemoryActivityExecutionStateStore(),
            activityExecutionInspectionWriter: durableInspections);
        var options = new CoalescingRuntimeCheckpointPersistenceOptions { CoalesceInspectionReads = coalesceInspectionReads };
        var session = new RuntimeCoalescingSession(WorkflowExecutionId, new InMemoryWorkflowSchedulerWorkQueue(), options);
        var accessor = new FixedCoalescingSessionAccessor(session);
        var commitStore = new CoalescingRuntimeCheckpointCommitStore(
            new CoalescingInner<IRuntimeCheckpointCommitStore>(innerCommits), accessor);
        var overlay = new CoalescingActivityExecutionInspectionStore(
            new CoalescingInner<IActivityExecutionInspectionStore>(countingInspections), accessor, options);
        var accumulator = new RuntimeActivityExecutionInspectionAccumulator(overlay);

        // A projection flushed by an earlier drain: its build below must take the Merge path off the durable row.
        await durableInspections.SaveAsync(ActivityExecutionInspectionProjection.FromState(
            NewActivityState("actexec-prior", ActivityExecutionStatus.Running, sequence: 1),
            "checkpoint-prior-drain",
            Now.AddMinutes(-5)));

        var built = new List<string>();
        var checkpoint = 0;

        async Task HopAsync(string activityExecutionId, ActivityExecutionStatus status, long sequence)
        {
            checkpoint++;
            var state = NewActivityState(activityExecutionId, status, sequence);
            var projection = await accumulator.BuildProjectionAsync(state, $"checkpoint-{checkpoint:D2}", Now.AddTicks(checkpoint));
            built.Add(JsonSerializer.Serialize(projection));
            await CommitPreparedAsync(
                NewHopCommit(checkpoint, state, projection),
                new(RuntimeCheckpointPersistenceMode.Deferred));
        }

        async Task CommitPreparedAsync(
            RuntimeCheckpointCommit commit,
            RuntimeCheckpointPersistenceDecision decision)
        {
            var preparation = await commitStore.PrepareAsync(
                RuntimeCheckpointPrepareRequest.From(commit) with { InitialPersistenceMode = decision.Mode });
            var token = Assert.IsType<RuntimeCheckpointPreparationToken>(preparation.Token);
            var preparedCommit = commit with
            {
                Checkpoint = commit.Checkpoint with { Provenance = token.Provenance },
                ExpectedFence = token.ExpectedFence
            };
            var result = await commitStore.CommitPreparedAsync(token, preparedCommit, decision);
            Assert.True(result.Status is RuntimeCheckpointCommitStoreStatus.Committed or RuntimeCheckpointCommitStoreStatus.Replay);
        }

        // Segment 1: two builds per activity — the first build of each pair is the intermediate the fold discards,
        // and its durable read is what the memo absorbs on the second build.
        await HopAsync("actexec-1", ActivityExecutionStatus.Running, 2);
        await HopAsync("actexec-1", ActivityExecutionStatus.Completed, 2);
        await HopAsync("actexec-2", ActivityExecutionStatus.Running, 3);
        await HopAsync("actexec-2", ActivityExecutionStatus.Completed, 3);
        await HopAsync("actexec-prior", ActivityExecutionStatus.Completed, 1);

        // Mid-drain attempt boundary: flushes the folded segment durably and keeps the session active. The flush must
        // invalidate the memo so segment 2 observes the durably flushed rows, exactly like the per-hop-read control.
        checkpoint++;
        await CommitPreparedAsync(
            NewCommit(checkpoint, RuntimeCheckpointNames.ActivityAttemptClaimed),
            new(RuntimeCheckpointPersistenceMode.Immediate));
        Assert.True(session.IsActive);

        // Segment 2: the first build re-reads the flushed durable row (Merge path); the second hits the memo.
        await HopAsync("actexec-1", ActivityExecutionStatus.Running, 4);
        await HopAsync("actexec-1", ActivityExecutionStatus.Completed, 4);

        // Terminal boundary folds segment 2 and deactivates the session.
        checkpoint++;
        await CommitPreparedAsync(
            NewCommit(checkpoint, RuntimeCheckpointNames.WorkflowCompleted),
            new(RuntimeCheckpointPersistenceMode.Immediate));
        Assert.False(session.IsActive);

        var flushedCommits = innerCommits.ListCommits()
            .Select(record => record.Commit)
            .OrderBy(commit => commit.CommitId, StringComparer.Ordinal)
            .Select(commit => JsonSerializer.Serialize(commit))
            .ToList();

        var finalDurable = new List<string>();
        foreach (var activityExecutionId in new[] { "actexec-1", "actexec-2", "actexec-prior" })
        {
            var projection = await durableInspections.FindAsync(WorkflowExecutionId, activityExecutionId);
            Assert.NotNull(projection);
            finalDurable.Add(JsonSerializer.Serialize(projection));
        }

        return new ScenarioResult(built, flushedCommits, finalDurable, built.Count, countingInspections.FindCount);
    }

    private sealed record ScenarioResult(
        IReadOnlyList<string> BuiltProjections,
        IReadOnlyList<string> FlushedCommits,
        IReadOnlyList<string> FinalDurableProjections,
        int BuildCount,
        int DurableReadCount);

    private static ActivityExecutionState NewActivityState(string activityExecutionId, ActivityExecutionStatus status, long sequence) =>
        new(
            Execution: new ActivityExecution(
                ActivityExecutionId: activityExecutionId,
                WorkflowExecutionId: WorkflowExecutionId,
                ExecutableNodeId: $"node-{activityExecutionId}",
                AuthoredActivityId: $"authored-{activityExecutionId}",
                ActivityType: "test/activity",
                ActivityTypeVersion: "1.0.0"),
            Status: status,
            SubStatus: null,
            ExecutionSequence: sequence,
            ScheduledAt: Now,
            StartedAt: Now,
            CompletedAt: status == ActivityExecutionStatus.Completed ? Now.AddSeconds(sequence) : null,
            SchedulingActivityExecutionId: null,
            ParentActivityExecutionId: null,
            BranchId: "branch-1",
            IterationId: null,
            Provenance: ActivitySchedulingProvenance.Empty,
            CallStackDepth: 0,
            BookmarkIds: [],
            IncidentIds: [],
            FaultCount: 0,
            AggregateFaultCount: 0,
            Metadata: new Dictionary<string, string>());

    private static RuntimeCheckpointCommit NewCommit(int checkpoint, string checkpointName) =>
        new(
            CommitId: $"commit-{checkpoint:D2}",
            Checkpoint: new RuntimeCheckpoint(
                CheckpointId: $"checkpoint-{checkpoint:D2}",
                Name: checkpointName,
                WorkflowExecutionId: WorkflowExecutionId,
                OccurredAt: Now.AddTicks(checkpoint),
                ActivityExecutionIds: [],
                Metadata: new Dictionary<string, string>()),
            StateChanges: new RuntimeCheckpointStateChangeSet(
                workflowExecution: null,
                scheduler: null,
                activityExecutions: [],
                bookmarks: [],
                durableValues: [],
                incidents: [],
                operational: []),
            PostCommitIntents: [],
            Metadata: new Dictionary<string, string>());

    private static RuntimeCheckpointCommit NewHopCommit(
        int checkpoint,
        ActivityExecutionState state,
        ActivityExecutionInspectionProjection projection) =>
        NewCommit(checkpoint, RuntimeCheckpointNames.ActivityCompleted) with
        {
            StateChanges = new RuntimeCheckpointStateChangeSet(
                workflowExecution: null,
                scheduler: null,
                activityExecutions:
                [
                    new RuntimeStateChange<ActivityExecutionState>(
                        state.Execution.ActivityExecutionId,
                        RuntimeStateChangeOperation.Upsert,
                        state,
                        new Dictionary<string, string>())
                ],
                bookmarks: [],
                durableValues: [],
                incidents: [],
                operational: [],
                activityExecutionInspections:
                [
                    new RuntimeStateChange<ActivityExecutionInspectionProjection>(
                        projection.ActivityExecutionId,
                        RuntimeStateChangeOperation.Upsert,
                        projection,
                        new Dictionary<string, string>())
                ])
        };

    private sealed class CountingInspectionStore(IActivityExecutionInspectionStore inner) : IActivityExecutionInspectionStore
    {
        public int FindCount { get; private set; }

        public ValueTask<ActivityExecutionInspectionProjection?> FindAsync(string workflowExecutionId, string activityExecutionId, CancellationToken cancellationToken = default)
        {
            FindCount++;
            return inner.FindAsync(workflowExecutionId, activityExecutionId, cancellationToken);
        }

        public ValueTask<ActivityExecutionInspectionSummaryPage> ListSummariesPageAsync(ActivityExecutionInspectionSummaryPageQuery query, CancellationToken cancellationToken = default) =>
            inner.ListSummariesPageAsync(query, cancellationToken);
    }

    private sealed class FixedCoalescingSessionAccessor(RuntimeCoalescingSession session) : IRuntimeCoalescingSessionAccessor
    {
        public RuntimeCoalescingSession? Current => session;

        public IDisposable Push(RuntimeCoalescingSession? pushedSession) =>
            throw new NotSupportedException("This test provides a fixed ambient session.");
    }
}
