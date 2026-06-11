using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class RuntimeStartActivityStateTests
{
    private readonly DateTimeOffset _now = new(2026, 6, 11, 12, 0, 0, TimeSpan.Zero);
    private readonly InMemoryWorkflowExecutableStore _executableStore = new();
    private readonly InMemoryActivityExecutionStateStore _activityStateStore = new();
    private readonly InMemoryWorkflowSchedulerWorkQueue _schedulerWorkQueue = new();

    [Fact]
    public async Task HandleAsync_TransitionsScheduledActivityExecutionStateToRunning()
    {
        var executable = NewExecutable();
        await _executableStore.SaveAsync(executable);
        await _activityStateStore.SaveAsync(NewScheduledState());
        var handler = NewHandler();

        await handler.HandleAsync(NewStartWorkItem(executable.Identity));

        var state = await _activityStateStore.FindAsync("wfexec-1", "actexec-1");
        Assert.NotNull(state);
        Assert.Equal(ActivityExecutionStatus.Running, state.Status);
        Assert.Equal(_now, state.StartedAt);
        Assert.Equal("actexec-1", state.Execution.ActivityExecutionId);
        Assert.Equal("node-start", state.Execution.ExecutableNodeId);
        Assert.Equal(RuntimeStartActivityCommandPayload.ScheduledActivityReason, state.Metadata["runtime.startReason"]);
        Assert.Equal("start-work", state.Metadata["runtime.startSchedulerWorkItemId"]);
        var invokeWork = Assert.Single(await _schedulerWorkQueue.ListAsync(new RuntimeSchedulerWorkQuery("wfexec-1")));
        Assert.Equal(WorkflowExecutionCommandKind.InvokeActivity, invokeWork.CommandKind);
        var invokePayload = invokeWork.Payload!.Value.Deserialize<RuntimeInvokeActivityCommandPayload>()!;
        Assert.Equal("actexec-1", invokePayload.ActivityExecutionId);
        Assert.Equal("node-start", invokePayload.ExecutableNodeId);
        Assert.Equal(RuntimeInvokeActivityCommandPayload.StartedActivityReason, invokePayload.Reason);
    }

    [Fact]
    public async Task HandleAsync_ReenqueuesInvokeActivityWorkForExistingRunningState()
    {
        var executable = NewExecutable();
        await _executableStore.SaveAsync(executable);
        await _activityStateStore.SaveAsync(NewScheduledState() with
        {
            Status = ActivityExecutionStatus.Running,
            StartedAt = _now.AddMinutes(-1)
        });
        var handler = NewHandler();

        await handler.HandleAsync(NewStartWorkItem(executable.Identity));

        var state = await _activityStateStore.FindAsync("wfexec-1", "actexec-1");
        Assert.NotNull(state);
        Assert.Equal(ActivityExecutionStatus.Running, state.Status);
        Assert.Equal(_now.AddMinutes(-1), state.StartedAt);
        var invokeWork = Assert.Single(await _schedulerWorkQueue.ListAsync(new RuntimeSchedulerWorkQuery("wfexec-1")));
        Assert.Equal(WorkflowExecutionCommandKind.InvokeActivity, invokeWork.CommandKind);
    }

    [Fact]
    public async Task HandleAsync_DoesNotOverwriteOrEnqueueForExistingLaterLifecycleState()
    {
        var executable = NewExecutable();
        await _executableStore.SaveAsync(executable);
        await _activityStateStore.SaveAsync(NewScheduledState() with
        {
            Status = ActivityExecutionStatus.Completed,
            StartedAt = _now.AddMinutes(-1),
            CompletedAt = _now
        });
        var handler = NewHandler();

        await handler.HandleAsync(NewStartWorkItem(executable.Identity));

        var state = await _activityStateStore.FindAsync("wfexec-1", "actexec-1");
        Assert.NotNull(state);
        Assert.Equal(ActivityExecutionStatus.Completed, state.Status);
        Assert.Equal(_now.AddMinutes(-1), state.StartedAt);
        Assert.Equal(_now, state.CompletedAt);
        Assert.Empty(await _schedulerWorkQueue.ListAsync(new RuntimeSchedulerWorkQuery("wfexec-1")));
    }

    [Fact]
    public async Task HandleAsync_IgnoresSourceReferenceWhenCheckingPinnedExecutableSnapshot()
    {
        var executable = NewExecutable();
        await _executableStore.SaveAsync(executable);
        await _activityStateStore.SaveAsync(NewScheduledState());
        var pinned = executable.Identity with
        {
            Source = new WorkflowExecutableSourceReference("WorkflowDefinitionVersion", "version-1", "1.0.0")
        };
        var handler = NewHandler();

        await handler.HandleAsync(NewStartWorkItem(pinned));

        var state = await _activityStateStore.FindAsync("wfexec-1", "actexec-1");
        Assert.NotNull(state);
        Assert.Equal(ActivityExecutionStatus.Running, state.Status);
    }

    [Fact]
    public async Task HandleAsync_RejectsMissingPayloadBeforeChangingState()
    {
        await _activityStateStore.SaveAsync(NewScheduledState());
        var handler = NewHandler();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => handler.HandleAsync(NewStartWorkItem(includePayload: false)).AsTask());

        Assert.Contains("requires a start activity payload", exception.Message);
        var state = await _activityStateStore.FindAsync("wfexec-1", "actexec-1");
        Assert.NotNull(state);
        Assert.Equal(ActivityExecutionStatus.Scheduled, state.Status);
    }

    [Fact]
    public async Task HandleAsync_RejectsMalformedPayloadBeforeChangingState()
    {
        using var document = JsonDocument.Parse("[]");
        await _activityStateStore.SaveAsync(NewScheduledState());
        var handler = NewHandler();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => handler.HandleAsync(NewStartWorkItem(payload: document.RootElement.Clone())).AsTask());

        Assert.Contains("not a valid start activity payload", exception.Message);
        var state = await _activityStateStore.FindAsync("wfexec-1", "actexec-1");
        Assert.NotNull(state);
        Assert.Equal(ActivityExecutionStatus.Scheduled, state.Status);
    }

    [Fact]
    public async Task HandleAsync_RejectsPinnedExecutableMismatchBeforeChangingState()
    {
        var executable = NewExecutable();
        await _executableStore.SaveAsync(executable);
        await _activityStateStore.SaveAsync(NewScheduledState());
        var pinned = executable.Identity with { ArtifactHash = "sha256:pinned" };
        var handler = NewHandler();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => handler.HandleAsync(NewStartWorkItem(pinned)).AsTask());

        Assert.Contains("pinned executable artifact", exception.Message);
        Assert.Contains("definition-1/version-1", exception.Message);
        var state = await _activityStateStore.FindAsync("wfexec-1", "actexec-1");
        Assert.NotNull(state);
        Assert.Equal(ActivityExecutionStatus.Scheduled, state.Status);
    }

    [Fact]
    public async Task HandleAsync_RejectsMissingActivityExecutionState()
    {
        var executable = NewExecutable();
        await _executableStore.SaveAsync(executable);
        var handler = NewHandler();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => handler.HandleAsync(NewStartWorkItem(executable.Identity)).AsTask());

        Assert.Contains("missing activity execution", exception.Message);
    }

    [Fact]
    public async Task HandleAsync_RejectsExecutableNodeMismatchBeforeChangingState()
    {
        var executable = NewExecutable();
        await _executableStore.SaveAsync(executable);
        await _activityStateStore.SaveAsync(NewScheduledState());
        var handler = NewHandler();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => handler.HandleAsync(NewStartWorkItem(executable.Identity, executableNodeId: "node-other")).AsTask());

        Assert.Contains("belongs to executable node 'node-start'", exception.Message);
        var state = await _activityStateStore.FindAsync("wfexec-1", "actexec-1");
        Assert.NotNull(state);
        Assert.Equal(ActivityExecutionStatus.Scheduled, state.Status);
    }

    [Fact]
    public void CanHandle_AcceptsOnlyStartActivityWork()
    {
        var handler = NewHandler();

        Assert.True(handler.CanHandle(NewStartWorkItem(NewIdentity())));
        Assert.False(handler.CanHandle(NewStartWorkItem(NewIdentity(), commandKind: WorkflowExecutionCommandKind.ScheduleActivity)));
    }

    private WorkflowStartActivitySchedulerWorkHandler NewHandler() =>
        new(_executableStore, _activityStateStore, _schedulerWorkQueue, new FixedTimeProvider(_now));

    private RuntimeSchedulerWorkItem NewStartWorkItem(
        WorkflowExecutableIdentity? pinnedExecutable = null,
        WorkflowExecutionCommandKind commandKind = WorkflowExecutionCommandKind.StartActivity,
        string executableNodeId = "node-start",
        JsonElement? payload = null,
        bool includePayload = true)
    {
        var resolvedPayload = includePayload
            ? payload ?? JsonSerializer.SerializeToElement(new RuntimeStartActivityCommandPayload(
                pinnedExecutable ?? NewIdentity(),
                executableNodeId,
                "actexec-1",
                RuntimeStartActivityCommandPayload.ScheduledActivityReason))
            : (JsonElement?)null;

        return new RuntimeSchedulerWorkItem(
            workItemId: "start-work",
            workflowExecutionId: "wfexec-1",
            commandId: "command-1",
            commandKind: commandKind,
            envelopeId: "envelope-1",
            idempotencyKey: "wfexec-1:start:actexec-1",
            enqueuedAt: _now,
            recordedAt: _now,
            sequence: 20,
            payload: resolvedPayload,
            commandMetadata: new Dictionary<string, string> { ["source"] = "test" },
            envelopeMetadata: new Dictionary<string, string> { ["transport"] = "in-process" });
    }

    private static ActivityExecutionState NewScheduledState() =>
        new(
            Execution: new ActivityExecution(
                ActivityExecutionId: "actexec-1",
                WorkflowExecutionId: "wfexec-1",
                ExecutableNodeId: "node-start",
                AuthoredActivityId: "authored-node-start",
                ActivityType: "test/activity",
                ActivityTypeVersion: "1.0.0"),
            Status: ActivityExecutionStatus.Scheduled,
            SubStatus: null,
            ScheduledAt: DateTimeOffset.UtcNow,
            StartedAt: null,
            CompletedAt: null,
            SchedulingActivityExecutionId: null,
            ParentActivityExecutionId: null,
            BranchId: null,
            IterationId: null,
            CallStackDepth: null,
            BookmarkIds: [],
            IncidentIds: [],
            FaultCount: 0,
            AggregateFaultCount: 0,
            Metadata: new Dictionary<string, string> { ["runtime.scheduleReason"] = "test" });

    private static WorkflowExecutable NewExecutable()
    {
        using var document = JsonDocument.Parse("""{"type":"test"}""");
        var start = NewNode("node-start", document.RootElement);
        var other = NewNode("node-other", document.RootElement);

        return new(
            identity: NewIdentity(),
            rootActivity: WithChildren(start, [other]),
            resumeTargets: new Dictionary<string, WorkflowExecutableResumeTarget>(),
            createdAt: DateTimeOffset.UtcNow,
            publishedAt: DateTimeOffset.UtcNow,
            compatibilityMetadata: new Dictionary<string, string>());
    }

    private static ExecutableNode WithChildren(ExecutableNode root, IReadOnlyCollection<ExecutableNode> children) =>
        new(
            executableNodeId: root.ExecutableNodeId,
            authoredActivityId: root.AuthoredActivityId,
            activityType: root.ActivityType,
            activityTypeVersion: root.ActivityTypeVersion,
            descriptorType: root.DescriptorType,
            descriptorPayload: root.DescriptorPayload,
            inputBindings: root.InputBindings,
            outputCaptures: root.OutputCaptures,
            metadata: root.Metadata,
            childSlots:
            [
                new ExecutableChildSlot(
                    ExecutableChildSlotNames.Activities,
                    children,
                    new Dictionary<string, string>
                    {
                        [ExecutableChildSlotMetadataKeys.StartActivityId] = children.First().ExecutableNodeId
                    })
            ]);

    private static ExecutableNode NewNode(string nodeId, JsonElement descriptorPayload) =>
        new(
            executableNodeId: nodeId,
            authoredActivityId: $"authored-{nodeId}",
            activityType: "test/activity",
            activityTypeVersion: "1.0.0",
            descriptorType: "test",
            descriptorPayload: descriptorPayload.Clone(),
            inputBindings: new Dictionary<string, RuntimeInputBinding>(),
            outputCaptures: new Dictionary<string, RuntimeOutputCapture>(),
            metadata: new Dictionary<string, string>());

    private static WorkflowExecutableIdentity NewIdentity() =>
        new("artifact-1", "definition-1", "version-1", "1.0.0", "sha256:test");

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
