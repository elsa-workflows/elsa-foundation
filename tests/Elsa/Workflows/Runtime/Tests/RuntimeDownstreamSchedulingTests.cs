using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class RuntimeDownstreamSchedulingTests
{
    private readonly DateTimeOffset _now = new(2026, 6, 11, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task CompleteActivityHandler_AttachesPostCommitSchedulerIntentsForMatchingOutgoingEdges()
    {
        var executableStore = new InMemoryWorkflowExecutableStore();
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var executable = NewExecutable(
            ["node-source", "node-next", "node-other"],
            [new ExecutableEdge("node-source", "Done", "node-next", "In"), new ExecutableEdge("node-source", "Other", "node-other", "In")]);
        await executableStore.SaveAsync(executable);
        var handler = NewCompleteHandler(queue, executableStore);

        await handler.HandleAsync(NewCompleteWorkItem(
            executable.Identity,
            outcomeNames: ["Done"],
            completionKind: SchedulerCompletionKind.ContinuationScheduling));

        var checkpointWork = Assert.Single(await queue.ListAsync(new RuntimeSchedulerWorkQuery("wfexec-1")));
        Assert.Equal(WorkflowExecutionCommandKind.Checkpoint, checkpointWork.CommandKind);
        var checkpointPayload = checkpointWork.Payload!.Value.Deserialize<RuntimeCheckpointCommandPayload>()!;
        var intent = Assert.Single(checkpointPayload.PostCommitIntents);
        Assert.Equal(RuntimePostCommitIntentKinds.EnqueueSchedulerWork, intent.Kind);
        Assert.Equal("actexec-source", intent.ActivityExecutionId);
        var downstreamWork = intent.Payload!.Value.Deserialize<RuntimeSchedulerWorkItem>()!;
        Assert.Equal(WorkflowExecutionCommandKind.ScheduleActivity, downstreamWork.CommandKind);
        Assert.Equal("work-1:schedule:1:Done:node-next", downstreamWork.WorkItemId);
        Assert.Equal(12, downstreamWork.Sequence);
        var schedulePayload = downstreamWork.Payload!.Value.Deserialize<RuntimeScheduleActivityCommandPayload>()!;
        Assert.Equal("node-next", schedulePayload.ExecutableNodeId);
        Assert.Equal("actexec-1", schedulePayload.ActivityExecutionId);
        Assert.Equal("actexec-source", schedulePayload.SchedulingActivityExecutionId);
        Assert.Equal(RuntimeScheduleActivityCommandPayload.ActivityCompletionReason, schedulePayload.Reason);
        Assert.Equal(executable.Identity, schedulePayload.PinnedExecutable);
    }

    [Fact]
    public async Task CompleteActivityHandler_DoesNotAttachSchedulerIntentsWhenNoOutcomeEdgesMatch()
    {
        var executableStore = new InMemoryWorkflowExecutableStore();
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var executable = NewExecutable(
            ["node-source", "node-next"],
            [new ExecutableEdge("node-source", "Done", "node-next", "In")]);
        await executableStore.SaveAsync(executable);
        var handler = NewCompleteHandler(queue, executableStore);

        await handler.HandleAsync(NewCompleteWorkItem(
            executable.Identity,
            outcomeNames: ["Other"],
            completionKind: SchedulerCompletionKind.ContinuationScheduling));

        var checkpointWork = Assert.Single(await queue.ListAsync(new RuntimeSchedulerWorkQuery("wfexec-1")));
        var checkpointPayload = checkpointWork.Payload!.Value.Deserialize<RuntimeCheckpointCommandPayload>()!;
        Assert.Empty(checkpointPayload.PostCommitIntents);
    }

    [Fact]
    public async Task CheckpointHandler_DispatchesSchedulerIntentAfterSuccessfulCommit()
    {
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var activityStateStore = new InMemoryActivityExecutionStateStore();
        var checkpointWriter = new InMemoryRuntimeCheckpointWriter();
        await activityStateStore.SaveAsync(NewCompletedActivityState());
        var handler = NewCheckpointHandler(activityStateStore, checkpointWriter, queue);
        var downstreamWork = NewScheduleWorkItem();

        await handler.HandleAsync(NewCheckpointWorkItem([NewSchedulerIntent(downstreamWork)]));

        Assert.Single(checkpointWriter.ListWrites());
        var queued = Assert.Single(await queue.ListAsync(new RuntimeSchedulerWorkQuery("wfexec-1")));
        Assert.Equal(downstreamWork.WorkItemId, queued.WorkItemId);
        Assert.Equal(WorkflowExecutionCommandKind.ScheduleActivity, queued.CommandKind);
    }

    [Fact]
    public async Task CheckpointHandler_DoesNotDispatchSchedulerIntentWhenCheckpointWriteFails()
    {
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var activityStateStore = new InMemoryActivityExecutionStateStore();
        await activityStateStore.SaveAsync(NewCompletedActivityState());
        var handler = NewCheckpointHandler(
            activityStateStore,
            new ThrowingCheckpointWriter(),
            queue);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.HandleAsync(NewCheckpointWorkItem([NewSchedulerIntent(NewScheduleWorkItem())])).AsTask());

        Assert.Empty(await queue.ListAsync(new RuntimeSchedulerWorkQuery("wfexec-1")));
    }

    [Fact]
    public void CompleteActivityHandler_RejectsHalfConfiguredDownstreamSchedulingDependencies()
    {
        var activityStateStore = new InMemoryActivityExecutionStateStore();
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var executableStore = new InMemoryWorkflowExecutableStore();

        Assert.Throws<ArgumentException>(() =>
            new WorkflowCompleteActivitySchedulerWorkHandler(
                activityStateStore,
                queue,
                executableStore,
                idGenerator: null,
                new FixedTimeProvider(_now)));

        Assert.Throws<ArgumentException>(() =>
            new WorkflowCompleteActivitySchedulerWorkHandler(
                activityStateStore,
                queue,
                workflowExecutableStore: null,
                new IncrementingRuntimeExecutionIdGenerator(),
                new FixedTimeProvider(_now)));
    }

    private WorkflowCompleteActivitySchedulerWorkHandler NewCompleteHandler(
        IWorkflowSchedulerWorkQueue queue,
        IWorkflowExecutableStore executableStore) =>
        new(
            new InMemoryActivityExecutionStateStore(),
            queue,
            executableStore,
            new IncrementingRuntimeExecutionIdGenerator(),
            new FixedTimeProvider(_now));

    private WorkflowCheckpointSchedulerWorkHandler NewCheckpointHandler(
        IActivityExecutionStateStore activityStateStore,
        IRuntimeCheckpointWriter checkpointWriter,
        IWorkflowSchedulerWorkQueue queue) =>
        new(
            activityStateStore,
            new RuntimeCheckpointCommitter(
                new ImmediateRuntimeCheckpointPersistencePolicy(),
                checkpointWriter,
                new RuntimeSchedulerPostCommitIntentDispatcher(queue)),
            new FixedTimeProvider(_now));

    private RuntimeSchedulerWorkItem NewCompleteWorkItem(
        WorkflowExecutableIdentity pinnedExecutable,
        IReadOnlyCollection<string> outcomeNames,
        SchedulerCompletionKind completionKind) =>
        new(
            workItemId: "work-1",
            workflowExecutionId: "wfexec-1",
            commandId: "command-1",
            commandKind: WorkflowExecutionCommandKind.CompleteActivity,
            envelopeId: "envelope-1",
            idempotencyKey: "wfexec-1:complete:actexec-source",
            enqueuedAt: _now,
            recordedAt: _now,
            sequence: 10,
            payload: JsonSerializer.SerializeToElement(new RuntimeCompleteActivityCommandPayload(
                pinnedExecutable,
                "node-source",
                "actexec-source",
                parentActivityExecutionId: null,
                branchId: "branch-a",
                outcomeNames: outcomeNames,
                reason: RuntimeCompleteActivityCommandPayload.ContinuationSchedulingReason,
                completionKind: completionKind)),
            commandMetadata: new Dictionary<string, string> { ["source"] = "test" },
            envelopeMetadata: new Dictionary<string, string> { ["transport"] = "in-process" });

    private RuntimeSchedulerWorkItem NewCheckpointWorkItem(IReadOnlyCollection<RuntimePostCommitIntent> postCommitIntents) =>
        new(
            workItemId: "checkpoint-work",
            workflowExecutionId: "wfexec-1",
            commandId: "command-1",
            commandKind: WorkflowExecutionCommandKind.Checkpoint,
            envelopeId: "envelope-1",
            idempotencyKey: "wfexec-1:checkpoint",
            enqueuedAt: _now,
            recordedAt: _now,
            sequence: 10,
            payload: JsonSerializer.SerializeToElement(new RuntimeCheckpointCommandPayload(
                NewIdentity(),
                RuntimeCheckpointNames.ActivityCompleted,
                ["actexec-source"],
                RuntimeCheckpointCommandPayload.ActivityCompletionPropagationReason,
                postCommitIntents)),
            commandMetadata: new Dictionary<string, string>(),
            envelopeMetadata: new Dictionary<string, string>());

    private RuntimePostCommitIntent NewSchedulerIntent(RuntimeSchedulerWorkItem workItem) =>
        new(
            intentId: "intent-schedule-1",
            workflowExecutionId: "wfexec-1",
            kind: RuntimePostCommitIntentKinds.EnqueueSchedulerWork,
            recordedAt: _now,
            activityExecutionId: "actexec-source",
            idempotencyKey: workItem.IdempotencyKey,
            payload: JsonSerializer.SerializeToElement(workItem),
            metadata: new Dictionary<string, string>());

    private RuntimeSchedulerWorkItem NewScheduleWorkItem() =>
        new(
            workItemId: "schedule-work",
            workflowExecutionId: "wfexec-1",
            commandId: "command-schedule",
            commandKind: WorkflowExecutionCommandKind.ScheduleActivity,
            envelopeId: "envelope-1",
            idempotencyKey: "wfexec-1:schedule:node-next",
            enqueuedAt: _now,
            recordedAt: _now,
            sequence: 12,
            payload: JsonSerializer.SerializeToElement(new RuntimeScheduleActivityCommandPayload(
                NewIdentity(),
                "node-next",
                "actexec-next",
                RuntimeScheduleActivityCommandPayload.ActivityCompletionReason,
                "actexec-source")),
            commandMetadata: new Dictionary<string, string>(),
            envelopeMetadata: new Dictionary<string, string>());

    private ActivityExecutionState NewCompletedActivityState() =>
        new(
            Execution: new ActivityExecution(
                ActivityExecutionId: "actexec-source",
                WorkflowExecutionId: "wfexec-1",
                ExecutableNodeId: "node-source",
                AuthoredActivityId: "authored-node-source",
                ActivityType: "test/source",
                ActivityTypeVersion: "1.0.0"),
            Status: ActivityExecutionStatus.Completed,
            SubStatus: null,
            ScheduledAt: _now.AddMinutes(-3),
            StartedAt: _now.AddMinutes(-2),
            CompletedAt: _now,
            SchedulingActivityExecutionId: null,
            ParentActivityExecutionId: null,
            BranchId: "branch-a",
            IterationId: null,
            CallStackDepth: 0,
            BookmarkIds: [],
            IncidentIds: [],
            FaultCount: 0,
            AggregateFaultCount: 0,
            Metadata: new Dictionary<string, string>());

    private static WorkflowExecutable NewExecutable(IReadOnlyCollection<string> nodeIds, IReadOnlyCollection<ExecutableEdge> edges) =>
        new(
            identity: NewIdentity(),
            nodes: nodeIds.Select(NewNode).ToArray(),
            edges: edges,
            startNodeIds: [],
            resumeTargets: new Dictionary<string, WorkflowExecutableResumeTarget>(),
            createdAt: DateTimeOffset.UtcNow,
            publishedAt: DateTimeOffset.UtcNow,
            compatibilityMetadata: new Dictionary<string, string>());

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

    private static WorkflowExecutableIdentity NewIdentity() =>
        new("artifact-1", "definition-1", "version-1", "1.0.0", "sha256:test");

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class IncrementingRuntimeExecutionIdGenerator : IRuntimeExecutionIdGenerator
    {
        private int _activityExecutionIndex;

        public string NewWorkflowExecutionId() => "wfexec-unused";

        public string NewWorkflowExecutionCommandId() => "command-unused";

        public string NewWorkflowExecutionCommandEnvelopeId() => "envelope-unused";

        public string NewActivityExecutionId() => $"actexec-{++_activityExecutionIndex}";
    }

    private sealed class ThrowingCheckpointWriter : IRuntimeCheckpointWriter
    {
        public ValueTask WriteAsync(RuntimeCheckpointCommit commit, RuntimeCheckpointPersistenceDecision decision, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("checkpoint write failed");
    }
}
