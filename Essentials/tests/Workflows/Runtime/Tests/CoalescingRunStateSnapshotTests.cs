using System.Reflection;
using Elsa.Workflows.Runtime.Contracts;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Elsa.Workflows.Runtime.Services.Coalescing;
using Elsa.Workflows.Runtime.Services.Executions;
using Elsa.Workflows.Runtime.Services.Scheduler;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

// A drain reads its run's workflow execution state once, when it begins. Until the session has a state of its own,
// a read of that state is answered with what the drain read, which may be "no state". Every way the stored state can
// change without the session holding the new one sends the read back to the store.
public sealed class CoalescingRunStateSnapshotTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan SignalTimeout = TimeSpan.FromSeconds(30);
    private const string WorkflowExecutionId = "wfexec-1";
    private static readonly ResolvedPersistenceMode NoRecord = new(PersistenceMode.Balanced, RunHasRecord: false);
    private static readonly ResolvedPersistenceMode HasRecord = new(PersistenceMode.Balanced, RunHasRecord: true);
    private static readonly RuntimeRunStateObservation NoState = new(State: null);

    [Fact]
    public async Task RunThatHadNoState_IsNotReadAgain()
    {
        var fixture = new Fixture(NoRecord, NoState);

        for (var read = 0; read < 3; read++)
            Assert.Null(await fixture.RunStates.FindAsync(WorkflowExecutionId));

        Assert.Equal(0, fixture.Durable.FindCount);
    }

    [Fact]
    public async Task RunThatHadAState_IsAnsweredWithWhatTheDrainRead()
    {
        var read = NewRunState(WorkflowExecutionStatus.Running);
        var fixture = new Fixture(HasRecord, new RuntimeRunStateObservation(read));
        await fixture.Durable.Inner.SaveAsync(read);

        for (var again = 0; again < 3; again++)
            Assert.Same(read, await fixture.RunStates.FindAsync(WorkflowExecutionId));

        Assert.Equal(0, fixture.Durable.FindCount);
    }

    // The session type is public and so is the factory contract. The guards around the snapshot live in the default
    // factory, so a session nobody began through it reads the store.
    [Fact]
    public async Task SessionThatWasGivenNothing_ReadsTheStore()
    {
        var fixture = new Fixture(NoRecord, observed: null);

        Assert.Null(await fixture.RunStates.FindAsync(WorkflowExecutionId));
        Assert.Null(await fixture.RunStates.FindAsync(WorkflowExecutionId));

        Assert.Equal(2, fixture.Durable.FindCount);
    }

    [Fact]
    public async Task StateTheSessionBuffered_WinsOverWhatTheDrainRead()
    {
        var fixture = new Fixture(NoRecord, NoState);
        var started = NewRunState(WorkflowExecutionStatus.Running);

        await fixture.Commits.CommitAsync(NewCommit(1, RuntimeCheckpointNames.WorkflowStarted, started), new(RuntimeCheckpointPersistenceMode.Deferred));

        Assert.True(fixture.Session.HasBufferedChanges);
        Assert.Same(started, await fixture.RunStates.FindAsync(WorkflowExecutionId));
        Assert.Equal(0, fixture.Durable.FindCount);
    }

    // The snapshot stops answering from the moment a write of the state is attempted. This commit's write lands and
    // then reports failure, as a lost acknowledgement does, so the session never learns the new state.
    [Fact]
    public async Task CommitOfTheRunStateWhoseOutcomeIsUncertain_IsReadFromTheStore()
    {
        var fixture = new Fixture(NoRecord, NoState, commitsLandThenThrow: true);
        Assert.Null(await fixture.RunStates.FindAsync(WorkflowExecutionId));

        await Assert.ThrowsAsync<IOException>(async () => await fixture.Commits.CommitAsync(
            NewCommit(1, RuntimeCheckpointNames.ActivityAttemptClaimed, NewRunState(WorkflowExecutionStatus.Running)),
            new(RuntimeCheckpointPersistenceMode.Immediate)));
        Assert.True(fixture.Session.IsActive);

        Assert.NotNull(await fixture.RunStates.FindAsync(WorkflowExecutionId));
        Assert.Equal(1, fixture.Durable.FindCount);
    }

    // A commit that carries no workflow execution state cannot have changed the stored one.
    [Fact]
    public async Task CommitThatDoesNotCarryTheRunState_LeavesTheSnapshotStanding()
    {
        var fixture = new Fixture(NoRecord, NoState, commitsLandThenThrow: true);

        await Assert.ThrowsAsync<IOException>(async () => await fixture.Commits.CommitAsync(
            NewCommit(1, RuntimeCheckpointNames.ActivityAttemptClaimed, runState: null),
            new(RuntimeCheckpointPersistenceMode.Immediate)));
        Assert.True(fixture.Session.IsActive);

        Assert.Null(await fixture.RunStates.FindAsync(WorkflowExecutionId));
        Assert.Equal(0, fixture.Durable.FindCount);
    }

    [Fact]
    public async Task StateSavedPastTheWorkingSet_IsReadFromTheStore()
    {
        var fixture = new Fixture(NoRecord, NoState);
        Assert.Null(await fixture.RunStates.FindAsync(WorkflowExecutionId));

        await fixture.RunStates.SaveAsync(NewRunState(WorkflowExecutionStatus.Running));

        Assert.NotNull(await fixture.RunStates.FindAsync(WorkflowExecutionId));
        Assert.Equal(1, fixture.Durable.FindCount);
    }

    // A direct save ends the snapshot before the write, as a commit does. This save lands and then reports failure.
    [Fact]
    public async Task StateSavedPastTheWorkingSet_WhoseOutcomeIsUncertain_IsReadFromTheStore()
    {
        var fixture = new Fixture(NoRecord, NoState);
        Assert.Null(await fixture.RunStates.FindAsync(WorkflowExecutionId));
        fixture.Durable.SavesLandThenThrow = true;

        await Assert.ThrowsAsync<IOException>(async () => await fixture.RunStates.SaveAsync(NewRunState(WorkflowExecutionStatus.Running)));

        Assert.NotNull(await fixture.RunStates.FindAsync(WorkflowExecutionId));
        Assert.Equal(1, fixture.Durable.FindCount);
    }

    [Fact]
    public async Task StateDeletedPastTheWorkingSet_IsReadFromTheStore()
    {
        var read = NewRunState(WorkflowExecutionStatus.Running);
        var fixture = new Fixture(HasRecord, new RuntimeRunStateObservation(read));
        await fixture.Durable.Inner.SaveAsync(read);
        Assert.Same(read, await fixture.RunStates.FindAsync(WorkflowExecutionId));

        Assert.True(await fixture.RunStates.DeleteAsync(WorkflowExecutionId));

        Assert.Null(await fixture.RunStates.FindAsync(WorkflowExecutionId));
        Assert.Equal(1, fixture.Durable.FindCount);
    }

    // A direct delete ends the snapshot before the write, as a save does. This delete lands and then reports failure.
    [Fact]
    public async Task StateDeletedPastTheWorkingSet_WhoseOutcomeIsUncertain_IsReadFromTheStore()
    {
        var read = NewRunState(WorkflowExecutionStatus.Running);
        var fixture = new Fixture(HasRecord, new RuntimeRunStateObservation(read));
        await fixture.Durable.Inner.SaveAsync(read);
        Assert.Same(read, await fixture.RunStates.FindAsync(WorkflowExecutionId));
        fixture.Durable.DeletesLandThenThrow = true;

        await Assert.ThrowsAsync<IOException>(async () => await fixture.RunStates.DeleteAsync(WorkflowExecutionId));

        Assert.Null(await fixture.RunStates.FindAsync(WorkflowExecutionId));
        Assert.Equal(1, fixture.Durable.FindCount);
    }

    // The ambient scope changes when a nested drain begins. From then on this run's state can be written without
    // passing through its session.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AnotherAmbientScope_EndsTheSnapshot(bool nestedSession)
    {
        var accessor = new AsyncLocalRuntimeCoalescingSessionAccessor();
        var session = NewSession(NoRecord, NoState);
        using var outer = accessor.Push(session);
        var (durable, counter) = CountingRunStateStore.Create();
        var runStates = new CoalescingWorkflowExecutionStateStore(new CoalescingInner<IWorkflowExecutionStateStore>(durable), accessor);
        Assert.Null(await runStates.FindAsync(WorkflowExecutionId));
        Assert.Equal(0, counter.FindCount);

        // Either a nested drain's session or a scope pushed with no session.
        var other = nestedSession
            ? new RuntimeCoalescingSession("wfexec-child", new InMemoryWorkflowSchedulerWorkQueue(), new PersistenceModeOptions())
            : null;
        using (accessor.Push(other))
            await counter.Inner.SaveAsync(NewRunState(WorkflowExecutionStatus.Running));

        Assert.NotNull(await runStates.FindAsync(WorkflowExecutionId));
        Assert.Equal(1, counter.FindCount);
    }

    // The session accessor is a replacement contract. A replacement that only keeps the stack tells the parent
    // session nothing, so the drain scope factory ends the parent's snapshot before it pushes a nested drain.
    [Fact]
    public async Task NestedDrainBegunThroughTheFactory_EndsTheParentsSnapshot_WhateverAccessorIsRegistered()
    {
        var accessor = new StackOnlySessionAccessor();
        var factory = NewFactory(accessor);
        var (durable, counter) = CountingRunStateStore.Create();
        var runStates = new CoalescingWorkflowExecutionStateStore(new CoalescingInner<IWorkflowExecutionStateStore>(durable), accessor);

        await using var parent = factory.Begin(WorkflowExecutionId, NoRecord, NoState);
        Assert.Null(await runStates.FindAsync(WorkflowExecutionId));
        Assert.Equal(0, counter.FindCount);

        await using (factory.Begin("wfexec-child", NoRecord))
            await counter.Inner.SaveAsync(NewRunState(WorkflowExecutionStatus.Running));

        Assert.Same(parent.Session, accessor.Current);
        Assert.NotNull(await runStates.FindAsync(WorkflowExecutionId));
        Assert.Equal(1, counter.FindCount);
    }

    // A drain that threw leaves its session active, with a state it buffered and never stored. A flow that captured
    // that session can begin the run's next drain. The mode resolver's read then goes through the registered store,
    // which answers from the left-behind session, so what it read says nothing about the durable store.
    [Fact]
    public async Task ObservationMadeUnderALeftBehindSessionOfTheSameRun_IsNotUsed()
    {
        var accessor = new StackOnlySessionAccessor();
        var factory = NewFactory(accessor);
        var (durable, counter) = CountingRunStateStore.Create();
        var runStates = new CoalescingWorkflowExecutionStateStore(new CoalescingInner<IWorkflowExecutionStateStore>(durable), accessor);
        var neverStored = NewRunState(WorkflowExecutionStatus.Running);
        var leftBehind = new RuntimeCoalescingSession(WorkflowExecutionId, new InMemoryWorkflowSchedulerWorkQueue(), new PersistenceModeOptions(), innerOutboxStore: null, NoRecord);
        leftBehind.BufferDeferred(NewCommit(1, RuntimeCheckpointNames.WorkflowStarted, neverStored));
        using var captured = accessor.Push(leftBehind);
        Assert.Same(neverStored, await runStates.FindAsync(WorkflowExecutionId));

        await using var next = factory.Begin(WorkflowExecutionId, HasRecord, new RuntimeRunStateObservation(neverStored));

        Assert.Null(await runStates.FindAsync(WorkflowExecutionId));
        Assert.Equal(1, counter.FindCount);
    }

    // A task started inside the drain captures the ambient session and still sees it after the drain popped it.
    // By then the drain no longer vouches for what the session knows.
    [Fact]
    public async Task FlowThatOutlivesTheAmbientScope_ReadsTheStore()
    {
        var accessor = new AsyncLocalRuntimeCoalescingSessionAccessor();
        var session = NewSession(NoRecord, NoState);
        var (durable, counter) = CountingRunStateStore.Create();
        var runStates = new CoalescingWorkflowExecutionStateStore(new CoalescingInner<IWorkflowExecutionStateStore>(durable), accessor);
        var scope = accessor.Push(session);
        Assert.Null(await runStates.FindAsync(WorkflowExecutionId));
        var popped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var late = Task.Run(async () =>
        {
            await popped.Task;
            Assert.Same(session, accessor.Current);
            return await runStates.FindAsync(WorkflowExecutionId);
        });

        scope.Dispose();
        Assert.True(session.IsActive);
        await counter.Inner.SaveAsync(NewRunState(WorkflowExecutionStatus.Running));
        popped.SetResult();

        Assert.NotNull(await late);
    }

    // A drain that throws skips its flush, so its session is still active when the scope is disposed. The accessor
    // here stands for any flow that still holds that session.
    [Fact]
    public async Task ScopeDisposedWhileItsSessionIsActive_EndsTheSnapshot()
    {
        var accessor = new StickySessionAccessor();
        var factory = NewFactory(accessor);
        var (durable, counter) = CountingRunStateStore.Create();
        var runStates = new CoalescingWorkflowExecutionStateStore(new CoalescingInner<IWorkflowExecutionStateStore>(durable), accessor);
        var scope = factory.Begin(WorkflowExecutionId, NoRecord, NoState);
        Assert.Null(await runStates.FindAsync(WorkflowExecutionId));
        Assert.Equal(0, counter.FindCount);

        await scope.DisposeAsync();
        Assert.True(scope.Session.IsActive);
        Assert.Same(scope.Session, accessor.Current);
        await counter.Inner.SaveAsync(NewRunState(WorkflowExecutionStatus.Running));

        Assert.NotNull(await runStates.FindAsync(WorkflowExecutionId));
    }

    // The coalescing commit store is what tells the session that a commit carries the run's state. A host that
    // registered its own commit store over it, or instead of it, has commits the session never hears of.
    [Fact]
    public async Task SessionWhoseCommitsBypassTheCoalescingStore_ReadsTheStore()
    {
        var accessor = new StackOnlySessionAccessor();
        var factory = NewFactory(accessor, commitsThroughTheDecorator: false);
        var (durable, counter) = CountingRunStateStore.Create();
        var runStates = new CoalescingWorkflowExecutionStateStore(new CoalescingInner<IWorkflowExecutionStateStore>(durable), accessor);

        await using var scope = factory.Begin(WorkflowExecutionId, NoRecord, NoState);
        Assert.Null(await runStates.FindAsync(WorkflowExecutionId));
        Assert.Null(await runStates.FindAsync(WorkflowExecutionId));

        Assert.Equal(2, counter.FindCount);
    }

    // An ended snapshot never answers again, whether it was ended after it began or before.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task EndedSnapshot_IsNotBegunAgain(bool begunBeforeItEnded)
    {
        var fixture = new Fixture(NoRecord, begunBeforeItEnded ? NoState : null);

        fixture.Session.EndStoreObservations();
        fixture.Session.BeginRunStateSnapshot(NoState);

        Assert.Null(await fixture.RunStates.FindAsync(WorkflowExecutionId));
        Assert.Equal(1, fixture.Durable.FindCount);
    }

    // What the drain read has to agree with the mode it was resolved under, and it has to be this run's state.
    [Fact]
    public async Task ObservationThatDoesNotFitTheSession_IsNotUsed()
    {
        var stateWithoutARecord = new Fixture(NoRecord, new RuntimeRunStateObservation(NewRunState(WorkflowExecutionStatus.Running)));
        var recordWithoutAState = new Fixture(HasRecord, NoState);
        var anotherRunsState = new Fixture(HasRecord, new RuntimeRunStateObservation(NewRunState(WorkflowExecutionStatus.Running, "wfexec-other")));

        foreach (var fixture in new[] { stateWithoutARecord, recordWithoutAState, anotherRunsState })
        {
            Assert.Null(await fixture.RunStates.FindAsync(WorkflowExecutionId));
            Assert.Equal(1, fixture.Durable.FindCount);
        }
    }

    // A session answers for its own run only.
    [Fact]
    public async Task StateOfAnotherRun_IsReadFromTheStore()
    {
        var fixture = new Fixture(NoRecord, NoState);
        await fixture.Durable.Inner.SaveAsync(NewRunState(WorkflowExecutionStatus.Running, "wfexec-other"));

        Assert.NotNull(await fixture.RunStates.FindAsync("wfexec-other"));

        Assert.Equal(1, fixture.Durable.FindCount);
    }

    [Fact]
    public async Task DeactivatedSession_ReadsTheStore()
    {
        var fixture = new Fixture(NoRecord, NoState);
        Assert.Null(await fixture.RunStates.FindAsync(WorkflowExecutionId));
        fixture.Session.Deactivate();

        Assert.Null(await fixture.RunStates.FindAsync(WorkflowExecutionId));

        Assert.Equal(1, fixture.Durable.FindCount);
    }

    // The store refuses a cancelled read before it queries. A read the snapshot answers refuses it too.
    [Fact]
    public async Task ReadTheSnapshotAnswers_StillRefusesACancelledToken()
    {
        var fixture = new Fixture(NoRecord, NoState);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await fixture.RunStates.FindAsync(WorkflowExecutionId, cancelled.Token));

        Assert.Equal(0, fixture.Durable.FindCount);
    }

    // The orchestrator hands the first segment what the default resolver read to find the run's mode. The drain then
    // reads the run's state through the overlay as often as it likes without another read of the store.
    [Fact]
    public async Task Drain_ReadsItsRunStateOnce()
    {
        var drain = new DrainFixture(defaultResolver: true);

        await drain.RunAsync();

        Assert.Equal(3, drain.Drainer.ReadsDuringTheDrain);
        Assert.Null(drain.Drainer.LastRead);
        Assert.Equal(1, drain.Durable.FindCount);
    }

    // The same for a run that has a state: the drain is handed the state the resolver read.
    [Fact]
    public async Task DrainOfARunThatHasAState_ReadsItOnce()
    {
        var drain = new DrainFixture(defaultResolver: true);
        var stored = NewRunState(WorkflowExecutionStatus.Running, recordedMode: PersistenceMode.Balanced);
        await drain.Durable.Inner.SaveAsync(stored);

        await drain.RunAsync();

        Assert.Equal(3, drain.Drainer.ReadsDuringTheDrain);
        Assert.Same(stored, drain.Drainer.LastRead);
        Assert.Equal(1, drain.Durable.FindCount);
    }

    // A replacement resolver says nothing about what it read, so the drain reads the state itself, as before.
    [Fact]
    public async Task DrainUnderAReplacementResolver_ReadsItsRunStateFromTheStore()
    {
        var drain = new DrainFixture(defaultResolver: false);

        await drain.RunAsync();

        Assert.Equal(3, drain.Drainer.ReadsDuringTheDrain);
        Assert.Equal(3, drain.Durable.FindCount);
    }

    // A failed lease heartbeat cancels the drain, so a cancelled drain may no longer own the run. A read that does
    // not carry the drain's token still reaches the overlay while the drain unwinds.
    [Fact]
    public async Task CancelledDrain_ReadsItsRunStateFromTheStore()
    {
        var drain = new DrainFixture(defaultResolver: true, holdForCancellation: true);
        using var cancellation = new CancellationTokenSource();

        var running = drain.RunAsync(cancellation.Token);
        await drain.Drainer.Answered.Task.WaitAsync(SignalTimeout);
        Assert.Equal(1, drain.Durable.FindCount);
        await drain.Durable.Inner.SaveAsync(NewRunState(WorkflowExecutionStatus.Running));
        await cancellation.CancelAsync();
        drain.Drainer.ContinueAfterCancellation.TrySetResult();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running.WaitAsync(SignalTimeout));
        Assert.NotNull(drain.Drainer.StateAfterCancellation);
        Assert.Equal(2, drain.Durable.FindCount);
    }

    // A session as the drain scope factory hands it out: begun with what the drain read, if anything.
    private static RuntimeCoalescingSession NewSession(ResolvedPersistenceMode mode, RuntimeRunStateObservation? observed)
    {
        var session = new RuntimeCoalescingSession(WorkflowExecutionId, new InMemoryWorkflowSchedulerWorkQueue(), new PersistenceModeOptions(), innerOutboxStore: null, mode);
        if (observed is not null)
            session.BeginRunStateSnapshot(observed);
        return session;
    }

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

    private static WorkflowExecutionState NewRunState(WorkflowExecutionStatus status, string workflowExecutionId = WorkflowExecutionId, PersistenceMode? recordedMode = null) =>
        new(
            workflowExecutionId,
            new WorkflowExecutableIdentity("artifact-1", "definition-1", "version-1", "1.0.0", "sha256:test"),
            status,
            null,
            Now,
            Now,
            Now,
            null,
            null,
            null,
            "tenant-1",
            recordedMode is { } mode
                ? new Dictionary<string, string> { [RuntimeMetadataKeys.PersistenceMode] = mode.ToString() }
                : new Dictionary<string, string>());

    private static RuntimeCheckpointCommit NewCommit(int checkpoint, string checkpointName, WorkflowExecutionState? runState) =>
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
                workflowExecution: runState is null
                    ? null
                    : new RuntimeStateChange<WorkflowExecutionState>(runState.WorkflowExecutionId, RuntimeStateChangeOperation.Upsert, runState, new Dictionary<string, string>()),
                scheduler: null,
                activityExecutions: [],
                bookmarks: [],
                durableValues: [],
                incidents: [],
                operational: []),
            PostCommitIntents: [],
            Metadata: new Dictionary<string, string>());

    private static WorkflowExecutionCommandEnvelope NewEnvelope()
    {
        var command = new WorkflowExecutionCommand(
            CommandId: "run-state-snapshot-command",
            WorkflowExecutionId: WorkflowExecutionId,
            Kind: WorkflowExecutionCommandKind.RunSchedulerWork,
            EnqueuedAt: Now,
            Payload: null,
            Metadata: new Dictionary<string, string>());
        return new WorkflowExecutionCommandEnvelope(
            envelopeId: "run-state-snapshot-envelope",
            workflowExecutionId: WorkflowExecutionId,
            command,
            idempotencyKey: "run-state-snapshot-command",
            deliveryMode: WorkflowExecutionCommandDeliveryMode.AtLeastOnce,
            enqueuedAt: Now);
    }

    private sealed class Fixture
    {
        public Fixture(ResolvedPersistenceMode mode, RuntimeRunStateObservation? observed, bool commitsLandThenThrow = false)
        {
            Session = NewSession(mode, observed);
            var accessor = new FixedSessionAccessor(Session);
            var (durable, counter) = CountingRunStateStore.Create();
            Durable = counter;
            RunStates = new CoalescingWorkflowExecutionStateStore(new CoalescingInner<IWorkflowExecutionStateStore>(durable), accessor);
            IRuntimeCheckpointCommitStore innerCommits = commitsLandThenThrow
                ? new LandThenThrowCommitStore(counter.Inner)
                : new InMemoryRuntimeCheckpointCommitStore();
            Commits = new CoalescingRuntimeCheckpointCommitStore(new CoalescingInner<IRuntimeCheckpointCommitStore>(innerCommits), accessor);
        }

        public RuntimeCoalescingSession Session { get; }
        public CountingRunStateStore Durable { get; }
        public CoalescingWorkflowExecutionStateStore RunStates { get; }
        public CoalescingRuntimeCheckpointCommitStore Commits { get; }
    }

    // A drain through WorkflowDrainOrchestrator over the real drain scope factory and session accessor. The drainer
    // stands in for the work handlers: it only reads the run's state through the overlay.
    private sealed class DrainFixture
    {
        private readonly WorkflowDrainOrchestrator _orchestrator;

        public DrainFixture(bool defaultResolver, bool holdForCancellation = false)
        {
            var accessor = new AsyncLocalRuntimeCoalescingSessionAccessor();
            var (durable, counter) = CountingRunStateStore.Create();
            Durable = counter;
            var runStates = new CoalescingWorkflowExecutionStateStore(new CoalescingInner<IWorkflowExecutionStateStore>(durable), accessor);
            Drainer = new ReadingDrainer(runStates, holdForCancellation);
            var queue = new InMemoryWorkflowSchedulerWorkQueue();
            var checkpointStore = new InMemoryRuntimeCheckpointCommitStore();
            IRuntimePersistenceModeResolver resolver = defaultResolver
                ? new RuntimePersistenceModeResolver(Unused<IWorkflowExecutableStore>.Create(), runStates, queue, [new PersistenceModeOptions()])
                : new FixedPersistenceModeResolver(PersistenceMode.Balanced, runHasRecord: false);
            _orchestrator = new WorkflowDrainOrchestrator(
                Drainer,
                new NothingToDeliver(),
                [],
                TestCheckpointRuleViolationFaulter.Create(),
                new HeldLeaseOwnershipService(),
                new AsyncLocalRuntimeExecutionOwnershipContextAccessor(),
                checkpointStore,
                checkpointStore,
                queue,
                coalescingScopeFactory: NewFactory(accessor),
                persistenceModeResolver: resolver);
        }

        public CountingRunStateStore Durable { get; }
        public ReadingDrainer Drainer { get; }

        public Task RunAsync(CancellationToken cancellationToken = default) =>
            _orchestrator.DrainAsync(NewEnvelope(), new RuntimeSchedulerDrainRequest(WorkflowExecutionId), cancellationToken).AsTask();
    }

    private sealed class ReadingDrainer(IWorkflowExecutionStateStore runStates, bool holdForCancellation) : IWorkflowSchedulerDrainer
    {
        public int ReadsDuringTheDrain { get; private set; }
        public WorkflowExecutionState? LastRead { get; private set; }
        public TaskCompletionSource Answered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ContinueAfterCancellation { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public WorkflowExecutionState? StateAfterCancellation { get; private set; }

        public async ValueTask<RuntimeSchedulerDrainResult> DrainAsync(RuntimeSchedulerDrainRequest request, CancellationToken cancellationToken = default)
        {
            for (var read = 0; read < 3; read++)
            {
                LastRead = await runStates.FindAsync(request.WorkflowExecutionId, CancellationToken.None);
                ReadsDuringTheDrain++;
            }

            Answered.TrySetResult();
            if (holdForCancellation)
            {
                await ContinueAfterCancellation.Task;
                StateAfterCancellation = await runStates.FindAsync(request.WorkflowExecutionId, CancellationToken.None);
            }

            return new RuntimeSchedulerDrainResult(request.WorkflowExecutionId, Now, Now, []);
        }
    }

    // Counts the reads that reach the durable store, and can stand in for a save whose acknowledgement is lost.
    private class CountingRunStateStore : DispatchProxy
    {
        public InMemoryWorkflowExecutionStateStore Inner { get; } = new();
        public int FindCount { get; private set; }
        public bool SavesLandThenThrow { get; set; }
        public bool DeletesLandThenThrow { get; set; }

        public static (IWorkflowExecutionStateStore Store, CountingRunStateStore Counter) Create()
        {
            var store = Create<IWorkflowExecutionStateStore, CountingRunStateStore>();
            return (store, (CountingRunStateStore)(object)store);
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            ArgumentNullException.ThrowIfNull(targetMethod);
            if (targetMethod.Name == nameof(IWorkflowExecutionStateStore.FindAsync))
                FindCount++;

            var result = targetMethod.Invoke(Inner, args);
            if (targetMethod.Name == nameof(IWorkflowExecutionStateStore.SaveAsync) && SavesLandThenThrow)
                return ValueTask.FromException<WorkflowExecutionState>(new IOException("The save's acknowledgement was lost."));
            if (targetMethod.Name == nameof(IWorkflowExecutionStateStore.DeleteAsync) && DeletesLandThenThrow)
                return ValueTask.FromException<bool>(new IOException("The delete's acknowledgement was lost."));
            return result;
        }
    }

    // A dependency the test never reaches.
    private class Unused<T> : DispatchProxy where T : class
    {
        public static T Create() => Create<T, Unused<T>>();

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            throw new NotSupportedException($"This test does not use {typeof(T).Name}.{targetMethod?.Name}.");
    }

    // A durable commit that writes the run's state, if the commit carries one, and then loses its acknowledgement.
    private sealed class LandThenThrowCommitStore(IWorkflowExecutionStateStore durable) : IRuntimeCheckpointCommitStore
    {
        public async ValueTask<RuntimeCheckpointCommitStoreResult> CommitAsync(RuntimeCheckpointCommit commit, RuntimeCheckpointPersistenceDecision decision, CancellationToken cancellationToken = default)
        {
            if (commit.StateChanges.WorkflowExecution is { } change)
                await durable.SaveAsync(change.State, cancellationToken);
            throw new IOException("The commit's acknowledgement was lost.");
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

    private sealed class FixedSessionAccessor(RuntimeCoalescingSession session) : IRuntimeCoalescingSessionAccessor
    {
        public RuntimeCoalescingSession? Current => session;

        public IDisposable Push(RuntimeCoalescingSession? pushedSession) =>
            throw new NotSupportedException("This test provides a fixed ambient session.");
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
}
