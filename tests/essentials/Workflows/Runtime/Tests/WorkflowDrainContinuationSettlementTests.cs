using System.Text.Json;
using System.Threading.Channels;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Elsa.Workflows.Runtime.Services.Executions;
using Elsa.Workflows.Runtime.Services.Incidents;
using Elsa.Workflows.Runtime.Services.Scheduler;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

/// <summary>
/// #2225 at the drain orchestrator, over the in-memory stores and the real outbox processor: what a drain reports when
/// another deliverer holds one of its execution's continuations, how long it waits, and when it reads its queue. Time is a
/// fake clock that fires every poll the drain parks on at once, so each wait is recorded exactly. The end-to-end
/// interleavings, with the real resumption sweep on every store, are the Runtime EF Core tests'
/// <c>LiveDrainSweepContentionContract</c>.
/// </summary>
public sealed class WorkflowDrainContinuationSettlementTests
{
    private const string Wfid = "wfexec-settlement";
    private const string OtherDeliverer = "other-deliverer";
    private static readonly DateTimeOffset Start = new(2030, 1, 2, 3, 4, 5, TimeSpan.Zero);
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);
    private static readonly RuntimePostCommitRetryPolicy Retrying = new(3, TimeSpan.FromSeconds(10));

    private readonly PollClock _clock = new(Start);
    private readonly InMemoryRuntimeCheckpointCommitStore _store = new();
    private readonly InMemoryWorkflowSchedulerWorkQueue _queue = new();
    private readonly InMemoryExecutionLivenessStateStore _liveness = new();
    private readonly AsyncLocalRuntimeLiveDrainDeliveryAccessor _liveDrain = new();
    private readonly ObservedClaimStore _observed;
    private readonly HopDrainer _drainer;

    /// <summary>The delay of every poll the drain parked on, in order.</summary>
    private readonly List<TimeSpan> _polls = [];

    public WorkflowDrainContinuationSettlementTests()
    {
        _observed = new ObservedClaimStore(_store);
        _drainer = new HopDrainer(_store, _queue, _clock);
    }

    [Fact]
    public async Task Cancelling_a_drain_that_waits_for_another_deliverers_claim_ends_it_without_quiescence()
    {
        using var cancellation = new CancellationTokenSource();
        _observed.OnListed = listed =>
        {
            if (listed.Count > 0)
                cancellation.Cancel();
            return Task.CompletedTask;
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => RunAsync(cancellationToken: cancellation.Token));

        Assert.Equal(1, _drainer.DrainCount);
        var continuation = await FindContinuationAsync(0);
        Assert.Equal(RuntimePostCommitOutboxStatus.Delivering, continuation.Status);
        Assert.Equal(OtherDeliverer, continuation.DeliveringOwnerId);
        Assert.Null((await _liveness.FindAsync(Wfid, RuntimeExecutionOwnershipStateId.For(Wfid)))?.ExecutionLease);
    }

    /// <summary>
    /// The other deliverer fails the continuation while the drain waits. Without a retry policy the store records the
    /// failure as final and the item leaves the listing, so the drain looks it up; with one it stays listed as failed.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_continuation_its_other_deliverer_failed_ends_the_drain_as_a_failed_delivery(bool retrying)
    {
        _drainer.RetryPolicy = retrying ? Retrying : RuntimePostCommitRetryPolicy.None;
        _observed.OnListed = async listed =>
        {
            if (listed.Any(item => item.Status == RuntimePostCommitOutboxStatus.Delivering))
                await FailAsOtherDelivererAsync(_drainer.OtherClaims[0]);
        };

        var result = await RunAsync();

        Assert.Equal(RuntimeSchedulerDrainStopReason.OutboxDeliveryFailed, result.StopReason);
        Assert.Equal(1, _drainer.DrainCount);
        Assert.Equal(
            retrying ? RuntimePostCommitOutboxStatus.FailedRetryable : RuntimePostCommitOutboxStatus.FailedFinal,
            (await FindContinuationAsync(0)).Status);
    }

    /// <summary>
    /// The direction that looked like success: the other deliverer's attempt failed before the drain's first read, so
    /// nothing was claimed, nothing was deliverable and nothing was queued, and the drain reported quiescence.
    /// </summary>
    [Fact]
    public async Task A_continuation_another_deliverer_failed_before_the_drains_first_read_ends_the_drain_as_a_failed_delivery()
    {
        _drainer.RetryPolicy = Retrying;
        _drainer.TakeContinuation = async item =>
        {
            await _drainer.ClaimAsOtherDelivererAsync(item);
            await FailAsOtherDelivererAsync(_drainer.OtherClaims[0]);
        };

        var result = await RunAsync();

        Assert.Equal(RuntimeSchedulerDrainStopReason.OutboxDeliveryFailed, result.StopReason);
        Assert.Equal(1, _drainer.DrainCount);
        Assert.Empty(_polls);
    }

    [Fact]
    public async Task The_wait_doubles_from_ten_milliseconds_to_a_half_second_cap_and_ends_at_the_deadline()
    {
        var result = await RunAsync(new WorkflowDrainOrchestratorOptions(continuationClaimWaitLimit: TimeSpan.FromSeconds(3)));

        Assert.Equal(RuntimeSchedulerDrainStopReason.OutboxDeliveryFailed, result.StopReason);
        Assert.Equal(Milliseconds(10, 20, 40, 80, 160, 320, 500, 500, 500, 500, 370), _polls);
        Assert.Equal(Start + TimeSpan.FromSeconds(3), _clock.GetUtcNow());
        var continuation = await FindContinuationAsync(0);
        Assert.Equal(RuntimePostCommitOutboxStatus.Delivering, continuation.Status);
        Assert.Equal(OtherDeliverer, continuation.DeliveringOwnerId);
    }

    [Fact]
    public async Task The_wait_wakes_at_the_earliest_lapse_and_the_drain_delivers_the_lapsed_continuation_itself()
    {
        _drainer.ClaimVisibility = TimeSpan.FromSeconds(1);

        var result = await RunAsync();

        Assert.Equal(RuntimeSchedulerDrainStopReason.Quiesced, result.StopReason);
        Assert.Equal(Milliseconds(10, 20, 40, 80, 160, 320, 370), _polls);
        Assert.Equal(Start + TimeSpan.FromSeconds(1), _clock.GetUtcNow());
        var continuation = await FindContinuationAsync(0);
        Assert.Equal(RuntimePostCommitOutboxStatus.Delivered, continuation.Status);
        Assert.Equal(2, continuation.DeliveryFencingToken);
        Assert.Equal(2, _drainer.DrainCount);
    }

    /// <summary>
    /// Two continuations in one drain request, each taken by the other deliverer. The first is delivered after a wait of
    /// 1.63 s; the second never is. Its wait ends at the request's one deadline, three seconds after the first wait began,
    /// not three seconds after the second began, so the drain's cycles cannot multiply the limit.
    /// </summary>
    [Fact]
    public async Task Every_wait_in_a_drain_request_shares_one_deadline()
    {
        _drainer.Continuations = 2;
        _observed.OnListed = async listed =>
        {
            if (_clock.GetUtcNow() >= Start + TimeSpan.FromSeconds(1) &&
                listed.Any(item => item.OutboxItemId == _drainer.OtherClaims[0].OutboxItemId))
                await DeliverAsOtherDelivererAsync(_drainer.OtherClaims[0]);
        };

        var result = await RunAsync(new WorkflowDrainOrchestratorOptions(continuationClaimWaitLimit: TimeSpan.FromSeconds(3)));

        Assert.Equal(RuntimeSchedulerDrainStopReason.OutboxDeliveryFailed, result.StopReason);
        Assert.Equal(
            Milliseconds(10, 20, 40, 80, 160, 320, 500, 500, 10, 20, 40, 80, 160, 320, 500, 240),
            _polls);
        Assert.Equal(Start + TimeSpan.FromSeconds(3), _clock.GetUtcNow());
        Assert.Equal(2, _drainer.DrainCount);
        Assert.Equal(RuntimePostCommitOutboxStatus.Delivering, (await FindContinuationAsync(1)).Status);
    }

    /// <summary>A dead deliverer's claim lapses inside the default wait, so the drain gets to deliver the item itself.</summary>
    [Fact]
    public void The_default_wait_limit_is_the_processors_claim_visibility_plus_a_margin()
    {
        Assert.True(WorkflowDrainOrchestratorOptions.ContinuationClaimWaitMargin > TimeSpan.Zero);
        Assert.Equal(
            RuntimePostCommitOutboxProcessing.ClaimVisibilityTimeout + WorkflowDrainOrchestratorOptions.ContinuationClaimWaitMargin,
            new WorkflowDrainOrchestratorOptions().ContinuationClaimWaitLimit);
    }

    [Fact]
    public async Task Queued_work_a_drain_could_not_claim_does_not_keep_it_draining()
    {
        // A scheduler drain that ran nothing could not take the queued head either, so finding it queued afterwards is no
        // sign that another deliverer just delivered it: the drain quiesces instead of cycling to its cap.
        await _queue.EnqueueAsync(WorkItem("work-held-elsewhere"));
        _drainer.RunsStart = false;
        _drainer.DrainsQueue = false;
        _drainer.Continuations = 0;

        var result = await RunAsync();

        Assert.Equal(RuntimeSchedulerDrainStopReason.Quiesced, result.StopReason);
        Assert.Equal(1, _drainer.DrainCount);
    }

    /// <summary>
    /// Another deliverer finished a continuation before the drain's read: no claim is left, only its queued work. A drain
    /// that ran dry reads its queue and drains that work before it quiesces.
    /// </summary>
    [Fact]
    public async Task Queued_work_after_a_drain_that_ran_dry_is_drained_before_quiescence()
    {
        await _queue.EnqueueAsync(WorkItem("work-delivered-elsewhere"));
        _drainer.Continuations = 0;

        var result = await RunAsync();

        Assert.Equal(RuntimeSchedulerDrainStopReason.Quiesced, result.StopReason);
        Assert.Equal(2, _drainer.DrainCount);
        Assert.Contains(result.Items, item => item.WorkItemId == "work-delivered-elsewhere");
        Assert.Empty((await _queue.ListAsync(new RuntimeSchedulerWorkQuery(Wfid, limit: 1))).Items);
    }

    [Fact]
    public async Task Queued_work_after_a_drain_that_reached_a_terminal_status_does_not_keep_it_draining()
    {
        await _queue.EnqueueAsync(WorkItem("work-after-the-end"));
        _drainer.Continuations = 0;
        _drainer.FirstStopReason = RuntimeSchedulerDrainStopReason.WorkflowTerminated;

        var result = await RunAsync();

        Assert.Equal(RuntimeSchedulerDrainStopReason.Quiesced, result.StopReason);
        Assert.Equal(1, _drainer.DrainCount);
        Assert.Single((await _queue.ListAsync(new RuntimeSchedulerWorkQuery(Wfid, limit: 10))).Items);
    }

    [Fact]
    public async Task Queued_work_after_a_drain_that_reached_its_budget_does_not_keep_it_draining()
    {
        await _queue.EnqueueAsync(WorkItem("work-over-budget"));
        _drainer.Continuations = 0;

        var result = await RunAsync(maxWorkItems: 1);

        Assert.Equal(RuntimeSchedulerDrainStopReason.Quiesced, result.StopReason);
        Assert.Equal(1, _drainer.DrainCount);
        Assert.Single((await _queue.ListAsync(new RuntimeSchedulerWorkQuery(Wfid, limit: 10))).Items);
    }

    /// <summary>
    /// The mutual-wait corner. A sweep claims a batch holding an item that needs this execution's mailbox, then this
    /// execution's continuation. The drain of the execution holds the mailbox, as <c>agent.EnqueueAsync</c> does around
    /// it, and waits for its continuation. Dispatched in claim order, the sweep would wait for the mailbox and the drain
    /// for the continuation, until the claim lapsed a minute later. The sweep dispatches continuations first, so the drain
    /// is done one poll after the continuation was delivered.
    /// </summary>
    [Fact]
    public async Task A_sweep_dispatches_a_drains_continuation_before_an_item_that_needs_the_drains_mailbox()
    {
        using var mailbox = new SemaphoreSlim(1, 1);
        await mailbox.WaitAsync();
        var drainWaiting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sweepDispatcher = new MailboxSweepDispatcher(mailbox, new RuntimeSchedulerPostCommitIntentDispatcher(_queue), drainWaiting.Task);
        var sweep = new RuntimePostCommitOutboxProcessor(_store, sweepDispatcher, _clock);
        // Recorded and available before the continuation, so the sweep claims it first.
        await _store.AddPendingForTestingAsync(MailboxItem());
        Task<RuntimePostCommitOutboxProcessResult>? sweeping = null;
        _drainer.TakeContinuation = async _ =>
        {
            sweeping = RunElsewhere(() => sweep.ProcessAsync(new RuntimePostCommitOutboxProcessRequest(limit: 10)).AsTask());
            await sweepDispatcher.Dispatching.WaitAsync(Patience);
        };
        var waited = 0;
        _observed.OnListed = async listed =>
        {
            if (listed.Count == 0 || Interlocked.Exchange(ref waited, 1) == 1)
                return;

            // The drain is waiting on its continuation: let the sweep go on, and look again once it reached the mailbox item.
            drainWaiting.TrySetResult();
            await sweepDispatcher.MailboxDispatchStarted.WaitAsync(Patience);
        };

        var result = await RunAsync();
        mailbox.Release();
        var swept = await sweeping!.WaitAsync(Patience);

        Assert.Equal(RuntimeSchedulerDrainStopReason.Quiesced, result.StopReason);
        Assert.Equal(["intent-work-bookmark-0", "intent-needs-mailbox"], sweepDispatcher.Dispatched);
        Assert.Equal(2, swept.DeliveredCount);
        var continuation = await FindContinuationAsync(0);
        Assert.Equal(RuntimePostCommitOutboxStatus.Delivered, continuation.Status);
        Assert.Equal(1, continuation.DeliveryFencingToken);
        Assert.Equal(Milliseconds(10), _polls);
    }

    private Task<RuntimeSchedulerDrainResult> RunAsync(
        WorkflowDrainOrchestratorOptions? options = null,
        int? maxWorkItems = null,
        CancellationToken cancellationToken = default)
    {
        var orchestrator = new WorkflowDrainOrchestrator(
            _drainer,
            new RuntimePostCommitOutboxProcessor(
                _store,
                new RuntimeSchedulerPostCommitIntentDispatcher(_queue),
                _clock,
                DefaultRuntimeFaultCapturePolicy.CreateDefault(),
                workflowDispatchStore: null,
                logger: null,
                _liveDrain,
                coalescingSessionAccessor: null),
            schedulerDrainObservers: [],
            checkpointRuleViolationFaulter: TestCheckpointRuleViolationFaulter.Create(_clock),
            // A lease far longer than any wait here, so its heartbeat never comes due.
            ownershipService: new RuntimeExecutionOwnershipService(
                _liveness,
                _clock,
                new RuntimeExecutionOwnershipOptions { LeaseDuration = TimeSpan.FromHours(1) }),
            ownershipContextAccessor: new AsyncLocalRuntimeExecutionOwnershipContextAccessor(),
            outboxClaimStore: _observed,
            outboxLookupStore: _observed,
            schedulerWorkQueue: _queue,
            options: options,
            liveDrainDeliveryAccessor: _liveDrain,
            timeProvider: _clock);
        return DriveAsync(orchestrator.DrainAsync(Envelope(), new RuntimeSchedulerDrainRequest(Wfid, maxWorkItems), cancellationToken).AsTask());
    }

    // Fires every poll the drain parks on as soon as it parks, moving the clock by exactly that poll's delay. The first timer
    // a drain creates is its lease heartbeat, before it drains anything; it is not a poll and never comes due here.
    private async Task<RuntimeSchedulerDrainResult> DriveAsync(Task<RuntimeSchedulerDrainResult> drain)
    {
        var heartbeat = true;
        while (true)
        {
            var timer = _clock.NextTimerAsync();
            if (await Task.WhenAny(timer, drain).WaitAsync(Patience) == drain)
                return await drain;

            var due = await timer;
            if (heartbeat)
            {
                heartbeat = false;
                continue;
            }

            _polls.Add(due);
            _clock.Advance(due);
        }
    }

    private async Task DeliverAsOtherDelivererAsync(RuntimePostCommitOutboxClaim claim)
    {
        await new RuntimeSchedulerPostCommitIntentDispatcher(_queue).DispatchAsync(claim.Item.Intent);
        await CompleteAsOtherDelivererAsync(claim, RuntimePostCommitOutboxStatus.Delivered);
    }

    private Task FailAsOtherDelivererAsync(RuntimePostCommitOutboxClaim claim) =>
        CompleteAsOtherDelivererAsync(claim, RuntimePostCommitOutboxStatus.FailedRetryable, "The other deliverer could not enqueue the work.");

    private async Task CompleteAsOtherDelivererAsync(
        RuntimePostCommitOutboxClaim claim,
        RuntimePostCommitOutboxStatus status,
        string? failureMessage = null) =>
        await _store.CompleteClaimAsync(new RuntimePostCommitOutboxClaimCompletion(
            claim,
            new RuntimePostCommitOutboxDeliveryResult(claim.OutboxItemId, status, _clock.GetUtcNow(), failureMessage)));

    private async Task<RuntimePostCommitOutboxItem> FindContinuationAsync(int index) =>
        Assert.IsType<RuntimePostCommitOutboxItem>(await _store.FindAsync(HopDrainer.ContinuationId(index)));

    // Runs a step where a pump's timer would: on the thread pool, with none of the drain's ambient state.
    private static Task<T> RunElsewhere<T>(Func<Task<T>> step)
    {
        using (ExecutionContext.SuppressFlow())
            return Task.Run(step);
    }

    private static TimeSpan[] Milliseconds(params int[] values) => values.Select(value => TimeSpan.FromMilliseconds(value)).ToArray();

    private static RuntimeSchedulerWorkItem WorkItem(string workItemId) =>
        new(
            workItemId: workItemId,
            workflowExecutionId: Wfid,
            commandId: $"command-{workItemId}",
            commandKind: WorkflowExecutionCommandKind.CreateBookmark,
            envelopeId: $"envelope-{workItemId}",
            idempotencyKey: $"{Wfid}:{workItemId}",
            enqueuedAt: Start,
            recordedAt: Start);

    // An item of another kind for the same execution whose dispatch needs the execution's mailbox, as a PublishStimulus
    // start or a DispatchWorkflow parent resume does.
    private static RuntimePostCommitOutboxItem MailboxItem() =>
        new(
            "outbox-needs-mailbox",
            new RuntimePostCommitIntent("intent-needs-mailbox", Wfid, MailboxSweepDispatcher.MailboxKind, Start - TimeSpan.FromSeconds(1), null, null, null),
            RuntimePostCommitOutboxStatus.Pending,
            Start - TimeSpan.FromSeconds(1),
            Start - TimeSpan.FromSeconds(1));

    private static WorkflowExecutionCommandEnvelope Envelope()
    {
        using var document = JsonDocument.Parse("""{"kind":"run"}""");
        return new(
            envelopeId: "envelope-drain",
            workflowExecutionId: Wfid,
            command: new WorkflowExecutionCommand(
                CommandId: "command-drain",
                WorkflowExecutionId: Wfid,
                Kind: WorkflowExecutionCommandKind.RunSchedulerWork,
                EnqueuedAt: Start,
                Payload: document.RootElement.Clone(),
                Metadata: new Dictionary<string, string>()),
            idempotencyKey: $"{Wfid}:command-drain",
            deliveryMode: WorkflowExecutionCommandDeliveryMode.AtLeastOnce,
            enqueuedAt: Start,
            sequence: 1,
            metadata: new Dictionary<string, string>());
    }

    /// <summary>
    /// Stands in for the scheduler drainer. Its first drain runs one item; later drains run what is queued. A drain that
    /// ran something commits a continuation, up to <see cref="Continuations"/> of them, and hands each to another
    /// deliverer before the drain's delivery step (by default that deliverer claims it), as the resumption sweep can.
    /// </summary>
    private sealed class HopDrainer(
        InMemoryRuntimeCheckpointCommitStore store,
        InMemoryWorkflowSchedulerWorkQueue queue,
        TimeProvider clock) : IWorkflowSchedulerDrainer
    {
        private int _committed;

        public int Continuations { get; set; } = 1;
        public bool RunsStart { get; set; } = true;
        public bool DrainsQueue { get; set; } = true;
        public RuntimeSchedulerDrainStopReason? FirstStopReason { get; set; }
        public RuntimePostCommitRetryPolicy RetryPolicy { get; set; } = RuntimePostCommitRetryPolicy.None;
        public TimeSpan ClaimVisibility { get; set; } = TimeSpan.FromMinutes(10);
        public Func<RuntimePostCommitOutboxItem, Task>? TakeContinuation { get; set; }
        public int DrainCount { get; private set; }
        public List<RuntimePostCommitOutboxClaim> OtherClaims { get; } = [];

        public static string ContinuationId(int index) => $"outbox-continuation-{index}";

        public async ValueTask<RuntimeSchedulerDrainResult> DrainAsync(RuntimeSchedulerDrainRequest request, CancellationToken cancellationToken = default)
        {
            var now = clock.GetUtcNow();
            List<string> ran = [];
            if (DrainCount++ == 0)
            {
                if (RunsStart)
                    ran.Add("work-start");
            }
            else if (DrainsQueue)
            {
                while (await queue.DequeueAsync(Wfid, cancellationToken) is { } work)
                    ran.Add(work.WorkItemId);
            }

            if (ran.Count > 0 && _committed < Continuations)
            {
                var continuation = Continuation(_committed++, now);
                await store.AddPendingForTestingAsync(continuation, cancellationToken);
                await (TakeContinuation ?? ClaimAsOtherDelivererAsync)(continuation);
            }

            return new RuntimeSchedulerDrainResult(
                Wfid,
                now,
                now,
                ran.Select(workItemId => new RuntimeSchedulerWorkItemResult(
                    workItemId,
                    Wfid,
                    WorkflowExecutionCommandKind.CreateBookmark,
                    RuntimeSchedulerWorkItemResultStatus.Completed,
                    nameof(HopDrainer),
                    now,
                    now)).ToArray(),
                stopReason: DrainCount == 1 ? FirstStopReason : null);
        }

        public async Task ClaimAsOtherDelivererAsync(RuntimePostCommitOutboxItem continuation) =>
            OtherClaims.Add(Assert.Single(await store.ClaimAsync(new RuntimePostCommitOutboxClaimRequest(
                OtherDeliverer,
                clock.GetUtcNow(),
                ClaimVisibility,
                limit: 1,
                Wfid,
                RuntimePostCommitIntentKinds.EnqueueSchedulerWork))));

        private RuntimePostCommitOutboxItem Continuation(int index, DateTimeOffset now)
        {
            var work = WorkItem($"work-bookmark-{index}");
            return new RuntimePostCommitOutboxItem(
                ContinuationId(index),
                new RuntimePostCommitIntent(
                    $"intent-{work.WorkItemId}",
                    Wfid,
                    RuntimePostCommitIntentKinds.EnqueueSchedulerWork,
                    now,
                    activityExecutionId: null,
                    idempotencyKey: $"checkpoint:{work.WorkItemId}",
                    payload: JsonSerializer.SerializeToElement(work)),
                RuntimePostCommitOutboxStatus.Pending,
                now,
                now,
                RetryPolicy);
        }
    }

    /// <summary>The in-memory store as the drain sees it, with a hook on every read of held continuations.</summary>
    private sealed class ObservedClaimStore(InMemoryRuntimeCheckpointCommitStore inner) : ForwardingOutboxStore(inner)
    {
        public Func<IReadOnlyCollection<RuntimePostCommitOutboxItem>, Task>? OnListed { get; set; }

        public override async ValueTask<IReadOnlyCollection<RuntimePostCommitOutboxItem>> ListClaimedAsync(
            RuntimePostCommitOutboxClaimedQuery query,
            CancellationToken cancellationToken = default)
        {
            var listed = await base.ListClaimedAsync(query, cancellationToken);
            if (OnListed is { } observe)
                await observe(listed);
            return listed;
        }
    }

    /// <summary>
    /// The sweep's dispatch. A continuation only enqueues, once the drain is waiting for it. Any other kind needs the
    /// execution's mailbox, which the drain holds.
    /// </summary>
    private sealed class MailboxSweepDispatcher(
        SemaphoreSlim mailbox,
        IRuntimePostCommitIntentDispatcher continuations,
        Task drainWaiting) : IRuntimePostCommitIntentDispatcher
    {
        public const string MailboxKind = "Tests.NeedsMailbox";

        private readonly TaskCompletionSource _dispatching = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _mailboxDispatchStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public List<string> Dispatched { get; } = [];
        public Task Dispatching => _dispatching.Task;
        public Task MailboxDispatchStarted => _mailboxDispatchStarted.Task;

        public async ValueTask DispatchAsync(RuntimePostCommitIntent intent, CancellationToken cancellationToken = default)
        {
            lock (Dispatched)
                Dispatched.Add(intent.IntentId);
            _dispatching.TrySetResult();

            if (intent.Kind == RuntimePostCommitIntentKinds.EnqueueSchedulerWork)
            {
                await drainWaiting.WaitAsync(Patience, cancellationToken);
                await continuations.DispatchAsync(intent, cancellationToken);
                return;
            }

            _mailboxDispatchStarted.TrySetResult();
            await mailbox.WaitAsync(cancellationToken);
            mailbox.Release();
        }
    }

    /// <summary>A fake clock that reports every timer the code under test parks on, so the test can fire it.</summary>
    private sealed class PollClock(DateTimeOffset start) : FakeTimeProvider(start)
    {
        private readonly Channel<TimeSpan> _timers = Channel.CreateUnbounded<TimeSpan>();

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = base.CreateTimer(callback, state, dueTime, period);
            _timers.Writer.TryWrite(dueTime);
            return timer;
        }

        public Task<TimeSpan> NextTimerAsync() => _timers.Reader.ReadAsync().AsTask();
    }
}
