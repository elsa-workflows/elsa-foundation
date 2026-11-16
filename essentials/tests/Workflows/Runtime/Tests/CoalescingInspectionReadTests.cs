using System.Text.Json;
using Elsa.Workflows.Runtime.Contracts;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.ActivityExecutions;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Elsa.Workflows.Runtime.Services.Coalescing;
using Elsa.Workflows.Runtime.Services.Scheduler;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

// Inspection reads during a folding session: the overlay serves the projection the session already recorded for an
// activity execution, so each activity's durable row is read at most once per drain, and a later build merges onto what
// the session recorded rather than onto the durable baseline.
public sealed class CoalescingInspectionReadTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 22, 12, 0, 0, TimeSpan.Zero);
    private const string WorkflowExecutionId = "wfexec-1";

    [Fact]
    public async Task InspectionReads_ReadEachActivitysDurableRowOnce_AndBuildOnWhatTheSessionRecorded()
    {
        var run = await RunScenarioAsync();

        // A build first takes the projection the session already recorded for its activity, so of the seven builds only
        // the first build of each activity reads the store: actexec-1, actexec-2 and actexec-prior once each. Segment 2
        // builds onto what segment 1 recorded for actexec-1, across the durable flush between them.
        Assert.Equal(7, run.BuildCount);
        Assert.Equal(3, run.DurableReadCount);
        Assert.Equal(3, run.FinalDurableProjections.Count);
    }

    [Fact]
    public async Task WithoutActiveSession_OverlayIsPassThrough()
    {
        var durable = new InMemoryActivityExecutionInspectionStore();
        var session = new RuntimeCoalescingSession(WorkflowExecutionId, new InMemoryWorkflowSchedulerWorkQueue(), new PersistenceModeOptions());
        session.Deactivate();
        var counting = new CountingInspectionStore(durable);
        var overlay = new CoalescingActivityExecutionInspectionStore(
            new CoalescingInner<IActivityExecutionInspectionStore>(counting),
            new FixedCoalescingSessionAccessor(session));

        Assert.Null(await overlay.FindAsync(WorkflowExecutionId, "actexec-1"));
        Assert.Null(await overlay.FindAsync(WorkflowExecutionId, "actexec-1"));

        // A deactivated session serves nothing, so out-of-drain readers (and post-terminal reads) keep hitting the
        // durable store directly.
        Assert.Equal(2, counting.FindCount);
    }

    private static async Task<ScenarioResult> RunScenarioAsync()
    {
        var durableInspections = new InMemoryActivityExecutionInspectionStore();
        var countingInspections = new CountingInspectionStore(durableInspections);
        var innerCommits = new InMemoryRuntimeCheckpointCommitStore(activityExecutionInspectionWriter: durableInspections);
        var session = new RuntimeCoalescingSession(WorkflowExecutionId, new InMemoryWorkflowSchedulerWorkQueue(), new PersistenceModeOptions());
        var accessor = new FixedCoalescingSessionAccessor(session);
        var commitStore = new CoalescingRuntimeCheckpointCommitStore(
            new CoalescingInner<IRuntimeCheckpointCommitStore>(innerCommits), accessor);
        var overlay = new CoalescingActivityExecutionInspectionStore(
            new CoalescingInner<IActivityExecutionInspectionStore>(countingInspections), accessor);
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
            await commitStore.CommitAsync(
                NewHopCommit(checkpoint, state, projection),
                new(RuntimeCheckpointPersistenceMode.Deferred));
        }

        // Segment 1: two builds per activity. The first build of each pair is the intermediate the fold discards; the
        // second builds onto it.
        await HopAsync("actexec-1", ActivityExecutionStatus.Running, 2);
        await HopAsync("actexec-1", ActivityExecutionStatus.Completed, 2);
        await HopAsync("actexec-2", ActivityExecutionStatus.Running, 3);
        await HopAsync("actexec-2", ActivityExecutionStatus.Completed, 3);
        await HopAsync("actexec-prior", ActivityExecutionStatus.Completed, 1);

        // Mid-drain attempt boundary: flushes the folded segment durably and keeps the session active.
        checkpoint++;
        await commitStore.CommitAsync(
            NewCommit(checkpoint, RuntimeCheckpointNames.ActivityAttemptClaimed),
            new(RuntimeCheckpointPersistenceMode.Immediate));
        Assert.True(session.IsActive);

        // Segment 2: both builds merge onto the projection the session recorded for actexec-1 in segment 1.
        await HopAsync("actexec-1", ActivityExecutionStatus.Running, 4);
        await HopAsync("actexec-1", ActivityExecutionStatus.Completed, 4);

        // Terminal boundary folds segment 2 and deactivates the session.
        checkpoint++;
        await commitStore.CommitAsync(
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
