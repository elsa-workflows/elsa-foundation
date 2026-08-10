using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class RuntimeSchedulerDrainTests
{
    private readonly DateTimeOffset _now = new(2026, 6, 11, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Constructor_RequiresTheWorkflowExecutionStateStore_SoTheTerminalGuardCannotBeSilentlyDisabled()
    {
        // RT-8 + W5: the terminal-status guard reads the workflow execution state store to stop sibling work once an
        // execution is terminal. The single primary constructor makes that store required by construction, so no caller
        // can pick a narrower overload that leaves the guard inert.
        Assert.Throws<ArgumentNullException>(() => new WorkflowSchedulerDrainer(
            new InMemoryWorkflowSchedulerWorkQueue(),
            [new NoopWorkflowSchedulerWorkHandler()],
            workflowExecutionStateStore: null!));
    }

    [Fact]
    public async Task DrainAsync_DispatchesQueuedWorkInFifoOrder()
    {
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var handler = new RecordingSchedulerWorkHandler();
        var drainer = TestSchedulerDrainer.Create(queue, [handler, new NoopWorkflowSchedulerWorkHandler()], new FixedTimeProvider(_now));
        await queue.EnqueueAsync(NewWorkItem(1));
        await queue.EnqueueAsync(NewWorkItem(2));
        await queue.EnqueueAsync(NewWorkItem(3));

        var result = await drainer.DrainAsync(new RuntimeSchedulerDrainRequest("wfexec-1"));

        Assert.Equal(3, result.DrainedCount);
        Assert.False(result.StoppedOnFault);
        Assert.Equal(_now, result.StartedAt);
        Assert.Equal(_now, result.CompletedAt);
        Assert.Equal(["work-1", "work-2", "work-3"], handler.WorkItemIds);
        Assert.All(result.Items, item => Assert.Equal(RuntimeSchedulerWorkItemResultStatus.Completed, item.Status));
        Assert.All(result.Items, item => Assert.Equal(handler.Name, item.HandlerName));
    }

    [Fact]
    public async Task DrainAsync_RenewsClaimAndCompletesWithRefreshedRevision()
    {
        var innerQueue = new InMemoryWorkflowSchedulerWorkQueue();
        var queue = new RenewalObservingWorkQueue(innerQueue);
        var handler = new BlockingSchedulerWorkHandler();
        var timeProvider = new ManualTimerTimeProvider(_now);
        var claimOptions = new RuntimeSchedulerWorkClaimOptions { VisibilityTimeout = TimeSpan.FromSeconds(9) };
        var drainer = TestSchedulerDrainer.Create(
            queue,
            [handler, new NoopWorkflowSchedulerWorkHandler()],
            timeProvider,
            claimOptions: claimOptions);
        await queue.EnqueueAsync(NewWorkItem(1));

        var drainTask = drainer.DrainAsync(new RuntimeSchedulerDrainRequest("wfexec-1")).AsTask();
        await handler.Started.WaitAsync(TimeSpan.FromSeconds(5));
        await timeProvider.TimerCreated.WaitAsync(TimeSpan.FromSeconds(5));
        timeProvider.AdvanceAndFire(TimeSpan.FromSeconds(3));
        await queue.FirstRenewalObserved.WaitAsync(TimeSpan.FromSeconds(5));
        handler.Release();

        var result = await drainTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(1, queue.RenewalAttempts);
        Assert.Equal(1, result.DrainedCount);
        Assert.Empty(await queue.ListAllAsync(new RuntimeSchedulerWorkQuery("wfexec-1")));
    }

    [Fact]
    public async Task DrainAsync_ClaimRenewalLossCancelsDispatchWithoutAcknowledgingOrPoisoning()
    {
        var innerQueue = new InMemoryWorkflowSchedulerWorkQueue();
        var queue = new RenewalObservingWorkQueue(innerQueue, loseFirstRenewal: true);
        var poisonStore = new InMemoryWorkflowSchedulerPoisonStore();
        var handler = new BlockingSchedulerWorkHandler();
        var timeProvider = new ManualTimerTimeProvider(_now);
        var claimOptions = new RuntimeSchedulerWorkClaimOptions { VisibilityTimeout = TimeSpan.FromSeconds(9) };
        var drainer = TestSchedulerDrainer.Create(
            queue,
            [handler, new NoopWorkflowSchedulerWorkHandler()],
            timeProvider,
            poisonStore: poisonStore,
            claimOptions: claimOptions);
        await queue.EnqueueAsync(NewWorkItem(1));

        var drainTask = drainer.DrainAsync(new RuntimeSchedulerDrainRequest("wfexec-1")).AsTask();
        await handler.Started.WaitAsync(TimeSpan.FromSeconds(5));
        await timeProvider.TimerCreated.WaitAsync(TimeSpan.FromSeconds(5));
        timeProvider.AdvanceAndFire(TimeSpan.FromSeconds(3));

        var exception = await Assert.ThrowsAnyAsync<InvalidOperationException>(
            () => drainTask.WaitAsync(TimeSpan.FromSeconds(5)));

        Assert.Contains("was lost during 'renew'", exception.Message);
        Assert.True(handler.CancellationObserved);
        Assert.Collection(
            await queue.ListAllAsync(new RuntimeSchedulerWorkQuery("wfexec-1")),
            item => Assert.Equal("work-1", item.WorkItemId));
        Assert.Empty(await poisonStore.ListAsync("wfexec-1"));
    }

    // #1254: the dispatch's own checkpoint commit consumes the claim, so a renewal that fires afterwards finds nothing
    // and used to read as a lost claim — cancelling a dispatch whose work had already committed and aborting the drain
    // with the queue row already gone, so nothing redelivered it. The precondition is constructed rather than raced: the
    // handler consumes, then the renewal timer is fired while it is parked.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DrainAsync_ClaimRenewalAfterOwnConsume_LeavesTheCommittedDispatchAlone(bool settleBeforeParking)
    {
        var innerQueue = new InMemoryWorkflowSchedulerWorkQueue();
        var queue = new RenewalObservingWorkQueue(innerQueue);
        var poisonStore = new InMemoryWorkflowSchedulerPoisonStore();
        var accessor = new ConsumeCheckObservingClaimAccessor(new RuntimeConsumedSchedulerWorkClaimAccessor());
        var handler = new ConsumingSchedulerWorkHandler(queue, accessor, settleBeforeParking);
        var timeProvider = new ManualTimerTimeProvider(_now);
        var claimOptions = new RuntimeSchedulerWorkClaimOptions { VisibilityTimeout = TimeSpan.FromSeconds(9) };
        var drainer = TestSchedulerDrainer.Create(
            queue,
            [handler, new NoopWorkflowSchedulerWorkHandler()],
            timeProvider,
            poisonStore: poisonStore,
            claimOptions: claimOptions,
            consumedWorkClaimAccessor: accessor);
        await queue.EnqueueAsync(NewWorkItem(1));

        var drainTask = drainer.DrainAsync(new RuntimeSchedulerDrainRequest("wfexec-1")).AsTask();
        await handler.Consumed.WaitAsync(TimeSpan.FromSeconds(5));
        await timeProvider.TimerCreated.WaitAsync(TimeSpan.FromSeconds(5));
        timeProvider.AdvanceAndFire(TimeSpan.FromSeconds(3));

        // The renewal loop woke, recognized its own consume, and stopped renewing instead of calling a queue that would
        // report the item gone. Waiting on the check itself keeps the assertion below from passing vacuously.
        await accessor.OwnConsumeObserved.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(0, queue.RenewalAttempts);
        handler.Release();

        var result = await drainTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(1, result.DrainedCount);
        Assert.False(result.StoppedOnFault);
        Assert.All(result.Items, item => Assert.Equal(RuntimeSchedulerWorkItemResultStatus.Completed, item.Status));
        Assert.False(handler.CancellationObserved);
        Assert.Empty(await queue.ListAllAsync(new RuntimeSchedulerWorkQuery("wfexec-1")));
        Assert.Empty(await poisonStore.ListAsync("wfexec-1"));
    }

    // The permissive branch above must not swallow a genuine loss: with a consume staged but never landed, a stale
    // renewal is still a successor taking the item, and it still cancels the dispatch and refuses to ack or poison it.
    [Fact]
    public async Task DrainAsync_ClaimRenewalLossWithoutOwnConsume_StillCancelsTheDispatch()
    {
        var innerQueue = new InMemoryWorkflowSchedulerWorkQueue();
        var queue = new RenewalObservingWorkQueue(innerQueue, loseFirstRenewal: true);
        var poisonStore = new InMemoryWorkflowSchedulerPoisonStore();
        var accessor = new ConsumeCheckObservingClaimAccessor(new RuntimeConsumedSchedulerWorkClaimAccessor());
        var handler = new BlockingSchedulerWorkHandler();
        var timeProvider = new ManualTimerTimeProvider(_now);
        var claimOptions = new RuntimeSchedulerWorkClaimOptions { VisibilityTimeout = TimeSpan.FromSeconds(9) };
        var drainer = TestSchedulerDrainer.Create(
            queue,
            [handler, new NoopWorkflowSchedulerWorkHandler()],
            timeProvider,
            poisonStore: poisonStore,
            claimOptions: claimOptions,
            consumedWorkClaimAccessor: accessor);
        await queue.EnqueueAsync(NewWorkItem(1));

        var drainTask = drainer.DrainAsync(new RuntimeSchedulerDrainRequest("wfexec-1")).AsTask();
        await handler.Started.WaitAsync(TimeSpan.FromSeconds(5));
        await timeProvider.TimerCreated.WaitAsync(TimeSpan.FromSeconds(5));
        timeProvider.AdvanceAndFire(TimeSpan.FromSeconds(3));

        var exception = await Assert.ThrowsAnyAsync<InvalidOperationException>(
            () => drainTask.WaitAsync(TimeSpan.FromSeconds(5)));

        Assert.Contains("was lost during 'renew'", exception.Message);
        Assert.True(handler.CancellationObserved);
        Assert.False(accessor.OwnConsumeObserved.IsCompleted);
        Assert.Collection(
            await queue.ListAllAsync(new RuntimeSchedulerWorkQuery("wfexec-1")),
            item => Assert.Equal("work-1", item.WorkItemId));
        Assert.Empty(await poisonStore.ListAsync("wfexec-1"));
    }

    // R3: the renewal loop used to renew for as long as the handler ran, so a hung activity kept its claim (and, via
    // the drain's ownership heartbeat, its liveness) fresh forever — invisible to the recovery scanner, whose only two
    // candidacy signals are exactly those. MaxDispatchDuration makes that bounded: past the ceiling the loop stops
    // renewing, cancels the dispatch, and the item lands in the poison ladder as a decided, visible outcome.
    [Fact]
    public async Task DrainAsync_DispatchDeadlineExceeded_CancelsDispatchAndPoisonsTheItem()
    {
        var innerQueue = new InMemoryWorkflowSchedulerWorkQueue();
        var queue = new RenewalObservingWorkQueue(innerQueue);
        var poisonStore = new InMemoryWorkflowSchedulerPoisonStore();
        var handler = new BlockingSchedulerWorkHandler();
        var timeProvider = new ManualTimerTimeProvider(_now);
        var claimOptions = new RuntimeSchedulerWorkClaimOptions
        {
            VisibilityTimeout = TimeSpan.FromSeconds(9),
            MaxDispatchDuration = TimeSpan.FromSeconds(2)
        };
        var drainer = TestSchedulerDrainer.Create(
            queue,
            [handler, new NoopWorkflowSchedulerWorkHandler()],
            timeProvider,
            poisonStore: poisonStore,
            claimOptions: claimOptions);
        await queue.EnqueueAsync(NewWorkItem(1));

        var drainTask = drainer.DrainAsync(new RuntimeSchedulerDrainRequest("wfexec-1")).AsTask();
        await handler.Started.WaitAsync(TimeSpan.FromSeconds(5));
        await timeProvider.TimerCreated.WaitAsync(TimeSpan.FromSeconds(5));

        // One cadence tick (3s) carries the clock past the 2s ceiling, so the watchdog breaches on its first wake.
        timeProvider.AdvanceAndFire(TimeSpan.FromSeconds(3));

        var result = await drainTask.WaitAsync(TimeSpan.FromSeconds(5));

        // The dispatch was cancelled, and the breach never extended the claim: renewal stops before renewing.
        Assert.True(handler.CancellationObserved);
        Assert.Equal(0, queue.RenewalAttempts);

        // A decided outcome, not a silent re-drive: the item is ack-deleted and recorded as poisoned, which the
        // drain observer then projects into a blocking incident.
        Assert.Equal(RuntimeSchedulerWorkItemResultStatus.Faulted, Assert.Single(result.Items).Status);
        Assert.Empty(await queue.ListAllAsync(new RuntimeSchedulerWorkQuery("wfexec-1")));
        var poisoned = Assert.Single(await poisonStore.ListAsync("wfexec-1"));
        Assert.Equal("work-1", poisoned.WorkItemId);
        Assert.Contains("exceeded the maximum dispatch duration", poisoned.Fault.Message);
    }

    // The converse, and the reason the deadline is not simply given precedence over the dispatch result: a ceiling that
    // fires in the same instant the handler returns must NOT poison work that already succeeded — by then its effects
    // are committed, so both the poison record and the incident would be false.
    [Fact]
    public async Task DrainAsync_DispatchDeadlineRacingASuccessfulDispatch_DoesNotPoison()
    {
        var innerQueue = new InMemoryWorkflowSchedulerWorkQueue();
        var queue = new RenewalObservingWorkQueue(innerQueue);
        var poisonStore = new InMemoryWorkflowSchedulerPoisonStore();
        // Ignores cancellation, so the breach below cancels the dispatch and the handler still completes successfully —
        // the exact interleaving the suppression exists for, made deterministic.
        var handler = new CancellationIgnoringSchedulerWorkHandler();
        var timeProvider = new ManualTimerTimeProvider(_now);
        var claimOptions = new RuntimeSchedulerWorkClaimOptions
        {
            VisibilityTimeout = TimeSpan.FromSeconds(9),
            MaxDispatchDuration = TimeSpan.FromSeconds(2)
        };
        var drainer = TestSchedulerDrainer.Create(
            queue,
            [handler, new NoopWorkflowSchedulerWorkHandler()],
            timeProvider,
            poisonStore: poisonStore,
            claimOptions: claimOptions);
        await queue.EnqueueAsync(NewWorkItem(1));

        var drainTask = drainer.DrainAsync(new RuntimeSchedulerDrainRequest("wfexec-1")).AsTask();
        await handler.Started.WaitAsync(TimeSpan.FromSeconds(5));
        await timeProvider.TimerCreated.WaitAsync(TimeSpan.FromSeconds(5));

        timeProvider.AdvanceAndFire(TimeSpan.FromSeconds(3));
        handler.Release();

        var result = await drainTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(RuntimeSchedulerWorkItemResultStatus.Completed, Assert.Single(result.Items).Status);
        Assert.Empty(await poisonStore.ListAsync("wfexec-1"));
        Assert.Empty(await queue.ListAllAsync(new RuntimeSchedulerWorkQuery("wfexec-1")));
    }

    // An infinite ceiling restores the pre-R3 behavior exactly, so a host that wants the old unbounded renewal can have
    // it back with one setting.
    [Fact]
    public async Task DrainAsync_InfiniteDispatchDeadline_RenewsWithoutLimit()
    {
        var innerQueue = new InMemoryWorkflowSchedulerWorkQueue();
        var queue = new RenewalObservingWorkQueue(innerQueue);
        var poisonStore = new InMemoryWorkflowSchedulerPoisonStore();
        var handler = new BlockingSchedulerWorkHandler();
        var timeProvider = new ManualTimerTimeProvider(_now);
        var claimOptions = new RuntimeSchedulerWorkClaimOptions
        {
            VisibilityTimeout = TimeSpan.FromSeconds(9),
            MaxDispatchDuration = Timeout.InfiniteTimeSpan
        };
        var drainer = TestSchedulerDrainer.Create(
            queue,
            [handler, new NoopWorkflowSchedulerWorkHandler()],
            timeProvider,
            poisonStore: poisonStore,
            claimOptions: claimOptions);
        await queue.EnqueueAsync(NewWorkItem(1));

        var drainTask = drainer.DrainAsync(new RuntimeSchedulerDrainRequest("wfexec-1")).AsTask();
        await handler.Started.WaitAsync(TimeSpan.FromSeconds(5));
        await timeProvider.TimerCreated.WaitAsync(TimeSpan.FromSeconds(5));

        // Far past what any finite default would allow; the renewal must still happen and the dispatch survive.
        timeProvider.AdvanceAndFire(TimeSpan.FromHours(12));
        await queue.FirstRenewalObserved.WaitAsync(TimeSpan.FromSeconds(5));
        handler.Release();

        var result = await drainTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(handler.CancellationObserved);
        Assert.Equal(1, queue.RenewalAttempts);
        Assert.Equal(RuntimeSchedulerWorkItemResultStatus.Completed, Assert.Single(result.Items).Status);
        Assert.Empty(await poisonStore.ListAsync("wfexec-1"));
    }

    [Fact]
    public async Task DrainAsync_TripsSingleWriterInvariant_WhenDequeueDoesNotMatchPeekedHead()
    {
        // Single-writer TOCTOU tripwire (RT-2): peek returns work-1 (the pause-gate decision is computed for it) but a
        // concurrent writer interleaves so the dequeue returns work-2. The drainer must fail fast rather than gate
        // work-2's dequeue on work-1's decision.
        var queue = new PeekDequeueMismatchWorkQueue(peeked: NewWorkItem(1), dequeued: NewWorkItem(2));
        var drainer = TestSchedulerDrainer.Create(queue, [new NoopWorkflowSchedulerWorkHandler()], new FixedTimeProvider(_now));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => drainer.DrainAsync(new RuntimeSchedulerDrainRequest("wfexec-1")).AsTask());

        Assert.Contains("Single-writer invariant violation", exception.Message);
        Assert.Contains("work-1", exception.Message);
        Assert.Contains("work-2", exception.Message);
    }

    [Fact]
    public async Task DrainAsync_RespectsMaxWorkItems()
    {
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var handler = new RecordingSchedulerWorkHandler();
        var drainer = TestSchedulerDrainer.Create(queue, [handler, new NoopWorkflowSchedulerWorkHandler()], new FixedTimeProvider(_now));
        await queue.EnqueueAsync(NewWorkItem(1));
        await queue.EnqueueAsync(NewWorkItem(2));
        await queue.EnqueueAsync(NewWorkItem(3));

        var result = await drainer.DrainAsync(new RuntimeSchedulerDrainRequest("wfexec-1", maxWorkItems: 2));
        var remaining = await queue.ListAllAsync(new RuntimeSchedulerWorkQuery("wfexec-1"));

        Assert.Equal(2, result.DrainedCount);
        Assert.Equal(["work-1", "work-2"], handler.WorkItemIds);
        Assert.Collection(remaining, item => Assert.Equal("work-3", item.WorkItemId));
    }

    [Fact]
    public async Task DrainAsync_StopsOnHandlerFault()
    {
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var handler = new RecordingSchedulerWorkHandler(faultOnWorkItemId: "work-2");
        var drainer = TestSchedulerDrainer.Create(queue, [handler, new NoopWorkflowSchedulerWorkHandler()], new FixedTimeProvider(_now));
        await queue.EnqueueAsync(NewWorkItem(1));
        await queue.EnqueueAsync(NewWorkItem(2));
        await queue.EnqueueAsync(NewWorkItem(3));

        var result = await drainer.DrainAsync(new RuntimeSchedulerDrainRequest("wfexec-1"));
        var remaining = await queue.ListAllAsync(new RuntimeSchedulerWorkQuery("wfexec-1"));

        Assert.True(result.StoppedOnFault);
        Assert.Equal(2, result.DrainedCount);
        Assert.Collection(
            result.Items,
            first => Assert.Equal(RuntimeSchedulerWorkItemResultStatus.Completed, first.Status),
            second =>
            {
                Assert.Equal(RuntimeSchedulerWorkItemResultStatus.Faulted, second.Status);
                Assert.Contains(nameof(InvalidOperationException), second.Error);
                Assert.Contains("Fault requested for work-2.", second.Error);
            });
        Assert.Collection(remaining, item => Assert.Equal("work-3", item.WorkItemId));
    }

    [Fact]
    public async Task DrainAsync_RedriveSafe_CrashBetweenFallbackWrites_LeavesSourceItemQueuedForRedelivery_AndConvergesOnRedrive()
    {
        // #412 item 3 — "Window C" closure at item granularity, exercised through the REAL
        // WorkflowScheduleActivitySchedulerWorkHandler in fallback mode (null committer). Its fallback does two
        // independent writes: SaveAsync(state = Scheduled), then enqueue the follow-up StartActivity item. We inject a
        // *process crash* (an OperationCanceledException the drainer re-throws rather than treating as a handler fault)
        // precisely between those two writes by making the follow-up enqueue throw.
        //
        // BEFORE this change the drainer destructively dequeued the source item up front, so a crash here stranded the
        // activity: source item gone, follow-up never enqueued, activity stuck at Scheduled, execution absent from
        // ListPendingWorkflowExecutionIdsAsync — nothing to re-drive. AFTER: the source item is not ack-deleted until
        // the effect is durable, so the crash leaves it queued; the execution is discoverable and a redrive with an
        // honest queue converges (activity Running, follow-up enqueued exactly once, source item consumed).
        var executableStore = new InMemoryWorkflowExecutableStore();
        await executableStore.SaveAsync(NewExecutable(["node-start", "node-next"]));
        var activityStateStore = new InMemoryActivityExecutionStateStore();
        var innerQueue = new InMemoryWorkflowSchedulerWorkQueue();

        // Phase 1 — crash. The decorator lets peek/dequeue/source-enqueue through but throws OperationCanceledException
        // on the follow-up StartActivity enqueue (WorkItemId ends ":start:...").
        var crashingQueue = new CrashOnFollowUpEnqueueWorkQueue(innerQueue, followUpMarker: ":start:");
        var crashingHandler = new WorkflowScheduleActivitySchedulerWorkHandler(
            executableStore,
            activityStateStore,
            crashingQueue,
            checkpointCommitter: null,
            inspectionAccumulator: null,
            new FixedTimeProvider(_now));
        var crashingDrainer = TestSchedulerDrainer.Create(crashingQueue, [crashingHandler, new NoopWorkflowSchedulerWorkHandler()], new FixedTimeProvider(_now));

        var scheduleItem = NewScheduleActivityWorkItem(1);
        await innerQueue.EnqueueAsync(scheduleItem);

        // The crash propagates out of DrainAsync (it is not swallowed as a handler fault).
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => crashingDrainer.DrainAsync(new RuntimeSchedulerDrainRequest("wfexec-1")).AsTask());

        // First fallback write landed: the activity exists at Scheduled.
        var afterCrash = await activityStateStore.FindAsync("wfexec-1", "actexec-1");
        Assert.NotNull(afterCrash);
        Assert.Equal(ActivityExecutionStatus.Scheduled, afterCrash!.Status);

        // The source ScheduleActivity item is STILL durably queued (this is the bite — RED before the reorder, when the
        // up-front dequeue had already deleted it) and the execution is discoverable for redrive.
        var pendingExecutions = await innerQueue.ListPendingWorkflowExecutionIdsAsync(10);
        Assert.Contains("wfexec-1", pendingExecutions);
        var queuedAfterCrash = await innerQueue.ListAllAsync(new RuntimeSchedulerWorkQuery("wfexec-1"));
        Assert.Collection(queuedAfterCrash, item => Assert.Equal(scheduleItem.WorkItemId, item.WorkItemId));

        // Phase 2 — redrive with an honest queue over the SAME durable stores. The handler re-runs idempotently.
        var honestHandler = new WorkflowScheduleActivitySchedulerWorkHandler(
            executableStore,
            activityStateStore,
            innerQueue,
            checkpointCommitter: null,
            inspectionAccumulator: null,
            new FixedTimeProvider(_now));
        var honestDrainer = TestSchedulerDrainer.Create(innerQueue, [honestHandler, new NoopWorkflowSchedulerWorkHandler()], new FixedTimeProvider(_now));

        var redriveResult = await honestDrainer.DrainAsync(new RuntimeSchedulerDrainRequest("wfexec-1", maxWorkItems: 1));

        Assert.Equal(1, redriveResult.DrainedCount);
        Assert.False(redriveResult.StoppedOnFault);

        // Convergence: the activity remains a single Scheduled record (no duplicate), the source item was ack-consumed,
        // and the follow-up StartActivity item was enqueued exactly once with the deterministic id.
        var converged = await activityStateStore.FindAsync("wfexec-1", "actexec-1");
        Assert.NotNull(converged);
        Assert.Equal(ActivityExecutionStatus.Scheduled, converged!.Status);

        var queuedAfterRedrive = await innerQueue.ListAllAsync(new RuntimeSchedulerWorkQuery("wfexec-1"));
        var followUp = Assert.Single(queuedAfterRedrive);
        Assert.Equal(WorkflowExecutionCommandKind.StartActivity, followUp.CommandKind);
        Assert.Equal(RuntimeChainId.Derive(scheduleItem.WorkItemId, "start:actexec-1"), followUp.WorkItemId);
    }

    [Fact]
    public async Task DrainAsync_StopsBeforeDequeuingPauseBlockedWork()
    {
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var handler = new RecordingSchedulerWorkHandler();
        var pauseGate = new RecordingWorkflowSchedulerPauseGate(BlockedDecision(RuntimePauseBoundary.BeforeActivityExecutionStart));
        var drainer = TestSchedulerDrainer.Create(queue, [handler, new NoopWorkflowSchedulerWorkHandler()], new FixedTimeProvider(_now), pauseGate);
        await queue.EnqueueAsync(NewStartActivityWorkItem(1));

        var result = await drainer.DrainAsync(new RuntimeSchedulerDrainRequest("wfexec-1"));
        var remaining = await queue.ListAllAsync(new RuntimeSchedulerWorkQuery("wfexec-1"));

        Assert.Equal(0, result.DrainedCount);
        Assert.False(result.StoppedOnFault);
        Assert.True(result.StoppedOnPause);
        Assert.Empty(handler.WorkItemIds);
        Assert.Collection(pauseGate.WorkItemIds, id => Assert.Equal("work-1", id));
        Assert.Collection(remaining, item => Assert.Equal("work-1", item.WorkItemId));
        var itemResult = Assert.Single(result.Items);
        Assert.Equal(RuntimeSchedulerWorkItemResultStatus.Paused, itemResult.Status);
        Assert.Equal(nameof(WorkflowSchedulerPauseGate), itemResult.HandlerName);
        Assert.Contains("pause-1", itemResult.Error);
        Assert.Contains(nameof(RuntimePauseBoundary.BeforeActivityExecutionStart), itemResult.Error);
    }

    [Fact]
    public async Task DrainAsync_StopsBeforeDequeuingWorkOnceWorkflowReachesTerminalStatus()
    {
        // A parallel fork enqueues several sibling InvokeActivity work items; one of them runs Finish, which
        // commits a terminal WorkflowCompleted status. The remaining queued siblings must not drain afterwards
        // and write post-completion state. The drainer reads workflow-execution status before each dequeue. (#293)
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var handler = new RecordingSchedulerWorkHandler();
        var workflowStateStore = new InMemoryWorkflowExecutionStateStore();
        await workflowStateStore.SaveAsync(NewWorkflowState(WorkflowExecutionStatus.Completed));
        var drainer = TestSchedulerDrainer.Create(
            queue,
            [handler, new NoopWorkflowSchedulerWorkHandler()],
            new FixedTimeProvider(_now),
            pauseGate: null,
            workflowStateStore);
        await queue.EnqueueAsync(NewInvokeActivityWorkItem(1));
        await queue.EnqueueAsync(NewInvokeActivityWorkItem(2));
        await queue.EnqueueAsync(NewInvokeActivityWorkItem(3));

        var result = await drainer.DrainAsync(new RuntimeSchedulerDrainRequest("wfexec-1"));
        var remaining = await queue.ListAllAsync(new RuntimeSchedulerWorkQuery("wfexec-1"));

        Assert.Equal(0, result.DrainedCount);
        Assert.False(result.StoppedOnFault);
        Assert.False(result.StoppedOnPause);
        Assert.True(result.StoppedOnTerminalStatus);
        Assert.Equal(RuntimeSchedulerDrainStopReason.WorkflowTerminated, result.StopReason);
        Assert.Empty(result.Items);
        Assert.Empty(handler.WorkItemIds);
        Assert.Collection(
            remaining,
            item => Assert.Equal("work-1", item.WorkItemId),
            item => Assert.Equal("work-2", item.WorkItemId),
            item => Assert.Equal("work-3", item.WorkItemId));
    }

    [Fact]
    public async Task DrainAsync_StopsMidDrainWhenWorkflowReachesTerminalStatus()
    {
        // The first dispatched item terminates the workflow (e.g. Finish marks it Completed). The sibling work
        // queued before the terminal commit must be left untouched on the next loop iteration. (#293)
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var workflowStateStore = new InMemoryWorkflowExecutionStateStore();
        await workflowStateStore.SaveAsync(NewWorkflowState(WorkflowExecutionStatus.Running));
        var handler = new TerminatingSchedulerWorkHandler(workflowStateStore, terminateOnWorkItemId: "work-1", _now);
        var drainer = TestSchedulerDrainer.Create(
            queue,
            [handler, new NoopWorkflowSchedulerWorkHandler()],
            new FixedTimeProvider(_now),
            pauseGate: null,
            workflowStateStore);
        await queue.EnqueueAsync(NewInvokeActivityWorkItem(1));
        await queue.EnqueueAsync(NewInvokeActivityWorkItem(2));
        await queue.EnqueueAsync(NewInvokeActivityWorkItem(3));

        var result = await drainer.DrainAsync(new RuntimeSchedulerDrainRequest("wfexec-1"));
        var remaining = await queue.ListAllAsync(new RuntimeSchedulerWorkQuery("wfexec-1"));

        Assert.Equal(1, result.DrainedCount);
        Assert.True(result.StoppedOnTerminalStatus);
        Assert.Equal(RuntimeSchedulerDrainStopReason.WorkflowTerminated, result.StopReason);
        Assert.Equal(["work-1"], handler.WorkItemIds);
        Assert.Collection(
            remaining,
            item => Assert.Equal("work-2", item.WorkItemId),
            item => Assert.Equal("work-3", item.WorkItemId));
    }

    [Fact]
    public async Task DrainAsync_DispatchesQueuedWorkAfterPauseHoldIsReleased()
    {
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var store = new InMemoryWorkflowHoldStateStore();
        var handler = new RecordingSchedulerWorkHandler();
        var pauseGate = new WorkflowSchedulerPauseGate(new RuntimePauseDecisionProvider(store), new FixedTimeProvider(_now));
        var drainer = TestSchedulerDrainer.Create(queue, [handler, new NoopWorkflowSchedulerWorkHandler()], new FixedTimeProvider(_now), pauseGate);
        await store.SaveAsync(new WorkflowHoldState(
            controlPlaneStateId: "control-1",
            workflowExecutionId: "wfexec-1",
            activeHolds: [WorkflowHold.ForWorkflowExecution("pause-1", "wfexec-1", _now, "operator", "Paused for maintenance.")]));
        await queue.EnqueueAsync(NewStartActivityWorkItem(1));

        var pausedResult = await drainer.DrainAsync(new RuntimeSchedulerDrainRequest("wfexec-1"));
        await store.SaveAsync(new WorkflowHoldState(
            controlPlaneStateId: "control-1",
            workflowExecutionId: "wfexec-1",
            releasedHolds:
            [
                new WorkflowHold(
                    holdId: "pause-1",
                    scope: WorkflowHoldScope.WorkflowExecution,
                    status: WorkflowHoldStatus.Released,
                    requestedAt: _now,
                    requestedBy: "operator",
                    reason: "Paused for maintenance.",
                    workflowExecutionId: "wfexec-1",
                    releasedAt: _now.AddMinutes(1),
                    releasedBy: "operator")
            ]));
        var resumedResult = await drainer.DrainAsync(new RuntimeSchedulerDrainRequest("wfexec-1"));
        var remaining = await queue.ListAllAsync(new RuntimeSchedulerWorkQuery("wfexec-1"));

        Assert.True(pausedResult.StoppedOnPause);
        Assert.False(resumedResult.StoppedOnPause);
        Assert.Equal(1, resumedResult.DrainedCount);
        Assert.Collection(handler.WorkItemIds, id => Assert.Equal("work-1", id));
        Assert.Empty(remaining);
        Assert.Equal(RuntimeSchedulerWorkItemResultStatus.Completed, Assert.Single(resumedResult.Items).Status);
    }

    [Fact]
    public async Task DrainAsync_EvaluatesGeneratedEventBoundaryBeforeDequeue()
    {
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var pauseGate = new RecordingWorkflowSchedulerPauseGate(BlockedDecision(RuntimePauseBoundary.BeforeGeneratorEmission));
        var drainer = TestSchedulerDrainer.Create(
            queue,
            [new MissingGeneratedEventSchedulerWorkHandler(), new NoopWorkflowSchedulerWorkHandler()],
            new FixedTimeProvider(_now),
            pauseGate);
        await queue.EnqueueAsync(NewGeneratedEventWorkItem(1));

        var result = await drainer.DrainAsync(new RuntimeSchedulerDrainRequest("wfexec-1"));
        var remaining = await queue.ListAllAsync(new RuntimeSchedulerWorkQuery("wfexec-1"));

        Assert.True(result.StoppedOnPause);
        Assert.Equal(0, result.DrainedCount);
        Assert.Collection(remaining, item => Assert.Equal("work-1", item.WorkItemId));
        Assert.Collection(pauseGate.WorkItemIds, id => Assert.Equal("work-1", id));
        var itemResult = Assert.Single(result.Items);
        Assert.Equal(RuntimeSchedulerWorkItemResultStatus.Paused, itemResult.Status);
        Assert.Equal(WorkflowExecutionCommandKind.GeneratedEvent, itemResult.CommandKind);
    }

    [Fact]
    public async Task WorkflowSchedulerPauseGate_MapsSchedulerWorkToPauseDecisionRequests()
    {
        var provider = new RecordingRuntimePauseDecisionProvider();
        var pauseGate = new WorkflowSchedulerPauseGate(provider, new FixedTimeProvider(_now));

        await pauseGate.EvaluateAsync(NewStartActivityWorkItem(1));
        await pauseGate.EvaluateAsync(NewInvokeActivityWorkItem(2));
        await pauseGate.EvaluateAsync(NewGeneratedEventWorkItem(3));
        var ignoredDecision = await pauseGate.EvaluateAsync(NewWorkItem(4));

        Assert.Null(ignoredDecision);
        Assert.Collection(
            provider.Requests,
            startRequest =>
            {
                Assert.Equal(RuntimePauseBoundary.BeforeActivityExecutionStart, startRequest.Boundary);
                Assert.Equal(_now, startRequest.EvaluatedAt);
                Assert.Equal("wfexec-1", startRequest.WorkflowExecutionId);
                Assert.Equal("actexec-1", startRequest.ActivityExecutionId);
                Assert.Null(startRequest.GeneratorId);
                Assert.Equal("work-1", startRequest.Metadata["runtime.schedulerWorkItemId"]);
                Assert.Equal(nameof(WorkflowExecutionCommandKind.StartActivity), startRequest.Metadata["runtime.schedulerCommandKind"]);
            },
            invokeRequest =>
            {
                Assert.Equal(RuntimePauseBoundary.BeforeActivityExecutionStart, invokeRequest.Boundary);
                Assert.Equal("wfexec-1", invokeRequest.WorkflowExecutionId);
                Assert.Equal("actexec-2", invokeRequest.ActivityExecutionId);
                Assert.Null(invokeRequest.GeneratorId);
                Assert.Equal("work-2", invokeRequest.Metadata["runtime.schedulerWorkItemId"]);
                Assert.Equal(nameof(WorkflowExecutionCommandKind.InvokeActivity), invokeRequest.Metadata["runtime.schedulerCommandKind"]);
            },
            generatedEventRequest =>
            {
                Assert.Equal(RuntimePauseBoundary.BeforeGeneratorEmission, generatedEventRequest.Boundary);
                Assert.Equal("wfexec-1", generatedEventRequest.WorkflowExecutionId);
                Assert.Equal("actexec-generator", generatedEventRequest.ActivityExecutionId);
                Assert.Equal("generator-3", generatedEventRequest.GeneratorId);
                Assert.Equal("work-3", generatedEventRequest.Metadata["runtime.schedulerWorkItemId"]);
                Assert.Equal(nameof(WorkflowExecutionCommandKind.GeneratedEvent), generatedEventRequest.Metadata["runtime.schedulerCommandKind"]);
            });
    }

    [Fact]
    public async Task WorkflowSchedulerPauseGate_ReadsCaseInsensitiveSchedulerPayloads()
    {
        var provider = new RecordingRuntimePauseDecisionProvider();
        var pauseGate = new WorkflowSchedulerPauseGate(provider, new FixedTimeProvider(_now));
        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        await pauseGate.EvaluateAsync(NewStartActivityWorkItem(7, jsonOptions));

        var request = Assert.Single(provider.Requests);
        Assert.Equal(RuntimePauseBoundary.BeforeActivityExecutionStart, request.Boundary);
        Assert.Equal("wfexec-1", request.WorkflowExecutionId);
        Assert.Equal("actexec-7", request.ActivityExecutionId);
    }

    [Fact]
    public async Task DrainAsync_UsesNoopFallbackWhenNoCustomHandlerMatches()
    {
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var customHandler = new RecordingSchedulerWorkHandler(canHandle: false);
        var drainer = TestSchedulerDrainer.Create(queue, [customHandler, new NoopWorkflowSchedulerWorkHandler()], new FixedTimeProvider(_now));
        await queue.EnqueueAsync(NewWorkItem(1));

        var result = await drainer.DrainAsync(new RuntimeSchedulerDrainRequest("wfexec-1"));

        var item = Assert.Single(result.Items);
        Assert.Equal(RuntimeSchedulerWorkItemResultStatus.Completed, item.Status);
        Assert.Equal(NoopWorkflowSchedulerWorkHandler.HandlerName, item.HandlerName);
        Assert.Empty(customHandler.WorkItemIds);
        Assert.Equal(1, customHandler.CanHandleCallCount);
    }

    [Fact]
    public async Task DrainAsync_DoesNotNoopInvokeActivityWorkWhenNoProviderMatches()
    {
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var drainer = TestSchedulerDrainer.Create(queue, [new NoopWorkflowSchedulerWorkHandler()], new FixedTimeProvider(_now));
        await queue.EnqueueAsync(NewWorkItem(1, commandKind: WorkflowExecutionCommandKind.InvokeActivity));

        var result = await drainer.DrainAsync(new RuntimeSchedulerDrainRequest("wfexec-1"));

        var item = Assert.Single(result.Items);
        Assert.Equal(RuntimeSchedulerWorkItemResultStatus.Faulted, item.Status);
        Assert.Equal("FaultingMissingSchedulerWorkHandler", item.HandlerName);
        Assert.Contains("No workflow scheduler work handler accepted command kind 'InvokeActivity'", item.Error);
    }

    [Fact]
    public async Task DrainAsync_DoesNotNoopGeneratedEventWorkWhenNoProviderMatches()
    {
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var drainer = TestSchedulerDrainer.Create(
            queue,
            [new MissingGeneratedEventSchedulerWorkHandler(), new NoopWorkflowSchedulerWorkHandler()],
            new FixedTimeProvider(_now));
        await queue.EnqueueAsync(NewWorkItem(1, commandKind: WorkflowExecutionCommandKind.GeneratedEvent));

        var result = await drainer.DrainAsync(new RuntimeSchedulerDrainRequest("wfexec-1"));

        var item = Assert.Single(result.Items);
        Assert.Equal(RuntimeSchedulerWorkItemResultStatus.Faulted, item.Status);
        Assert.Equal(MissingGeneratedEventSchedulerWorkHandler.HandlerName, item.HandlerName);
        Assert.Contains("no generated-event provider", item.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DrainAsync_DoesNotNoopBookmarkResumeWorkWhenNoProviderMatches()
    {
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var drainer = TestSchedulerDrainer.Create(
            queue,
            [new MissingBookmarkResumeSchedulerWorkHandler(), new NoopWorkflowSchedulerWorkHandler()],
            new FixedTimeProvider(_now));
        await queue.EnqueueAsync(NewWorkItem(1, commandKind: WorkflowExecutionCommandKind.ResumeBookmark));

        var result = await drainer.DrainAsync(new RuntimeSchedulerDrainRequest("wfexec-1"));

        var item = Assert.Single(result.Items);
        Assert.Equal(RuntimeSchedulerWorkItemResultStatus.Faulted, item.Status);
        Assert.Equal(MissingBookmarkResumeSchedulerWorkHandler.HandlerName, item.HandlerName);
        Assert.Contains("no bookmark resume provider", item.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DrainAsync_DispatchesCompleteActivityWorkThroughNamedHandler()
    {
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var activityStateStore = new InMemoryActivityExecutionStateStore();
        var drainer = TestSchedulerDrainer.Create(
            queue,
            [new WorkflowCompleteActivitySchedulerWorkHandler(activityStateStore, queue, new FixedTimeProvider(_now)), new NoopWorkflowSchedulerWorkHandler()],
            new FixedTimeProvider(_now));
        await queue.EnqueueAsync(NewWorkItem(
            1,
            commandKind: WorkflowExecutionCommandKind.CompleteActivity,
            payload: JsonSerializer.SerializeToElement(NewCompleteActivityPayload())));

        var result = await drainer.DrainAsync(new RuntimeSchedulerDrainRequest("wfexec-1", maxWorkItems: 1));

        var item = Assert.Single(result.Items);
        Assert.Equal(RuntimeSchedulerWorkItemResultStatus.Completed, item.Status);
        Assert.Equal(WorkflowCompleteActivitySchedulerWorkHandler.HandlerName, item.HandlerName);
    }

    [Fact]
    public async Task DrainAsync_DispatchesCheckpointWorkThroughNamedHandler()
    {
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var activityStateStore = new InMemoryActivityExecutionStateStore();
        var checkpointWriter = new InMemoryRuntimeCheckpointCommitStore();
        await activityStateStore.SaveAsync(NewActivityState("actexec-1", ActivityExecutionStatus.Completed));
        var drainer = TestSchedulerDrainer.Create(
            queue,
            [NewCheckpointHandler(activityStateStore, checkpointWriter), new NoopWorkflowSchedulerWorkHandler()],
            new FixedTimeProvider(_now));
        await queue.EnqueueAsync(NewWorkItem(
            1,
            commandKind: WorkflowExecutionCommandKind.Checkpoint,
            payload: JsonSerializer.SerializeToElement(NewCheckpointPayload())));

        var result = await drainer.DrainAsync(new RuntimeSchedulerDrainRequest("wfexec-1"));

        var item = Assert.Single(result.Items);
        Assert.Equal(RuntimeSchedulerWorkItemResultStatus.Completed, item.Status);
        Assert.Equal(WorkflowCheckpointSchedulerWorkHandler.HandlerName, item.HandlerName);
        var write = Assert.Single(checkpointWriter.ListCommits());
        Assert.Equal(RuntimeCheckpointPersistenceMode.Immediate, write.Decision.Mode);
        Assert.Equal("commit:work-1", write.Commit.CommitId);
        Assert.Equal("checkpoint:work-1", write.Commit.Checkpoint.CheckpointId);
        Assert.Equal(RuntimeCheckpointNames.ActivityCompleted, write.Commit.Checkpoint.Name);
        Assert.Equal(_now, write.Commit.Checkpoint.OccurredAt);
        Assert.Equal(["actexec-1"], write.Commit.Checkpoint.ActivityExecutionIds);
        var activityChange = Assert.Single(write.Commit.StateChanges.ActivityExecutions);
        Assert.Equal("actexec-1", activityChange.StateId);
        Assert.Equal(RuntimeStateChangeOperation.Upsert, activityChange.Operation);
        Assert.Equal(ActivityExecutionStatus.Completed, activityChange.State.Status);
        Assert.Null(write.Commit.StateChanges.WorkflowExecution);
        Assert.Null(write.Commit.StateChanges.Scheduler);
        Assert.Empty(write.Commit.StateChanges.Bookmarks);
        Assert.Empty(write.Commit.StateChanges.DurableValues);
        Assert.Empty(write.Commit.StateChanges.Incidents);
        Assert.Empty(write.Commit.StateChanges.Operational);
    }

    [Fact]
    public async Task CheckpointHandler_StagesCommitForTheCheckpointSlot_WhenDispatchedThroughThePipeline()
    {
        // RT-6 (Move 2): the migrated Checkpoint handler's context-aware overload stages its commit on the workspace
        // for the Checkpoint slot instead of committing inline — mirroring the Cancel handler.
        var activityStateStore = new InMemoryActivityExecutionStateStore();
        var checkpointWriter = new InMemoryRuntimeCheckpointCommitStore();
        await activityStateStore.SaveAsync(NewActivityState("actexec-1", ActivityExecutionStatus.Completed));
        var handler = NewCheckpointHandler(activityStateStore, checkpointWriter);
        var workItem = NewWorkItem(1, commandKind: WorkflowExecutionCommandKind.Checkpoint, payload: JsonSerializer.SerializeToElement(NewCheckpointPayload()));
        var context = new WorkflowRuntimePipelineContext(workItem);

        await handler.HandleAsync(workItem, context);

        Assert.Empty(checkpointWriter.ListCommits());
        var staged = context.Workspace.PendingCheckpointCommit;
        Assert.NotNull(staged);
        Assert.Equal("commit:work-1", staged.CommitId);
        Assert.Equal(RuntimeCheckpointNames.ActivityCompleted, staged.Checkpoint.Name);
    }

    [Fact]
    public async Task CheckpointHandler_CommitsInline_OnDirectDispatch()
    {
        // Behaviour-preserving fallback: dispatched without a pipeline it commits inline, as before Move 2.
        var activityStateStore = new InMemoryActivityExecutionStateStore();
        var checkpointWriter = new InMemoryRuntimeCheckpointCommitStore();
        await activityStateStore.SaveAsync(NewActivityState("actexec-1", ActivityExecutionStatus.Completed));
        var handler = NewCheckpointHandler(activityStateStore, checkpointWriter);
        var workItem = NewWorkItem(1, commandKind: WorkflowExecutionCommandKind.Checkpoint, payload: JsonSerializer.SerializeToElement(NewCheckpointPayload()));

        await handler.HandleAsync(workItem);

        Assert.Single(checkpointWriter.ListCommits());
    }

    [Fact]
    public async Task DrainAsync_FaultsMalformedCheckpointWorkThroughNamedHandler()
    {
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var drainer = TestSchedulerDrainer.Create(
            queue,
            [NewCheckpointHandler(new InMemoryActivityExecutionStateStore(), new InMemoryRuntimeCheckpointCommitStore()), new NoopWorkflowSchedulerWorkHandler()],
            new FixedTimeProvider(_now));
        using var document = JsonDocument.Parse("""{"checkpointName":" "}""");
        await queue.EnqueueAsync(NewWorkItem(
            1,
            commandKind: WorkflowExecutionCommandKind.Checkpoint,
            payload: document.RootElement.Clone()));

        var result = await drainer.DrainAsync(new RuntimeSchedulerDrainRequest("wfexec-1"));

        var item = Assert.Single(result.Items);
        Assert.Equal(RuntimeSchedulerWorkItemResultStatus.Faulted, item.Status);
        Assert.Equal(WorkflowCheckpointSchedulerWorkHandler.HandlerName, item.HandlerName);
        Assert.Contains("not a valid checkpoint payload", item.Error);
    }

    [Fact]
    public async Task DrainAsync_FaultsCheckpointWorkWhenReferencedActivityStateIsMissing()
    {
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var checkpointWriter = new InMemoryRuntimeCheckpointCommitStore();
        var drainer = TestSchedulerDrainer.Create(
            queue,
            [NewCheckpointHandler(new InMemoryActivityExecutionStateStore(), checkpointWriter), new NoopWorkflowSchedulerWorkHandler()],
            new FixedTimeProvider(_now));
        await queue.EnqueueAsync(NewWorkItem(
            1,
            commandKind: WorkflowExecutionCommandKind.Checkpoint,
            payload: JsonSerializer.SerializeToElement(NewCheckpointPayload())));

        var result = await drainer.DrainAsync(new RuntimeSchedulerDrainRequest("wfexec-1"));

        var item = Assert.Single(result.Items);
        Assert.Equal(RuntimeSchedulerWorkItemResultStatus.Faulted, item.Status);
        Assert.Equal(WorkflowCheckpointSchedulerWorkHandler.HandlerName, item.HandlerName);
        Assert.Contains("missing activity execution 'actexec-1'", item.Error);
        Assert.Empty(checkpointWriter.ListCommits());
    }

    [Fact]
    public async Task CompleteActivityHandler_EnqueuesParentCompletionEvaluationWorkForCompletedChildWithParent()
    {
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var activityStateStore = new InMemoryActivityExecutionStateStore();
        await activityStateStore.SaveAsync(NewParentActivityState());
        var handler = new WorkflowCompleteActivitySchedulerWorkHandler(activityStateStore, queue, new FixedTimeProvider(_now));

        await handler.HandleAsync(NewWorkItem(
            1,
            commandKind: WorkflowExecutionCommandKind.CompleteActivity,
            payload: JsonSerializer.SerializeToElement(NewCompleteActivityPayload(parentActivityExecutionId: "actexec-parent"))));

        var parentWork = Assert.Single(await queue.ListAllAsync(new RuntimeSchedulerWorkQuery("wfexec-1")));
        Assert.Equal(WorkflowExecutionCommandKind.CompleteActivity, parentWork.CommandKind);
        Assert.Equal(RuntimeChainId.Derive("work-1", "parent:actexec-parent:child:actexec-1"), parentWork.WorkItemId);
        Assert.Equal(2, parentWork.Sequence);
        var parentPayload = parentWork.Payload!.Value.Deserialize<RuntimeCompleteActivityCommandPayload>()!;
        Assert.Equal(SchedulerCompletionKind.ParentCompletionEvaluation, parentPayload.CompletionKind);
        Assert.Equal("actexec-parent", parentPayload.ActivityExecutionId);
        Assert.Equal("actexec-1", parentPayload.CompletedChildActivityExecutionId);
        Assert.Equal("node-parent", parentPayload.ExecutableNodeId);
        Assert.Equal("branch-a", parentPayload.BranchId);
        Assert.Equal(["Done"], parentPayload.OutcomeNames);
    }

    [Fact]
    public async Task CompleteActivityHandler_EnqueuesContinuationSchedulingForRootCompletion()
    {
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var handler = new WorkflowCompleteActivitySchedulerWorkHandler(
            new InMemoryActivityExecutionStateStore(),
            queue,
            new FixedTimeProvider(_now));

        await handler.HandleAsync(NewWorkItem(
            1,
            commandKind: WorkflowExecutionCommandKind.CompleteActivity,
            payload: JsonSerializer.SerializeToElement(NewCompleteActivityPayload(parentActivityExecutionId: null))));

        var continuationWork = Assert.Single(await queue.ListAllAsync(new RuntimeSchedulerWorkQuery("wfexec-1")));
        Assert.Equal(WorkflowExecutionCommandKind.CompleteActivity, continuationWork.CommandKind);
        Assert.Equal(RuntimeChainId.Derive("work-1", "continuation:actexec-1"), continuationWork.WorkItemId);
        Assert.Equal(2, continuationWork.Sequence);
        var continuationPayload = continuationWork.Payload!.Value.Deserialize<RuntimeCompleteActivityCommandPayload>()!;
        Assert.Equal(SchedulerCompletionKind.ContinuationScheduling, continuationPayload.CompletionKind);
        Assert.Equal(RuntimeCompleteActivityCommandPayload.ContinuationSchedulingReason, continuationPayload.Reason);
        Assert.Equal("actexec-1", continuationPayload.ActivityExecutionId);
        Assert.Null(continuationPayload.ParentActivityExecutionId);
        Assert.Equal(["Done"], continuationPayload.OutcomeNames);
        Assert.Null(continuationPayload.CompletedChildActivityExecutionId);
    }

    [Fact]
    public async Task CompleteActivityHandler_EnqueuesContinuationSchedulingWorkForParentCompletionEvaluation()
    {
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var handler = new WorkflowCompleteActivitySchedulerWorkHandler(
            new InMemoryActivityExecutionStateStore(),
            queue,
            new FixedTimeProvider(_now));

        await handler.HandleAsync(NewWorkItem(
            1,
            commandKind: WorkflowExecutionCommandKind.CompleteActivity,
            payload: JsonSerializer.SerializeToElement(NewCompleteActivityPayload(
                activityExecutionId: "actexec-parent",
                parentActivityExecutionId: "actexec-grandparent",
                branchId: "branch-parent",
                outcomeNames: ["ParentDone"],
                completionKind: SchedulerCompletionKind.ParentCompletionEvaluation,
                completedChildActivityExecutionId: "actexec-1"))));

        var continuationWork = Assert.Single(await queue.ListAllAsync(new RuntimeSchedulerWorkQuery("wfexec-1")));
        Assert.Equal(WorkflowExecutionCommandKind.CompleteActivity, continuationWork.CommandKind);
        Assert.Equal(RuntimeChainId.Derive("work-1", "continuation:actexec-parent"), continuationWork.WorkItemId);
        Assert.Equal(2, continuationWork.Sequence);
        var continuationPayload = continuationWork.Payload!.Value.Deserialize<RuntimeCompleteActivityCommandPayload>()!;
        Assert.Equal(SchedulerCompletionKind.ContinuationScheduling, continuationPayload.CompletionKind);
        Assert.Equal(RuntimeCompleteActivityCommandPayload.ContinuationSchedulingReason, continuationPayload.Reason);
        Assert.Equal("actexec-parent", continuationPayload.ActivityExecutionId);
        Assert.Equal("actexec-grandparent", continuationPayload.ParentActivityExecutionId);
        Assert.Equal("branch-parent", continuationPayload.BranchId);
        Assert.Equal(["ParentDone"], continuationPayload.OutcomeNames);
        Assert.Null(continuationPayload.CompletedChildActivityExecutionId);
    }

    [Fact]
    public async Task DrainAsync_LeavesParentEvaluationToFallbackWhenActivityRuntimeHandlerIsAbsent()
    {
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var activityStateStore = new InMemoryActivityExecutionStateStore();
        var checkpointWriter = new InMemoryRuntimeCheckpointCommitStore();
        var executableStore = new InMemoryWorkflowExecutableStore();
        await executableStore.SaveAsync(NewExecutable(["node-start", "node-next"]));
        await activityStateStore.SaveAsync(NewActivityState("actexec-parent", ActivityExecutionStatus.Completed));
        var handler = new WorkflowCompleteActivitySchedulerWorkHandler(
            activityStateStore,
            queue,
            executableStore,
            new FixedTimeProvider(_now));
        var drainer = TestSchedulerDrainer.Create(queue, [handler, NewCheckpointHandler(activityStateStore, checkpointWriter), new NoopWorkflowSchedulerWorkHandler()], new FixedTimeProvider(_now));
        await queue.EnqueueAsync(NewWorkItem(
            1,
            commandKind: WorkflowExecutionCommandKind.CompleteActivity,
            payload: JsonSerializer.SerializeToElement(NewCompleteActivityPayload(
                activityExecutionId: "actexec-parent",
                parentActivityExecutionId: "actexec-grandparent",
                branchId: "branch-parent",
                outcomeNames: ["ParentDone"],
                completionKind: SchedulerCompletionKind.ParentCompletionEvaluation,
                completedChildActivityExecutionId: "actexec-1"))));

        var result = await drainer.DrainAsync(new RuntimeSchedulerDrainRequest("wfexec-1"));
        var remaining = await queue.ListAllAsync(new RuntimeSchedulerWorkQuery("wfexec-1"));

        Assert.Equal(1, result.DrainedCount);
        Assert.Empty(remaining);
        var item = Assert.Single(result.Items);
        Assert.Equal("work-1", item.WorkItemId);
        Assert.Equal(NoopWorkflowSchedulerWorkHandler.HandlerName, item.HandlerName);
        Assert.Empty(checkpointWriter.ListCommits());
    }

    [Fact]
    public async Task CompleteActivityHandler_EnqueuesCheckpointWorkForContinuationScheduling()
    {
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var executableStore = new InMemoryWorkflowExecutableStore();
        await executableStore.SaveAsync(NewExecutable(["node-start", "node-next"]));
        var handler = new WorkflowCompleteActivitySchedulerWorkHandler(
            new InMemoryActivityExecutionStateStore(),
            queue,
            executableStore,
            new FixedTimeProvider(_now));

        await handler.HandleAsync(NewWorkItem(
            1,
            commandKind: WorkflowExecutionCommandKind.CompleteActivity,
            payload: JsonSerializer.SerializeToElement(NewCompleteActivityPayload(
                activityExecutionId: "actexec-parent",
                parentActivityExecutionId: "actexec-grandparent",
                branchId: "branch-parent",
                outcomeNames: ["ParentDone"],
                completionKind: SchedulerCompletionKind.ContinuationScheduling))));

        var checkpointWork = Assert.Single(await queue.ListAllAsync(new RuntimeSchedulerWorkQuery("wfexec-1")));
        Assert.Equal(WorkflowExecutionCommandKind.Checkpoint, checkpointWork.CommandKind);
        Assert.Equal(RuntimeChainId.Derive("work-1", "checkpoint:WorkflowCompleted:actexec-parent"), checkpointWork.WorkItemId);
        Assert.Equal(2, checkpointWork.Sequence);
        var checkpointPayload = checkpointWork.Payload!.Value.Deserialize<RuntimeCheckpointCommandPayload>()!;
        Assert.Equal(RuntimeCheckpointNames.WorkflowCompleted, checkpointPayload.CheckpointName);
        Assert.Equal(RuntimeCheckpointCommandPayload.ActivityCompletionPropagationReason, checkpointPayload.Reason);
        Assert.Equal(["actexec-parent"], checkpointPayload.ActivityExecutionIds);
    }

    [Fact]
    public async Task CompleteActivityHandler_FaultsWhenParentActivityStateIsMissing()
    {
        var handler = new WorkflowCompleteActivitySchedulerWorkHandler(
            new InMemoryActivityExecutionStateStore(),
            new InMemoryWorkflowSchedulerWorkQueue(),
            new FixedTimeProvider(_now));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => handler.HandleAsync(NewWorkItem(
            1,
            commandKind: WorkflowExecutionCommandKind.CompleteActivity,
            payload: JsonSerializer.SerializeToElement(NewCompleteActivityPayload(parentActivityExecutionId: "actexec-missing")))).AsTask());

        Assert.Contains("missing parent activity execution 'actexec-missing'", exception.Message);
    }

    [Fact]
    public async Task DrainAsync_UsesMarkedFallbackAfterCustomHandlers()
    {
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var customHandler = new RecordingSchedulerWorkHandler(canHandle: false);
        var fallbackHandler = new RecordingFallbackSchedulerWorkHandler();
        var drainer = TestSchedulerDrainer.Create(queue, [customHandler, fallbackHandler], new FixedTimeProvider(_now));
        await queue.EnqueueAsync(NewWorkItem(1));

        var result = await drainer.DrainAsync(new RuntimeSchedulerDrainRequest("wfexec-1"));

        var item = Assert.Single(result.Items);
        Assert.Equal(RuntimeSchedulerWorkItemResultStatus.Completed, item.Status);
        Assert.Equal(fallbackHandler.Name, item.HandlerName);
        Assert.Equal(1, customHandler.CanHandleCallCount);
        Assert.Equal(["work-1"], fallbackHandler.WorkItemIds);
    }

    [Fact]
    public void RuntimeSchedulerDrainModels_RejectInvalidResults()
    {
        var result = new RuntimeSchedulerDrainResult(
            workflowExecutionId: "wfexec-1",
            startedAt: _now,
            completedAt: _now,
            items: [CompletedResult("wfexec-1")],
            outboxDeliveryResults:
            [
                new RuntimePostCommitOutboxProcessResult(
                [
                    new RuntimePostCommitOutboxProcessedItem(
                        OutboxItemId: "outbox-1",
                        IntentId: "intent-1",
                        RequestedDeliveryResultStatus: RuntimePostCommitOutboxStatus.Delivered,
                        FailureMessage: null)
                ])
            ]);

        Assert.Equal(RuntimeSchedulerDrainStopReason.Quiesced, result.StopReason);
        Assert.Equal(1, result.OutboxAttemptedCount);
        Assert.Equal(1, result.OutboxDeliveredCount);
        Assert.Equal(0, result.OutboxFailedCount);

        Assert.Throws<ArgumentException>(() => new RuntimeSchedulerDrainRequest(" "));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RuntimeSchedulerDrainRequest("wfexec-1", maxWorkItems: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new WorkflowDrainOrchestratorOptions(maxDrainCycles: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new WorkflowDrainOrchestratorOptions(outboxDeliveryBatchSize: 0));
        Assert.Throws<ArgumentNullException>(() => new RuntimeSchedulerWorkItemResult(
            workItemId: "work-1",
            workflowExecutionId: "wfexec-1",
            commandKind: WorkflowExecutionCommandKind.RunSchedulerWork,
            status: RuntimeSchedulerWorkItemResultStatus.Faulted,
            handlerName: "handler",
            startedAt: _now,
            completedAt: _now));
        Assert.Throws<ArgumentException>(() => new RuntimeSchedulerWorkItemResult(
            workItemId: "work-1",
            workflowExecutionId: "wfexec-1",
            commandKind: WorkflowExecutionCommandKind.RunSchedulerWork,
            status: RuntimeSchedulerWorkItemResultStatus.Completed,
            handlerName: "handler",
            startedAt: _now,
            completedAt: _now,
            error: "No error expected."));
        Assert.Throws<ArgumentNullException>(() => new RuntimeSchedulerWorkItemResult(
            workItemId: "work-1",
            workflowExecutionId: "wfexec-1",
            commandKind: WorkflowExecutionCommandKind.RunSchedulerWork,
            status: RuntimeSchedulerWorkItemResultStatus.Paused,
            handlerName: "handler",
            startedAt: _now,
            completedAt: _now));
        Assert.Throws<ArgumentException>(() => new RuntimeSchedulerDrainResult(
            workflowExecutionId: "wfexec-1",
            startedAt: _now,
            completedAt: _now,
            items: [CompletedResult("wfexec-2")]));
    }

    private RuntimeSchedulerWorkItem NewWorkItem(
        int index,
        string workflowExecutionId = "wfexec-1",
        WorkflowExecutionCommandKind commandKind = WorkflowExecutionCommandKind.RunSchedulerWork,
        JsonElement? payload = null,
        IReadOnlyDictionary<string, string>? commandMetadata = null)
    {
        using var document = JsonDocument.Parse($$"""{"workItemId":"work-{{index}}"}""");
        return new(
            workItemId: $"work-{index}",
            workflowExecutionId: workflowExecutionId,
            commandId: $"command-{index}",
            commandKind: commandKind,
            envelopeId: $"envelope-{index}",
            idempotencyKey: $"{workflowExecutionId}:command-{index}",
            enqueuedAt: _now,
            recordedAt: _now,
            sequence: index,
            payload: payload ?? document.RootElement.Clone(),
            commandMetadata: commandMetadata);
    }

    private RuntimeSchedulerWorkItem NewScheduleActivityWorkItem(int index) =>
        NewWorkItem(
            index,
            commandKind: WorkflowExecutionCommandKind.ScheduleActivity,
            payload: JsonSerializer.SerializeToElement(new RuntimeScheduleActivityCommandPayload(
                NewPinnedExecutable(),
                "node-start",
                $"actexec-{index}",
                RuntimeScheduleActivityCommandPayload.WorkflowStartReason)));

    private RuntimeSchedulerWorkItem NewStartActivityWorkItem(int index, JsonSerializerOptions? jsonSerializerOptions = null) =>
        NewWorkItem(
            index,
            commandKind: WorkflowExecutionCommandKind.StartActivity,
            payload: JsonSerializer.SerializeToElement(new RuntimeStartActivityCommandPayload(
                NewPinnedExecutable(),
                "node-start",
                $"actexec-{index}",
                RuntimeStartActivityCommandPayload.ScheduledActivityReason), jsonSerializerOptions));

    private RuntimeSchedulerWorkItem NewInvokeActivityWorkItem(int index) =>
        NewWorkItem(
            index,
            commandKind: WorkflowExecutionCommandKind.InvokeActivity,
            payload: JsonSerializer.SerializeToElement(new RuntimeInvokeActivityCommandPayload(
                NewPinnedExecutable(),
                "node-start",
                $"actexec-{index}",
                RuntimeInvokeActivityCommandPayload.StartedActivityReason)));

    private RuntimeSchedulerWorkItem NewGeneratedEventWorkItem(int index) =>
        NewWorkItem(
            index,
            commandKind: WorkflowExecutionCommandKind.GeneratedEvent,
            payload: JsonSerializer.SerializeToElement(new SchedulerGeneratedEventWorkItem(
                workItemId: $"generated-work-{index}",
                generatedEvent: new GeneratedEvent(
                    generatedEventId: $"event-{index}",
                    workflowExecutionId: "wfexec-1",
                    generatorActivityExecutionId: "actexec-generator",
                    branchId: "branch-1",
                    name: "Tick",
                    sequence: index,
                    occurredAt: _now,
                    durability: GeneratedEventDurability.PolicyControlled),
                enqueuedAt: _now,
                reason: "GeneratorEmitted")),
            commandMetadata: new Dictionary<string, string>
            {
                ["GeneratorId"] = $"generator-{index}"
            });

    private static RuntimeCompleteActivityCommandPayload NewCompleteActivityPayload(
        string activityExecutionId = "actexec-1",
        string? parentActivityExecutionId = null,
        string? branchId = null,
        IReadOnlyCollection<string>? outcomeNames = null,
        SchedulerCompletionKind completionKind = SchedulerCompletionKind.ActivityCompleted,
        string? completedChildActivityExecutionId = null) =>
        new(
            pinnedExecutable: new WorkflowExecutableIdentity("artifact-1", "definition-1", "version-1", "1.0.0", "sha256:test"),
            executableNodeId: "node-start",
            activityExecutionId: activityExecutionId,
            parentActivityExecutionId: parentActivityExecutionId,
            branchId: branchId,
            outcomeNames: outcomeNames ?? ["Done"],
            reason: CompletionReason(completionKind),
            completionKind: completionKind,
            completedChildActivityExecutionId: completedChildActivityExecutionId);

    private static WorkflowExecutableIdentity NewPinnedExecutable() =>
        new("artifact-1", "definition-1", "version-1", "1.0.0", "sha256:test");

    private WorkflowExecutionState NewWorkflowState(WorkflowExecutionStatus status) =>
        new(
            WorkflowExecutionId: "wfexec-1",
            PinnedExecutable: NewPinnedExecutable(),
            Status: status,
            SubStatus: null,
            CreatedAt: _now,
            StartedAt: _now,
            UpdatedAt: _now,
            CompletedAt: status.IsTerminal() ? _now : null,
            CorrelationId: null,
            ParentWorkflowExecutionId: null,
            TenantId: null,
            SystemMetadata: new Dictionary<string, string>());

    private static RuntimeCheckpointCommandPayload NewCheckpointPayload() =>
        new(
            pinnedExecutable: NewPinnedExecutable(),
            checkpointName: RuntimeCheckpointNames.ActivityCompleted,
            activityExecutionIds: ["actexec-1"],
            reason: RuntimeCheckpointCommandPayload.ActivityCompletionPropagationReason);

    private static WorkflowExecutable NewExecutable(IReadOnlyCollection<string> nodeIds) =>
        new(
            identity: new WorkflowExecutableIdentity("artifact-1", "definition-1", "version-1", "1.0.0", "sha256:test"),
            rootActivity: ToRootActivity(nodeIds.Select(NewNode).ToArray()),
            resumeTargets: new Dictionary<string, WorkflowExecutableResumeTarget>(),
            createdAt: DateTimeOffset.UtcNow,
            compatibilityMetadata: new Dictionary<string, string>(),
            incidentStrategy: IncidentStrategyBuiltIns.FaultReference);

    private static ExecutableNode ToRootActivity(IReadOnlyCollection<ExecutableNode> nodes)
    {
        var nodeSnapshot = nodes.ToArray();
        if (nodeSnapshot.Length == 1)
            return nodeSnapshot[0];

        return new ExecutableNode(
            executableNodeId: "$root",
            authoredActivityId: "$root",
            activityType: "test/root",
            activityTypeVersion: "1.0.0",
            descriptor: new RuntimeActivityDescriptor("test", RuntimeActivityDescriptor.InitialSchemaVersion, JsonSerializer.SerializeToElement(new { type = "root" })),
            inputBindings: new Dictionary<string, RuntimeInputBinding>(),
            metadata: new Dictionary<string, string>(),
            childSlots:
            [
                new ExecutableChildSlot("children", nodeSnapshot)
            ]);
    }

    private static ExecutableNode NewNode(string nodeId)
    {
        using var document = JsonDocument.Parse("""{"type":"test"}""");
        return new(
            executableNodeId: nodeId,
            authoredActivityId: $"authored-{nodeId}",
            activityType: "test/activity",
            activityTypeVersion: "1.0.0",
            descriptor: new RuntimeActivityDescriptor("test", RuntimeActivityDescriptor.InitialSchemaVersion, document.RootElement.Clone()),
            inputBindings: new Dictionary<string, RuntimeInputBinding>(),
            metadata: new Dictionary<string, string>());
    }

    private WorkflowCheckpointSchedulerWorkHandler NewCheckpointHandler(
        IActivityExecutionStateStore activityStateStore,
        IRuntimeCheckpointCommitStore checkpointWriter) =>
        new(
            activityStateStore,
            new RuntimeCheckpointCommitter(
                new ImmediateRuntimeCheckpointPersistencePolicy(),
                checkpointWriter),
            inspectionAccumulator: null,
            timeProvider: new FixedTimeProvider(_now));

    private static string CompletionReason(SchedulerCompletionKind completionKind) =>
        completionKind switch
        {
            SchedulerCompletionKind.ActivityCompleted => RuntimeCompleteActivityCommandPayload.ActivityInvocationCompletedReason,
            SchedulerCompletionKind.ParentCompletionEvaluation => RuntimeCompleteActivityCommandPayload.ParentCompletionEvaluationReason,
            SchedulerCompletionKind.ContinuationScheduling => RuntimeCompleteActivityCommandPayload.ContinuationSchedulingReason,
            _ => completionKind.ToString()
        };

    private ActivityExecutionState NewParentActivityState() =>
        NewActivityState("actexec-parent", ActivityExecutionStatus.Running);

    private ActivityExecutionState NewActivityState(
        string activityExecutionId,
        ActivityExecutionStatus status) =>
        new(
            Execution: new ActivityExecution(
                ActivityExecutionId: activityExecutionId,
                WorkflowExecutionId: "wfexec-1",
                ExecutableNodeId: "node-parent",
                AuthoredActivityId: "authored-node-parent",
                ActivityType: "test/parent",
                ActivityTypeVersion: "1.0.0"),
            Status: status,
            SubStatus: null,
            ScheduledAt: _now.AddMinutes(-3),
            StartedAt: _now.AddMinutes(-2),
            CompletedAt: status == ActivityExecutionStatus.Completed ? _now : null,
            SchedulingActivityExecutionId: null,
            ParentActivityExecutionId: null,
            BranchId: "branch-a",
            IterationId: null,
            CallStackDepth: 0,
            BookmarkIds: [],
            IncidentIds: [],
            FaultCount: 0,
            AggregateFaultCount: 0,
            Metadata: new Dictionary<string, string>());

    private RuntimeSchedulerWorkItemResult CompletedResult(string workflowExecutionId) =>
        new(
            workItemId: "work-1",
            workflowExecutionId: workflowExecutionId,
            commandKind: WorkflowExecutionCommandKind.RunSchedulerWork,
            status: RuntimeSchedulerWorkItemResultStatus.Completed,
            handlerName: "handler",
            startedAt: _now,
            completedAt: _now);

    private static SchedulerPauseDecision BlockedDecision(RuntimePauseBoundary boundary) =>
        new(
            canAdvance: false,
            boundary: boundary,
            continuationPolicy: RuntimePauseContinuationPolicy.StrictPause,
            holdId: "pause-1",
            reason: "Paused by test.");

    private sealed class RecordingSchedulerWorkHandler(string? faultOnWorkItemId = null, bool canHandle = true) : IWorkflowSchedulerWorkHandler
    {
        public string Name => nameof(RecordingSchedulerWorkHandler);
        public List<string> WorkItemIds { get; } = [];
        public int CanHandleCallCount { get; private set; }

        public bool CanHandle(RuntimeSchedulerWorkItem workItem)
        {
            ArgumentNullException.ThrowIfNull(workItem);

            CanHandleCallCount++;
            return canHandle;
        }

        public ValueTask HandleAsync(RuntimeSchedulerWorkItem workItem, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (workItem.WorkItemId == faultOnWorkItemId)
                throw new InvalidOperationException($"Fault requested for {workItem.WorkItemId}.");

            WorkItemIds.Add(workItem.WorkItemId);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class TerminatingSchedulerWorkHandler(
        IWorkflowExecutionStateStore workflowStateStore,
        string terminateOnWorkItemId,
        DateTimeOffset now) : IWorkflowSchedulerWorkHandler
    {
        public string Name => nameof(TerminatingSchedulerWorkHandler);
        public List<string> WorkItemIds { get; } = [];

        public bool CanHandle(RuntimeSchedulerWorkItem workItem)
        {
            ArgumentNullException.ThrowIfNull(workItem);

            return true;
        }

        public async ValueTask HandleAsync(RuntimeSchedulerWorkItem workItem, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            WorkItemIds.Add(workItem.WorkItemId);

            if (workItem.WorkItemId != terminateOnWorkItemId)
                return;

            var state = await workflowStateStore.FindAsync(workItem.WorkflowExecutionId, cancellationToken);
            await workflowStateStore.SaveAsync(state! with
            {
                Status = WorkflowExecutionStatus.Completed,
                CompletedAt = now
            }, cancellationToken);
        }
    }

    private sealed class RecordingWorkflowSchedulerPauseGate(SchedulerPauseDecision? decision) : IWorkflowSchedulerPauseGate
    {
        public List<string> WorkItemIds { get; } = [];

        public ValueTask<SchedulerPauseDecision?> EvaluateAsync(RuntimeSchedulerWorkItem workItem, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(workItem);
            cancellationToken.ThrowIfCancellationRequested();

            WorkItemIds.Add(workItem.WorkItemId);
            return new ValueTask<SchedulerPauseDecision?>(decision);
        }
    }

    private sealed class RecordingRuntimePauseDecisionProvider : IRuntimePauseDecisionProvider
    {
        public List<RuntimePauseDecisionRequest> Requests { get; } = [];

        public ValueTask<SchedulerPauseDecision> DecideAsync(RuntimePauseDecisionRequest request, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            cancellationToken.ThrowIfCancellationRequested();

            Requests.Add(request);
            return new(new SchedulerPauseDecision(
                canAdvance: true,
                boundary: request.Boundary,
                continuationPolicy: RuntimePauseContinuationPolicy.NotPaused,
                holdId: null,
                reason: null));
        }
    }

    private sealed class RecordingFallbackSchedulerWorkHandler : IFallbackWorkflowSchedulerWorkHandler
    {
        public string Name => nameof(RecordingFallbackSchedulerWorkHandler);
        public List<string> WorkItemIds { get; } = [];

        public bool CanHandle(RuntimeSchedulerWorkItem workItem)
        {
            ArgumentNullException.ThrowIfNull(workItem);

            return true;
        }

        public ValueTask HandleAsync(RuntimeSchedulerWorkItem workItem, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            WorkItemIds.Add(workItem.WorkItemId);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class BlockingSchedulerWorkHandler : IWorkflowSchedulerWorkHandler
    {
        private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string Name => nameof(BlockingSchedulerWorkHandler);
        public Task Started => _started.Task;
        public bool CancellationObserved { get; private set; }

        public bool CanHandle(RuntimeSchedulerWorkItem workItem) => true;

        public async ValueTask HandleAsync(RuntimeSchedulerWorkItem workItem, CancellationToken cancellationToken = default)
        {
            _started.TrySetResult();
            try
            {
                await _release.Task.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                CancellationObserved = true;
                throw;
            }
        }

        public void Release() => _release.TrySetResult();
    }

    // Completes successfully when released even if its cancellation token has already fired — models an activity whose
    // body is past the point of no return (or simply does not observe cancellation) when a deadline breaches.
    private sealed class CancellationIgnoringSchedulerWorkHandler : IWorkflowSchedulerWorkHandler
    {
        private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string Name => nameof(CancellationIgnoringSchedulerWorkHandler);
        public Task Started => _started.Task;

        public bool CanHandle(RuntimeSchedulerWorkItem workItem) => true;

        public async ValueTask HandleAsync(RuntimeSchedulerWorkItem workItem, CancellationToken cancellationToken = default)
        {
            _started.TrySetResult();
            await _release.Task;
        }

        public void Release() => _release.TrySetResult();
    }

    // Stands in for a handler whose checkpoint commit folds the claim's deletion into its unit-of-work: the queue row
    // disappears inside the "store call" (the consume attempt window), and only once that call returns does the
    // committer mark it consumed. settleBeforeParking picks which side of that window the handler parks on, so one
    // theory covers both a renewal landing after the consume settled and one landing while it is still in flight.
    private sealed class ConsumingSchedulerWorkHandler(
        IWorkflowSchedulerWorkQueue queue,
        IRuntimeConsumedSchedulerWorkClaimAccessor accessor,
        bool settleBeforeParking) : IWorkflowSchedulerWorkHandler
    {
        private readonly TaskCompletionSource _consumed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string Name => nameof(ConsumingSchedulerWorkHandler);

        /// <summary>Completes once the work item is gone from the queue, whether or not the consume has settled.</summary>
        public Task Consumed => _consumed.Task;

        public bool CancellationObserved { get; private set; }

        public bool CanHandle(RuntimeSchedulerWorkItem workItem) => true;

        public async ValueTask HandleAsync(RuntimeSchedulerWorkItem workItem, CancellationToken cancellationToken = default)
        {
            var attempt = accessor.BeginConsumeAttempt(workItem.WorkItemId);
            await queue.DeleteAsync(workItem.WorkflowExecutionId, workItem.WorkItemId, cancellationToken);
            if (settleBeforeParking)
                Settle(workItem.WorkItemId, attempt);

            _consumed.TrySetResult();
            try
            {
                await _release.Task.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                CancellationObserved = true;
                throw;
            }
            finally
            {
                if (!settleBeforeParking)
                    Settle(workItem.WorkItemId, attempt);
            }
        }

        public void Release() => _release.TrySetResult();

        private void Settle(string workItemId, IDisposable attempt)
        {
            accessor.MarkConsumedDurably(workItemId);
            attempt.Dispose();
        }
    }

    // Decorates the real accessor so a test can wait for the exact moment the renewal loop recognizes its own consume,
    // rather than inferring it from timing. Both signals are watched because the loop consults them in order: a landed
    // consume ends the loop on WasConsumedDurably alone and never reaches the in-flight predicate. Until the handler is
    // released the renewal loop is the only reader of either, so a completion here means the loop looked.
    private sealed class ConsumeCheckObservingClaimAccessor(IRuntimeConsumedSchedulerWorkClaimAccessor inner)
        : IRuntimeConsumedSchedulerWorkClaimAccessor
    {
        private readonly TaskCompletionSource _observed = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task OwnConsumeObserved => _observed.Task;

        public ConsumedSchedulerWorkItem? PendingConsume => inner.PendingConsume;

        public bool WasConsumedDurably => Observe(inner.WasConsumedDurably);

        public IDisposable Begin(ConsumedSchedulerWorkItem consume) => inner.Begin(consume);

        public void MarkConsumedDurably(string workItemId) => inner.MarkConsumedDurably(workItemId);

        public IDisposable BeginConsumeAttempt(string workItemId) => inner.BeginConsumeAttempt(workItemId);

        public bool IsConsumeInFlightOrDurable(string workItemId) => Observe(inner.IsConsumeInFlightOrDurable(workItemId));

        private bool Observe(bool recognized)
        {
            if (recognized)
                _observed.TrySetResult();
            return recognized;
        }
    }

    private sealed class RenewalObservingWorkQueue(
        IWorkflowSchedulerWorkQueue inner,
        bool loseFirstRenewal = false) : IWorkflowSchedulerWorkQueue
    {
        private readonly TaskCompletionSource _firstRenewalObserved =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool SupportsClaimTransitions => true;
        public int RenewalAttempts { get; private set; }
        public Task FirstRenewalObserved => _firstRenewalObserved.Task;

        public ValueTask<RuntimeSchedulerWorkItem> EnqueueAsync(
            RuntimeSchedulerWorkItem workItem,
            CancellationToken cancellationToken = default) =>
            inner.EnqueueAsync(workItem, cancellationToken);

        public ValueTask<RuntimeStorePage<RuntimeSchedulerWorkItem>> ListAsync(
            RuntimeSchedulerWorkQuery query,
            CancellationToken cancellationToken = default) =>
            inner.ListAsync(query, cancellationToken);

        public ValueTask<RuntimeSchedulerWorkItem?> DequeueAsync(
            string workflowExecutionId,
            CancellationToken cancellationToken = default) =>
            inner.DequeueAsync(workflowExecutionId, cancellationToken);

        public ValueTask<bool> DeleteAsync(
            string workflowExecutionId,
            string workItemId,
            CancellationToken cancellationToken = default) =>
            inner.DeleteAsync(workflowExecutionId, workItemId, cancellationToken);

        public ValueTask<IReadOnlyCollection<string>> ListPendingWorkflowExecutionIdsAsync(
            int limit,
            CancellationToken cancellationToken = default) =>
            inner.ListPendingWorkflowExecutionIdsAsync(limit, cancellationToken);

        public ValueTask<RuntimeSchedulerWorkClaim?> ClaimAsync(
            RuntimeSchedulerWorkClaimRequest request,
            CancellationToken cancellationToken = default) =>
            inner.ClaimAsync(request, cancellationToken);

        public async ValueTask<RuntimeSchedulerWorkClaimTransitionResult> RenewClaimAsync(
            RuntimeSchedulerWorkClaim claim,
            DateTimeOffset now,
            TimeSpan visibilityTimeout,
            CancellationToken cancellationToken = default)
        {
            RenewalAttempts++;
            RuntimeSchedulerWorkClaimTransitionResult result;
            if (loseFirstRenewal && RenewalAttempts == 1)
                result = RuntimeSchedulerWorkClaimTransitionResult.Stale;
            else
                result = await inner.RenewClaimAsync(claim, now, visibilityTimeout, cancellationToken);

            _firstRenewalObserved.TrySetResult();
            return result;
        }

        public ValueTask<RuntimeSchedulerWorkClaimTransitionResult> CompleteClaimAsync(
            RuntimeSchedulerWorkClaim claim,
            CancellationToken cancellationToken = default) =>
            inner.CompleteClaimAsync(claim, cancellationToken);

        public ValueTask<RuntimeSchedulerWorkClaimTransitionResult> ReleaseClaimAsync(
            RuntimeSchedulerWorkClaim claim,
            DateTimeOffset visibleAt,
            CancellationToken cancellationToken = default) =>
            inner.ReleaseClaimAsync(claim, visibleAt, cancellationToken);
    }

    // Wraps a real InMemory queue but throws OperationCanceledException when the drainer/handler enqueues a follow-up
    // work item (whose WorkItemId contains followUpMarker, e.g. ":start:"). This simulates a *process crash* — not a
    // handler fault — landing between the fallback handler's two independent writes (SaveAsync state, then enqueue the
    // follow-up): OperationCanceledException is re-thrown by the drainer, so nothing is ack-deleted and the source item
    // stays durably queued. Peek/dequeue/source-enqueue pass straight through to the inner queue.
    private sealed class CrashOnFollowUpEnqueueWorkQueue(IWorkflowSchedulerWorkQueue inner, string followUpMarker) : IWorkflowSchedulerWorkQueue
    {
        public ValueTask<RuntimeSchedulerWorkItem> EnqueueAsync(RuntimeSchedulerWorkItem workItem, CancellationToken cancellationToken = default)
        {
            if (workItem.WorkItemId.Contains(followUpMarker, StringComparison.Ordinal))
                throw new OperationCanceledException($"Simulated crash before follow-up enqueue of '{workItem.WorkItemId}'.");

            return inner.EnqueueAsync(workItem, cancellationToken);
        }

        public ValueTask<RuntimeStorePage<RuntimeSchedulerWorkItem>> ListAsync(RuntimeSchedulerWorkQuery query, CancellationToken cancellationToken = default) =>
            inner.ListAsync(query, cancellationToken);

        public ValueTask<RuntimeSchedulerWorkItem?> DequeueAsync(string workflowExecutionId, CancellationToken cancellationToken = default) =>
            inner.DequeueAsync(workflowExecutionId, cancellationToken);

        public ValueTask<IReadOnlyCollection<string>> ListPendingWorkflowExecutionIdsAsync(int limit, CancellationToken cancellationToken = default) =>
            inner.ListPendingWorkflowExecutionIdsAsync(limit, cancellationToken);
    }

    private sealed class PeekDequeueMismatchWorkQueue(RuntimeSchedulerWorkItem peeked, RuntimeSchedulerWorkItem dequeued) : IWorkflowSchedulerWorkQueue
    {
        public ValueTask<RuntimeSchedulerWorkItem> EnqueueAsync(RuntimeSchedulerWorkItem workItem, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<RuntimeStorePage<RuntimeSchedulerWorkItem>> ListAsync(RuntimeSchedulerWorkQuery query, CancellationToken cancellationToken = default) =>
            new(new RuntimeStorePage<RuntimeSchedulerWorkItem>(query, [peeked]));

        public ValueTask<RuntimeSchedulerWorkItem?> DequeueAsync(string workflowExecutionId, CancellationToken cancellationToken = default) =>
            new(dequeued);

        public ValueTask<IReadOnlyCollection<string>> ListPendingWorkflowExecutionIdsAsync(int limit, CancellationToken cancellationToken = default) =>
            new((IReadOnlyCollection<string>)[]);
    }


    private sealed class IncrementingRuntimeExecutionIdGenerator : IRuntimeExecutionIdGenerator
    {
        private int _activityExecutionIndex;

        public string NewWorkflowExecutionId() => "wfexec-unused";

        public string NewWorkflowExecutionCommandId() => "command-unused";

        public string NewWorkflowExecutionCommandEnvelopeId() => "envelope-unused";

        public string NewActivityExecutionId() => $"actexec-{++_activityExecutionIndex}";
    }
}
