using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class RuntimeGeneratorEmissionSchedulerTests
{
    private readonly DateTimeOffset _now = new(2026, 6, 11, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ScheduleAsync_EnqueuesGeneratedEventAsDeterministicSchedulerWork()
    {
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var scheduler = new RuntimeGeneratorEmissionScheduler(queue, new FixedTimeProvider(_now));
        var generatedEvent = NewGeneratedEvent(5);

        var result = await scheduler.ScheduleAsync(new RuntimeGeneratorEmissionScheduleRequest(
            generatedEvent: generatedEvent,
            reason: "GeneratorEmitted",
            enqueuedAt: _now.AddSeconds(1),
            metadata: new Dictionary<string, string> { ["Source"] = "generator-test" }));

        var workItem = Assert.Single(await queue.ListAllAsync(new RuntimeSchedulerWorkQuery("wfexec-1")));
        Assert.Equal(workItem.WorkItemId, result.SchedulerWorkItem.WorkItemId);
        Assert.Equal(workItem.IdempotencyKey, result.SchedulerWorkItem.IdempotencyKey);
        Assert.Equal("wfexec-1:generated-event:event-5", workItem.WorkItemId);
        Assert.Equal("wfexec-1:generated-event-command:event-5", workItem.CommandId);
        Assert.Equal("wfexec-1:generated-event-envelope:event-5", workItem.EnvelopeId);
        Assert.Equal("wfexec-1:generated-event-idempotency:event-5", workItem.IdempotencyKey);
        Assert.Equal(WorkflowExecutionCommandKind.GeneratedEvent, workItem.CommandKind);
        Assert.Equal(5, workItem.Sequence);
        Assert.Equal(_now.AddSeconds(1), workItem.EnqueuedAt);
        Assert.Equal(_now, workItem.RecordedAt);
        Assert.Equal("actexec-generator", workItem.CommandMetadata["GeneratorActivityExecutionId"]);
        Assert.Equal("branch-a", workItem.CommandMetadata["BranchId"]);
        Assert.Equal("durable-payload-5", workItem.CommandMetadata["PayloadValueId"]);
        Assert.Equal("PolicyControlled", workItem.CommandMetadata["GeneratedEventDurability"]);
        Assert.Equal("generator-test", workItem.EnvelopeMetadata["Source"]);

        var payload = workItem.Payload!.Value;
        Assert.Equal("wfexec-1:generated-event:event-5", payload.GetProperty("WorkItemId").GetString());
        Assert.Equal("event-5", payload.GetProperty("GeneratedEvent").GetProperty("GeneratedEventId").GetString());
        Assert.Equal("actexec-generator", payload.GetProperty("GeneratorActivityExecutionId").GetString());
        Assert.Equal("GeneratorEmitted", payload.GetProperty("Reason").GetString());
    }

    [Fact]
    public async Task ScheduleAsync_IsIdempotentByGeneratedEventIdentity()
    {
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var scheduler = new RuntimeGeneratorEmissionScheduler(queue, new FixedTimeProvider(_now));
        var generatedEvent = NewGeneratedEvent(1);

        var first = await scheduler.ScheduleAsync(new RuntimeGeneratorEmissionScheduleRequest(generatedEvent, "GeneratorEmitted"));
        var duplicate = await scheduler.ScheduleAsync(new RuntimeGeneratorEmissionScheduleRequest(generatedEvent, "DuplicateEmission"));

        Assert.Equal(first.SchedulerWorkItem.WorkItemId, duplicate.SchedulerWorkItem.WorkItemId);
        Assert.Equal(first.SchedulerWorkItem.IdempotencyKey, duplicate.SchedulerWorkItem.IdempotencyKey);
        Assert.Equal("GeneratorEmitted", duplicate.GeneratedEventWorkItem.Reason);
        Assert.Single(await queue.ListAllAsync(new RuntimeSchedulerWorkQuery("wfexec-1")));
        Assert.Equal("GeneratorEmitted", first.SchedulerWorkItem.Payload!.Value.GetProperty("Reason").GetString());
    }

    [Fact]
    public async Task ScheduleAsync_IsolatesWorkflowExecutionQueues()
    {
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var scheduler = new RuntimeGeneratorEmissionScheduler(queue, new FixedTimeProvider(_now));

        await scheduler.ScheduleAsync(new RuntimeGeneratorEmissionScheduleRequest(NewGeneratedEvent(1), "GeneratorEmitted"));
        await scheduler.ScheduleAsync(new RuntimeGeneratorEmissionScheduleRequest(NewGeneratedEvent(1, "wfexec-2"), "GeneratorEmitted"));

        Assert.Single(await queue.ListAllAsync(new RuntimeSchedulerWorkQuery("wfexec-1")));
        var secondWorkflowWork = Assert.Single(await queue.ListAllAsync(new RuntimeSchedulerWorkQuery("wfexec-2")));
        Assert.Equal("wfexec-2:generated-event:event-1", secondWorkflowWork.WorkItemId);
    }

    [Fact]
    public async Task ScheduleAsync_UsesConfiguredPayloadJsonOptions()
    {
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var scheduler = new RuntimeGeneratorEmissionScheduler(
            queue,
            new FixedTimeProvider(_now),
            new JsonSerializerOptions(JsonSerializerDefaults.General)
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                PropertyNameCaseInsensitive = true
            });
        var generatedEvent = NewGeneratedEvent(2);

        var first = await scheduler.ScheduleAsync(new RuntimeGeneratorEmissionScheduleRequest(generatedEvent, "GeneratorEmitted"));
        var duplicate = await scheduler.ScheduleAsync(new RuntimeGeneratorEmissionScheduleRequest(generatedEvent, "DuplicateEmission"));

        Assert.Equal("event-2", first.SchedulerWorkItem.Payload!.Value.GetProperty("generatedEvent").GetProperty("generatedEventId").GetString());
        Assert.Equal("GeneratorEmitted", duplicate.GeneratedEventWorkItem.Reason);
    }

    [Fact]
    public void RuntimeGeneratorEmissionScheduleModels_RejectInvalidInput()
    {
        Assert.Throws<ArgumentNullException>(() => new RuntimeGeneratorEmissionScheduleRequest(null!, "GeneratorEmitted"));
        Assert.Throws<ArgumentException>(() => new RuntimeGeneratorEmissionScheduleRequest(NewGeneratedEvent(1), " "));
        Assert.Throws<ArgumentException>(() => new RuntimeGeneratorEmissionScheduleResult(
            new SchedulerGeneratedEventWorkItem(
                workItemId: "generated-work-1",
                generatedEvent: NewGeneratedEvent(1),
                enqueuedAt: _now,
                reason: "GeneratorEmitted"),
            new RuntimeSchedulerWorkItem(
                workItemId: "generated-work-1",
                workflowExecutionId: "wfexec-2",
                commandId: "command-1",
                commandKind: WorkflowExecutionCommandKind.GeneratedEvent,
                envelopeId: "envelope-1",
                idempotencyKey: "wfexec-2:generated-event:event-1",
                enqueuedAt: _now,
                recordedAt: _now)));
        Assert.Throws<ArgumentException>(() => new RuntimeGeneratorEmissionScheduleResult(
            new SchedulerGeneratedEventWorkItem(
                workItemId: "generated-work-1",
                generatedEvent: NewGeneratedEvent(1),
                enqueuedAt: _now,
                reason: "GeneratorEmitted"),
            new RuntimeSchedulerWorkItem(
                workItemId: "generated-work-2",
                workflowExecutionId: "wfexec-1",
                commandId: "command-1",
                commandKind: WorkflowExecutionCommandKind.GeneratedEvent,
                envelopeId: "envelope-1",
                idempotencyKey: "wfexec-1:generated-event:event-1",
                enqueuedAt: _now,
                recordedAt: _now)));
        Assert.Throws<ArgumentException>(() => new RuntimeGeneratorEmissionScheduleResult(
            new SchedulerGeneratedEventWorkItem(
                workItemId: "generated-work-1",
                generatedEvent: NewGeneratedEvent(1),
                enqueuedAt: _now,
                reason: "GeneratorEmitted"),
            new RuntimeSchedulerWorkItem(
                workItemId: "generated-work-1",
                workflowExecutionId: "wfexec-1",
                commandId: "command-1",
                commandKind: WorkflowExecutionCommandKind.RunSchedulerWork,
                envelopeId: "envelope-1",
                idempotencyKey: "wfexec-1:generated-event:event-1",
                enqueuedAt: _now,
                recordedAt: _now)));
    }

    private GeneratedEvent NewGeneratedEvent(long sequence, string workflowExecutionId = "wfexec-1") => new(
        generatedEventId: $"event-{sequence}",
        workflowExecutionId: workflowExecutionId,
        generatorActivityExecutionId: "actexec-generator",
        branchId: "branch-a",
        name: "Tick",
        sequence: sequence,
        occurredAt: _now.AddSeconds(sequence),
        durability: GeneratedEventDurability.PolicyControlled,
        payloadValue: new GeneratedEventPayloadReference($"durable-payload-{sequence}"),
        metadata: new Dictionary<string, string> { ["Generator"] = "timer" });

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
