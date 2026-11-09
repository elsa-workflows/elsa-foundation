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

// Point reads during the drain of a run that has no durable record. One probe per store shows the store holds none of
// the run's rows; after it, a row the session has not sent to the store is not read. Every way a row can reach the
// store without the session answering for it sends its read back to the store.
public sealed class CoalescingEmptyBaselineReadTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);
    private const string WorkflowExecutionId = "wfexec-1";
    private static readonly ResolvedPersistenceMode NoRecord = new(PersistenceMode.Balanced, RunHasRecord: false);
    private static readonly ResolvedPersistenceMode HasRecord = new(PersistenceMode.Balanced, RunHasRecord: true);

    [Fact]
    public async Task RunWithNoRecord_ProbesEachStoreOnce_ThenReadsNoNewRow()
    {
        var fixture = new Fixture(NoRecord);

        foreach (var id in new[] { "actexec-1", "actexec-2", "actexec-3" })
        {
            Assert.Null(await fixture.Activities.FindAsync(WorkflowExecutionId, id));
            Assert.Null(await fixture.Inspections.FindAsync(WorkflowExecutionId, id));
        }

        Assert.Equal(1, fixture.DurableActivities.ProbeCount);
        Assert.Equal(0, fixture.DurableActivities.FindCount);
        Assert.Equal(1, fixture.DurableInspections.ProbeCount);
        Assert.Equal(0, fixture.DurableInspections.FindCount);
    }

    [Fact]
    public async Task RunWithARecord_IsNotProbed_AndReadsEveryRowFromTheStore()
    {
        var fixture = new Fixture(HasRecord);

        Assert.Null(await fixture.Activities.FindAsync(WorkflowExecutionId, "actexec-1"));
        Assert.Null(await fixture.Activities.FindAsync(WorkflowExecutionId, "actexec-2"));
        Assert.Null(await fixture.Inspections.FindAsync(WorkflowExecutionId, "actexec-1"));
        Assert.Null(await fixture.Inspections.FindAsync(WorkflowExecutionId, "actexec-2"));

        Assert.Equal(0, fixture.DurableActivities.ProbeCount);
        Assert.Equal(2, fixture.DurableActivities.FindCount);
        Assert.Equal(0, fixture.DurableInspections.ProbeCount);
        Assert.Equal(2, fixture.DurableInspections.FindCount);
    }

    // No record is not proof of no rows: a start that was lost, a direct save and a partly deleted run all leave rows
    // behind without one. The probe finds them, and the session then answers for nothing.
    [Fact]
    public async Task RunWithNoRecord_WhoseStoresHoldARow_ReadsEveryRowFromTheStore()
    {
        var fixture = new Fixture(NoRecord);
        var stray = NewActivityState("actexec-stray");
        await fixture.DurableActivities.Inner.SaveAsync(stray);
        await fixture.DurableInspections.Inner.SaveAsync(ActivityExecutionInspectionProjection.FromState(stray, "checkpoint-stray", Now));

        Assert.Null(await fixture.Activities.FindAsync(WorkflowExecutionId, "actexec-1"));
        Assert.NotNull(await fixture.Activities.FindAsync(WorkflowExecutionId, "actexec-stray"));
        Assert.Null(await fixture.Inspections.FindAsync(WorkflowExecutionId, "actexec-1"));
        Assert.NotNull(await fixture.Inspections.FindAsync(WorkflowExecutionId, "actexec-stray"));

        Assert.Equal(1, fixture.DurableActivities.ProbeCount);
        Assert.Equal(2, fixture.DurableActivities.FindCount);
        Assert.Equal(1, fixture.DurableInspections.ProbeCount);
        Assert.Equal(2, fixture.DurableInspections.FindCount);
    }

    [Fact]
    public async Task ActivitySavedPastTheWorkingSet_IsReadFromTheStore()
    {
        var fixture = new Fixture(NoRecord);
        Assert.Null(await fixture.Activities.FindAsync(WorkflowExecutionId, "actexec-1"));

        await fixture.Activities.SaveAsync(NewActivityState("actexec-direct"));

        Assert.NotNull(await fixture.Activities.FindAsync(WorkflowExecutionId, "actexec-direct"));
        Assert.Null(await fixture.Activities.FindAsync(WorkflowExecutionId, "actexec-2"));
        Assert.Equal(1, fixture.DurableActivities.FindCount);
    }

    // The rows of a commit count as possibly durable from the moment the write is attempted. This commit's write
    // lands and then reports failure, as a lost acknowledgement does.
    [Fact]
    public async Task RowsOfACommitWhoseOutcomeIsUncertain_AreReadFromTheStore()
    {
        var fixture = new Fixture(NoRecord, commitsLandThenThrow: true);
        Assert.Null(await fixture.Activities.FindAsync(WorkflowExecutionId, "actexec-1"));
        Assert.Null(await fixture.Inspections.FindAsync(WorkflowExecutionId, "actexec-1"));

        var state = NewActivityState("actexec-1");
        var projection = ActivityExecutionInspectionProjection.FromState(state, "checkpoint-01", Now);
        await Assert.ThrowsAsync<IOException>(async () => await fixture.Commits.CommitAsync(
            NewCommit(1, RuntimeCheckpointNames.ActivityAttemptClaimed, state, projection),
            new(RuntimeCheckpointPersistenceMode.Immediate)));
        Assert.True(fixture.Session.IsActive);

        Assert.NotNull(await fixture.Activities.FindAsync(WorkflowExecutionId, "actexec-1"));
        Assert.NotNull(await fixture.Inspections.FindAsync(WorkflowExecutionId, "actexec-1"));
        Assert.Equal(1, fixture.DurableActivities.FindCount);
        Assert.Equal(1, fixture.DurableInspections.FindCount);

        // What the session knows about every other row is unchanged.
        Assert.Null(await fixture.Activities.FindAsync(WorkflowExecutionId, "actexec-2"));
        Assert.Null(await fixture.Inspections.FindAsync(WorkflowExecutionId, "actexec-2"));
        Assert.Equal(1, fixture.DurableActivities.FindCount);
        Assert.Equal(1, fixture.DurableInspections.FindCount);
    }

    // A row written while the store had not been probed yet is still recorded, so a later probe of a store that
    // looked empty cannot make the session answer for it.
    [Fact]
    public async Task RowSentBeforeTheProbe_IsReadFromTheStore()
    {
        var fixture = new Fixture(NoRecord, commitsLandThenThrow: true);
        var state = NewActivityState("actexec-1");
        await Assert.ThrowsAsync<IOException>(async () => await fixture.Commits.CommitAsync(
            NewCommit(1, RuntimeCheckpointNames.ActivityAttemptClaimed, state, inspection: null),
            new(RuntimeCheckpointPersistenceMode.Immediate)));

        fixture.DurableActivities.ReportEmptyOnce = true;

        Assert.NotNull(await fixture.Activities.FindAsync(WorkflowExecutionId, "actexec-1"));
        Assert.Equal(1, fixture.DurableActivities.ProbeCount);
        Assert.Equal(1, fixture.DurableActivities.FindCount);
    }

    // The ambient scope changes when a nested drain begins. From then on this run's rows can be written without
    // passing through its session.
    [Fact]
    public async Task AnotherAmbientScope_EndsWhatTheSessionKnows()
    {
        var accessor = new AsyncLocalRuntimeCoalescingSessionAccessor();
        var session = NewSession(NoRecord);
        using var outer = accessor.Push(session);
        var durable = new ProbingActivityStore();
        var activities = new CoalescingActivityExecutionStateStore(new CoalescingInner<IActivityExecutionStateStore>(durable), accessor);
        Assert.Null(await activities.FindAsync(WorkflowExecutionId, "actexec-1"));
        Assert.Equal(0, durable.FindCount);

        using (accessor.Push(new RuntimeCoalescingSession("wfexec-child", new InMemoryWorkflowSchedulerWorkQueue(), new PersistenceModeOptions())))
            await durable.Inner.SaveAsync(NewActivityState("actexec-unseen"));

        Assert.NotNull(await activities.FindAsync(WorkflowExecutionId, "actexec-unseen"));
        Assert.Null(await activities.FindAsync(WorkflowExecutionId, "actexec-2"));
        Assert.Equal(2, durable.FindCount);
    }

    [Fact]
    public async Task StoreThatCannotBeProbed_HasEveryRowReadFromIt()
    {
        var session = NewSession(NoRecord);
        var accessor = new FixedCoalescingSessionAccessor(session);
        var durable = new CountingActivityStore(new InMemoryActivityExecutionStateStore());
        var activities = new CoalescingActivityExecutionStateStore(new CoalescingInner<IActivityExecutionStateStore>(durable), accessor);

        Assert.Null(await activities.FindAsync(WorkflowExecutionId, "actexec-1"));
        Assert.Null(await activities.FindAsync(WorkflowExecutionId, "actexec-2"));

        Assert.Equal(2, durable.FindCount);
    }

    [Fact]
    public async Task DeactivatedSession_ReadsEveryRowFromTheStore()
    {
        var fixture = new Fixture(NoRecord);
        Assert.Null(await fixture.Activities.FindAsync(WorkflowExecutionId, "actexec-1"));
        fixture.Session.Deactivate();

        Assert.Null(await fixture.Activities.FindAsync(WorkflowExecutionId, "actexec-2"));
        Assert.Null(await fixture.Inspections.FindAsync(WorkflowExecutionId, "actexec-2"));

        Assert.Equal(1, fixture.DurableActivities.FindCount);
        Assert.Equal(1, fixture.DurableInspections.FindCount);
    }

    private static RuntimeCoalescingSession NewSession(ResolvedPersistenceMode mode) =>
        new(WorkflowExecutionId, new InMemoryWorkflowSchedulerWorkQueue(), new PersistenceModeOptions(), innerOutboxStore: null, mode);

    private sealed class Fixture
    {
        public Fixture(ResolvedPersistenceMode mode, bool commitsLandThenThrow = false)
        {
            Session = NewSession(mode);
            var accessor = new FixedCoalescingSessionAccessor(Session);
            Activities = new CoalescingActivityExecutionStateStore(new CoalescingInner<IActivityExecutionStateStore>(DurableActivities), accessor);
            Inspections = new CoalescingActivityExecutionInspectionStore(new CoalescingInner<IActivityExecutionInspectionStore>(DurableInspections), accessor);
            IRuntimeCheckpointCommitStore innerCommits = new InMemoryRuntimeCheckpointCommitStore(
                activityExecutionStateStore: DurableActivities.Inner,
                activityExecutionInspectionWriter: DurableInspections.Inner);
            if (commitsLandThenThrow)
                innerCommits = new LandThenThrowCommitStore(innerCommits);
            Commits = new CoalescingRuntimeCheckpointCommitStore(new CoalescingInner<IRuntimeCheckpointCommitStore>(innerCommits), accessor);
        }

        public RuntimeCoalescingSession Session { get; }
        public ProbingActivityStore DurableActivities { get; } = new();
        public ProbingInspectionStore DurableInspections { get; } = new();
        public CoalescingActivityExecutionStateStore Activities { get; }
        public CoalescingActivityExecutionInspectionStore Inspections { get; }
        public CoalescingRuntimeCheckpointCommitStore Commits { get; }
    }

    private static ActivityExecutionState NewActivityState(string activityExecutionId) =>
        new(
            Execution: new ActivityExecution(
                ActivityExecutionId: activityExecutionId,
                WorkflowExecutionId: WorkflowExecutionId,
                ExecutableNodeId: $"node-{activityExecutionId}",
                AuthoredActivityId: $"authored-{activityExecutionId}",
                ActivityType: "test/activity",
                ActivityTypeVersion: "1.0.0"),
            Status: ActivityExecutionStatus.Running,
            SubStatus: null,
            ExecutionSequence: 1,
            ScheduledAt: Now,
            StartedAt: Now,
            CompletedAt: null,
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

    private static RuntimeCheckpointCommit NewCommit(
        int checkpoint,
        string checkpointName,
        ActivityExecutionState state,
        ActivityExecutionInspectionProjection? inspection) =>
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
                activityExecutionInspections: inspection is null
                    ? []
                    :
                    [
                        new RuntimeStateChange<ActivityExecutionInspectionProjection>(
                            inspection.ActivityExecutionId,
                            RuntimeStateChangeOperation.Upsert,
                            inspection,
                            new Dictionary<string, string>())
                    ]),
            PostCommitIntents: [],
            Metadata: new Dictionary<string, string>());

    private class CountingActivityStore(IActivityExecutionStateStore inner) : IActivityExecutionStateStore
    {
        public IActivityExecutionStateStore Inner => inner;
        public int FindCount { get; private set; }

        public ValueTask<ActivityExecutionState> SaveAsync(ActivityExecutionState state, CancellationToken cancellationToken = default) =>
            inner.SaveAsync(state, cancellationToken);

        public ValueTask<ActivityExecutionState?> FindAsync(string workflowExecutionId, string activityExecutionId, CancellationToken cancellationToken = default)
        {
            FindCount++;
            return inner.FindAsync(workflowExecutionId, activityExecutionId, cancellationToken);
        }

        public ValueTask<long> CountAsync(string workflowExecutionId, CancellationToken cancellationToken = default) =>
            inner.CountAsync(workflowExecutionId, cancellationToken);

        public ValueTask<RuntimeStorePage<ActivityExecutionState>> ListPageAsync(ActivityExecutionStatePageQuery query, CancellationToken cancellationToken = default) =>
            inner.ListPageAsync(query, cancellationToken);

        public ValueTask<RuntimeStorePage<ActivityExecutionState>> ListByParentPageAsync(ActivityExecutionStateParentPageQuery query, CancellationToken cancellationToken = default) =>
            inner.ListByParentPageAsync(query, cancellationToken);
    }

    private sealed class ProbingActivityStore() : CountingActivityStore(new InMemoryActivityExecutionStateStore()), IWorkflowExecutionRowPresenceProbe
    {
        public int ProbeCount { get; private set; }

        // Stands in for a probe that ran before an uncertain write became visible.
        public bool ReportEmptyOnce { get; set; }

        public async ValueTask<bool> HasAnyAsync(string workflowExecutionId, CancellationToken cancellationToken = default)
        {
            ProbeCount++;
            if (ReportEmptyOnce)
            {
                ReportEmptyOnce = false;
                return false;
            }

            return await Inner.CountAsync(workflowExecutionId, cancellationToken) > 0;
        }
    }

    private sealed class ProbingInspectionStore : IActivityExecutionInspectionStore, IWorkflowExecutionRowPresenceProbe
    {
        public InMemoryActivityExecutionInspectionStore Inner { get; } = new();
        public int FindCount { get; private set; }
        public int ProbeCount { get; private set; }

        public ValueTask<ActivityExecutionInspectionProjection?> FindAsync(string workflowExecutionId, string activityExecutionId, CancellationToken cancellationToken = default)
        {
            FindCount++;
            return Inner.FindAsync(workflowExecutionId, activityExecutionId, cancellationToken);
        }

        public ValueTask<ActivityExecutionInspectionSummaryPage> ListSummariesPageAsync(ActivityExecutionInspectionSummaryPageQuery query, CancellationToken cancellationToken = default) =>
            Inner.ListSummariesPageAsync(query, cancellationToken);

        public async ValueTask<bool> HasAnyAsync(string workflowExecutionId, CancellationToken cancellationToken = default)
        {
            ProbeCount++;
            return (await Inner.ListAllSummariesAsync(workflowExecutionId, cancellationToken)).Count > 0;
        }
    }

    private sealed class LandThenThrowCommitStore(IRuntimeCheckpointCommitStore inner) : IRuntimeCheckpointCommitStore
    {
        public async ValueTask<RuntimeCheckpointCommitStoreResult> CommitAsync(RuntimeCheckpointCommit commit, RuntimeCheckpointPersistenceDecision decision, CancellationToken cancellationToken = default)
        {
            await inner.CommitAsync(commit, decision, cancellationToken);
            throw new IOException("The commit's acknowledgement was lost.");
        }
    }

    private sealed class FixedCoalescingSessionAccessor(RuntimeCoalescingSession session) : IRuntimeCoalescingSessionAccessor
    {
        public RuntimeCoalescingSession? Current => session;

        public IDisposable Push(RuntimeCoalescingSession? pushedSession) =>
            throw new NotSupportedException("This test provides a fixed ambient session.");
    }
}
