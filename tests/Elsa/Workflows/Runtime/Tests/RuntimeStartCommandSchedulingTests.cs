using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class RuntimeStartCommandSchedulingTests
{
    private readonly DateTimeOffset _now = new(2026, 6, 11, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_EnqueuesScheduleActivityWorkForExecutableStartNodes()
    {
        var store = new InMemoryWorkflowExecutableStore();
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var executable = NewExecutable(["node-start", "node-other"], ["node-start"]);
        await store.SaveAsync(executable);
        var handler = new WorkflowStartSchedulerWorkHandler(store, queue, new FixedTimeProvider(_now));

        await handler.HandleAsync(NewStartWorkItem(executable.Identity));

        var scheduled = Assert.Single(await queue.ListAsync(new RuntimeSchedulerWorkQuery("wfexec-1")));
        Assert.Equal(WorkflowExecutionCommandKind.ScheduleActivity, scheduled.CommandKind);
        Assert.Equal("wfexec-1", scheduled.WorkflowExecutionId);
        Assert.Equal("start-work:schedule:node-start", scheduled.WorkItemId);
        Assert.Equal(_now, scheduled.EnqueuedAt);
        Assert.Equal(_now, scheduled.RecordedAt);

        var payload = scheduled.Payload!.Value.Deserialize<RuntimeScheduleActivityCommandPayload>()!;
        Assert.Equal("node-start", payload.ExecutableNodeId);
        Assert.Equal(RuntimeScheduleActivityCommandPayload.WorkflowStartReason, payload.Reason);
        Assert.Equal(executable.Identity, payload.PinnedExecutable);
    }

    [Fact]
    public async Task HandleAsync_EnqueuesOneScheduleActivityWorkItemPerStartNode()
    {
        var store = new InMemoryWorkflowExecutableStore();
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var executable = NewExecutable(["node-a", "node-b", "node-c"], ["node-a", "node-c"]);
        await store.SaveAsync(executable);
        var handler = new WorkflowStartSchedulerWorkHandler(store, queue, new FixedTimeProvider(_now));

        await handler.HandleAsync(NewStartWorkItem(executable.Identity));

        var scheduled = await queue.ListAsync(new RuntimeSchedulerWorkQuery("wfexec-1"));
        Assert.Equal(new[] { "node-a", "node-c" }, scheduled
            .Select(item => item.Payload!.Value.Deserialize<RuntimeScheduleActivityCommandPayload>()!.ExecutableNodeId)
            .ToArray());
        Assert.All(scheduled, item => Assert.Equal(WorkflowExecutionCommandKind.ScheduleActivity, item.CommandKind));
    }

    [Fact]
    public async Task HandleAsync_RejectsPinnedExecutableMismatchBeforeScheduling()
    {
        var store = new InMemoryWorkflowExecutableStore();
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var executable = NewExecutable(["node-start"], ["node-start"]);
        await store.SaveAsync(executable);
        var pinned = executable.Identity with { ArtifactHash = "sha256:pinned" };
        var handler = new WorkflowStartSchedulerWorkHandler(store, queue, new FixedTimeProvider(_now));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => handler.HandleAsync(NewStartWorkItem(pinned)).AsTask());

        Assert.Contains("pinned executable artifact", exception.Message);
        Assert.Contains("definition-1/version-1", exception.Message);
        Assert.Empty(await queue.ListAsync(new RuntimeSchedulerWorkQuery("wfexec-1")));
    }

    [Fact]
    public async Task HandleAsync_IgnoresSourceReferenceWhenCheckingPinnedExecutableSnapshot()
    {
        var store = new InMemoryWorkflowExecutableStore();
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var executable = NewExecutable(["node-start"], ["node-start"]);
        await store.SaveAsync(executable);
        var pinned = executable.Identity with
        {
            Source = new WorkflowExecutableSourceReference("WorkflowDefinitionVersion", "version-1", "1.0.0")
        };
        var handler = new WorkflowStartSchedulerWorkHandler(store, queue, new FixedTimeProvider(_now));

        await handler.HandleAsync(NewStartWorkItem(pinned));

        var scheduled = Assert.Single(await queue.ListAsync(new RuntimeSchedulerWorkQuery("wfexec-1")));
        Assert.Equal(WorkflowExecutionCommandKind.ScheduleActivity, scheduled.CommandKind);
    }

    [Fact]
    public async Task HandleAsync_RejectsMissingStartPayloadBeforeScheduling()
    {
        var store = new InMemoryWorkflowExecutableStore();
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var handler = new WorkflowStartSchedulerWorkHandler(store, queue, new FixedTimeProvider(_now));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => handler.HandleAsync(NewStartWorkItem(includePayload: false)).AsTask());

        Assert.Contains("requires a start command payload", exception.Message);
        Assert.Empty(await queue.ListAsync(new RuntimeSchedulerWorkQuery("wfexec-1")));
    }

    [Fact]
    public async Task HandleAsync_RejectsMalformedStartPayloadBeforeScheduling()
    {
        using var document = JsonDocument.Parse("[]");
        var store = new InMemoryWorkflowExecutableStore();
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var handler = new WorkflowStartSchedulerWorkHandler(store, queue, new FixedTimeProvider(_now));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => handler.HandleAsync(NewStartWorkItem(payload: document.RootElement.Clone())).AsTask());

        Assert.Contains("not a valid start command payload", exception.Message);
        Assert.Empty(await queue.ListAsync(new RuntimeSchedulerWorkQuery("wfexec-1")));
    }

    [Fact]
    public async Task HandleAsync_RejectsExecutableWithoutStartNodesBeforeScheduling()
    {
        var store = new InMemoryWorkflowExecutableStore();
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var executable = NewExecutable(["node-a"], []);
        await store.SaveAsync(executable);
        var handler = new WorkflowStartSchedulerWorkHandler(store, queue, new FixedTimeProvider(_now));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => handler.HandleAsync(NewStartWorkItem(executable.Identity)).AsTask());

        Assert.Contains("does not declare any start nodes", exception.Message);
        Assert.Empty(await queue.ListAsync(new RuntimeSchedulerWorkQuery("wfexec-1")));
    }

    [Fact]
    public void CanHandle_AcceptsOnlyStartCommandWork()
    {
        var handler = new WorkflowStartSchedulerWorkHandler(
            new InMemoryWorkflowExecutableStore(),
            new InMemoryWorkflowSchedulerWorkQueue(),
            new FixedTimeProvider(_now));

        Assert.True(handler.CanHandle(NewStartWorkItem(NewIdentity())));
        Assert.False(handler.CanHandle(NewStartWorkItem(NewIdentity(), WorkflowExecutionCommandKind.ScheduleActivity)));
    }

    private RuntimeSchedulerWorkItem NewStartWorkItem(
        WorkflowExecutableIdentity? pinnedExecutable = null,
        WorkflowExecutionCommandKind commandKind = WorkflowExecutionCommandKind.Start,
        JsonElement? payload = null,
        bool includePayload = true)
    {
        var resolvedPayload = includePayload
            ? payload ?? JsonSerializer.SerializeToElement(new WorkflowExecutionStartCommandPayload(
                pinnedExecutable ?? NewIdentity(),
                "artifact-1"))
            : (JsonElement?)null;

        return new RuntimeSchedulerWorkItem(
            workItemId: "start-work",
            workflowExecutionId: "wfexec-1",
            commandId: "command-1",
            commandKind: commandKind,
            envelopeId: "envelope-1",
            idempotencyKey: "wfexec-1:start:artifact-1",
            enqueuedAt: _now,
            recordedAt: _now,
            sequence: 10,
            payload: resolvedPayload,
            commandMetadata: new Dictionary<string, string> { ["source"] = "test" },
            envelopeMetadata: new Dictionary<string, string> { ["transport"] = "in-process" });
    }

    private static WorkflowExecutable NewExecutable(IReadOnlyCollection<string> nodeIds, IReadOnlyCollection<string> startNodeIds) =>
        new(
            identity: NewIdentity(),
            nodes: nodeIds.Select(NewNode).ToArray(),
            edges: [],
            startNodeIds: startNodeIds,
            resumeTargets: new Dictionary<string, WorkflowExecutableResumeTarget>(),
            createdAt: DateTimeOffset.UtcNow,
            publishedAt: DateTimeOffset.UtcNow,
            compatibilityMetadata: new Dictionary<string, string>());

    private static WorkflowExecutableIdentity NewIdentity() =>
        new("artifact-1", "definition-1", "version-1", "1.0.0", "sha256:test");

    private static ExecutableNode NewNode(string nodeId)
    {
        using var document = JsonDocument.Parse("""{"type":"test"}""");
        return new(
            executableNodeId: nodeId,
            authoredActivityId: $"authored-{nodeId}",
            activityType: "test/activity",
            activityTypeVersion: "1.0.0",
            descriptorType: "test",
            descriptorPayload: document.RootElement.Clone(),
            inputBindings: new Dictionary<string, RuntimeInputBinding>(),
            outputCaptures: new Dictionary<string, RuntimeOutputCapture>(),
            metadata: new Dictionary<string, string>());
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
