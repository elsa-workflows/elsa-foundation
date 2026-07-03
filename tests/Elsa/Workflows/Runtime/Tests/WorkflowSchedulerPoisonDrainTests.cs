using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

/// <summary>
/// Guardrails for RT-1 gap b / RT-12: a scheduler work handler that throws must not have its already-dequeued work item
/// silently dropped. The drainer records a poison record honoring <see cref="IRuntimeDomainRetryPolicy"/>, and only
/// re-enqueues immediately for <see cref="RuntimeDomainRetryMode.RetryNow"/>.
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
        var remaining = await queue.ListAsync(new RuntimeSchedulerWorkQuery("wfexec-1"));

        Assert.True(result.StoppedOnFault);
        var record = Assert.Single(await poisonStore.ListAsync("wfexec-1"));
        Assert.Equal(RuntimeSchedulerPoisonDisposition.Poisoned, record.Disposition);
        Assert.Null(record.NextRetryAt);
        Assert.Equal(1, record.FailureCount);
        Assert.Equal(nameof(InvalidOperationException), record.Fault.ExceptionType.Split('.')[^1]);
        Assert.Empty(remaining); // Not re-enqueued: the default policy is a safe no-loop park.
    }

    [Fact]
    public async Task DrainAsync_WithRetryNowPolicy_ReEnqueuesThroughQueueContractAndRecordsRetryScheduled()
    {
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var poisonStore = new InMemoryWorkflowSchedulerPoisonStore();
        var drainer = NewDrainer(queue, poisonStore, new StubRetryPolicy(new RuntimeDomainRetryDecision(RuntimeDomainRetryMode.RetryNow, null, "retry-now")));
        await queue.EnqueueAsync(NewWorkItem(1));

        var result = await drainer.DrainAsync(new RuntimeSchedulerDrainRequest("wfexec-1", maxWorkItems: 1));
        var remaining = await queue.ListAsync(new RuntimeSchedulerWorkQuery("wfexec-1"));

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
        var remaining = await queue.ListAsync(new RuntimeSchedulerWorkQuery("wfexec-1"));

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

    private WorkflowSchedulerDrainer NewDrainer(
        InMemoryWorkflowSchedulerWorkQueue queue,
        IWorkflowSchedulerPoisonStore poisonStore,
        IRuntimeDomainRetryPolicy retryPolicy) =>
        TestSchedulerDrainer.Create(
            queue,
            [new AlwaysFaultingSchedulerWorkHandler(), new NoopWorkflowSchedulerWorkHandler()],
            new FixedTimeProvider(_now),
            pauseGate: null,
            NoopWorkflowExecutionAmbientServicesAccessor.Instance,
            workflowExecutionStateStore: null,
            pipelineDispatcher: null,
            faultCapturePolicy: new DefaultRuntimeFaultCapturePolicy(),
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

    private sealed class AlwaysFaultingSchedulerWorkHandler : IWorkflowSchedulerWorkHandler
    {
        public string Name => nameof(AlwaysFaultingSchedulerWorkHandler);
        public bool CanHandle(RuntimeSchedulerWorkItem workItem) => true;

        public ValueTask HandleAsync(RuntimeSchedulerWorkItem workItem, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException($"Handler crashed for {workItem.WorkItemId}.");
    }

    private sealed class StubRetryPolicy(RuntimeDomainRetryDecision decision) : IRuntimeDomainRetryPolicy
    {
        public RuntimeDomainRetryDecision Decide(RuntimeDomainRetryRequest request) => decision;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
