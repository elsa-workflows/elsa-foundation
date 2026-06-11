using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class RuntimeSchedulerWorkQueueTests
{
    private readonly DateTimeOffset _now = new(2026, 6, 11, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task EnqueueAsync_PreservesPerWorkflowInsertionOrder()
    {
        var queue = new InMemoryWorkflowSchedulerWorkQueue();

        await queue.EnqueueAsync(NewWorkItem(1));
        await queue.EnqueueAsync(NewWorkItem(2));
        await queue.EnqueueAsync(NewWorkItem(3));

        var items = await queue.ListAsync(new RuntimeSchedulerWorkQuery("wfexec-1"));

        Assert.Collection(
            items,
            first => Assert.Equal("work-1", first.WorkItemId),
            second => Assert.Equal("work-2", second.WorkItemId),
            third => Assert.Equal("work-3", third.WorkItemId));
    }

    [Fact]
    public async Task EnqueueAsync_IsIdempotentByWorkItemId()
    {
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var first = NewWorkItem(1, commandId: "command-1");
        var duplicate = NewWorkItem(1, commandId: "command-duplicate");

        var firstResult = await queue.EnqueueAsync(first);
        var duplicateResult = await queue.EnqueueAsync(duplicate);
        var items = await queue.ListAsync(new RuntimeSchedulerWorkQuery("wfexec-1"));

        Assert.Same(firstResult, duplicateResult);
        Assert.Same(first, duplicateResult);
        Assert.Single(items);
        Assert.Equal("command-1", items.Single().CommandId);
    }

    [Fact]
    public async Task DequeueAsync_IsolatesWorkflowExecutionQueues()
    {
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        await queue.EnqueueAsync(NewWorkItem(1, workflowExecutionId: "wfexec-1"));
        await queue.EnqueueAsync(NewWorkItem(1, workflowExecutionId: "wfexec-2"));

        var secondWorkflowItem = await queue.DequeueAsync("wfexec-2");
        var firstWorkflowItems = await queue.ListAsync(new RuntimeSchedulerWorkQuery("wfexec-1"));
        var secondWorkflowItems = await queue.ListAsync(new RuntimeSchedulerWorkQuery("wfexec-2"));

        Assert.Equal("wfexec-2", secondWorkflowItem!.WorkflowExecutionId);
        Assert.Equal("work-1", secondWorkflowItem.WorkItemId);
        Assert.Single(firstWorkflowItems);
        Assert.Empty(secondWorkflowItems);
    }

    [Fact]
    public async Task WorkflowSchedulerCommandProcessor_RecordsEnvelopeAsSchedulerWork()
    {
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var processor = new WorkflowSchedulerCommandProcessor(queue, new FixedTimeProvider(_now));
        var envelope = NewEnvelope(1);

        await processor.ProcessAsync(envelope);

        var workItem = Assert.Single(await queue.ListAsync(new RuntimeSchedulerWorkQuery("wfexec-1")));
        Assert.Equal(envelope.EnvelopeId, workItem.WorkItemId);
        Assert.Equal(envelope.Command.CommandId, workItem.CommandId);
        Assert.Equal(envelope.Command.Kind, workItem.CommandKind);
        Assert.Equal(envelope.IdempotencyKey, workItem.IdempotencyKey);
        Assert.Equal(envelope.Sequence, workItem.Sequence);
        Assert.Equal(_now, workItem.RecordedAt);
        Assert.Equal("work-1", workItem.Payload!.Value.GetProperty("workItemId").GetString());
        Assert.Equal("test", workItem.CommandMetadata["source"]);
        Assert.Equal("in-process", workItem.EnvelopeMetadata["transport"]);
    }

    [Fact]
    public async Task InProcessAgent_QueuesAcceptedCommandsThroughDefaultProcessor()
    {
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var processor = new WorkflowSchedulerCommandProcessor(queue);
        var provider = new InProcessWorkflowExecutionAgentProvider(processor);
        var agent = await provider.GetAgentAsync(NewActivationRequest("wfexec-1"));
        var envelope = NewEnvelope(1);

        var first = await agent.EnqueueAsync(envelope);
        var duplicate = await agent.EnqueueAsync(NewEnvelope(2, idempotencyKey: envelope.IdempotencyKey));

        var workItem = Assert.Single(await queue.ListAsync(new RuntimeSchedulerWorkQuery("wfexec-1")));
        Assert.Equal(WorkflowExecutionCommandDispatchStatus.Accepted, first.Status);
        Assert.Equal(WorkflowExecutionCommandDispatchStatus.Duplicate, duplicate.Status);
        Assert.Equal(envelope.EnvelopeId, workItem.EnvelopeId);
    }

    [Fact]
    public void RuntimeSchedulerWorkModels_RejectInvalidQueueMetadata()
    {
        Assert.Throws<ArgumentException>(() => NewWorkItem(1, workflowExecutionId: " "));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RuntimeSchedulerWorkQuery("wfexec-1", limit: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RuntimeSchedulerWorkItem(
            workItemId: "work-1",
            workflowExecutionId: "wfexec-1",
            commandId: "command-1",
            commandKind: WorkflowExecutionCommandKind.RunSchedulerWork,
            envelopeId: "envelope-1",
            idempotencyKey: "wfexec-1:command-1",
            enqueuedAt: _now,
            recordedAt: _now,
            sequence: -1));
    }

    private RuntimeSchedulerWorkItem NewWorkItem(
        int index,
        string workflowExecutionId = "wfexec-1",
        string? commandId = null)
    {
        using var document = JsonDocument.Parse($$"""{"workItemId":"work-{{index}}"}""");
        return new(
            workItemId: $"work-{index}",
            workflowExecutionId: workflowExecutionId,
            commandId: commandId ?? $"command-{index}",
            commandKind: WorkflowExecutionCommandKind.RunSchedulerWork,
            envelopeId: $"envelope-{index}",
            idempotencyKey: $"{workflowExecutionId}:command-{index}",
            enqueuedAt: _now,
            recordedAt: _now,
            sequence: index,
            payload: document.RootElement.Clone());
    }

    private WorkflowExecutionAgentActivationRequest NewActivationRequest(
        string workflowExecutionId,
        WorkflowExecutionAgentCapabilities requiredCapabilities = WorkflowExecutionAgentCapabilities.InProcessMailbox) =>
        new(
            workflowExecutionId: workflowExecutionId,
            reason: WorkflowExecutionAgentActivationReason.Start,
            requestedAt: _now,
            requestedBy: "runtime-test",
            requiredCapabilities: requiredCapabilities);

    private WorkflowExecutionCommandEnvelope NewEnvelope(
        int index,
        string workflowExecutionId = "wfexec-1",
        string? idempotencyKey = null)
    {
        using var document = JsonDocument.Parse($$"""{"workItemId":"work-{{index}}"}""");
        var command = new WorkflowExecutionCommand(
            CommandId: $"command-{index}",
            WorkflowExecutionId: workflowExecutionId,
            Kind: WorkflowExecutionCommandKind.RunSchedulerWork,
            EnqueuedAt: _now,
            Payload: document.RootElement.Clone(),
            Metadata: new Dictionary<string, string> { ["source"] = "test" });

        return new(
            envelopeId: $"envelope-{index}",
            workflowExecutionId: workflowExecutionId,
            command: command,
            idempotencyKey: idempotencyKey ?? $"{workflowExecutionId}:command-{index}",
            deliveryMode: WorkflowExecutionCommandDeliveryMode.AtLeastOnce,
            enqueuedAt: _now,
            sequence: index,
            metadata: new Dictionary<string, string> { ["transport"] = "in-process" });
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
