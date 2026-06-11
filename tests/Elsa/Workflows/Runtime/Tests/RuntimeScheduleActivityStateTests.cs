using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class RuntimeScheduleActivityStateTests
{
    private readonly DateTimeOffset _now = new(2026, 6, 11, 12, 0, 0, TimeSpan.Zero);
    private readonly InMemoryWorkflowExecutableStore _executableStore = new();
    private readonly InMemoryActivityExecutionStateStore _activityStateStore = new();

    [Fact]
    public async Task HandleAsync_RecordsScheduledActivityExecutionState()
    {
        var executable = NewExecutable();
        await _executableStore.SaveAsync(executable);
        var handler = NewHandler();

        await handler.HandleAsync(NewScheduleWorkItem(executable.Identity));

        var state = Assert.Single(await _activityStateStore.ListAsync("wfexec-1"));
        Assert.Equal("actexec-1", state.Execution.ActivityExecutionId);
        Assert.Equal("wfexec-1", state.Execution.WorkflowExecutionId);
        Assert.Equal("node-start", state.Execution.ExecutableNodeId);
        Assert.Equal("authored-node-start", state.Execution.AuthoredActivityId);
        Assert.Equal("test/activity", state.Execution.ActivityType);
        Assert.Equal("1.0.0", state.Execution.ActivityTypeVersion);
        Assert.Equal(ActivityExecutionStatus.Scheduled, state.Status);
        Assert.Equal(_now, state.ScheduledAt);
        Assert.Null(state.StartedAt);
        Assert.Null(state.CompletedAt);
        Assert.Equal("actexec-parent", state.SchedulingActivityExecutionId);
        Assert.Equal(RuntimeScheduleActivityCommandPayload.WorkflowStartReason, state.Metadata["runtime.scheduleReason"]);
        Assert.Equal("schedule-work", state.Metadata["runtime.schedulerWorkItemId"]);
        Assert.Equal("artifact-1", state.Metadata["runtime.pinnedArtifactId"]);
    }

    [Fact]
    public async Task HandleAsync_DoesNotOverwriteExistingActivityExecutionStateForRepeatedScheduleWork()
    {
        var executable = NewExecutable();
        await _executableStore.SaveAsync(executable);
        var handler = NewHandler();
        var workItem = NewScheduleWorkItem(executable.Identity);

        await handler.HandleAsync(workItem);
        var scheduled = Assert.Single(await _activityStateStore.ListAsync("wfexec-1"));
        await _activityStateStore.SaveAsync(scheduled with { Status = ActivityExecutionStatus.Running });
        await handler.HandleAsync(workItem);

        var state = Assert.Single(await _activityStateStore.ListAsync("wfexec-1"));
        Assert.Equal(ActivityExecutionStatus.Running, state.Status);
    }

    [Fact]
    public async Task ActivityExecutionStateStore_SaveAsync_UpdatesExistingState()
    {
        var executable = NewExecutable();
        await _executableStore.SaveAsync(executable);
        var handler = NewHandler();
        await handler.HandleAsync(NewScheduleWorkItem(executable.Identity));
        var scheduled = Assert.Single(await _activityStateStore.ListAsync("wfexec-1"));

        var saved = await _activityStateStore.SaveAsync(scheduled with { Status = ActivityExecutionStatus.Running });

        Assert.Equal(ActivityExecutionStatus.Running, saved.Status);
        var state = await _activityStateStore.FindAsync("wfexec-1", "actexec-1");
        Assert.NotNull(state);
        Assert.Equal(ActivityExecutionStatus.Running, state.Status);
    }

    [Fact]
    public async Task HandleAsync_IgnoresSourceReferenceWhenCheckingPinnedExecutableSnapshot()
    {
        var executable = NewExecutable();
        await _executableStore.SaveAsync(executable);
        var pinned = executable.Identity with
        {
            Source = new WorkflowExecutableSourceReference("WorkflowDefinitionVersion", "version-1", "1.0.0")
        };
        var handler = NewHandler();

        await handler.HandleAsync(NewScheduleWorkItem(pinned));

        Assert.Single(await _activityStateStore.ListAsync("wfexec-1"));
    }

    [Fact]
    public async Task HandleAsync_RejectsMissingPayloadBeforeRecordingState()
    {
        var handler = NewHandler();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => handler.HandleAsync(NewScheduleWorkItem(includePayload: false)).AsTask());

        Assert.Contains("requires a schedule activity payload", exception.Message);
        Assert.Empty(await _activityStateStore.ListAsync("wfexec-1"));
    }

    [Fact]
    public async Task HandleAsync_RejectsMalformedPayloadBeforeRecordingState()
    {
        using var document = JsonDocument.Parse("[]");
        var handler = NewHandler();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => handler.HandleAsync(NewScheduleWorkItem(payload: document.RootElement.Clone())).AsTask());

        Assert.Contains("not a valid schedule activity payload", exception.Message);
        Assert.Empty(await _activityStateStore.ListAsync("wfexec-1"));
    }

    [Fact]
    public async Task HandleAsync_RejectsPinnedExecutableMismatchBeforeRecordingState()
    {
        var executable = NewExecutable();
        await _executableStore.SaveAsync(executable);
        var pinned = executable.Identity with { ArtifactHash = "sha256:pinned" };
        var handler = NewHandler();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => handler.HandleAsync(NewScheduleWorkItem(pinned)).AsTask());

        Assert.Contains("pinned executable artifact", exception.Message);
        Assert.Contains("definition-1/version-1", exception.Message);
        Assert.Empty(await _activityStateStore.ListAsync("wfexec-1"));
    }

    [Fact]
    public async Task HandleAsync_RejectsMissingExecutableNodeBeforeRecordingState()
    {
        var executable = NewExecutable();
        await _executableStore.SaveAsync(executable);
        var handler = NewHandler();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => handler.HandleAsync(NewScheduleWorkItem(executable.Identity, executableNodeId: "node-missing")).AsTask());

        Assert.Contains("node-missing", exception.Message);
        Assert.Empty(await _activityStateStore.ListAsync("wfexec-1"));
    }

    [Fact]
    public void CanHandle_AcceptsOnlyScheduleActivityWork()
    {
        var handler = NewHandler();

        Assert.True(handler.CanHandle(NewScheduleWorkItem(NewIdentity())));
        Assert.False(handler.CanHandle(NewScheduleWorkItem(NewIdentity(), commandKind: WorkflowExecutionCommandKind.Start)));
    }

    private WorkflowScheduleActivitySchedulerWorkHandler NewHandler() =>
        new(_executableStore, _activityStateStore, new FixedTimeProvider(_now));

    private RuntimeSchedulerWorkItem NewScheduleWorkItem(
        WorkflowExecutableIdentity? pinnedExecutable = null,
        WorkflowExecutionCommandKind commandKind = WorkflowExecutionCommandKind.ScheduleActivity,
        string executableNodeId = "node-start",
        JsonElement? payload = null,
        bool includePayload = true)
    {
        var resolvedPayload = includePayload
            ? payload ?? JsonSerializer.SerializeToElement(new RuntimeScheduleActivityCommandPayload(
                pinnedExecutable ?? NewIdentity(),
                executableNodeId,
                "actexec-1",
                RuntimeScheduleActivityCommandPayload.WorkflowStartReason,
                "actexec-parent"))
            : (JsonElement?)null;

        return new RuntimeSchedulerWorkItem(
            workItemId: "schedule-work",
            workflowExecutionId: "wfexec-1",
            commandId: "command-1",
            commandKind: commandKind,
            envelopeId: "envelope-1",
            idempotencyKey: "wfexec-1:schedule:node-start",
            enqueuedAt: _now,
            recordedAt: _now,
            sequence: 10,
            payload: resolvedPayload,
            commandMetadata: new Dictionary<string, string> { ["source"] = "test" },
            envelopeMetadata: new Dictionary<string, string> { ["transport"] = "in-process" });
    }

    private static WorkflowExecutable NewExecutable()
    {
        using var document = JsonDocument.Parse("""{"type":"test"}""");
        var node = new ExecutableNode(
            executableNodeId: "node-start",
            authoredActivityId: "authored-node-start",
            activityType: "test/activity",
            activityTypeVersion: "1.0.0",
            descriptorType: "test",
            descriptorPayload: document.RootElement.Clone(),
            inputBindings: new Dictionary<string, RuntimeInputBinding>(),
            outputCaptures: new Dictionary<string, RuntimeOutputCapture>(),
            metadata: new Dictionary<string, string>());

        return new(
            identity: NewIdentity(),
            nodes: [node],
            edges: [],
            startNodeIds: ["node-start"],
            resumeTargets: new Dictionary<string, WorkflowExecutableResumeTarget>(),
            createdAt: DateTimeOffset.UtcNow,
            publishedAt: DateTimeOffset.UtcNow,
            compatibilityMetadata: new Dictionary<string, string>());
    }

    private static WorkflowExecutableIdentity NewIdentity() =>
        new("artifact-1", "definition-1", "version-1", "1.0.0", "sha256:test");

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
