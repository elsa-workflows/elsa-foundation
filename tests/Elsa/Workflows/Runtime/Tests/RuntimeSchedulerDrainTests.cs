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
    public async Task DrainAsync_StopsBeforeDequeuingPauseBlockedWork()
    {
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var handler = new RecordingSchedulerWorkHandler();
        var pauseGate = new RecordingWorkflowSchedulerPauseGate(BlockedDecision(RuntimePauseBoundary.BeforeActivityExecutionStart));
        var drainer = new WorkflowSchedulerDrainer(queue, [handler, new NoopWorkflowSchedulerWorkHandler()], new FixedTimeProvider(_now), pauseGate);
        await queue.EnqueueAsync(NewStartActivityWorkItem(1));

        var result = await drainer.DrainAsync(new RuntimeSchedulerDrainRequest("wfexec-1"));
        var remaining = await queue.ListAsync(new RuntimeSchedulerWorkQuery("wfexec-1"));

        Assert.Equal(0, result.DrainedCount);
        Assert.False(result.StoppedOnFault);
        Assert.True(result.StoppedOnPause);
        Assert.Empty(handler.WorkItemIds);
        Assert.Collection(pauseGate.WorkItemIds, id => Assert.Equal("work-1", id));
        Assert.Collection(remaining, item => Assert.Equal("work-1", item.WorkItemId));
        var itemResult = Assert.Single(result.Items);
        Assert.Equal(RuntimeSchedulerWorkItemResultStatus.Paused, itemResult.Status);
        Assert.Equal(nameof(WorkflowSchedulerPauseGate), itemResult.HandlerName);
        Assert.Contains("pause-1", itemResult.Error);
        Assert.Contains(nameof(RuntimePauseBoundary.BeforeActivityExecutionStart), itemResult.Error);
    }

    [Fact]
    public async Task DrainAsync_DispatchesQueuedWorkAfterPauseHoldIsReleased()
    {
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var store = new InMemoryControlPlaneStateStore();
        var handler = new RecordingSchedulerWorkHandler();
        var pauseGate = new WorkflowSchedulerPauseGate(new RuntimePauseDecisionProvider(store), new FixedTimeProvider(_now));
        var drainer = new WorkflowSchedulerDrainer(queue, [handler, new NoopWorkflowSchedulerWorkHandler()], new FixedTimeProvider(_now), pauseGate);
        await store.SaveAsync(new ControlPlaneState(
            controlPlaneStateId: "control-1",
            workflowExecutionId: "wfexec-1",
            activeHolds: [ControlPlaneHold.ForWorkflowExecution("pause-1", "wfexec-1", _now, "operator", "Paused for maintenance.")]));
        await queue.EnqueueAsync(NewStartActivityWorkItem(1));

        var pausedResult = await drainer.DrainAsync(new RuntimeSchedulerDrainRequest("wfexec-1"));
        await store.SaveAsync(new ControlPlaneState(
            controlPlaneStateId: "control-1",
            workflowExecutionId: "wfexec-1",
            releasedHolds:
            [
                new ControlPlaneHold(
                    holdId: "pause-1",
                    scope: ControlPlaneHoldScope.WorkflowExecution,
                    status: ControlPlaneHoldStatus.Released,
                    requestedAt: _now,
                    requestedBy: "operator",
                    reason: "Paused for maintenance.",
                    workflowExecutionId: "wfexec-1",
                    releasedAt: _now.AddMinutes(1),
                    releasedBy: "operator")
            ]));
        var resumedResult = await drainer.DrainAsync(new RuntimeSchedulerDrainRequest("wfexec-1"));
        var remaining = await queue.ListAsync(new RuntimeSchedulerWorkQuery("wfexec-1"));

        Assert.True(pausedResult.StoppedOnPause);
        Assert.False(resumedResult.StoppedOnPause);
        Assert.Equal(1, resumedResult.DrainedCount);
        Assert.Collection(handler.WorkItemIds, id => Assert.Equal("work-1", id));
        Assert.Empty(remaining);
        Assert.Equal(RuntimeSchedulerWorkItemResultStatus.Completed, Assert.Single(resumedResult.Items).Status);
    }

    [Fact]
    public async Task DrainAsync_EvaluatesGeneratedEventBoundaryBeforeDequeue()
    {
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var pauseGate = new RecordingWorkflowSchedulerPauseGate(BlockedDecision(RuntimePauseBoundary.BeforeGeneratorEmission));
        var drainer = new WorkflowSchedulerDrainer(
            queue,
            [new MissingGeneratedEventSchedulerWorkHandler(), new NoopWorkflowSchedulerWorkHandler()],
            new FixedTimeProvider(_now),
            pauseGate);
        await queue.EnqueueAsync(NewGeneratedEventWorkItem(1));

        var result = await drainer.DrainAsync(new RuntimeSchedulerDrainRequest("wfexec-1"));
        var remaining = await queue.ListAsync(new RuntimeSchedulerWorkQuery("wfexec-1"));

        Assert.True(result.StoppedOnPause);
        Assert.Equal(0, result.DrainedCount);
        Assert.Collection(remaining, item => Assert.Equal("work-1", item.WorkItemId));
        Assert.Collection(pauseGate.WorkItemIds, id => Assert.Equal("work-1", id));
        var itemResult = Assert.Single(result.Items);
        Assert.Equal(RuntimeSchedulerWorkItemResultStatus.Paused, itemResult.Status);
        Assert.Equal(WorkflowExecutionCommandKind.GeneratedEvent, itemResult.CommandKind);
    }

    [Fact]
    public async Task WorkflowSchedulerPauseGate_MapsSchedulerWorkToPauseDecisionRequests()
    {
        var provider = new RecordingRuntimePauseDecisionProvider();
        var pauseGate = new WorkflowSchedulerPauseGate(provider, new FixedTimeProvider(_now));

        await pauseGate.EvaluateAsync(NewStartActivityWorkItem(1));
        await pauseGate.EvaluateAsync(NewInvokeActivityWorkItem(2));
        await pauseGate.EvaluateAsync(NewGeneratedEventWorkItem(3));
        var ignoredDecision = await pauseGate.EvaluateAsync(NewWorkItem(4));

        Assert.Null(ignoredDecision);
        Assert.Collection(
            provider.Requests,
            startRequest =>
            {
                Assert.Equal(RuntimePauseBoundary.BeforeActivityExecutionStart, startRequest.Boundary);
                Assert.Equal(_now, startRequest.EvaluatedAt);
                Assert.Equal("wfexec-1", startRequest.WorkflowExecutionId);
                Assert.Equal("actexec-1", startRequest.ActivityExecutionId);
                Assert.Null(startRequest.GeneratorId);
                Assert.Equal("work-1", startRequest.Metadata["runtime.schedulerWorkItemId"]);
                Assert.Equal(nameof(WorkflowExecutionCommandKind.StartActivity), startRequest.Metadata["runtime.schedulerCommandKind"]);
            },
            invokeRequest =>
            {
                Assert.Equal(RuntimePauseBoundary.BeforeActivityExecutionStart, invokeRequest.Boundary);
                Assert.Equal("wfexec-1", invokeRequest.WorkflowExecutionId);
                Assert.Equal("actexec-2", invokeRequest.ActivityExecutionId);
                Assert.Null(invokeRequest.GeneratorId);
                Assert.Equal("work-2", invokeRequest.Metadata["runtime.schedulerWorkItemId"]);
                Assert.Equal(nameof(WorkflowExecutionCommandKind.InvokeActivity), invokeRequest.Metadata["runtime.schedulerCommandKind"]);
            },
            generatedEventRequest =>
            {
                Assert.Equal(RuntimePauseBoundary.BeforeGeneratorEmission, generatedEventRequest.Boundary);
                Assert.Equal("wfexec-1", generatedEventRequest.WorkflowExecutionId);
                Assert.Equal("actexec-generator", generatedEventRequest.ActivityExecutionId);
                Assert.Equal("generator-3", generatedEventRequest.GeneratorId);
                Assert.Equal("work-3", generatedEventRequest.Metadata["runtime.schedulerWorkItemId"]);
                Assert.Equal(nameof(WorkflowExecutionCommandKind.GeneratedEvent), generatedEventRequest.Metadata["runtime.schedulerCommandKind"]);
            });
    }

    [Fact]
    public async Task WorkflowSchedulerPauseGate_ReadsCaseInsensitiveSchedulerPayloads()
    {
        var provider = new RecordingRuntimePauseDecisionProvider();
        var pauseGate = new WorkflowSchedulerPauseGate(provider, new FixedTimeProvider(_now));
        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        await pauseGate.EvaluateAsync(NewStartActivityWorkItem(7, jsonOptions));

        var request = Assert.Single(provider.Requests);
        Assert.Equal(RuntimePauseBoundary.BeforeActivityExecutionStart, request.Boundary);
        Assert.Equal("wfexec-1", request.WorkflowExecutionId);
        Assert.Equal("actexec-7", request.ActivityExecutionId);
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
    public async Task DrainAsync_DoesNotNoopGeneratedEventWorkWhenNoProviderMatches()
    {
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var drainer = new WorkflowSchedulerDrainer(
            queue,
            [new MissingGeneratedEventSchedulerWorkHandler(), new NoopWorkflowSchedulerWorkHandler()],
            new FixedTimeProvider(_now));
        await queue.EnqueueAsync(NewWorkItem(1, commandKind: WorkflowExecutionCommandKind.GeneratedEvent));

        var result = await drainer.DrainAsync(new RuntimeSchedulerDrainRequest("wfexec-1"));

        var item = Assert.Single(result.Items);
        Assert.Equal(RuntimeSchedulerWorkItemResultStatus.Faulted, item.Status);
        Assert.Equal(MissingGeneratedEventSchedulerWorkHandler.HandlerName, item.HandlerName);
        Assert.Contains("no generated-event provider", item.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DrainAsync_DoesNotNoopBookmarkResumeWorkWhenNoProviderMatches()
    {
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var drainer = new WorkflowSchedulerDrainer(
            queue,
            [new MissingBookmarkResumeSchedulerWorkHandler(), new NoopWorkflowSchedulerWorkHandler()],
            new FixedTimeProvider(_now));
        await queue.EnqueueAsync(NewWorkItem(1, commandKind: WorkflowExecutionCommandKind.ResumeBookmark));

        var result = await drainer.DrainAsync(new RuntimeSchedulerDrainRequest("wfexec-1"));

        var item = Assert.Single(result.Items);
        Assert.Equal(RuntimeSchedulerWorkItemResultStatus.Faulted, item.Status);
        Assert.Equal(MissingBookmarkResumeSchedulerWorkHandler.HandlerName, item.HandlerName);
        Assert.Contains("no bookmark resume provider", item.Error, StringComparison.OrdinalIgnoreCase);
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

        var result = await drainer.DrainAsync(new RuntimeSchedulerDrainRequest("wfexec-1", maxWorkItems: 1));

        var item = Assert.Single(result.Items);
        Assert.Equal(RuntimeSchedulerWorkItemResultStatus.Completed, item.Status);
        Assert.Equal(WorkflowCompleteActivitySchedulerWorkHandler.HandlerName, item.HandlerName);
    }

    [Fact]
    public async Task DrainAsync_DispatchesCheckpointWorkThroughNamedHandler()
    {
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var activityStateStore = new InMemoryActivityExecutionStateStore();
        var checkpointWriter = new InMemoryRuntimeCheckpointCommitStore();
        await activityStateStore.SaveAsync(NewActivityState("actexec-1", ActivityExecutionStatus.Completed));
        var drainer = new WorkflowSchedulerDrainer(
            queue,
            [NewCheckpointHandler(activityStateStore, checkpointWriter), new NoopWorkflowSchedulerWorkHandler()],
            new FixedTimeProvider(_now));
        await queue.EnqueueAsync(NewWorkItem(
            1,
            commandKind: WorkflowExecutionCommandKind.Checkpoint,
            payload: JsonSerializer.SerializeToElement(NewCheckpointPayload())));

        var result = await drainer.DrainAsync(new RuntimeSchedulerDrainRequest("wfexec-1"));

        var item = Assert.Single(result.Items);
        Assert.Equal(RuntimeSchedulerWorkItemResultStatus.Completed, item.Status);
        Assert.Equal(WorkflowCheckpointSchedulerWorkHandler.HandlerName, item.HandlerName);
        var write = Assert.Single(checkpointWriter.ListCommits());
        Assert.Equal(RuntimeCheckpointPersistenceMode.Immediate, write.Decision.Mode);
        Assert.Equal("commit:work-1", write.Commit.CommitId);
        Assert.Equal("checkpoint:work-1", write.Commit.Checkpoint.CheckpointId);
        Assert.Equal(RuntimeCheckpointNames.ActivityCompleted, write.Commit.Checkpoint.Name);
        Assert.Equal(_now, write.Commit.Checkpoint.OccurredAt);
        Assert.Equal(["actexec-1"], write.Commit.Checkpoint.ActivityExecutionIds);
        var activityChange = Assert.Single(write.Commit.StateChanges.ActivityExecutions);
        Assert.Equal("actexec-1", activityChange.StateId);
        Assert.Equal(RuntimeStateChangeOperation.Upsert, activityChange.Operation);
        Assert.Equal(ActivityExecutionStatus.Completed, activityChange.State.Status);
        Assert.Null(write.Commit.StateChanges.WorkflowExecution);
        Assert.Null(write.Commit.StateChanges.Scheduler);
        Assert.Empty(write.Commit.StateChanges.Bookmarks);
        Assert.Empty(write.Commit.StateChanges.DurableValues);
        Assert.Empty(write.Commit.StateChanges.Incidents);
        Assert.Empty(write.Commit.StateChanges.Operational);
    }

    [Fact]
    public async Task DrainAsync_FaultsMalformedCheckpointWorkThroughNamedHandler()
    {
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var drainer = new WorkflowSchedulerDrainer(
            queue,
            [NewCheckpointHandler(new InMemoryActivityExecutionStateStore(), new InMemoryRuntimeCheckpointCommitStore()), new NoopWorkflowSchedulerWorkHandler()],
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
    public async Task DrainAsync_FaultsCheckpointWorkWhenReferencedActivityStateIsMissing()
    {
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var checkpointWriter = new InMemoryRuntimeCheckpointCommitStore();
        var drainer = new WorkflowSchedulerDrainer(
            queue,
            [NewCheckpointHandler(new InMemoryActivityExecutionStateStore(), checkpointWriter), new NoopWorkflowSchedulerWorkHandler()],
            new FixedTimeProvider(_now));
        await queue.EnqueueAsync(NewWorkItem(
            1,
            commandKind: WorkflowExecutionCommandKind.Checkpoint,
            payload: JsonSerializer.SerializeToElement(NewCheckpointPayload())));

        var result = await drainer.DrainAsync(new RuntimeSchedulerDrainRequest("wfexec-1"));

        var item = Assert.Single(result.Items);
        Assert.Equal(RuntimeSchedulerWorkItemResultStatus.Faulted, item.Status);
        Assert.Equal(WorkflowCheckpointSchedulerWorkHandler.HandlerName, item.HandlerName);
        Assert.Contains("missing activity execution 'actexec-1'", item.Error);
        Assert.Empty(checkpointWriter.ListCommits());
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
        Assert.Equal(["Done"], parentPayload.OutcomeNames);
    }

    [Fact]
    public async Task CompleteActivityHandler_EnqueuesContinuationSchedulingForRootCompletion()
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

        var continuationWork = Assert.Single(await queue.ListAsync(new RuntimeSchedulerWorkQuery("wfexec-1")));
        Assert.Equal(WorkflowExecutionCommandKind.CompleteActivity, continuationWork.CommandKind);
        Assert.Equal("work-1:continuation:actexec-1", continuationWork.WorkItemId);
        Assert.Equal(2, continuationWork.Sequence);
        var continuationPayload = continuationWork.Payload!.Value.Deserialize<RuntimeCompleteActivityCommandPayload>()!;
        Assert.Equal(SchedulerCompletionKind.ContinuationScheduling, continuationPayload.CompletionKind);
        Assert.Equal(RuntimeCompleteActivityCommandPayload.ContinuationSchedulingReason, continuationPayload.Reason);
        Assert.Equal("actexec-1", continuationPayload.ActivityExecutionId);
        Assert.Null(continuationPayload.ParentActivityExecutionId);
        Assert.Equal(["Done"], continuationPayload.OutcomeNames);
        Assert.Null(continuationPayload.CompletedChildActivityExecutionId);
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
    public async Task DrainAsync_LeavesParentEvaluationToFallbackWhenActivityRuntimeHandlerIsAbsent()
    {
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var activityStateStore = new InMemoryActivityExecutionStateStore();
        var checkpointWriter = new InMemoryRuntimeCheckpointCommitStore();
        var executableStore = new InMemoryWorkflowExecutableStore();
        await executableStore.SaveAsync(NewExecutable(["node-start", "node-next"]));
        await activityStateStore.SaveAsync(NewActivityState("actexec-parent", ActivityExecutionStatus.Completed));
        var handler = new WorkflowCompleteActivitySchedulerWorkHandler(
            activityStateStore,
            queue,
            executableStore,
            new FixedTimeProvider(_now));
        var drainer = new WorkflowSchedulerDrainer(queue, [handler, NewCheckpointHandler(activityStateStore, checkpointWriter), new NoopWorkflowSchedulerWorkHandler()], new FixedTimeProvider(_now));
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

        Assert.Equal(1, result.DrainedCount);
        Assert.Empty(remaining);
        var item = Assert.Single(result.Items);
        Assert.Equal("work-1", item.WorkItemId);
        Assert.Equal(NoopWorkflowSchedulerWorkHandler.HandlerName, item.HandlerName);
        Assert.Empty(checkpointWriter.ListCommits());
    }

    [Fact]
    public async Task CompleteActivityHandler_EnqueuesCheckpointWorkForContinuationScheduling()
    {
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var executableStore = new InMemoryWorkflowExecutableStore();
        await executableStore.SaveAsync(NewExecutable(["node-start", "node-next"]));
        var handler = new WorkflowCompleteActivitySchedulerWorkHandler(
            new InMemoryActivityExecutionStateStore(),
            queue,
            executableStore,
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
        Assert.Equal("work-1:checkpoint:WorkflowCompleted:actexec-parent", checkpointWork.WorkItemId);
        Assert.Equal(2, checkpointWork.Sequence);
        var checkpointPayload = checkpointWork.Payload!.Value.Deserialize<RuntimeCheckpointCommandPayload>()!;
        Assert.Equal(RuntimeCheckpointNames.WorkflowCompleted, checkpointPayload.CheckpointName);
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
        var result = new RuntimeSchedulerDrainResult(
            workflowExecutionId: "wfexec-1",
            startedAt: _now,
            completedAt: _now,
            items: [CompletedResult("wfexec-1")],
            outboxDeliveryResults:
            [
                new RuntimePostCommitOutboxProcessResult(
                [
                    new RuntimePostCommitOutboxProcessedItem(
                        OutboxItemId: "outbox-1",
                        IntentId: "intent-1",
                        RequestedDeliveryResultStatus: RuntimePostCommitOutboxStatus.Delivered,
                        FailureMessage: null)
                ])
            ]);

        Assert.Equal(RuntimeSchedulerDrainStopReason.Quiesced, result.StopReason);
        Assert.Equal(1, result.OutboxAttemptedCount);
        Assert.Equal(1, result.OutboxDeliveredCount);
        Assert.Equal(0, result.OutboxFailedCount);

        Assert.Throws<ArgumentException>(() => new RuntimeSchedulerDrainRequest(" "));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RuntimeSchedulerDrainRequest("wfexec-1", maxWorkItems: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new WorkflowExecutionDrainCoordinatorOptions(maxDrainCycles: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new WorkflowExecutionDrainCoordinatorOptions(outboxDeliveryBatchSize: 0));
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
        Assert.Throws<ArgumentNullException>(() => new RuntimeSchedulerWorkItemResult(
            workItemId: "work-1",
            workflowExecutionId: "wfexec-1",
            commandKind: WorkflowExecutionCommandKind.RunSchedulerWork,
            status: RuntimeSchedulerWorkItemResultStatus.Paused,
            handlerName: "handler",
            startedAt: _now,
            completedAt: _now));
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
        JsonElement? payload = null,
        IReadOnlyDictionary<string, string>? commandMetadata = null)
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
            payload: payload ?? document.RootElement.Clone(),
            commandMetadata: commandMetadata);
    }

    private RuntimeSchedulerWorkItem NewStartActivityWorkItem(int index, JsonSerializerOptions? jsonSerializerOptions = null) =>
        NewWorkItem(
            index,
            commandKind: WorkflowExecutionCommandKind.StartActivity,
            payload: JsonSerializer.SerializeToElement(new RuntimeStartActivityCommandPayload(
                NewPinnedExecutable(),
                "node-start",
                $"actexec-{index}",
                RuntimeStartActivityCommandPayload.ScheduledActivityReason), jsonSerializerOptions));

    private RuntimeSchedulerWorkItem NewInvokeActivityWorkItem(int index) =>
        NewWorkItem(
            index,
            commandKind: WorkflowExecutionCommandKind.InvokeActivity,
            payload: JsonSerializer.SerializeToElement(new RuntimeInvokeActivityCommandPayload(
                NewPinnedExecutable(),
                "node-start",
                $"actexec-{index}",
                RuntimeInvokeActivityCommandPayload.StartedActivityReason)));

    private RuntimeSchedulerWorkItem NewGeneratedEventWorkItem(int index) =>
        NewWorkItem(
            index,
            commandKind: WorkflowExecutionCommandKind.GeneratedEvent,
            payload: JsonSerializer.SerializeToElement(new SchedulerGeneratedEventWorkItem(
                workItemId: $"generated-work-{index}",
                generatedEvent: new GeneratedEvent(
                    generatedEventId: $"event-{index}",
                    workflowExecutionId: "wfexec-1",
                    generatorActivityExecutionId: "actexec-generator",
                    branchId: "branch-1",
                    name: "Tick",
                    sequence: index,
                    occurredAt: _now,
                    durability: GeneratedEventDurability.PolicyControlled),
                enqueuedAt: _now,
                reason: "GeneratorEmitted")),
            commandMetadata: new Dictionary<string, string>
            {
                ["GeneratorId"] = $"generator-{index}"
            });

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

    private static WorkflowExecutableIdentity NewPinnedExecutable() =>
        new("artifact-1", "definition-1", "version-1", "1.0.0", "sha256:test");

    private static RuntimeCheckpointCommandPayload NewCheckpointPayload() =>
        new(
            pinnedExecutable: NewPinnedExecutable(),
            checkpointName: RuntimeCheckpointNames.ActivityCompleted,
            activityExecutionIds: ["actexec-1"],
            reason: RuntimeCheckpointCommandPayload.ActivityCompletionPropagationReason);

    private static WorkflowExecutable NewExecutable(IReadOnlyCollection<string> nodeIds) =>
        new(
            identity: new WorkflowExecutableIdentity("artifact-1", "definition-1", "version-1", "1.0.0", "sha256:test"),
            rootActivity: ToRootActivity(nodeIds.Select(NewNode).ToArray()),
            resumeTargets: new Dictionary<string, WorkflowExecutableResumeTarget>(),
            createdAt: DateTimeOffset.UtcNow,
            publishedAt: DateTimeOffset.UtcNow,
            compatibilityMetadata: new Dictionary<string, string>());

    private static ExecutableNode ToRootActivity(IReadOnlyCollection<ExecutableNode> nodes)
    {
        var nodeSnapshot = nodes.ToArray();
        if (nodeSnapshot.Length == 1)
            return nodeSnapshot[0];

        return new ExecutableNode(
            executableNodeId: "$root",
            authoredActivityId: "$root",
            activityType: "test/root",
            activityTypeVersion: "1.0.0",
            descriptorType: "test",
            descriptorPayload: JsonSerializer.SerializeToElement(new { type = "root" }),
            inputBindings: new Dictionary<string, RuntimeInputBinding>(),
            outputCaptures: new Dictionary<string, RuntimeOutputCapture>(),
            metadata: new Dictionary<string, string>(),
            childSlots:
            [
                new ExecutableChildSlot("children", nodeSnapshot)
            ]);
    }

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

    private WorkflowCheckpointSchedulerWorkHandler NewCheckpointHandler(
        IActivityExecutionStateStore activityStateStore,
        IRuntimeCheckpointCommitStore checkpointWriter) =>
        new(
            activityStateStore,
            new RuntimeCheckpointCommitter(
                new ImmediateRuntimeCheckpointPersistencePolicy(),
                checkpointWriter),
            inspectionAccumulator: null,
            timeProvider: new FixedTimeProvider(_now));

    private static string CompletionReason(SchedulerCompletionKind completionKind) =>
        completionKind switch
        {
            SchedulerCompletionKind.ActivityCompleted => RuntimeCompleteActivityCommandPayload.ActivityInvocationCompletedReason,
            SchedulerCompletionKind.ParentCompletionEvaluation => RuntimeCompleteActivityCommandPayload.ParentCompletionEvaluationReason,
            SchedulerCompletionKind.ContinuationScheduling => RuntimeCompleteActivityCommandPayload.ContinuationSchedulingReason,
            _ => completionKind.ToString()
        };

    private ActivityExecutionState NewParentActivityState() =>
        NewActivityState("actexec-parent", ActivityExecutionStatus.Running);

    private ActivityExecutionState NewActivityState(
        string activityExecutionId,
        ActivityExecutionStatus status) =>
        new(
            Execution: new ActivityExecution(
                ActivityExecutionId: activityExecutionId,
                WorkflowExecutionId: "wfexec-1",
                ExecutableNodeId: "node-parent",
                AuthoredActivityId: "authored-node-parent",
                ActivityType: "test/parent",
                ActivityTypeVersion: "1.0.0"),
            Status: status,
            SubStatus: null,
            ScheduledAt: _now.AddMinutes(-3),
            StartedAt: _now.AddMinutes(-2),
            CompletedAt: status == ActivityExecutionStatus.Completed ? _now : null,
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

    private static SchedulerPauseDecision BlockedDecision(RuntimePauseBoundary boundary) =>
        new(
            canAdvance: false,
            boundary: boundary,
            continuationPolicy: RuntimePauseContinuationPolicy.StrictPause,
            holdId: "pause-1",
            reason: "Paused by test.");

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

    private sealed class RecordingWorkflowSchedulerPauseGate(SchedulerPauseDecision? decision) : IWorkflowSchedulerPauseGate
    {
        public List<string> WorkItemIds { get; } = [];

        public ValueTask<SchedulerPauseDecision?> EvaluateAsync(RuntimeSchedulerWorkItem workItem, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(workItem);
            cancellationToken.ThrowIfCancellationRequested();

            WorkItemIds.Add(workItem.WorkItemId);
            return new ValueTask<SchedulerPauseDecision?>(decision);
        }
    }

    private sealed class RecordingRuntimePauseDecisionProvider : IRuntimePauseDecisionProvider
    {
        public List<RuntimePauseDecisionRequest> Requests { get; } = [];

        public ValueTask<SchedulerPauseDecision> DecideAsync(RuntimePauseDecisionRequest request, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            cancellationToken.ThrowIfCancellationRequested();

            Requests.Add(request);
            return new(new SchedulerPauseDecision(
                canAdvance: true,
                boundary: request.Boundary,
                continuationPolicy: RuntimePauseContinuationPolicy.NotPaused,
                holdId: null,
                reason: null));
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

    private sealed class IncrementingRuntimeExecutionIdGenerator : IRuntimeExecutionIdGenerator
    {
        private int _activityExecutionIndex;

        public string NewWorkflowExecutionId() => "wfexec-unused";

        public string NewWorkflowExecutionCommandId() => "command-unused";

        public string NewWorkflowExecutionCommandEnvelopeId() => "envelope-unused";

        public string NewActivityExecutionId() => $"actexec-{++_activityExecutionIndex}";
    }
}
