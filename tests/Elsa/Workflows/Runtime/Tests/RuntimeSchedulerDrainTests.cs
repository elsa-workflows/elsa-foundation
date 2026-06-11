using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Constants;
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
                Assert.Contains(nameof(InvalidOperationException), second.Error);
                Assert.Contains("Fault requested for work-2.", second.Error);
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
    public async Task DrainAsync_DoesNotNoopInvokeActivityWorkWhenNoProviderMatches()
    {
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var drainer = new WorkflowSchedulerDrainer(queue, [new NoopWorkflowSchedulerWorkHandler()], new FixedTimeProvider(_now));
        await queue.EnqueueAsync(NewWorkItem(1, commandKind: WorkflowExecutionCommandKind.InvokeActivity));

        var result = await drainer.DrainAsync(new RuntimeSchedulerDrainRequest("wfexec-1"));

        var item = Assert.Single(result.Items);
        Assert.Equal(RuntimeSchedulerWorkItemResultStatus.Faulted, item.Status);
        Assert.Equal("FaultingMissingSchedulerWorkHandler", item.HandlerName);
        Assert.Contains("No workflow scheduler work handler accepted command kind 'InvokeActivity'", item.Error);
    }

    [Fact]
    public async Task DrainAsync_DispatchesCompleteActivityWorkThroughNamedHandler()
    {
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var activityStateStore = new InMemoryActivityExecutionStateStore();
        var drainer = new WorkflowSchedulerDrainer(
            queue,
            [new WorkflowCompleteActivitySchedulerWorkHandler(activityStateStore, queue, new FixedTimeProvider(_now)), new NoopWorkflowSchedulerWorkHandler()],
            new FixedTimeProvider(_now));
        await queue.EnqueueAsync(NewWorkItem(
            1,
            commandKind: WorkflowExecutionCommandKind.CompleteActivity,
            payload: JsonSerializer.SerializeToElement(NewCompleteActivityPayload())));

        var result = await drainer.DrainAsync(new RuntimeSchedulerDrainRequest("wfexec-1"));

        var item = Assert.Single(result.Items);
        Assert.Equal(RuntimeSchedulerWorkItemResultStatus.Completed, item.Status);
        Assert.Equal(WorkflowCompleteActivitySchedulerWorkHandler.HandlerName, item.HandlerName);
    }

    [Fact]
    public async Task DrainAsync_DispatchesCheckpointWorkThroughNamedHandler()
    {
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var drainer = new WorkflowSchedulerDrainer(
            queue,
            [new WorkflowCheckpointSchedulerWorkHandler(), new NoopWorkflowSchedulerWorkHandler()],
            new FixedTimeProvider(_now));
        await queue.EnqueueAsync(NewWorkItem(
            1,
            commandKind: WorkflowExecutionCommandKind.Checkpoint,
            payload: JsonSerializer.SerializeToElement(NewCheckpointPayload())));

        var result = await drainer.DrainAsync(new RuntimeSchedulerDrainRequest("wfexec-1"));

        var item = Assert.Single(result.Items);
        Assert.Equal(RuntimeSchedulerWorkItemResultStatus.Completed, item.Status);
        Assert.Equal(WorkflowCheckpointSchedulerWorkHandler.HandlerName, item.HandlerName);
    }

    [Fact]
    public async Task DrainAsync_FaultsMalformedCheckpointWorkThroughNamedHandler()
    {
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var drainer = new WorkflowSchedulerDrainer(
            queue,
            [new WorkflowCheckpointSchedulerWorkHandler(), new NoopWorkflowSchedulerWorkHandler()],
            new FixedTimeProvider(_now));
        using var document = JsonDocument.Parse("""{"checkpointName":" "}""");
        await queue.EnqueueAsync(NewWorkItem(
            1,
            commandKind: WorkflowExecutionCommandKind.Checkpoint,
            payload: document.RootElement.Clone()));

        var result = await drainer.DrainAsync(new RuntimeSchedulerDrainRequest("wfexec-1"));

        var item = Assert.Single(result.Items);
        Assert.Equal(RuntimeSchedulerWorkItemResultStatus.Faulted, item.Status);
        Assert.Equal(WorkflowCheckpointSchedulerWorkHandler.HandlerName, item.HandlerName);
        Assert.Contains("not a valid checkpoint payload", item.Error);
    }

    [Fact]
    public async Task CompleteActivityHandler_EnqueuesParentCompletionEvaluationWorkForCompletedChildWithParent()
    {
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var activityStateStore = new InMemoryActivityExecutionStateStore();
        await activityStateStore.SaveAsync(NewParentActivityState());
        var handler = new WorkflowCompleteActivitySchedulerWorkHandler(activityStateStore, queue, new FixedTimeProvider(_now));

        await handler.HandleAsync(NewWorkItem(
            1,
            commandKind: WorkflowExecutionCommandKind.CompleteActivity,
            payload: JsonSerializer.SerializeToElement(NewCompleteActivityPayload(parentActivityExecutionId: "actexec-parent"))));

        var parentWork = Assert.Single(await queue.ListAsync(new RuntimeSchedulerWorkQuery("wfexec-1")));
        Assert.Equal(WorkflowExecutionCommandKind.CompleteActivity, parentWork.CommandKind);
        Assert.Equal("work-1:parent:actexec-parent:child:actexec-1", parentWork.WorkItemId);
        Assert.Equal(2, parentWork.Sequence);
        var parentPayload = parentWork.Payload!.Value.Deserialize<RuntimeCompleteActivityCommandPayload>()!;
        Assert.Equal(SchedulerCompletionKind.ParentCompletionEvaluation, parentPayload.CompletionKind);
        Assert.Equal("actexec-parent", parentPayload.ActivityExecutionId);
        Assert.Equal("actexec-1", parentPayload.CompletedChildActivityExecutionId);
        Assert.Equal("node-parent", parentPayload.ExecutableNodeId);
        Assert.Equal("branch-a", parentPayload.BranchId);
        Assert.Empty(parentPayload.OutcomeNames);
    }

    [Fact]
    public async Task CompleteActivityHandler_DoesNotEnqueueParentEvaluationForRootCompletion()
    {
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var handler = new WorkflowCompleteActivitySchedulerWorkHandler(
            new InMemoryActivityExecutionStateStore(),
            queue,
            new FixedTimeProvider(_now));

        await handler.HandleAsync(NewWorkItem(
            1,
            commandKind: WorkflowExecutionCommandKind.CompleteActivity,
            payload: JsonSerializer.SerializeToElement(NewCompleteActivityPayload(parentActivityExecutionId: null))));

        Assert.Empty(await queue.ListAsync(new RuntimeSchedulerWorkQuery("wfexec-1")));
    }

    [Fact]
    public async Task CompleteActivityHandler_EnqueuesContinuationSchedulingWorkForParentCompletionEvaluation()
    {
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var handler = new WorkflowCompleteActivitySchedulerWorkHandler(
            new InMemoryActivityExecutionStateStore(),
            queue,
            new FixedTimeProvider(_now));

        await handler.HandleAsync(NewWorkItem(
            1,
            commandKind: WorkflowExecutionCommandKind.CompleteActivity,
            payload: JsonSerializer.SerializeToElement(NewCompleteActivityPayload(
                activityExecutionId: "actexec-parent",
                parentActivityExecutionId: "actexec-grandparent",
                branchId: "branch-parent",
                outcomeNames: ["ParentDone"],
                completionKind: SchedulerCompletionKind.ParentCompletionEvaluation,
                completedChildActivityExecutionId: "actexec-1"))));

        var continuationWork = Assert.Single(await queue.ListAsync(new RuntimeSchedulerWorkQuery("wfexec-1")));
        Assert.Equal(WorkflowExecutionCommandKind.CompleteActivity, continuationWork.CommandKind);
        Assert.Equal("work-1:continuation:actexec-parent", continuationWork.WorkItemId);
        Assert.Equal(2, continuationWork.Sequence);
        var continuationPayload = continuationWork.Payload!.Value.Deserialize<RuntimeCompleteActivityCommandPayload>()!;
        Assert.Equal(SchedulerCompletionKind.ContinuationScheduling, continuationPayload.CompletionKind);
        Assert.Equal(RuntimeCompleteActivityCommandPayload.ContinuationSchedulingReason, continuationPayload.Reason);
        Assert.Equal("actexec-parent", continuationPayload.ActivityExecutionId);
        Assert.Equal("actexec-grandparent", continuationPayload.ParentActivityExecutionId);
        Assert.Equal("branch-parent", continuationPayload.BranchId);
        Assert.Equal(["ParentDone"], continuationPayload.OutcomeNames);
        Assert.Null(continuationPayload.CompletedChildActivityExecutionId);
    }

    [Fact]
    public async Task DrainAsync_DrainsParentEvaluationContinuationSchedulingAndCheckpointInOrder()
    {
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var handler = new WorkflowCompleteActivitySchedulerWorkHandler(
            new InMemoryActivityExecutionStateStore(),
            queue,
            new FixedTimeProvider(_now));
        var drainer = new WorkflowSchedulerDrainer(queue, [handler, new WorkflowCheckpointSchedulerWorkHandler(), new NoopWorkflowSchedulerWorkHandler()], new FixedTimeProvider(_now));
        await queue.EnqueueAsync(NewWorkItem(
            1,
            commandKind: WorkflowExecutionCommandKind.CompleteActivity,
            payload: JsonSerializer.SerializeToElement(NewCompleteActivityPayload(
                activityExecutionId: "actexec-parent",
                parentActivityExecutionId: "actexec-grandparent",
                branchId: "branch-parent",
                outcomeNames: ["ParentDone"],
                completionKind: SchedulerCompletionKind.ParentCompletionEvaluation,
                completedChildActivityExecutionId: "actexec-1"))));

        var result = await drainer.DrainAsync(new RuntimeSchedulerDrainRequest("wfexec-1"));
        var remaining = await queue.ListAsync(new RuntimeSchedulerWorkQuery("wfexec-1"));

        Assert.Equal(3, result.DrainedCount);
        Assert.Empty(remaining);
        Assert.Collection(
            result.Items,
            first =>
            {
                Assert.Equal("work-1", first.WorkItemId);
                Assert.Equal(WorkflowCompleteActivitySchedulerWorkHandler.HandlerName, first.HandlerName);
            },
            second =>
            {
                Assert.Equal("work-1:continuation:actexec-parent", second.WorkItemId);
                Assert.Equal(WorkflowCompleteActivitySchedulerWorkHandler.HandlerName, second.HandlerName);
            },
            third =>
            {
                Assert.Equal("work-1:continuation:actexec-parent:checkpoint:ActivityCompleted:actexec-parent", third.WorkItemId);
                Assert.Equal(WorkflowCheckpointSchedulerWorkHandler.HandlerName, third.HandlerName);
            });
    }

    [Fact]
    public async Task CompleteActivityHandler_EnqueuesCheckpointWorkForContinuationScheduling()
    {
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var handler = new WorkflowCompleteActivitySchedulerWorkHandler(
            new InMemoryActivityExecutionStateStore(),
            queue,
            new FixedTimeProvider(_now));

        await handler.HandleAsync(NewWorkItem(
            1,
            commandKind: WorkflowExecutionCommandKind.CompleteActivity,
            payload: JsonSerializer.SerializeToElement(NewCompleteActivityPayload(
                activityExecutionId: "actexec-parent",
                parentActivityExecutionId: "actexec-grandparent",
                branchId: "branch-parent",
                outcomeNames: ["ParentDone"],
                completionKind: SchedulerCompletionKind.ContinuationScheduling))));

        var checkpointWork = Assert.Single(await queue.ListAsync(new RuntimeSchedulerWorkQuery("wfexec-1")));
        Assert.Equal(WorkflowExecutionCommandKind.Checkpoint, checkpointWork.CommandKind);
        Assert.Equal("work-1:checkpoint:ActivityCompleted:actexec-parent", checkpointWork.WorkItemId);
        Assert.Equal(2, checkpointWork.Sequence);
        var checkpointPayload = checkpointWork.Payload!.Value.Deserialize<RuntimeCheckpointCommandPayload>()!;
        Assert.Equal(RuntimeCheckpointNames.ActivityCompleted, checkpointPayload.CheckpointName);
        Assert.Equal(RuntimeCheckpointCommandPayload.ActivityCompletionPropagationReason, checkpointPayload.Reason);
        Assert.Equal(["actexec-parent"], checkpointPayload.ActivityExecutionIds);
    }

    [Fact]
    public async Task CompleteActivityHandler_FaultsWhenParentActivityStateIsMissing()
    {
        var handler = new WorkflowCompleteActivitySchedulerWorkHandler(
            new InMemoryActivityExecutionStateStore(),
            new InMemoryWorkflowSchedulerWorkQueue(),
            new FixedTimeProvider(_now));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => handler.HandleAsync(NewWorkItem(
            1,
            commandKind: WorkflowExecutionCommandKind.CompleteActivity,
            payload: JsonSerializer.SerializeToElement(NewCompleteActivityPayload(parentActivityExecutionId: "actexec-missing")))).AsTask());

        Assert.Contains("missing parent activity execution 'actexec-missing'", exception.Message);
    }

    [Fact]
    public async Task DrainAsync_UsesMarkedFallbackAfterCustomHandlers()
    {
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var customHandler = new RecordingSchedulerWorkHandler(canHandle: false);
        var fallbackHandler = new RecordingFallbackSchedulerWorkHandler();
        var drainer = new WorkflowSchedulerDrainer(queue, [customHandler, fallbackHandler], new FixedTimeProvider(_now));
        await queue.EnqueueAsync(NewWorkItem(1));

        var result = await drainer.DrainAsync(new RuntimeSchedulerDrainRequest("wfexec-1"));

        var item = Assert.Single(result.Items);
        Assert.Equal(RuntimeSchedulerWorkItemResultStatus.Completed, item.Status);
        Assert.Equal(fallbackHandler.Name, item.HandlerName);
        Assert.Equal(1, customHandler.CanHandleCallCount);
        Assert.Equal(["work-1"], fallbackHandler.WorkItemIds);
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

    private RuntimeSchedulerWorkItem NewWorkItem(
        int index,
        string workflowExecutionId = "wfexec-1",
        WorkflowExecutionCommandKind commandKind = WorkflowExecutionCommandKind.RunSchedulerWork,
        JsonElement? payload = null)
    {
        using var document = JsonDocument.Parse($$"""{"workItemId":"work-{{index}}"}""");
        return new(
            workItemId: $"work-{index}",
            workflowExecutionId: workflowExecutionId,
            commandId: $"command-{index}",
            commandKind: commandKind,
            envelopeId: $"envelope-{index}",
            idempotencyKey: $"{workflowExecutionId}:command-{index}",
            enqueuedAt: _now,
            recordedAt: _now,
            sequence: index,
            payload: payload ?? document.RootElement.Clone());
    }

    private static RuntimeCompleteActivityCommandPayload NewCompleteActivityPayload(
        string activityExecutionId = "actexec-1",
        string? parentActivityExecutionId = null,
        string? branchId = null,
        IReadOnlyCollection<string>? outcomeNames = null,
        SchedulerCompletionKind completionKind = SchedulerCompletionKind.ActivityCompleted,
        string? completedChildActivityExecutionId = null) =>
        new(
            pinnedExecutable: new WorkflowExecutableIdentity("artifact-1", "definition-1", "version-1", "1.0.0", "sha256:test"),
            executableNodeId: "node-start",
            activityExecutionId: activityExecutionId,
            parentActivityExecutionId: parentActivityExecutionId,
            branchId: branchId,
            outcomeNames: outcomeNames ?? ["Done"],
            reason: CompletionReason(completionKind),
            completionKind: completionKind,
            completedChildActivityExecutionId: completedChildActivityExecutionId);

    private static RuntimeCheckpointCommandPayload NewCheckpointPayload() =>
        new(
            pinnedExecutable: new WorkflowExecutableIdentity("artifact-1", "definition-1", "version-1", "1.0.0", "sha256:test"),
            checkpointName: RuntimeCheckpointNames.ActivityCompleted,
            activityExecutionIds: ["actexec-1"],
            reason: RuntimeCheckpointCommandPayload.ActivityCompletionPropagationReason);

    private static string CompletionReason(SchedulerCompletionKind completionKind) =>
        completionKind switch
        {
            SchedulerCompletionKind.ActivityCompleted => RuntimeCompleteActivityCommandPayload.ActivityInvocationCompletedReason,
            SchedulerCompletionKind.ParentCompletionEvaluation => RuntimeCompleteActivityCommandPayload.ParentCompletionEvaluationReason,
            SchedulerCompletionKind.ContinuationScheduling => RuntimeCompleteActivityCommandPayload.ContinuationSchedulingReason,
            _ => completionKind.ToString()
        };

    private ActivityExecutionState NewParentActivityState() =>
        new(
            Execution: new ActivityExecution(
                ActivityExecutionId: "actexec-parent",
                WorkflowExecutionId: "wfexec-1",
                ExecutableNodeId: "node-parent",
                AuthoredActivityId: "authored-node-parent",
                ActivityType: "test/parent",
                ActivityTypeVersion: "1.0.0"),
            Status: ActivityExecutionStatus.Running,
            SubStatus: null,
            ScheduledAt: _now.AddMinutes(-3),
            StartedAt: _now.AddMinutes(-2),
            CompletedAt: null,
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

    private sealed class RecordingFallbackSchedulerWorkHandler : IFallbackWorkflowSchedulerWorkHandler
    {
        public string Name => nameof(RecordingFallbackSchedulerWorkHandler);
        public List<string> WorkItemIds { get; } = [];

        public bool CanHandle(RuntimeSchedulerWorkItem workItem)
        {
            ArgumentNullException.ThrowIfNull(workItem);

            return true;
        }

        public ValueTask HandleAsync(RuntimeSchedulerWorkItem workItem, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            WorkItemIds.Add(workItem.WorkItemId);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
