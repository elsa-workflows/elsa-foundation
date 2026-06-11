using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class RuntimeSchedulerDrainTests
{
    private readonly DateTimeOffset _now = new(2026, 6, 11, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task DrainAsync_DispatchesQueuedWorkInFifoOrder()
    {
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var handler = new RecordingSchedulerWorkHandler();
        var drainer = new WorkflowSchedulerDrainer(queue, [handler, new NoopWorkflowSchedulerWorkHandler()], new FixedTimeProvider(_now));
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
    public async Task DrainAsync_RespectsMaxWorkItems()
    {
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var handler = new RecordingSchedulerWorkHandler();
        var drainer = new WorkflowSchedulerDrainer(queue, [handler, new NoopWorkflowSchedulerWorkHandler()], new FixedTimeProvider(_now));
        await queue.EnqueueAsync(NewWorkItem(1));
        await queue.EnqueueAsync(NewWorkItem(2));
        await queue.EnqueueAsync(NewWorkItem(3));

        var result = await drainer.DrainAsync(new RuntimeSchedulerDrainRequest("wfexec-1", maxWorkItems: 2));
        var remaining = await queue.ListAsync(new RuntimeSchedulerWorkQuery("wfexec-1"));

        Assert.Equal(2, result.DrainedCount);
        Assert.Equal(["work-1", "work-2"], handler.WorkItemIds);
        Assert.Collection(remaining, item => Assert.Equal("work-3", item.WorkItemId));
    }

    [Fact]
    public async Task DrainAsync_StopsOnHandlerFault()
    {
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var handler = new RecordingSchedulerWorkHandler(faultOnWorkItemId: "work-2");
        var drainer = new WorkflowSchedulerDrainer(queue, [handler, new NoopWorkflowSchedulerWorkHandler()], new FixedTimeProvider(_now));
        await queue.EnqueueAsync(NewWorkItem(1));
        await queue.EnqueueAsync(NewWorkItem(2));
        await queue.EnqueueAsync(NewWorkItem(3));

        var result = await drainer.DrainAsync(new RuntimeSchedulerDrainRequest("wfexec-1"));
        var remaining = await queue.ListAsync(new RuntimeSchedulerWorkQuery("wfexec-1"));

        Assert.True(result.StoppedOnFault);
        Assert.Equal(2, result.DrainedCount);
        Assert.Collection(
            result.Items,
            first => Assert.Equal(RuntimeSchedulerWorkItemResultStatus.Completed, first.Status),
            second =>
            {
                Assert.Equal(RuntimeSchedulerWorkItemResultStatus.Faulted, second.Status);
                Assert.Equal("Fault requested for work-2.", second.Error);
            });
        Assert.Collection(remaining, item => Assert.Equal("work-3", item.WorkItemId));
    }

    [Fact]
    public async Task DrainAsync_UsesNoopFallbackWhenNoCustomHandlerMatches()
    {
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var customHandler = new RecordingSchedulerWorkHandler(canHandle: false);
        var drainer = new WorkflowSchedulerDrainer(queue, [customHandler, new NoopWorkflowSchedulerWorkHandler()], new FixedTimeProvider(_now));
        await queue.EnqueueAsync(NewWorkItem(1));

        var result = await drainer.DrainAsync(new RuntimeSchedulerDrainRequest("wfexec-1"));

        var item = Assert.Single(result.Items);
        Assert.Equal(RuntimeSchedulerWorkItemResultStatus.Completed, item.Status);
        Assert.Equal(NoopWorkflowSchedulerWorkHandler.HandlerName, item.HandlerName);
        Assert.Empty(customHandler.WorkItemIds);
        Assert.Equal(1, customHandler.CanHandleCallCount);
    }

    [Fact]
    public void RuntimeSchedulerDrainModels_RejectInvalidResults()
    {
        Assert.Throws<ArgumentException>(() => new RuntimeSchedulerDrainRequest(" "));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RuntimeSchedulerDrainRequest("wfexec-1", maxWorkItems: 0));
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
        Assert.Throws<ArgumentException>(() => new RuntimeSchedulerDrainResult(
            workflowExecutionId: "wfexec-1",
            startedAt: _now,
            completedAt: _now,
            items: [CompletedResult("wfexec-2")]));
    }

    private RuntimeSchedulerWorkItem NewWorkItem(int index, string workflowExecutionId = "wfexec-1")
    {
        using var document = JsonDocument.Parse($$"""{"workItemId":"work-{{index}}"}""");
        return new(
            workItemId: $"work-{index}",
            workflowExecutionId: workflowExecutionId,
            commandId: $"command-{index}",
            commandKind: WorkflowExecutionCommandKind.RunSchedulerWork,
            envelopeId: $"envelope-{index}",
            idempotencyKey: $"{workflowExecutionId}:command-{index}",
            enqueuedAt: _now,
            recordedAt: _now,
            sequence: index,
            payload: document.RootElement.Clone());
    }

    private RuntimeSchedulerWorkItemResult CompletedResult(string workflowExecutionId) =>
        new(
            workItemId: "work-1",
            workflowExecutionId: workflowExecutionId,
            commandKind: WorkflowExecutionCommandKind.RunSchedulerWork,
            status: RuntimeSchedulerWorkItemResultStatus.Completed,
            handlerName: "handler",
            startedAt: _now,
            completedAt: _now);

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

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
