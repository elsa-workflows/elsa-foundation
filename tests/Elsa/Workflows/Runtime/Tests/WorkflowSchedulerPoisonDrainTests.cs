using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

/// <summary>
/// Guardrails for RT-1 gap b / RT-12: a scheduler work handler that throws must not have its work item silently
/// dropped. The drainer records a poison record honoring <see cref="IRuntimeDomainRetryPolicy"/>, and only re-enqueues
/// immediately for <see cref="RuntimeDomainRetryMode.RetryNow"/>.
///
/// #412 item 3 (redrive-safe drain): dispatch no longer destructively dequeues up front. On a *handler fault* the
/// drainer ack-deletes the source item BEFORE recording poison / honoring a RetryNow re-enqueue, so a
/// deterministically-poisoning handler cannot hot-loop: the item is either parked (no re-enqueue) or re-enqueued
/// exactly once by RetryNow, never both. <see cref="DrainAsync_WithDefaultNoopRetryPolicy_RecordsPoisonWithoutRetryOrReEnqueue"/>
/// (empty queue after a fault) and <see cref="DrainAsync_RetryNowPoisonedHandler_DeliversBoundedTimes_DoesNotHotLoop"/>
/// are the bites: without the ack-on-fault, the still-queued source item would be re-driven by the sweep AND requeued
/// by RetryNow, and repeated drains would redeliver forever.
/// </summary>
public sealed class WorkflowSchedulerPoisonDrainTests
{
    private readonly DateTimeOffset _now = new(2026, 7, 2, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task DrainAsync_WithDefaultNoopRetryPolicy_RecordsPoisonWithoutRetryOrReEnqueue()
    {
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var poisonStore = new InMemoryWorkflowSchedulerPoisonStore();
        var drainer = NewDrainer(queue, poisonStore, new NoopRuntimeDomainRetryPolicy());
        await queue.EnqueueAsync(NewWorkItem(1));

        var result = await drainer.DrainAsync(new RuntimeSchedulerDrainRequest("wfexec-1"));
        var remaining = await queue.ListAllAsync(new RuntimeSchedulerWorkQuery("wfexec-1"));

        Assert.True(result.StoppedOnFault);
        var record = Assert.Single(await poisonStore.ListAsync("wfexec-1"));
        Assert.Equal(RuntimeSchedulerPoisonDisposition.Poisoned, record.Disposition);
        Assert.Null(record.NextRetryAt);
        Assert.Equal(1, record.FailureCount);
        Assert.Equal(nameof(InvalidOperationException), record.Fault.ExceptionType.Split('.')[^1]);
        Assert.Null(record.InnerFault); // The handler exception had no inner exception.
        Assert.Empty(remaining); // Not re-enqueued: the default policy is a safe no-loop park.
    }

    [Fact]
    public async Task DrainAsync_HandlerFaultWithNoRetryPolicyAtAll_StillLeavesADurableRecord()
    {
        // #1271: the weakest configuration the drainer can now be built in — no retry policy, no fault capture policy,
        // nothing but the four required collaborators. The item is ack-deleted either way, so the invariant this pins is
        // that the record survives the ack: an empty queue must always be paired with a poison record, never with
        // nothing. Before the poison store became required, this same configuration dropped the item without a trace.
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var poisonStore = new InMemoryWorkflowSchedulerPoisonStore();
        var drainer = new WorkflowSchedulerDrainer(
            queue,
            [new AlwaysFaultingSchedulerWorkHandler(), new NoopWorkflowSchedulerWorkHandler()],
            new InMemoryWorkflowExecutionStateStore(),
            poisonStore,
            new FakeTimeProvider(_now));
        await queue.EnqueueAsync(NewWorkItem(1));

        var result = await drainer.DrainAsync(new RuntimeSchedulerDrainRequest("wfexec-1"));

        Assert.True(result.StoppedOnFault);
        Assert.Empty(await queue.ListAllAsync(new RuntimeSchedulerWorkQuery("wfexec-1")));
        var record = Assert.Single(await poisonStore.ListAsync("wfexec-1"));
        Assert.Equal(RuntimeSchedulerPoisonDisposition.Poisoned, record.Disposition);
        Assert.Equal("work-1", record.WorkItemId);
        Assert.Empty(record.Metadata); // No policy was consulted, so no retry-mode metadata is stamped.
    }

    [Fact]
    public async Task DrainAsync_HandlerCrashWithWrappedException_RecordsInnerFaultOnPoisonRecord()
    {
        // #1031: handler crashes are routinely wrapped (e.g. GroundworkRuntimeCheckpointWriterException around the
        // physical storage fault). The poison record must carry the first inner fault, captured under the same
        // policy as the outer one, or the root cause is undiagnosable without temporary logging.
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var poisonStore = new InMemoryWorkflowSchedulerPoisonStore();
        var handler = new AlwaysFaultingSchedulerWorkHandler(workItem => new InvalidOperationException(
            $"Checkpoint write failed for {workItem.WorkItemId}.",
            new ArgumentException("GW-PHYSICAL-037: projected column overflow.")));
        var drainer = TestSchedulerDrainer.Create(
            queue,
            [handler, new NoopWorkflowSchedulerWorkHandler()],
            new FakeTimeProvider(_now),
            poisonStore: poisonStore,
            retryPolicy: new NoopRuntimeDomainRetryPolicy());
        await queue.EnqueueAsync(NewWorkItem(1));

        var result = await drainer.DrainAsync(new RuntimeSchedulerDrainRequest("wfexec-1"));

        Assert.True(result.StoppedOnFault);
        var record = Assert.Single(await poisonStore.ListAsync("wfexec-1"));
        Assert.Equal(typeof(InvalidOperationException).FullName, record.Fault.ExceptionType);
        Assert.NotNull(record.InnerFault);
        Assert.Equal(typeof(ArgumentException).FullName, record.InnerFault!.ExceptionType);
        Assert.Equal("GW-PHYSICAL-037: projected column overflow.", record.InnerFault.Message);
    }

    [Fact]
    public async Task DrainAsync_WithRetryNowPolicy_ReEnqueuesThroughQueueContractAndRecordsRetryScheduled()
    {
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var poisonStore = new InMemoryWorkflowSchedulerPoisonStore();
        var drainer = NewDrainer(queue, poisonStore, new StubRetryPolicy(new RuntimeDomainRetryDecision(RuntimeDomainRetryMode.RetryNow, null, "retry-now")));
        await queue.EnqueueAsync(NewWorkItem(1));

        var result = await drainer.DrainAsync(new RuntimeSchedulerDrainRequest("wfexec-1", maxWorkItems: 1));
        var remaining = await queue.ListAllAsync(new RuntimeSchedulerWorkQuery("wfexec-1"));

        Assert.True(result.StoppedOnFault);
        var record = Assert.Single(await poisonStore.ListAsync("wfexec-1"));
        Assert.Equal(RuntimeSchedulerPoisonDisposition.RetryScheduled, record.Disposition);
        Assert.Equal(_now, record.NextRetryAt);
        Assert.Collection(remaining, item => Assert.Equal("work-1", item.WorkItemId)); // Re-enqueued via the public queue contract.
    }

    [Fact]
    public async Task DrainAsync_WithRetryAfterPolicy_RecordsRetryScheduledWithoutImmediateReEnqueue()
    {
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var poisonStore = new InMemoryWorkflowSchedulerPoisonStore();
        var delay = TimeSpan.FromMinutes(5);
        var drainer = NewDrainer(queue, poisonStore, new StubRetryPolicy(new RuntimeDomainRetryDecision(RuntimeDomainRetryMode.RetryAfter, delay, "retry-after")));
        await queue.EnqueueAsync(NewWorkItem(1));

        var result = await drainer.DrainAsync(new RuntimeSchedulerDrainRequest("wfexec-1"));
        var remaining = await queue.ListAllAsync(new RuntimeSchedulerWorkQuery("wfexec-1"));

        Assert.True(result.StoppedOnFault);
        var record = Assert.Single(await poisonStore.ListAsync("wfexec-1"));
        Assert.Equal(RuntimeSchedulerPoisonDisposition.RetryScheduled, record.Disposition);
        Assert.Equal(_now + delay, record.NextRetryAt);
        // Not immediately re-enqueued: honoring the delay is left to the resumption sweep (W2 follow-up).
        Assert.Empty(remaining);
    }

    [Fact]
    public async Task DrainAsync_RepeatedHandlerCrash_AccumulatesFailureCount()
    {
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var poisonStore = new InMemoryWorkflowSchedulerPoisonStore();
        var drainer = NewDrainer(queue, poisonStore, new NoopRuntimeDomainRetryPolicy());

        await queue.EnqueueAsync(NewWorkItem(1));
        await drainer.DrainAsync(new RuntimeSchedulerDrainRequest("wfexec-1"));
        await queue.EnqueueAsync(NewWorkItem(1));
        await drainer.DrainAsync(new RuntimeSchedulerDrainRequest("wfexec-1"));

        var record = Assert.Single(await poisonStore.ListAsync("wfexec-1"));
        Assert.Equal(2, record.FailureCount);
        Assert.Equal(_now, record.FirstFailedAt);
    }

    [Fact]
    public async Task DrainAsync_RetryNowPoisonedHandler_DeliversBoundedTimes_DoesNotHotLoop()
    {
        // #412 item 3 — the load-bearing poison-path edge under the redrive-safe drain. A handler that
        // deterministically faults, paired with a RetryNow policy, must be delivered EXACTLY ONCE per drain and leave
        // EXACTLY ONE copy of the source item queued afterwards. This holds only because the fault branch ack-deletes
        // the source item BEFORE HandleHandlerCrashAsync re-enqueues it via RetryNow: the single drain terminates on
        // fault (one delivery), and the queue converges to a single item (ack removed the original; RetryNow put one
        // back). If the ack-on-fault were removed, the original would survive AND RetryNow would re-enqueue — the item
        // would still be idempotent-by-id single-instance, but the source item would never be consumed, so a
        // resumption re-drive plus RetryNow would keep redelivering it. This test pins the per-drain bound and the
        // steady-state single-instance queue that make poison delivery bounded rather than an infinite hot-loop.
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var poisonStore = new InMemoryWorkflowSchedulerPoisonStore();
        var handler = new AlwaysFaultingSchedulerWorkHandler();
        var drainer = TestSchedulerDrainer.Create(
            queue,
            [handler, new NoopWorkflowSchedulerWorkHandler()],
            new FakeTimeProvider(_now),
            poisonStore: poisonStore,
            retryPolicy: new StubRetryPolicy(new RuntimeDomainRetryDecision(RuntimeDomainRetryMode.RetryNow, null, "retry-now")));

        await queue.EnqueueAsync(NewWorkItem(1));

        // Three successive drains (simulating the pump re-driving the RetryNow-requeued item). Each drain must fault
        // once and stop — never loop within a single drain — and the queue must never accumulate duplicates.
        const int drains = 3;
        for (var i = 0; i < drains; i++)
        {
            var result = await drainer.DrainAsync(new RuntimeSchedulerDrainRequest("wfexec-1", maxWorkItems: 1));
            Assert.True(result.StoppedOnFault);
            Assert.Equal(1, result.DrainedCount); // exactly one item processed per drain — no in-drain hot-loop

            var afterDrain = await queue.ListAllAsync(new RuntimeSchedulerWorkQuery("wfexec-1"));
            Assert.Single(afterDrain); // RetryNow re-enqueued exactly one copy; the ack-deleted original did not linger
            Assert.Equal("work-1", afterDrain.Single().WorkItemId);
        }

        // Bounded delivery: exactly one handler invocation per drain, never an unbounded loop.
        Assert.Equal(drains, handler.InvocationCount);

        // The poison record accumulated one failure per drain (single logical item, not duplicated instances).
        var record = Assert.Single(await poisonStore.ListAsync("wfexec-1"));
        Assert.Equal(drains, record.FailureCount);

        // Second leg — the ack-on-fault bite. With a NoopRetry policy (no re-enqueue), a single drain of a faulting
        // handler must leave the queue EMPTY: the ack-delete on fault is the *only* thing that removes the source item.
        // If the ack-on-fault were dropped, the faulted item would linger and the sweep would redeliver it forever.
        var noopQueue = new InMemoryWorkflowSchedulerWorkQueue();
        var noopHandler = new AlwaysFaultingSchedulerWorkHandler();
        var noopDrainer = TestSchedulerDrainer.Create(
            noopQueue,
            [noopHandler, new NoopWorkflowSchedulerWorkHandler()],
            new FakeTimeProvider(_now),
            poisonStore: new InMemoryWorkflowSchedulerPoisonStore(),
            retryPolicy: new NoopRuntimeDomainRetryPolicy());
        await noopQueue.EnqueueAsync(NewWorkItem(2));

        var noopResult = await noopDrainer.DrainAsync(new RuntimeSchedulerDrainRequest("wfexec-1", maxWorkItems: 1));

        Assert.True(noopResult.StoppedOnFault);
        Assert.Equal(1, noopHandler.InvocationCount);
        Assert.Empty(await noopQueue.ListAllAsync(new RuntimeSchedulerWorkQuery("wfexec-1"))); // ack-on-fault removed the item; NoopRetry did not re-enqueue
    }

    private WorkflowSchedulerDrainer NewDrainer(
        InMemoryWorkflowSchedulerWorkQueue queue,
        IWorkflowSchedulerPoisonStore poisonStore,
        IRuntimeDomainRetryPolicy retryPolicy) =>
        TestSchedulerDrainer.Create(
            queue,
            [new AlwaysFaultingSchedulerWorkHandler(), new NoopWorkflowSchedulerWorkHandler()],
            new FakeTimeProvider(_now),
            pauseGate: null,
            workflowExecutionStateStore: null,
            pipelineDispatcher: null,
            faultCapturePolicy: new DefaultRuntimeFaultCapturePolicy(Options.Create(new RuntimeFaultCaptureOptions())),
            poisonStore: poisonStore,
            retryPolicy: retryPolicy);

    private RuntimeSchedulerWorkItem NewWorkItem(int index)
    {
        using var document = JsonDocument.Parse($$"""{"workItemId":"work-{{index}}"}""");
        return new(
            workItemId: $"work-{index}",
            workflowExecutionId: "wfexec-1",
            commandId: $"command-{index}",
            commandKind: WorkflowExecutionCommandKind.RunSchedulerWork,
            envelopeId: $"envelope-{index}",
            idempotencyKey: $"wfexec-1:command-{index}:{Guid.NewGuid():N}",
            enqueuedAt: _now,
            recordedAt: _now,
            sequence: index,
            payload: document.RootElement.Clone());
    }

    private sealed class AlwaysFaultingSchedulerWorkHandler(Func<RuntimeSchedulerWorkItem, Exception>? exceptionFactory = null) : IWorkflowSchedulerWorkHandler
    {
        public string Name => nameof(AlwaysFaultingSchedulerWorkHandler);
        public int InvocationCount { get; private set; }
        public bool CanHandle(RuntimeSchedulerWorkItem workItem) => true;

        public ValueTask HandleAsync(RuntimeSchedulerWorkItem workItem, CancellationToken cancellationToken = default)
        {
            InvocationCount++;
            throw exceptionFactory?.Invoke(workItem) ?? new InvalidOperationException($"Handler crashed for {workItem.WorkItemId}.");
        }
    }

    private sealed class StubRetryPolicy(RuntimeDomainRetryDecision decision) : IRuntimeDomainRetryPolicy
    {
        public RuntimeDomainRetryDecision Decide(RuntimeDomainRetryRequest request) => decision;
    }
}
