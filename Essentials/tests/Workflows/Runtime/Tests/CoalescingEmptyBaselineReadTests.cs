using Elsa.Workflows.Runtime.Contracts;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.ActivityExecutions;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Elsa.Workflows.Runtime.Services.Coalescing;
using Elsa.Workflows.Runtime.Services.Executions;
using Elsa.Workflows.Runtime.Services.Scheduler;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

// Point reads during the drain of a run that has no durable record. One probe per store shows the store holds none of
// the run's rows; after it, a row the session has not sent to the store is not read. Every way a row can reach the
// store without the session answering for it sends its read back to the store.
public sealed class CoalescingEmptyBaselineReadTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan SignalTimeout = TimeSpan.FromSeconds(30);
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

    // A direct save counts as possibly durable from the moment it is attempted, as a commit does. This save lands
    // and then reports failure, as a lost acknowledgement does.
    [Fact]
    public async Task ActivitySavedPastTheWorkingSet_WhoseOutcomeIsUncertain_IsReadFromTheStore()
    {
        var fixture = new Fixture(NoRecord);
        Assert.Null(await fixture.Activities.FindAsync(WorkflowExecutionId, "actexec-1"));
        fixture.DurableActivities.SavesLandThenThrow = true;

        await Assert.ThrowsAsync<IOException>(async () => await fixture.Activities.SaveAsync(NewActivityState("actexec-direct")));

        Assert.NotNull(await fixture.Activities.FindAsync(WorkflowExecutionId, "actexec-direct"));
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
            NewCommit(1, RuntimeCheckpointNames.ActivityAttemptClaimed, state, ActivityExecutionInspectionProjection.FromState(state, "checkpoint-01", Now)),
            new(RuntimeCheckpointPersistenceMode.Immediate)));

        fixture.DurableActivities.ReportEmptyOnce = true;
        fixture.DurableInspections.ReportEmptyOnce = true;

        Assert.NotNull(await fixture.Activities.FindAsync(WorkflowExecutionId, "actexec-1"));
        Assert.NotNull(await fixture.Inspections.FindAsync(WorkflowExecutionId, "actexec-1"));
        Assert.Equal(1, fixture.DurableActivities.ProbeCount);
        Assert.Equal(1, fixture.DurableActivities.FindCount);
        Assert.Equal(1, fixture.DurableInspections.ProbeCount);
        Assert.Equal(1, fixture.DurableInspections.FindCount);
    }

    // The ambient scope changes when a nested drain begins. From then on this run's rows can be written without
    // passing through its session.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AnotherAmbientScope_EndsWhatTheSessionKnows(bool nestedSession)
    {
        var accessor = new AsyncLocalRuntimeCoalescingSessionAccessor();
        var session = NewSession(NoRecord);
        using var outer = accessor.Push(session);
        var durableActivities = new ProbingActivityStore();
        var durableInspections = new ProbingInspectionStore();
        var activities = new CoalescingActivityExecutionStateStore(new CoalescingInner<IActivityExecutionStateStore>(durableActivities), accessor);
        var inspections = new CoalescingActivityExecutionInspectionStore(new CoalescingInner<IActivityExecutionInspectionStore>(durableInspections), accessor);
        Assert.Null(await activities.FindAsync(WorkflowExecutionId, "actexec-1"));
        Assert.Null(await inspections.FindAsync(WorkflowExecutionId, "actexec-1"));
        Assert.Equal(0, durableActivities.FindCount);
        Assert.Equal(0, durableInspections.FindCount);

        // Either a nested drain's session or a scope that hides the session altogether.
        var other = nestedSession
            ? new RuntimeCoalescingSession("wfexec-child", new InMemoryWorkflowSchedulerWorkQueue(), new PersistenceModeOptions())
            : null;
        using (accessor.Push(other))
        {
            var unseen = NewActivityState("actexec-unseen");
            await durableActivities.Inner.SaveAsync(unseen);
            await durableInspections.Inner.SaveAsync(ActivityExecutionInspectionProjection.FromState(unseen, "checkpoint-unseen", Now));
        }

        Assert.NotNull(await activities.FindAsync(WorkflowExecutionId, "actexec-unseen"));
        Assert.Null(await activities.FindAsync(WorkflowExecutionId, "actexec-2"));
        Assert.NotNull(await inspections.FindAsync(WorkflowExecutionId, "actexec-unseen"));
        Assert.Null(await inspections.FindAsync(WorkflowExecutionId, "actexec-2"));
        Assert.Equal(2, durableActivities.FindCount);
        Assert.Equal(2, durableInspections.FindCount);
    }

    // Before it reads, a store refuses a blank id, a cancelled read and an access context it does not admit. A read
    // the session answers is refused by the same store, with the same exception.
    [Fact]
    public async Task ReadTheSessionAnswers_StillRefusesWhatTheStoreWouldRefuse()
    {
        var fixture = new Fixture(NoRecord);
        Assert.Null(await fixture.Activities.FindAsync(WorkflowExecutionId, "actexec-1"));
        Assert.Null(await fixture.Inspections.FindAsync(WorkflowExecutionId, "actexec-1"));
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<ArgumentException>(async () => await fixture.Activities.FindAsync(WorkflowExecutionId, " "));
        await Assert.ThrowsAnyAsync<ArgumentException>(async () => await fixture.Inspections.FindAsync(WorkflowExecutionId, " "));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await fixture.Activities.FindAsync(WorkflowExecutionId, "actexec-2", cancelled.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await fixture.Inspections.FindAsync(WorkflowExecutionId, "actexec-2", cancelled.Token));

        var refusal = new InvalidOperationException("The store does not admit this access context.");
        fixture.DurableActivities.Refusal = refusal;
        fixture.DurableInspections.Refusal = refusal;
        Assert.Same(refusal, await Assert.ThrowsAsync<InvalidOperationException>(async () => await fixture.Activities.FindAsync(WorkflowExecutionId, "actexec-2")));
        Assert.Same(refusal, await Assert.ThrowsAsync<InvalidOperationException>(async () => await fixture.Inspections.FindAsync(WorkflowExecutionId, "actexec-2")));
        Assert.Equal(0, fixture.DurableActivities.FindCount);
        Assert.Equal(0, fixture.DurableInspections.FindCount);
    }

    // The probe saw one persistence scope. A read under another scope is not answered from what it found there.
    [Fact]
    public async Task ReadUnderAnotherScopeThanTheProbe_IsReadFromTheStore()
    {
        var fixture = new Fixture(NoRecord);
        Assert.Null(await fixture.Activities.FindAsync(WorkflowExecutionId, "actexec-1"));
        Assert.Null(await fixture.Inspections.FindAsync(WorkflowExecutionId, "actexec-1"));

        fixture.DurableActivities.Scope = "scope-b";
        fixture.DurableInspections.Scope = "scope-b";
        Assert.Null(await fixture.Activities.FindAsync(WorkflowExecutionId, "actexec-2"));
        Assert.Null(await fixture.Inspections.FindAsync(WorkflowExecutionId, "actexec-2"));
        Assert.Equal(1, fixture.DurableActivities.FindCount);
        Assert.Equal(1, fixture.DurableInspections.FindCount);

        // Back under the probed scope the session answers again, without a second probe.
        fixture.DurableActivities.Scope = "scope-a";
        fixture.DurableInspections.Scope = "scope-a";
        Assert.Null(await fixture.Activities.FindAsync(WorkflowExecutionId, "actexec-3"));
        Assert.Null(await fixture.Inspections.FindAsync(WorkflowExecutionId, "actexec-3"));
        Assert.Equal(1, fixture.DurableActivities.FindCount);
        Assert.Equal(1, fixture.DurableInspections.FindCount);
        Assert.Equal(1, fixture.DurableActivities.ProbeCount);
        Assert.Equal(1, fixture.DurableInspections.ProbeCount);
    }

    // The session accessor is a replacement contract. A replacement that only keeps the stack does not tell the
    // parent session anything, so the drain scope factory ends what the parent knows before it pushes a nested drain.
    [Fact]
    public async Task NestedDrainBegunThroughTheFactory_EndsWhatTheParentKnows_WhateverAccessorIsRegistered()
    {
        var accessor = new StackOnlySessionAccessor();
        var factory = NewFactory(accessor);
        var durableActivities = new ProbingActivityStore();
        var durableInspections = new ProbingInspectionStore();
        var activities = new CoalescingActivityExecutionStateStore(new CoalescingInner<IActivityExecutionStateStore>(durableActivities), accessor);
        var inspections = new CoalescingActivityExecutionInspectionStore(new CoalescingInner<IActivityExecutionInspectionStore>(durableInspections), accessor);

        await using var parent = factory.Begin(WorkflowExecutionId, NoRecord);
        Assert.Null(await activities.FindAsync(WorkflowExecutionId, "actexec-1"));
        Assert.Null(await inspections.FindAsync(WorkflowExecutionId, "actexec-1"));
        Assert.Equal(0, durableActivities.FindCount);
        Assert.Equal(0, durableInspections.FindCount);

        await using (factory.Begin("wfexec-child", NoRecord))
        {
            var unseen = NewActivityState("actexec-unseen");
            await durableActivities.Inner.SaveAsync(unseen);
            await durableInspections.Inner.SaveAsync(ActivityExecutionInspectionProjection.FromState(unseen, "checkpoint-unseen", Now));
        }

        Assert.Same(parent.Session, accessor.Current);
        Assert.NotNull(await activities.FindAsync(WorkflowExecutionId, "actexec-unseen"));
        Assert.Null(await activities.FindAsync(WorkflowExecutionId, "actexec-2"));
        Assert.NotNull(await inspections.FindAsync(WorkflowExecutionId, "actexec-unseen"));
        Assert.Null(await inspections.FindAsync(WorkflowExecutionId, "actexec-2"));
        Assert.Equal(2, durableActivities.FindCount);
        Assert.Equal(2, durableInspections.FindCount);
    }

    // The session type is public and so is the factory contract. The guards around a session's answers live in the
    // default factory, so a session nobody began through it answers nothing.
    [Fact]
    public async Task SessionTheFactoryDidNotBegin_ReadsEveryRowFromTheStore()
    {
        var session = NewSessionNoFactoryBegan(NoRecord);
        var accessor = new FixedCoalescingSessionAccessor(session);
        var durableActivities = new ProbingActivityStore();
        var durableInspections = new ProbingInspectionStore();
        var activities = new CoalescingActivityExecutionStateStore(new CoalescingInner<IActivityExecutionStateStore>(durableActivities), accessor);
        var inspections = new CoalescingActivityExecutionInspectionStore(new CoalescingInner<IActivityExecutionInspectionStore>(durableInspections), accessor);

        foreach (var id in new[] { "actexec-1", "actexec-2" })
        {
            Assert.Null(await activities.FindAsync(WorkflowExecutionId, id));
            Assert.Null(await inspections.FindAsync(WorkflowExecutionId, id));
        }

        Assert.Equal(0, durableActivities.ProbeCount);
        Assert.Equal(2, durableActivities.FindCount);
        Assert.Equal(0, durableInspections.ProbeCount);
        Assert.Equal(2, durableInspections.FindCount);
    }

    // An ended baseline never answers again, so beginning it a second time changes nothing.
    [Fact]
    public async Task EndedSession_IsNotBegunAgain()
    {
        var fixture = new Fixture(NoRecord);
        Assert.Null(await fixture.Activities.FindAsync(WorkflowExecutionId, "actexec-1"));
        Assert.Null(await fixture.Inspections.FindAsync(WorkflowExecutionId, "actexec-1"));

        fixture.Session.EndStartObservations();
        fixture.Session.BeginEmptyBaselines();

        Assert.Null(await fixture.Activities.FindAsync(WorkflowExecutionId, "actexec-2"));
        Assert.Null(await fixture.Inspections.FindAsync(WorkflowExecutionId, "actexec-2"));
        Assert.Equal(1, fixture.DurableActivities.ProbeCount);
        Assert.Equal(1, fixture.DurableActivities.FindCount);
        Assert.Equal(1, fixture.DurableInspections.ProbeCount);
        Assert.Equal(1, fixture.DurableInspections.FindCount);
    }

    // A task started inside the drain captures the ambient session and still sees it after the drain popped it.
    // By then the drain no longer vouches for what the session knows.
    [Fact]
    public async Task FlowThatOutlivesTheAmbientScope_ReadsEveryRowFromTheStore()
    {
        var accessor = new AsyncLocalRuntimeCoalescingSessionAccessor();
        var session = NewSession(NoRecord);
        var durableActivities = new ProbingActivityStore();
        var durableInspections = new ProbingInspectionStore();
        var activities = new CoalescingActivityExecutionStateStore(new CoalescingInner<IActivityExecutionStateStore>(durableActivities), accessor);
        var inspections = new CoalescingActivityExecutionInspectionStore(new CoalescingInner<IActivityExecutionInspectionStore>(durableInspections), accessor);
        var scope = accessor.Push(session);
        Assert.Null(await activities.FindAsync(WorkflowExecutionId, "actexec-1"));
        Assert.Null(await inspections.FindAsync(WorkflowExecutionId, "actexec-1"));
        var popped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var late = Task.Run(async () =>
        {
            await popped.Task;
            Assert.Same(session, accessor.Current);
            return (
                State: await activities.FindAsync(WorkflowExecutionId, "actexec-unseen"),
                Projection: await inspections.FindAsync(WorkflowExecutionId, "actexec-unseen"));
        });

        scope.Dispose();
        Assert.True(session.IsActive);
        var unseen = NewActivityState("actexec-unseen");
        await durableActivities.Inner.SaveAsync(unseen);
        await durableInspections.Inner.SaveAsync(ActivityExecutionInspectionProjection.FromState(unseen, "checkpoint-unseen", Now));
        popped.SetResult();

        var read = await late;
        Assert.NotNull(read.State);
        Assert.NotNull(read.Projection);
    }

    // A drain that throws skips its flush, so its session is still active when the scope is disposed. The accessor
    // here stands for any flow that still holds that session.
    [Fact]
    public async Task ScopeDisposedWhileItsSessionIsActive_EndsWhatTheSessionKnows()
    {
        var accessor = new StickySessionAccessor();
        var factory = NewFactory(accessor);
        var durableActivities = new ProbingActivityStore();
        var durableInspections = new ProbingInspectionStore();
        var activities = new CoalescingActivityExecutionStateStore(new CoalescingInner<IActivityExecutionStateStore>(durableActivities), accessor);
        var inspections = new CoalescingActivityExecutionInspectionStore(new CoalescingInner<IActivityExecutionInspectionStore>(durableInspections), accessor);
        var scope = factory.Begin(WorkflowExecutionId, NoRecord);
        Assert.Null(await activities.FindAsync(WorkflowExecutionId, "actexec-1"));
        Assert.Null(await inspections.FindAsync(WorkflowExecutionId, "actexec-1"));
        Assert.Equal(0, durableActivities.FindCount);
        Assert.Equal(0, durableInspections.FindCount);

        await scope.DisposeAsync();
        Assert.True(scope.Session.IsActive);
        Assert.Same(scope.Session, accessor.Current);
        var unseen = NewActivityState("actexec-unseen");
        await durableActivities.Inner.SaveAsync(unseen);
        await durableInspections.Inner.SaveAsync(ActivityExecutionInspectionProjection.FromState(unseen, "checkpoint-unseen", Now));

        Assert.NotNull(await activities.FindAsync(WorkflowExecutionId, "actexec-unseen"));
        Assert.NotNull(await inspections.FindAsync(WorkflowExecutionId, "actexec-unseen"));
    }

    // The coalescing commit store is what records the rows a commit sends. A host that registered its own commit
    // store over it, or instead of it, has commits the session never hears of, so the session must not answer.
    [Fact]
    public async Task SessionWhoseCommitsBypassTheCoalescingStore_ReadsEveryRowFromTheStore()
    {
        var accessor = new StackOnlySessionAccessor();
        var factory = NewFactory(accessor, commitsThroughTheDecorator: false);
        var durableActivities = new ProbingActivityStore();
        var durableInspections = new ProbingInspectionStore();
        var activities = new CoalescingActivityExecutionStateStore(new CoalescingInner<IActivityExecutionStateStore>(durableActivities), accessor);
        var inspections = new CoalescingActivityExecutionInspectionStore(new CoalescingInner<IActivityExecutionInspectionStore>(durableInspections), accessor);

        await using var scope = factory.Begin(WorkflowExecutionId, NoRecord);
        foreach (var id in new[] { "actexec-1", "actexec-2" })
        {
            Assert.Null(await activities.FindAsync(WorkflowExecutionId, id));
            Assert.Null(await inspections.FindAsync(WorkflowExecutionId, id));
        }

        Assert.Equal(0, durableActivities.ProbeCount);
        Assert.Equal(2, durableActivities.FindCount);
        Assert.Equal(0, durableInspections.ProbeCount);
        Assert.Equal(2, durableInspections.FindCount);
    }

    // The session can be ended from another thread while its probe is still running: a failed lease heartbeat
    // cancels the drain, and the cancellation callback ends the session. A probe that then comes back "empty" must
    // not make the session answer.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SessionEndedWhileItsProbeIsInFlight_ReadsTheRowFromTheStore(bool activityStoreIsProbed)
    {
        var fixture = new Fixture(NoRecord);
        var late = NewActivityState("actexec-late");
        await fixture.DurableActivities.Inner.SaveAsync(late);
        await fixture.DurableInspections.Inner.SaveAsync(ActivityExecutionInspectionProjection.FromState(late, "checkpoint-late", Now));
        // The probe ran before the row became visible, and the session was ended before the probe returned.
        fixture.DurableActivities.ReportEmptyOnce = true;
        fixture.DurableInspections.ReportEmptyOnce = true;
        fixture.DurableActivities.DuringProbe = fixture.Session.EndStartObservations;
        fixture.DurableInspections.DuringProbe = fixture.Session.EndStartObservations;

        // The store that is read first is the one whose probe is in flight when the session ends. That read ends
        // both baselines, so the other store is not probed at all.
        if (activityStoreIsProbed)
        {
            Assert.NotNull(await fixture.Activities.FindAsync(WorkflowExecutionId, "actexec-late"));
            Assert.NotNull(await fixture.Inspections.FindAsync(WorkflowExecutionId, "actexec-late"));
        }
        else
        {
            Assert.NotNull(await fixture.Inspections.FindAsync(WorkflowExecutionId, "actexec-late"));
            Assert.NotNull(await fixture.Activities.FindAsync(WorkflowExecutionId, "actexec-late"));
        }

        Assert.Equal(activityStoreIsProbed ? 1 : 0, fixture.DurableActivities.ProbeCount);
        Assert.Equal(1, fixture.DurableActivities.FindCount);
        Assert.Equal(activityStoreIsProbed ? 0 : 1, fixture.DurableInspections.ProbeCount);
        Assert.Equal(1, fixture.DurableInspections.FindCount);
    }

    // A session answers for its own run only. Another run's rows are read from the store, whatever this session
    // found for its own.
    [Fact]
    public async Task RowOfAnotherRun_IsReadFromTheStore()
    {
        var fixture = new Fixture(NoRecord);
        Assert.Null(await fixture.Activities.FindAsync(WorkflowExecutionId, "actexec-1"));
        Assert.Null(await fixture.Inspections.FindAsync(WorkflowExecutionId, "actexec-1"));
        var other = NewActivityState("actexec-other", "wfexec-other");
        await fixture.DurableActivities.Inner.SaveAsync(other);
        await fixture.DurableInspections.Inner.SaveAsync(ActivityExecutionInspectionProjection.FromState(other, "checkpoint-other", Now));

        Assert.NotNull(await fixture.Activities.FindAsync("wfexec-other", "actexec-other"));
        Assert.NotNull(await fixture.Inspections.FindAsync("wfexec-other", "actexec-other"));

        Assert.Equal(1, fixture.DurableActivities.FindCount);
        Assert.Equal(1, fixture.DurableInspections.FindCount);
    }

    // Ending comes first here, as it would if a scope were torn down before the factory finished. The session
    // must still never answer.
    [Fact]
    public async Task SessionEndedBeforeItWasBegun_IsNotBegun()
    {
        var session = NewSessionNoFactoryBegan(NoRecord);
        session.EndStartObservations();
        session.BeginEmptyBaselines();
        var accessor = new FixedCoalescingSessionAccessor(session);
        var durableActivities = new ProbingActivityStore();
        var durableInspections = new ProbingInspectionStore();
        var activities = new CoalescingActivityExecutionStateStore(new CoalescingInner<IActivityExecutionStateStore>(durableActivities), accessor);
        var inspections = new CoalescingActivityExecutionInspectionStore(new CoalescingInner<IActivityExecutionInspectionStore>(durableInspections), accessor);

        Assert.Null(await activities.FindAsync(WorkflowExecutionId, "actexec-1"));
        Assert.Null(await inspections.FindAsync(WorkflowExecutionId, "actexec-1"));

        Assert.Equal(0, durableActivities.ProbeCount);
        Assert.Equal(1, durableActivities.FindCount);
        Assert.Equal(0, durableInspections.ProbeCount);
        Assert.Equal(1, durableInspections.FindCount);
    }

    // A probe that cannot tell must throw. The session then knows nothing: the read fails as a failed lookup would,
    // and the next read asks again.
    [Fact]
    public async Task ProbeThatThrows_AnswersNothing_AndIsAskedAgain()
    {
        var fixture = new Fixture(NoRecord);
        var failure = new IOException("The store could not be reached.");
        fixture.DurableActivities.ProbeFailsOnce = failure;
        fixture.DurableInspections.ProbeFailsOnce = failure;

        Assert.Same(failure, await Assert.ThrowsAsync<IOException>(async () => await fixture.Activities.FindAsync(WorkflowExecutionId, "actexec-1")));
        Assert.Same(failure, await Assert.ThrowsAsync<IOException>(async () => await fixture.Inspections.FindAsync(WorkflowExecutionId, "actexec-1")));
        Assert.Null(await fixture.Activities.FindAsync(WorkflowExecutionId, "actexec-1"));
        Assert.Null(await fixture.Inspections.FindAsync(WorkflowExecutionId, "actexec-1"));

        Assert.Equal(2, fixture.DurableActivities.ProbeCount);
        Assert.Equal(0, fixture.DurableActivities.FindCount);
        Assert.Equal(2, fixture.DurableInspections.ProbeCount);
        Assert.Equal(0, fixture.DurableInspections.FindCount);
    }

    // A failed lease heartbeat cancels the drain, so a cancelled drain may no longer own the run. A read that does
    // not carry the drain's token still reaches the overlay while the drain unwinds.
    [Fact]
    public async Task CancelledDrain_ReadsEveryRowFromTheStore()
    {
        var accessor = new AsyncLocalRuntimeCoalescingSessionAccessor();
        var durableActivities = new ProbingActivityStore();
        var durableInspections = new ProbingInspectionStore();
        var drainer = new CancellationHoldingDrainer(
            new CoalescingActivityExecutionStateStore(new CoalescingInner<IActivityExecutionStateStore>(durableActivities), accessor),
            new CoalescingActivityExecutionInspectionStore(new CoalescingInner<IActivityExecutionInspectionStore>(durableInspections), accessor));
        var checkpointStore = new InMemoryRuntimeCheckpointCommitStore();
        var orchestrator = new WorkflowDrainOrchestrator(
            drainer,
            new NothingToDeliver(),
            [],
            TestCheckpointRuleViolationFaulter.Create(),
            new HeldLeaseOwnershipService(),
            new AsyncLocalRuntimeExecutionOwnershipContextAccessor(),
            checkpointStore,
            checkpointStore,
            new InMemoryWorkflowSchedulerWorkQueue(),
            coalescingScopeFactory: NewFactory(accessor),
            persistenceModeResolver: new FixedPersistenceModeResolver(PersistenceMode.Balanced, runHasRecord: false));
        using var cancellation = new CancellationTokenSource();

        var drain = orchestrator.DrainAsync(NewEnvelope(), new RuntimeSchedulerDrainRequest(WorkflowExecutionId), cancellation.Token).AsTask();
        await drainer.Answered.Task.WaitAsync(SignalTimeout);
        Assert.Equal(1, durableActivities.ProbeCount);
        Assert.Equal(0, durableActivities.FindCount);
        Assert.Equal(1, durableInspections.ProbeCount);
        Assert.Equal(0, durableInspections.FindCount);
        var unseen = NewActivityState("actexec-unseen");
        await durableActivities.Inner.SaveAsync(unseen);
        await durableInspections.Inner.SaveAsync(ActivityExecutionInspectionProjection.FromState(unseen, "checkpoint-unseen", Now));
        await cancellation.CancelAsync();
        drainer.ContinueAfterCancellation.TrySetResult();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => drain.WaitAsync(SignalTimeout));
        Assert.NotNull(drainer.StateAfterCancellation);
        Assert.NotNull(drainer.ProjectionAfterCancellation);
    }

    [Fact]
    public async Task StoreThatCannotBeProbed_HasEveryRowReadFromIt()
    {
        var session = NewSession(NoRecord);
        var accessor = new FixedCoalescingSessionAccessor(session);
        var durable = new CountingActivityStore(new InMemoryActivityExecutionStateStore());
        var durableInspections = new InMemoryActivityExecutionInspectionStore();
        var activities = new CoalescingActivityExecutionStateStore(new CoalescingInner<IActivityExecutionStateStore>(durable), accessor);
        var inspections = new CoalescingActivityExecutionInspectionStore(new CoalescingInner<IActivityExecutionInspectionStore>(durableInspections), accessor);
        Assert.Null(await activities.FindAsync(WorkflowExecutionId, "actexec-1"));
        Assert.Null(await inspections.FindAsync(WorkflowExecutionId, "actexec-1"));
        var unseen = NewActivityState("actexec-unseen");
        await durable.Inner.SaveAsync(unseen);
        await durableInspections.SaveAsync(ActivityExecutionInspectionProjection.FromState(unseen, "checkpoint-unseen", Now));

        Assert.NotNull(await activities.FindAsync(WorkflowExecutionId, "actexec-unseen"));
        Assert.NotNull(await inspections.FindAsync(WorkflowExecutionId, "actexec-unseen"));
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

    // A session as the drain scope factory hands it out: allowed to answer for a run that has no record.
    private static RuntimeCoalescingSession NewSession(ResolvedPersistenceMode mode)
    {
        var session = NewSessionNoFactoryBegan(mode);
        session.BeginEmptyBaselines();
        return session;
    }

    private static RuntimeCoalescingSession NewSessionNoFactoryBegan(ResolvedPersistenceMode mode) =>
        new(WorkflowExecutionId, new InMemoryWorkflowSchedulerWorkQueue(), new PersistenceModeOptions(), innerOutboxStore: null, mode);

    // As AddPersistenceModes composes it: the committer writes to the coalescing commit store, which wraps the
    // durable one. commitsThroughTheDecorator: false stands for a host that registered its own commit store later.
    private static RuntimeCoalescingDrainScopeFactory NewFactory(IRuntimeCoalescingSessionAccessor accessor, bool commitsThroughTheDecorator = true)
    {
        var commitStore = new InMemoryRuntimeCheckpointCommitStore();
        IRuntimeCheckpointCommitStore committed = commitsThroughTheDecorator
            ? new CoalescingRuntimeCheckpointCommitStore(new CoalescingInner<IRuntimeCheckpointCommitStore>(commitStore), accessor)
            : commitStore;
        return new RuntimeCoalescingDrainScopeFactory(
            accessor,
            new RuntimeCheckpointCommitter(new ImmediateRuntimeCheckpointPersistencePolicy(), committed, new AsyncLocalRuntimeExecutionOwnershipContextAccessor(), [], []),
            new CoalescingInner<IWorkflowSchedulerWorkQueue>(new InMemoryWorkflowSchedulerWorkQueue()),
            new CoalescingInner<IRuntimePostCommitOutboxStore>(commitStore),
            new PersistenceModeOptions(),
            TimeProvider.System);
    }

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

    private static ActivityExecutionState NewActivityState(string activityExecutionId, string workflowExecutionId = WorkflowExecutionId) =>
        new(
            Execution: new ActivityExecution(
                ActivityExecutionId: activityExecutionId,
                WorkflowExecutionId: workflowExecutionId,
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

        // Stands in for a save whose acknowledgement is lost after it landed.
        public bool SavesLandThenThrow { get; set; }

        public async ValueTask<ActivityExecutionState> SaveAsync(ActivityExecutionState state, CancellationToken cancellationToken = default)
        {
            var saved = await inner.SaveAsync(state, cancellationToken);
            return SavesLandThenThrow ? throw new IOException("The save's acknowledgement was lost.") : saved;
        }

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

    private sealed class ProbingActivityStore() : CountingActivityStore(new InMemoryActivityExecutionStateStore()), IRunRowPresenceProbe
    {
        public int ProbeCount { get; private set; }

        // Stands in for a probe that ran before an uncertain write became visible.
        public bool ReportEmptyOnce { get; set; }

        public Exception? ProbeFailsOnce { get; set; }

        // Runs while the probe is in flight.
        public Action? DuringProbe { get; set; }
        public string Scope { get; set; } = "scope-a";
        public Exception? Refusal { get; set; }

        public string ResolveReadScope(string workflowExecutionId, string activityExecutionId, CancellationToken cancellationToken = default) =>
            StoreRefusals.ResolveReadScope(activityExecutionId, Refusal, Scope, cancellationToken);

        public async ValueTask<bool> HasAnyAsync(string workflowExecutionId, CancellationToken cancellationToken = default)
        {
            ProbeCount++;
            DuringProbe?.Invoke();
            if (ProbeFailsOnce is { } failure)
            {
                ProbeFailsOnce = null;
                throw failure;
            }

            if (ReportEmptyOnce)
            {
                ReportEmptyOnce = false;
                return false;
            }

            return await Inner.CountAsync(workflowExecutionId, cancellationToken) > 0;
        }
    }

    private sealed class ProbingInspectionStore : IActivityExecutionInspectionStore, IRunRowPresenceProbe
    {
        public InMemoryActivityExecutionInspectionStore Inner { get; } = new();
        public int FindCount { get; private set; }
        public int ProbeCount { get; private set; }
        public string Scope { get; set; } = "scope-a";
        public Exception? Refusal { get; set; }

        // Stands in for a probe that ran before an uncertain write became visible.
        public bool ReportEmptyOnce { get; set; }

        public Exception? ProbeFailsOnce { get; set; }

        // Runs while the probe is in flight.
        public Action? DuringProbe { get; set; }

        public string ResolveReadScope(string workflowExecutionId, string activityExecutionId, CancellationToken cancellationToken = default) =>
            StoreRefusals.ResolveReadScope(activityExecutionId, Refusal, Scope, cancellationToken);

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
            DuringProbe?.Invoke();
            if (ProbeFailsOnce is { } failure)
            {
                ProbeFailsOnce = null;
                throw failure;
            }

            if (ReportEmptyOnce)
            {
                ReportEmptyOnce = false;
                return false;
            }

            return (await Inner.ListAllSummariesAsync(workflowExecutionId, cancellationToken)).Count > 0;
        }
    }

    // What a store checks of the row id and the token before a point read queries.
    private static class StoreRefusals
    {
        public static string ResolveReadScope(string rowId, Exception? refusal, string scope, CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(rowId);
            cancellationToken.ThrowIfCancellationRequested();
            return refusal is null ? scope : throw refusal;
        }
    }

    private static WorkflowExecutionCommandEnvelope NewEnvelope()
    {
        var command = new WorkflowExecutionCommand(
            CommandId: "empty-baseline-command",
            WorkflowExecutionId: WorkflowExecutionId,
            Kind: WorkflowExecutionCommandKind.RunSchedulerWork,
            EnqueuedAt: Now,
            Payload: null,
            Metadata: new Dictionary<string, string>());
        return new WorkflowExecutionCommandEnvelope(
            envelopeId: "empty-baseline-envelope",
            workflowExecutionId: WorkflowExecutionId,
            command,
            idempotencyKey: "empty-baseline-command",
            deliveryMode: WorkflowExecutionCommandDeliveryMode.AtLeastOnce,
            enqueuedAt: Now);
    }

    // Reads through the overlay, waits for the test to cancel the drain, and reads again without the drain's token.
    private sealed class CancellationHoldingDrainer(
        IActivityExecutionStateStore activities,
        IActivityExecutionInspectionStore inspections) : IWorkflowSchedulerDrainer
    {
        public TaskCompletionSource Answered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ContinueAfterCancellation { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ActivityExecutionState? StateAfterCancellation { get; private set; }
        public ActivityExecutionInspectionProjection? ProjectionAfterCancellation { get; private set; }

        public async ValueTask<RuntimeSchedulerDrainResult> DrainAsync(RuntimeSchedulerDrainRequest request, CancellationToken cancellationToken = default)
        {
            foreach (var id in new[] { "actexec-1", "actexec-2" })
            {
                Assert.Null(await activities.FindAsync(request.WorkflowExecutionId, id, CancellationToken.None));
                Assert.Null(await inspections.FindAsync(request.WorkflowExecutionId, id, CancellationToken.None));
            }

            Answered.TrySetResult();
            await ContinueAfterCancellation.Task;
            StateAfterCancellation = await activities.FindAsync(request.WorkflowExecutionId, "actexec-unseen", CancellationToken.None);
            ProjectionAfterCancellation = await inspections.FindAsync(request.WorkflowExecutionId, "actexec-unseen", CancellationToken.None);
            return new RuntimeSchedulerDrainResult(request.WorkflowExecutionId, Now, Now, []);
        }
    }

    private sealed class NothingToDeliver : IRuntimePostCommitOutboxProcessor
    {
        public ValueTask<RuntimePostCommitOutboxProcessResult> ProcessAsync(RuntimePostCommitOutboxProcessRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new RuntimePostCommitOutboxProcessResult([]));
    }

    private sealed class HeldLeaseOwnershipService : IRuntimeExecutionOwnershipService
    {
        public ValueTask<RuntimeExecutionLease> AcquireAsync(string workflowExecutionId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new RuntimeExecutionLease("lease", workflowExecutionId, "owner", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1), 1));

        public ValueTask<RuntimeExecutionOwnershipTransitionResult> HeartbeatAsync(RuntimeExecutionLease lease, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new RuntimeExecutionOwnershipTransitionResult(RuntimeExecutionOwnershipTransitionStatus.Applied, lease.FencingToken));

        public ValueTask<RuntimeExecutionOwnershipTransitionResult> ReleaseAsync(RuntimeExecutionLease lease, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new RuntimeExecutionOwnershipTransitionResult(RuntimeExecutionOwnershipTransitionStatus.Applied, lease.FencingToken));

        public ValueTask EnsureCurrentAsync(string workflowExecutionId, long fencingToken, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }

    // Keeps showing the last pushed session, as a flow does that captured it before the scope was disposed.
    private sealed class StickySessionAccessor : IRuntimeCoalescingSessionAccessor
    {
        public RuntimeCoalescingSession? Current { get; private set; }

        public IDisposable Push(RuntimeCoalescingSession? session)
        {
            Current = session;
            return new Kept();
        }

        private sealed class Kept : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }

    // A replacement accessor that keeps the stack and nothing else.
    private sealed class StackOnlySessionAccessor : IRuntimeCoalescingSessionAccessor
    {
        private readonly Stack<RuntimeCoalescingSession?> _stack = new();

        public RuntimeCoalescingSession? Current => _stack.Count == 0 ? null : _stack.Peek();

        public IDisposable Push(RuntimeCoalescingSession? session)
        {
            _stack.Push(session);
            return new Pop(_stack);
        }

        private sealed class Pop(Stack<RuntimeCoalescingSession?> stack) : IDisposable
        {
            public void Dispose() => stack.Pop();
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
