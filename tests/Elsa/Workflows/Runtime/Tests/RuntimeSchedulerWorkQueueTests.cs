using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Constants;
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

        var items = await queue.ListAllAsync(new RuntimeSchedulerWorkQuery("wfexec-1"));

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
        var items = await queue.ListAllAsync(new RuntimeSchedulerWorkQuery("wfexec-1"));

        Assert.Same(firstResult, duplicateResult);
        Assert.Same(first, duplicateResult);
        Assert.Single(items);
        Assert.Equal("command-1", items.Single().CommandId);
    }

    [Fact]
    public async Task EnqueueAsync_ReEnqueuingANonHeadItem_KeepsQueueContentsAndOrderUnchanged()
    {
        // ADR 0031: redelivery de-duplication is a mandated queue-provider contract — re-enqueueing an
        // item already present for a workflow execution must add no duplicate and must not reorder the FIFO.
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        await queue.EnqueueAsync(NewWorkItem(1));
        await queue.EnqueueAsync(NewWorkItem(2));
        await queue.EnqueueAsync(NewWorkItem(3));

        // Redeliver the middle item with a different command payload; the original must win and stay in place.
        var redelivered = await queue.EnqueueAsync(NewWorkItem(2, commandId: "command-redelivered"));

        var items = await queue.ListAsync(new RuntimeSchedulerWorkQuery("wfexec-1"));
        Assert.Equal(new[] { "work-1", "work-2", "work-3" }, items.Select(item => item.WorkItemId));
        Assert.Equal("command-2", redelivered.CommandId);
        Assert.Equal("command-2", items.ElementAt(1).CommandId);
    }

    [Fact]
    public async Task ListPendingWorkflowExecutionIdsAsync_ReturnsDistinctOrderedBacklog()
    {
        var queue = new InMemoryWorkflowSchedulerWorkQueue();

        Assert.Empty(await queue.ListPendingWorkflowExecutionIdsAsync(10));

        await queue.EnqueueAsync(NewWorkItem(1, workflowExecutionId: "wfexec-b"));
        await queue.EnqueueAsync(NewWorkItem(2, workflowExecutionId: "wfexec-b"));
        await queue.EnqueueAsync(NewWorkItem(3, workflowExecutionId: "wfexec-a"));

        Assert.Equal(new[] { "wfexec-a", "wfexec-b" }, await queue.ListPendingWorkflowExecutionIdsAsync(10));
        Assert.Equal(new[] { "wfexec-a" }, await queue.ListPendingWorkflowExecutionIdsAsync(1));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => queue.ListPendingWorkflowExecutionIdsAsync(0).AsTask());
    }

    [Fact]
    public async Task DequeueAsync_IsolatesWorkflowExecutionQueues()
    {
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        await queue.EnqueueAsync(NewWorkItem(1, workflowExecutionId: "wfexec-1"));
        await queue.EnqueueAsync(NewWorkItem(1, workflowExecutionId: "wfexec-2"));

        var secondWorkflowItem = await queue.DequeueAsync("wfexec-2");
        var firstWorkflowItems = await queue.ListAllAsync(new RuntimeSchedulerWorkQuery("wfexec-1"));
        var secondWorkflowItems = await queue.ListAllAsync(new RuntimeSchedulerWorkQuery("wfexec-2"));

        Assert.Equal("wfexec-2", secondWorkflowItem!.WorkflowExecutionId);
        Assert.Equal("work-1", secondWorkflowItem.WorkItemId);
        Assert.Single(firstWorkflowItems);
        Assert.Empty(secondWorkflowItems);
    }

    [Fact]
    public async Task WorkflowSchedulerCommandProcessor_RecordsEnvelopeAsSchedulerWork()
    {
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var processor = new WorkflowSchedulerCommandRouter(
            queue,
            DeferredSchedulerDrainPolicy.Instance,
            new WorkflowDrainOrchestrator(ThrowingSchedulerDrainer.Instance, EmptyPostCommitOutboxProcessor.Instance, []),
            new FixedTimeProvider(_now));
        var envelope = NewEnvelope(1);

        await processor.ProcessAsync(envelope);

        var workItem = Assert.Single(await queue.ListAllAsync(new RuntimeSchedulerWorkQuery("wfexec-1")));
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
    public async Task WorkflowSchedulerCommandProcessor_ProjectsBookmarkResumeIntoItsActivityScope()
    {
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var states = new InMemoryActivityExecutionStateStore();
        await states.SaveAsync(new ActivityExecutionState(
            new ActivityExecution("child", "wfexec-1", "node-child", "authored-child", "test", "1"),
            ActivityExecutionStatus.Suspended,
            null,
            1,
            _now,
            _now,
            null,
            "outer",
            "outer",
            null,
            null,
            ActivitySchedulingProvenance.From("wfexec-1", "outer", "outer", null, null, null, "outer", "test"),
            null,
            [],
            [],
            0,
            0,
            new Dictionary<string, string>(),
            ExecutionScopeId: "outer"));
        var processor = new WorkflowSchedulerCommandRouter(
            queue,
            DeferredSchedulerDrainPolicy.Instance,
            new WorkflowDrainOrchestrator(ThrowingSchedulerDrainer.Instance, EmptyPostCommitOutboxProcessor.Instance, []),
            new FixedTimeProvider(_now),
            states);
        var command = new WorkflowExecutionCommand(
            "command-resume",
            "wfexec-1",
            WorkflowExecutionCommandKind.ResumeBookmark,
            _now,
            Payload: null,
            Metadata: new Dictionary<string, string> { [RuntimeMetadataKeys.ActivityExecutionId] = "child" });
        var envelope = new WorkflowExecutionCommandEnvelope(
            "envelope-resume",
            "wfexec-1",
            command,
            "wfexec-1:resume",
            WorkflowExecutionCommandDeliveryMode.AtLeastOnce,
            _now);

        await processor.ProcessAsync(envelope);

        var workItem = Assert.Single(await queue.ListAllAsync(new RuntimeSchedulerWorkQuery("wfexec-1")));
        Assert.Equal("outer", workItem.ExecutionScopeId);
    }

    [Fact]
    public async Task InProcessAgent_QueuesAcceptedCommandsThroughDefaultProcessor()
    {
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var processor = new WorkflowSchedulerCommandRouter(
            queue,
            DeferredSchedulerDrainPolicy.Instance,
            new WorkflowDrainOrchestrator(ThrowingSchedulerDrainer.Instance, EmptyPostCommitOutboxProcessor.Instance, []));
        var provider = new InProcessWorkflowExecutionActorProvider(processor);
        var agent = await provider.GetAgentAsync(NewActivationRequest("wfexec-1"));
        var envelope = NewEnvelope(1);

        var first = await agent.EnqueueAsync(envelope);
        var duplicate = await agent.EnqueueAsync(NewEnvelope(2, idempotencyKey: envelope.IdempotencyKey));

        var workItem = Assert.Single(await queue.ListAllAsync(new RuntimeSchedulerWorkQuery("wfexec-1")));
        Assert.Equal(WorkflowExecutionCommandDispatchStatus.Accepted, first.Status);
        Assert.Equal(WorkflowExecutionCommandDispatchStatus.Duplicate, duplicate.Status);
        Assert.Equal(envelope.EnvelopeId, workItem.EnvelopeId);
    }

    [Fact]
    public void RuntimeSchedulerWorkModels_RejectInvalidQueueMetadata()
    {
        var workItem = NewWorkItem(1);
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
        Assert.Throws<ArgumentException>(() =>
            new RuntimeSchedulerWorkClaimRequest("wfexec-1", " ", _now, TimeSpan.FromMinutes(1)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new RuntimeSchedulerWorkClaimRequest("wfexec-1", "owner-1", _now, TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new RuntimeSchedulerWorkClaim(workItem, "owner-1", 0, 1, _now, _now.AddMinutes(1)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new RuntimeSchedulerWorkClaim(workItem, "owner-1", 1, 0, _now, _now.AddMinutes(1)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new RuntimeSchedulerWorkClaim(workItem, "owner-1", 1, 1, _now, _now));
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

    private WorkflowExecutionActorActivationRequest NewActivationRequest(
        string workflowExecutionId,
        WorkflowExecutionActorCapabilities requiredCapabilities = WorkflowExecutionActorCapabilities.InProcessMailbox) =>
        new(
            workflowExecutionId: workflowExecutionId,
            reason: WorkflowExecutionActorActivationReason.Start,
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

    private sealed class DeferredSchedulerDrainPolicy : IWorkflowSchedulerDrainPolicy
    {
        public static readonly DeferredSchedulerDrainPolicy Instance = new();

        private DeferredSchedulerDrainPolicy()
        {
        }

        public RuntimeSchedulerDrainRequest? CreateDrainRequest(
            WorkflowExecutionCommandEnvelope envelope,
            RuntimeSchedulerWorkItem workItem) => null;
    }

    private sealed class ThrowingSchedulerDrainer : IWorkflowSchedulerDrainer
    {
        public static readonly ThrowingSchedulerDrainer Instance = new();

        private ThrowingSchedulerDrainer()
        {
        }

        public ValueTask<RuntimeSchedulerDrainResult> DrainAsync(RuntimeSchedulerDrainRequest request, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Deferred scheduler drain policy should not invoke the drainer.");
    }

    private sealed class EmptyPostCommitOutboxProcessor : IRuntimePostCommitOutboxProcessor
    {
        public static readonly EmptyPostCommitOutboxProcessor Instance = new();

        private EmptyPostCommitOutboxProcessor()
        {
        }

        public ValueTask<RuntimePostCommitOutboxProcessResult> ProcessAsync(
            RuntimePostCommitOutboxProcessRequest request,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new RuntimePostCommitOutboxProcessResult([]));
    }
}
