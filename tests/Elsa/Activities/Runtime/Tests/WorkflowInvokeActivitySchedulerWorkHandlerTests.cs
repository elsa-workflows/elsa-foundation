using System.Text.Json;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Runtime.Services;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

public sealed class WorkflowInvokeActivitySchedulerWorkHandlerTests
{
    private readonly DateTimeOffset _now = new(2026, 6, 11, 12, 0, 0, TimeSpan.Zero);
    private readonly InMemoryWorkflowExecutableStore _executableStore = new();
    private readonly InMemoryActivityExecutionStateStore _activityStateStore = new();
    private readonly InMemoryWorkflowSchedulerWorkQueue _schedulerWorkQueue = new();
    private readonly InMemoryRuntimeActivityOutputRegister _activityOutputRegister = new();
    private readonly InMemoryDurableValueStateStore _durableValueStateStore = new();
    private readonly InMemoryIncidentStateStore _incidentStateStore = new();
    private readonly InMemoryActivityExecutionInspectionStore _inspectionStore = new();
    private readonly InMemoryRuntimeCheckpointCommitStore _checkpointWriter;

    public WorkflowInvokeActivitySchedulerWorkHandlerTests()
    {
        _checkpointWriter = new InMemoryRuntimeCheckpointCommitStore(
            workflowExecutionStateStore: null,
            activityExecutionStateStore: _activityStateStore,
            bookmarkStateStore: null,
            durableValueStateStore: _durableValueStateStore,
            incidentStateStore: _incidentStateStore,
            operationalStateStore: null,
            schedulerStateStore: null,
            activityExecutionInspectionWriter: _inspectionStore);
    }

    [Fact]
    public async Task HandleAsync_InvokesRunningActivityAndRecordsCompletedState()
    {
        var activity = new RecordingActivity();
        var factory = new RecordingActivityFactory(activity);
        await _executableStore.SaveAsync(NewExecutable());
        await _activityStateStore.SaveAsync(NewRunningState());
        await using var provider = NewProvider(factory);
        var handler = NewHandler(provider);

        await handler.HandleAsync(NewInvokeWorkItem(NewIdentity()));

        Assert.Equal("hello", activity.ObservedText);
        Assert.Equal("actexec-1", activity.Id);
        Assert.Equal("node-start", activity.NodeId);
        Assert.Equal("test", factory.LastDescriptorType);
        Assert.Single(factory.LastInputs);
        var state = await _activityStateStore.FindAsync("wfexec-1", "actexec-1");
        Assert.NotNull(state);
        Assert.Equal(ActivityExecutionStatus.Completed, state.Status);
        Assert.Null(state.SubStatus);
        Assert.Equal(_now, state.CompletedAt);
        Assert.Equal(RuntimeInvokeActivityCommandPayload.StartedActivityReason, state.Metadata["runtime.invokeReason"]);
        Assert.Equal("invoke-work", state.Metadata["runtime.invokeSchedulerWorkItemId"]);
        var completionPayload = await AssertCompletionWorkAsync();
        Assert.Equal("actexec-1", completionPayload.ActivityExecutionId);
        Assert.Equal("node-start", completionPayload.ExecutableNodeId);
        Assert.Equal([ActivityOutcomes.Done], completionPayload.OutcomeNames);
        Assert.Equal(RuntimeCompleteActivityCommandPayload.ActivityInvocationCompletedReason, completionPayload.Reason);
    }

    [Fact]
    public async Task HandleAsync_CheckpointsCompletedStateInspectionAndPostCommitCompletionWork()
    {
        var activity = new RecordingActivity();
        await _executableStore.SaveAsync(NewExecutable());
        await _activityStateStore.SaveAsync(NewRunningState());
        await using var provider = NewProvider(new RecordingActivityFactory(activity), includeInspection: true);
        var handler = NewHandler(provider);

        await handler.HandleAsync(NewInvokeWorkItem(NewIdentity()));

        var write = Assert.Single(_checkpointWriter.ListCommits());
        Assert.Equal(RuntimeCheckpointNames.ActivityCompleted, write.Commit.Checkpoint.Name);
        Assert.Equal(RuntimeMetadataKeys.CheckpointRequirementMandatory, write.Commit.Checkpoint.Metadata[RuntimeMetadataKeys.CheckpointRequirement]);
        Assert.Single(write.Commit.StateChanges.ActivityExecutions);
        Assert.Single(write.Commit.StateChanges.ActivityExecutionInspections);
        Assert.Single(write.Commit.PostCommitIntents);

        var projection = await _inspectionStore.FindAsync("wfexec-1", "actexec-1");
        Assert.NotNull(projection);
        Assert.Equal(ActivityExecutionStatus.Completed, projection.Status);
        Assert.Equal([ActivityOutcomes.Done], projection.OutcomeNames);
        var inputSnapshot = Assert.Single(projection.ValueSnapshots);
        Assert.Equal(ActivityExecutionInspectionValueSubject.ActivityInput, inputSnapshot.Subject);
        Assert.Equal(RuntimePayloadCaptureMode.None, inputSnapshot.CaptureMode);
        Assert.Null(inputSnapshot.Payload);

        await AssertCompletionWorkAsync();
    }

    [Fact]
    public async Task HandleAsync_RecordsCompletedSkippedStateWhenCanExecuteReturnsFalse()
    {
        var activity = new RecordingActivity { ShouldExecute = false };
        await _executableStore.SaveAsync(NewExecutable());
        await _activityStateStore.SaveAsync(NewRunningState());
        await using var provider = NewProvider(
            new RecordingActivityFactory(activity),
            includeInspection: true,
            payloadCapturePolicy: new CaptureAllRuntimePayloadCapturePolicy());
        var handler = NewHandler(provider);

        await handler.HandleAsync(NewInvokeWorkItem(NewIdentity()));

        Assert.Null(activity.ObservedText);
        var state = await _activityStateStore.FindAsync("wfexec-1", "actexec-1");
        Assert.NotNull(state);
        Assert.Equal(ActivityExecutionStatus.Completed, state.Status);
        Assert.Equal("Skipped", state.SubStatus);
        Assert.Equal(bool.TrueString, state.Metadata["runtime.invokeSkipped"]);
        var completionPayload = await AssertCompletionWorkAsync();
        Assert.Empty(completionPayload.OutcomeNames);
    }

    [Fact]
    public async Task HandleAsync_EnqueuesCompletionWorkWithActivityOutcomes()
    {
        var activity = new RecordingActivity { Outcomes = ["Approved", "Escalated"] };
        await _executableStore.SaveAsync(NewExecutable());
        await _activityStateStore.SaveAsync(NewRunningState());
        await using var provider = NewProvider(
            new RecordingActivityFactory(activity),
            includeInspection: true,
            payloadCapturePolicy: new CaptureAllRuntimePayloadCapturePolicy());
        var handler = NewHandler(provider);

        await handler.HandleAsync(NewInvokeWorkItem(NewIdentity()));

        var completionPayload = await AssertCompletionWorkAsync();
        Assert.Equal(["Approved", "Escalated"], completionPayload.OutcomeNames);
        var state = await _activityStateStore.FindAsync("wfexec-1", "actexec-1");
        Assert.NotNull(state);
        Assert.Contains("Approved", state.Metadata[RuntimeMetadataKeys.CompletionOutcomeNames]);
    }

    [Fact]
    public async Task HandleAsync_MaterializesInputFromActiveActivityOutputBinding()
    {
        _activityOutputRegister.Set(new ActiveActivityOutput(
            key: new ActiveActivityOutputKey("wfexec-1", "actexec-fetch", "status"),
            value: JsonSerializer.SerializeToElement("delivered"),
            type: new RuntimeValueTypeDescriptor("primitive", "string", null),
            recordedAt: _now));
        var activity = new RecordingActivity();
        await _executableStore.SaveAsync(NewExecutableWithInputBinding(ActivityOutputTextBinding("actexec-fetch", "status")));
        await _activityStateStore.SaveAsync(NewRunningState());
        await using var provider = NewProvider(
            new RecordingActivityFactory(activity),
            includeInspection: true,
            payloadCapturePolicy: new CaptureAllRuntimePayloadCapturePolicy());
        var handler = NewHandler(provider);

        await handler.HandleAsync(NewInvokeWorkItem(NewIdentity()));

        Assert.Equal("delivered", activity.ObservedText);
        await AssertCompletionWorkAsync();
    }

    [Fact]
    public async Task HandleAsync_MaterializesInputFromDurableValueBinding()
    {
        await _durableValueStateStore.SaveAsync(new DurableValueState(
            durableValueId: "durable-status",
            workflowExecutionId: "wfexec-1",
            valueId: "status",
            type: new RuntimeValueTypeDescriptor("primitive", "string", null),
            lifecycle: DurableValueLifecycle.Instance,
            storage: DurableValueStorage.Inline,
            inlineValue: JsonSerializer.SerializeToElement("stored"),
            externalReference: null,
            sourceActivityExecutionId: "actexec-fetch",
            capturedAt: _now,
            metadata: new Dictionary<string, string>()));
        var activity = new RecordingActivity();
        await _executableStore.SaveAsync(NewExecutableWithInputBinding(DurableTextBinding("status")));
        await _activityStateStore.SaveAsync(NewRunningState());
        await using var provider = NewProvider(
            new RecordingActivityFactory(activity),
            includeInspection: true,
            payloadCapturePolicy: new CaptureAllRuntimePayloadCapturePolicy());
        var handler = NewHandler(provider);

        await handler.HandleAsync(NewInvokeWorkItem(NewIdentity()));

        Assert.Equal("stored", activity.ObservedText);
        await AssertCompletionWorkAsync();
    }

    [Fact]
    public async Task HandleAsync_PublishesActiveOutputsAndCapturesDeclaredDurableValuesWhenActivityCompletes()
    {
        var activity = new OutputProducingActivity("""{"id":"customer-1"}""");
        var factory = new RecordingActivityFactory(activity);
        await _executableStore.SaveAsync(NewExecutableWithOutputCapture());
        await _activityStateStore.SaveAsync(NewRunningState());
        await using var provider = NewProvider(factory, includeInspection: true);
        var handler = NewHandler(provider);

        await handler.HandleAsync(NewInvokeWorkItem(NewIdentity()));

        Assert.True(factory.LastOutputs.ContainsKey("customer"));
        var outputKey = new ActiveActivityOutputKey("wfexec-1", "actexec-1", "customer");
        Assert.True(_activityOutputRegister.TryGet(outputKey, out var activeOutput));
        Assert.Equal("customer-1", activeOutput.Value.GetProperty("id").GetString());
        Assert.Equal("node-start", activeOutput.Metadata["runtime.executableNodeId"]);
        var durableValue = await _durableValueStateStore.FindAsync("wfexec-1", "durable-customer");
        Assert.NotNull(durableValue);
        Assert.Equal("customer", durableValue.ValueId);
        Assert.Equal("actexec-1", durableValue.SourceActivityExecutionId);
        Assert.Equal("customer-1", durableValue.InlineValue!.Value.GetProperty("id").GetString());
        var write = Assert.Single(_checkpointWriter.ListCommits());
        Assert.Equal(RuntimeCheckpointNames.ActivityCompleted, write.Commit.Checkpoint.Name);
        var durableChange = Assert.Single(write.Commit.StateChanges.DurableValues);
        Assert.Equal("durable-customer", durableChange.State!.DurableValueId);
        await AssertCompletionWorkAsync();
    }

    [Fact]
    public async Task HandleAsync_CapturesPolicyGovernedInputAndOutputInspectionSnapshots()
    {
        var activity = new OutputProducingActivity("""{"id":"customer-1"}""");
        await _executableStore.SaveAsync(NewExecutableWithOutputCapture());
        await _activityStateStore.SaveAsync(NewRunningState());
        await using var provider = NewProvider(
            new RecordingActivityFactory(activity),
            includeInspection: true,
            payloadCapturePolicy: new CaptureAllRuntimePayloadCapturePolicy());
        var handler = NewHandler(provider);

        await handler.HandleAsync(NewInvokeWorkItem(NewIdentity()));

        var projection = await _inspectionStore.FindAsync("wfexec-1", "actexec-1");
        Assert.NotNull(projection);
        Assert.Contains(projection.ValueSnapshots, snapshot =>
            snapshot.Subject == ActivityExecutionInspectionValueSubject.ActivityInput &&
            snapshot.Name == "Text" &&
            snapshot.CaptureMode == RuntimePayloadCaptureMode.Payload &&
            snapshot.Payload is { } payload &&
            payload.ValueKind == JsonValueKind.String);
        Assert.Contains(projection.ValueSnapshots, snapshot =>
            snapshot.Subject == ActivityExecutionInspectionValueSubject.ActivityOutput &&
            snapshot.Name == "customer" &&
            snapshot.CaptureMode == RuntimePayloadCaptureMode.Payload &&
            snapshot.Payload is { } payload &&
            payload.ValueKind == JsonValueKind.Object &&
            payload.GetProperty("id").GetString() == "customer-1");
    }

    [Fact]
    public async Task HandleAsync_EnqueuesCreateBookmarkWorkWhenActivityRequestsDurableBookmark()
    {
        var activity = new BookmarkRequestingActivity(
            new ActivityBookmarkRequest(
                bookmarkId: "bookmark-1",
                resumeTargetId: "resume-target:delivery",
                stimulusType: "delivery-status",
                stimulusHash: "sha256:delivery-status:order-123",
                payload: Json("""{"orderId":"order-123"}"""),
                expiresAt: _now.AddMinutes(10),
                metadata: new Dictionary<string, string> { ["customer"] = "northwind" }));
        await _executableStore.SaveAsync(NewExecutable());
        await _activityStateStore.SaveAsync(NewRunningState());
        await using var provider = NewProvider(
            new RecordingActivityFactory(activity),
            includeInspection: true,
            payloadCapturePolicy: new CaptureAllRuntimePayloadCapturePolicy());
        var handler = NewHandler(provider);

        await handler.HandleAsync(NewInvokeWorkItem(NewIdentity()));

        var state = await _activityStateStore.FindAsync("wfexec-1", "actexec-1");
        Assert.NotNull(state);
        Assert.Equal(ActivityExecutionStatus.Running, state.Status);
        Assert.Null(state.CompletedAt);
        var workItem = Assert.Single(await _schedulerWorkQueue.ListAsync(new RuntimeSchedulerWorkQuery("wfexec-1")));
        Assert.Equal(WorkflowExecutionCommandKind.CreateBookmark, workItem.CommandKind);
        Assert.Equal("invoke-work:create-bookmark:bookmark-1", workItem.WorkItemId);
        Assert.Equal("command-1:create-bookmark:bookmark-1", workItem.CommandId);
        Assert.Equal("wfexec-1:invoke:actexec-1:create-bookmark:bookmark-1", workItem.IdempotencyKey);
        Assert.Equal(31, workItem.Sequence);
        Assert.Equal("northwind", workItem.CommandMetadata["customer"]);
        Assert.Equal("bookmark-1", workItem.CommandMetadata["runtime.bookmarkId"]);
        var payload = workItem.Payload!.Value.Deserialize<RuntimeCreateBookmarkCommandPayload>()!;
        Assert.Equal(NewIdentity(), payload.PinnedExecutable);
        Assert.Equal("bookmark-1", payload.BookmarkId);
        Assert.Equal("actexec-1", payload.ActivityExecutionId);
        Assert.Equal("node-start", payload.ExecutableNodeId);
        Assert.Equal("resume-target:delivery", payload.ResumeTargetId);
        Assert.Equal("delivery-status", payload.StimulusType);
        Assert.Equal("sha256:delivery-status:order-123", payload.StimulusHash);
        Assert.Equal("order-123", payload.Payload!.Value.GetProperty("orderId").GetString());
        Assert.Equal(_now.AddMinutes(10), payload.ExpiresAt);
        Assert.Equal(RuntimeCreateBookmarkCommandPayload.ActivitySuspendedReason, payload.Reason);
        Assert.Equal("northwind", payload.Metadata["customer"]);
        var snapshot = Assert.Single(payload.ValueSnapshots);
        Assert.Equal(ActivityExecutionInspectionValueSubject.ActivityInput, snapshot.Subject);
        Assert.Equal(RuntimePayloadCaptureMode.Payload, snapshot.CaptureMode);
    }

    [Fact]
    public async Task HandleAsync_DoesNotPublishOrCaptureOutputsWhenActivityRequestsDurableBookmark()
    {
        var activity = new OutputBookmarkRequestingActivity(
            """{"id":"customer-1"}""",
            new ActivityBookmarkRequest("bookmark-1", "resume-target:delivery", "event", "hash"));
        await _executableStore.SaveAsync(NewExecutableWithOutputCapture());
        await _activityStateStore.SaveAsync(NewRunningState());
        await using var provider = NewProvider(
            new RecordingActivityFactory(activity),
            includeInspection: true,
            payloadCapturePolicy: new CaptureAllRuntimePayloadCapturePolicy());
        var handler = NewHandler(provider);

        await handler.HandleAsync(NewInvokeWorkItem(NewIdentity()));

        Assert.False(_activityOutputRegister.TryGet(new ActiveActivityOutputKey("wfexec-1", "actexec-1", "customer"), out _));
        Assert.Null(await _durableValueStateStore.FindAsync("wfexec-1", "durable-customer"));
        Assert.Empty(_checkpointWriter.ListCommits());
        Assert.Equal(WorkflowExecutionCommandKind.CreateBookmark, Assert.Single(await _schedulerWorkQueue.ListAsync(new RuntimeSchedulerWorkQuery("wfexec-1"))).CommandKind);
    }

    [Fact]
    public async Task HandleAsync_EnqueuesCreateBookmarkWorkInRequestOrder()
    {
        var activity = new BookmarkRequestingActivity(
            new ActivityBookmarkRequest("bookmark-1", "resume-target:first", "event", "hash-1"),
            new ActivityBookmarkRequest("bookmark-2", "resume-target:second", "event", "hash-2"));
        await _executableStore.SaveAsync(NewExecutable());
        await _activityStateStore.SaveAsync(NewRunningState());
        await using var provider = NewProvider(new RecordingActivityFactory(activity));
        var handler = NewHandler(provider);

        await handler.HandleAsync(NewInvokeWorkItem(NewIdentity()));

        var workItems = (await _schedulerWorkQueue.ListAsync(new RuntimeSchedulerWorkQuery("wfexec-1"))).ToArray();
        Assert.Equal(2, workItems.Length);
        Assert.Equal(["bookmark-1", "bookmark-2"], workItems.Select(workItem => workItem.Payload!.Value.Deserialize<RuntimeCreateBookmarkCommandPayload>()!.BookmarkId));
        Assert.Equal([31L, 32L], workItems.Select(workItem => workItem.Sequence!.Value));
    }

    [Fact]
    public async Task HandleAsync_CheckpointsInputSnapshotsBeforePostCommitChildScheduling()
    {
        var activity = new ChildSchedulingActivity();
        await _executableStore.SaveAsync(NewExecutable());
        await _activityStateStore.SaveAsync(NewRunningState());
        await using var provider = NewProvider(
            new RecordingActivityFactory(activity),
            includeInspection: true,
            payloadCapturePolicy: new CaptureAllRuntimePayloadCapturePolicy());
        var handler = NewHandler(provider);

        await handler.HandleAsync(NewInvokeWorkItem(NewIdentity()));

        var write = Assert.Single(_checkpointWriter.ListCommits());
        Assert.Equal(RuntimeCheckpointNames.ActivityInspectionCaptured, write.Commit.Checkpoint.Name);
        var inspectionChange = Assert.Single(write.Commit.StateChanges.ActivityExecutionInspections);
        Assert.Contains(inspectionChange.State.ValueSnapshots, snapshot =>
            snapshot.Subject == ActivityExecutionInspectionValueSubject.ActivityInput &&
            snapshot.CaptureMode == RuntimePayloadCaptureMode.Payload);
        Assert.Single(write.Commit.PostCommitIntents);
        var scheduleWork = AssertSchedulerPostCommitWork(WorkflowExecutionCommandKind.ScheduleActivity);
        Assert.Equal(WorkflowExecutionCommandKind.ScheduleActivity, scheduleWork.CommandKind);
    }

    [Fact]
    public async Task HandleAsync_FaultsActivityWhenDuplicateBookmarkRequestsAreRecorded()
    {
        var activity = new DuplicateBookmarkRequestActivity();
        await _executableStore.SaveAsync(NewExecutable());
        await _activityStateStore.SaveAsync(NewRunningState());
        await using var provider = NewProvider(new RecordingActivityFactory(activity));
        var handler = NewHandler(provider);

        await handler.HandleAsync(NewInvokeWorkItem(NewIdentity()));

        var state = await _activityStateStore.FindAsync("wfexec-1", "actexec-1");
        Assert.NotNull(state);
        Assert.Equal(ActivityExecutionStatus.Faulted, state.Status);
        Assert.Equal("ActivityFaulted", state.SubStatus);
        Assert.Contains("already registered", state.Metadata["runtime.faultMessage"]);
        await AssertIncidentRecordedAsync("ActivityFaulted", message => Assert.Contains("already registered", message));
        Assert.Empty(await _schedulerWorkQueue.ListAsync(new RuntimeSchedulerWorkQuery("wfexec-1")));
    }

    [Fact]
    public async Task HandleAsync_RecordsFaultedStateWhenActivityThrows()
    {
        var activity = new RecordingActivity { Exception = new InvalidOperationException("boom") };
        await _executableStore.SaveAsync(NewExecutable());
        await _activityStateStore.SaveAsync(NewRunningState());
        await using var provider = NewProvider(
            new RecordingActivityFactory(activity),
            includeInspection: true,
            payloadCapturePolicy: new CaptureAllRuntimePayloadCapturePolicy());
        var handler = NewHandler(provider);

        await handler.HandleAsync(NewInvokeWorkItem(NewIdentity()));

        var state = await _activityStateStore.FindAsync("wfexec-1", "actexec-1");
        Assert.NotNull(state);
        Assert.Equal(ActivityExecutionStatus.Faulted, state.Status);
        Assert.Equal("ActivityFaulted", state.SubStatus);
        Assert.Equal(_now, state.CompletedAt);
        Assert.Equal(1, state.FaultCount);
        Assert.Equal(1, state.AggregateFaultCount);
        Assert.Equal(typeof(InvalidOperationException).FullName, state.Metadata["runtime.faultType"]);
        Assert.Equal("boom", state.Metadata["runtime.faultMessage"]);
        await AssertIncidentRecordedAsync("ActivityFaulted", message => Assert.Equal("boom", message));
        var projection = await _inspectionStore.FindAsync("wfexec-1", "actexec-1");
        Assert.NotNull(projection);
        Assert.Contains(projection.ValueSnapshots, snapshot => snapshot.Subject == ActivityExecutionInspectionValueSubject.ActivityInput);
        Assert.Empty(await _schedulerWorkQueue.ListAsync(new RuntimeSchedulerWorkQuery("wfexec-1")));
    }

    [Fact]
    public async Task HandleAsync_RecordsFaultedStateWhenInputMaterializationFails()
    {
        await _executableStore.SaveAsync(NewExecutableWithInputBinding(new RuntimeInputBinding(
            inputName: "Text",
            source: RuntimeInputBindingSource.Expression,
            expression: new RuntimeExpressionBinding("JavaScript", "workflow.input"))));
        await _activityStateStore.SaveAsync(NewRunningState());
        var factory = new RecordingActivityFactory(new RecordingActivity());
        await using var provider = NewProvider(factory);
        var handler = NewHandler(provider);

        await handler.HandleAsync(NewInvokeWorkItem(NewIdentity()));

        Assert.Equal(0, factory.CreateCalls);
        var state = await _activityStateStore.FindAsync("wfexec-1", "actexec-1");
        Assert.NotNull(state);
        Assert.Equal(ActivityExecutionStatus.Faulted, state.Status);
        Assert.Equal("InputMaterializationFailed", state.SubStatus);
        Assert.Equal(_now, state.CompletedAt);
        Assert.Equal(1, state.FaultCount);
        Assert.Contains("typeName", state.Metadata["runtime.faultMessage"]);
        await AssertIncidentRecordedAsync("InputMaterializationFailed", message => Assert.Contains("typeName", message));
        Assert.Empty(await _schedulerWorkQueue.ListAsync(new RuntimeSchedulerWorkQuery("wfexec-1")));
    }

    [Fact]
    public async Task HandleAsync_RecordsInputMaterializationFaultInspection()
    {
        await _executableStore.SaveAsync(NewExecutableWithInputBinding(new RuntimeInputBinding(
            inputName: "Text",
            source: RuntimeInputBindingSource.Expression,
            expression: new RuntimeExpressionBinding("JavaScript", "workflow.input"))));
        await _activityStateStore.SaveAsync(NewRunningState());
        await using var provider = NewProvider(new RecordingActivityFactory(new RecordingActivity()), includeInspection: true);
        var handler = NewHandler(provider);

        await handler.HandleAsync(NewInvokeWorkItem(NewIdentity()));

        var projection = await _inspectionStore.FindAsync("wfexec-1", "actexec-1");
        Assert.NotNull(projection);
        Assert.Equal(ActivityExecutionStatus.Faulted, projection.Status);
        Assert.Equal("InputMaterializationFailed", projection.SubStatus);
        var incident = Assert.Single(projection.Incidents);
        Assert.Equal("InputMaterializationFailed", incident.FailureType);
        Assert.True(incident.IsBlocking);

        var write = Assert.Single(_checkpointWriter.ListCommits());
        Assert.Equal(RuntimeCheckpointNames.IncidentRecorded, write.Commit.Checkpoint.Name);
        Assert.Single(write.Commit.StateChanges.ActivityExecutionInspections);
    }

    [Fact]
    public async Task HandleAsync_PropagatesCompletionPersistenceFailureWithoutRecordingActivityFault()
    {
        var activity = new RecordingActivity();
        var factory = new RecordingActivityFactory(activity);
        var throwingStore = new ThrowingSaveActivityExecutionStateStore(
            _activityStateStore,
            ActivityExecutionStatus.Completed,
            new InvalidOperationException("storage down"));
        await _executableStore.SaveAsync(NewExecutable());
        await _activityStateStore.SaveAsync(NewRunningState());
        await using var provider = NewProvider(factory, throwingStore);
        var handler = NewHandler(provider);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => handler.HandleAsync(NewInvokeWorkItem(NewIdentity())).AsTask());

        Assert.Equal("storage down", exception.Message);
        Assert.Equal([ActivityExecutionStatus.Completed], throwingStore.AttemptedSaveStatuses);
        var state = await _activityStateStore.FindAsync("wfexec-1", "actexec-1");
        Assert.NotNull(state);
        Assert.Equal(ActivityExecutionStatus.Running, state.Status);
        Assert.Empty(await _schedulerWorkQueue.ListAsync(new RuntimeSchedulerWorkQuery("wfexec-1")));
    }

    [Fact]
    public async Task HandleAsync_PropagatesConstructionFailureWithoutChangingRunningState()
    {
        await _executableStore.SaveAsync(NewExecutable());
        await _activityStateStore.SaveAsync(NewRunningState());
        var factory = new ThrowingActivityFactory(new InvalidOperationException("missing constructor"));
        await using var provider = NewProvider(factory);
        var handler = NewHandler(provider);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => handler.HandleAsync(NewInvokeWorkItem(NewIdentity())).AsTask());

        Assert.Equal("missing constructor", exception.Message);
        var state = await _activityStateStore.FindAsync("wfexec-1", "actexec-1");
        Assert.NotNull(state);
        Assert.Equal(ActivityExecutionStatus.Running, state.Status);
        Assert.Null(state.CompletedAt);
        Assert.Empty(await _schedulerWorkQueue.ListAsync(new RuntimeSchedulerWorkQuery("wfexec-1")));
    }

    [Fact]
    public async Task HandleAsync_ReenqueuesCompletionWorkForExistingCompletedState()
    {
        var activity = new RecordingActivity();
        await _executableStore.SaveAsync(NewExecutable());
        await _activityStateStore.SaveAsync(NewRunningState() with
        {
            Status = ActivityExecutionStatus.Completed,
            CompletedAt = _now.AddMinutes(-1)
        });
        var factory = new RecordingActivityFactory(activity);
        await using var provider = NewProvider(factory);
        var handler = NewHandler(provider);

        await handler.HandleAsync(NewInvokeWorkItem(NewIdentity()));

        Assert.Equal(0, factory.CreateCalls);
        var state = await _activityStateStore.FindAsync("wfexec-1", "actexec-1");
        Assert.NotNull(state);
        Assert.Equal(ActivityExecutionStatus.Completed, state.Status);
        Assert.Equal(_now.AddMinutes(-1), state.CompletedAt);
        var completionPayload = await AssertCompletionWorkAsync();
        Assert.Equal("actexec-1", completionPayload.ActivityExecutionId);
    }

    [Fact]
    public async Task HandleAsync_ReenqueuesCompletionWorkForExistingSkippedCompletedState()
    {
        var activity = new RecordingActivity();
        await _executableStore.SaveAsync(NewExecutable());
        await _activityStateStore.SaveAsync(NewRunningState() with
        {
            Status = ActivityExecutionStatus.Completed,
            SubStatus = "Skipped",
            CompletedAt = _now.AddMinutes(-1)
        });
        var factory = new RecordingActivityFactory(activity);
        await using var provider = NewProvider(factory);
        var handler = NewHandler(provider);

        await handler.HandleAsync(NewInvokeWorkItem(NewIdentity()));

        Assert.Equal(0, factory.CreateCalls);
        var completionPayload = await AssertCompletionWorkAsync();
        Assert.Empty(completionPayload.OutcomeNames);
    }

    [Fact]
    public async Task HandleAsync_RejectsPayloadNodeMismatchBeforeInvokingActivity()
    {
        var activity = new RecordingActivity();
        await _executableStore.SaveAsync(NewExecutable());
        await _activityStateStore.SaveAsync(NewRunningState());
        var factory = new RecordingActivityFactory(activity);
        await using var provider = NewProvider(factory);
        var handler = NewHandler(provider);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => handler.HandleAsync(NewInvokeWorkItem(NewIdentity(), executableNodeId: "node-other")).AsTask());

        Assert.Contains("belongs to executable node 'node-start'", exception.Message);
        Assert.Equal(0, factory.CreateCalls);
        var state = await _activityStateStore.FindAsync("wfexec-1", "actexec-1");
        Assert.NotNull(state);
        Assert.Equal(ActivityExecutionStatus.Running, state.Status);
    }

    [Fact]
    public async Task HandleAsync_RejectsMissingPayloadBeforeInvokingActivity()
    {
        var factory = new RecordingActivityFactory(new RecordingActivity());
        await _activityStateStore.SaveAsync(NewRunningState());
        await using var provider = NewProvider(factory);
        var handler = NewHandler(provider);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => handler.HandleAsync(NewInvokeWorkItem(includePayload: false)).AsTask());

        Assert.Contains("requires an invoke activity payload", exception.Message);
        Assert.Equal(0, factory.CreateCalls);
    }

    [Fact]
    public void CanHandle_AcceptsOnlyInvokeActivityWork()
    {
        using var provider = NewProvider(new RecordingActivityFactory(new RecordingActivity()));
        var handler = NewHandler(provider);

        Assert.True(handler.CanHandle(NewInvokeWorkItem(NewIdentity())));
        Assert.False(handler.CanHandle(NewInvokeWorkItem(NewIdentity(), commandKind: WorkflowExecutionCommandKind.StartActivity)));
    }

    private WorkflowInvokeActivitySchedulerWorkHandler NewHandler(ServiceProvider provider) =>
        new(
            new RuntimeActivityInputMaterializer(),
            provider.GetRequiredService<IServiceScopeFactory>(),
            new FixedTimeProvider(_now));

    private async Task<RuntimeCompleteActivityCommandPayload> AssertCompletionWorkAsync()
    {
        var completionWork = await AssertCompletionSchedulerWorkAsync();
        Assert.Equal(WorkflowExecutionCommandKind.CompleteActivity, completionWork.CommandKind);
        Assert.Equal("invoke-work:complete:actexec-1", completionWork.WorkItemId);
        Assert.Equal("command-1:complete:actexec-1", completionWork.CommandId);
        Assert.Equal("wfexec-1:invoke:actexec-1:complete:actexec-1", completionWork.IdempotencyKey);
        Assert.Equal(31, completionWork.Sequence);
        Assert.NotNull(completionWork.Payload);
        return completionWork.Payload.Value.Deserialize<RuntimeCompleteActivityCommandPayload>()!;
    }

    private async Task<RuntimeSchedulerWorkItem> AssertCompletionSchedulerWorkAsync()
    {
        var intents = _checkpointWriter.ListCommits().SelectMany(write => write.Commit.PostCommitIntents).ToArray();
        if (intents.Length == 0)
            return Assert.Single(await _schedulerWorkQueue.ListAsync(new RuntimeSchedulerWorkQuery("wfexec-1")));

        var intent = Assert.Single(intents);
        Assert.Equal(RuntimePostCommitIntentKinds.EnqueueSchedulerWork, intent.Kind);
        Assert.NotNull(intent.Payload);
        return intent.Payload.Value.Deserialize<RuntimeSchedulerWorkItem>()!;
    }

    private RuntimeSchedulerWorkItem AssertSchedulerPostCommitWork(WorkflowExecutionCommandKind commandKind)
    {
        var intent = Assert.Single(_checkpointWriter.ListCommits().SelectMany(write => write.Commit.PostCommitIntents));
        Assert.Equal(RuntimePostCommitIntentKinds.EnqueueSchedulerWork, intent.Kind);
        Assert.NotNull(intent.Payload);
        var workItem = intent.Payload.Value.Deserialize<RuntimeSchedulerWorkItem>()!;
        Assert.Equal(commandKind, workItem.CommandKind);
        return workItem;
    }

    private async Task AssertIncidentRecordedAsync(string failureType, Action<string> assertMessage)
    {
        var incidentId = $"incident:invoke-work:actexec-1:{failureType}";
        var state = await _activityStateStore.FindAsync("wfexec-1", "actexec-1");
        Assert.NotNull(state);
        Assert.Equal([incidentId], state.IncidentIds);
        Assert.Equal(incidentId, state.Metadata["runtime.incidentId"]);

        var incident = await _incidentStateStore.FindAsync("wfexec-1", incidentId);
        Assert.NotNull(incident);
        Assert.Equal("wfexec-1", incident.WorkflowExecutionId);
        Assert.Equal("actexec-1", incident.ActivityExecutionId);
        Assert.Equal("node-start", incident.ExecutableNodeId);
        Assert.Equal(IncidentSeverity.Error, incident.Severity);
        Assert.Equal(IncidentStatus.Blocking, incident.Status);
        Assert.Equal(IncidentResolutionAction.WaitForIntervention, incident.ResolutionAction);
        Assert.Equal(failureType, incident.FailureType);
        Assert.Equal(_now, incident.CreatedAt);
        Assert.Null(incident.ResolvedAt);
        Assert.Equal("invoke-work", incident.Metadata["runtime.schedulerWorkItemId"]);
        Assert.Equal("command-1", incident.Metadata["runtime.commandId"]);
        Assert.Equal(failureType, incident.Metadata["runtime.faultSubStatus"]);
        assertMessage(incident.Message);

        var write = Assert.Single(_checkpointWriter.ListCommits());
        Assert.Equal(RuntimeCheckpointNames.IncidentRecorded, write.Commit.Checkpoint.Name);
        Assert.Equal(incidentId, write.Commit.Checkpoint.Metadata["runtime.incidentId"]);
        Assert.Equal(["actexec-1"], write.Commit.Checkpoint.ActivityExecutionIds);
    }

    private ServiceProvider NewProvider(
        IActivityFactory factory,
        IActivityExecutionStateStore? activityExecutionStateStore = null,
        bool includeInspection = false,
        IRuntimePayloadCapturePolicy? payloadCapturePolicy = null)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => factory);
        services.AddSingleton(_executableStore);
        services.AddSingleton<IWorkflowExecutableStore>(_ => _executableStore);
        services.AddSingleton(_activityStateStore);
        services.AddSingleton<IActivityExecutionStateStore>(_ => activityExecutionStateStore ?? _activityStateStore);
        services.AddSingleton(_schedulerWorkQueue);
        services.AddSingleton<IWorkflowSchedulerWorkQueue>(_ => _schedulerWorkQueue);
        services.AddSingleton<TimeProvider>(new FixedTimeProvider(_now));
        services.AddSingleton(_activityOutputRegister);
        services.AddSingleton<IRuntimeActivityOutputRegister>(_ => _activityOutputRegister);
        services.AddSingleton(_durableValueStateStore);
        services.AddSingleton<IDurableValueStateStore>(_ => _durableValueStateStore);
        services.AddSingleton(_incidentStateStore);
        services.AddSingleton<IIncidentStateStore>(_ => _incidentStateStore);
        services.AddSingleton<IRuntimeCheckpointCommitStore>(_ => _checkpointWriter);
        services.AddSingleton<IRuntimeCheckpointPersistencePolicy, ImmediateRuntimeCheckpointPersistencePolicy>();
        if (includeInspection)
            services.AddSingleton<IRuntimePostCommitIntentDispatcher, RuntimeSchedulerPostCommitIntentDispatcher>();
        else
            services.AddSingleton<IRuntimePostCommitIntentDispatcher, NoopRuntimePostCommitIntentDispatcher>();
        services.AddSingleton<RuntimeCheckpointCommitter>();
        services.AddSingleton<ActivityFaultIncidentRecorder>();
        services.AddSingleton<IRuntimeExecutionIdGenerator, GuidRuntimeExecutionIdGenerator>();
        if (includeInspection)
        {
            services.AddSingleton<IActivityExecutionInspectionStore>(_ => _inspectionStore);
            services.AddSingleton<IRuntimeActivityExecutionInspectionAccumulator, RuntimeActivityExecutionInspectionAccumulator>();
            if (payloadCapturePolicy is null)
                services.AddSingleton<IRuntimePayloadCapturePolicy, DefaultRuntimePayloadCapturePolicy>();
            else
                services.AddSingleton<IRuntimePayloadCapturePolicy>(_ => payloadCapturePolicy);
        }
        return services.BuildServiceProvider();
    }

    private RuntimeSchedulerWorkItem NewInvokeWorkItem(
        WorkflowExecutableIdentity? pinnedExecutable = null,
        WorkflowExecutionCommandKind commandKind = WorkflowExecutionCommandKind.InvokeActivity,
        string executableNodeId = "node-start",
        JsonElement? payload = null,
        bool includePayload = true)
    {
        var resolvedPayload = includePayload
            ? payload ?? JsonSerializer.SerializeToElement(new RuntimeInvokeActivityCommandPayload(
                pinnedExecutable ?? NewIdentity(),
                executableNodeId,
                "actexec-1",
                RuntimeInvokeActivityCommandPayload.StartedActivityReason))
            : (JsonElement?)null;

        return new RuntimeSchedulerWorkItem(
            workItemId: "invoke-work",
            workflowExecutionId: "wfexec-1",
            commandId: "command-1",
            commandKind: commandKind,
            envelopeId: "envelope-1",
            idempotencyKey: "wfexec-1:invoke:actexec-1",
            enqueuedAt: _now,
            recordedAt: _now,
            sequence: 30,
            payload: resolvedPayload,
            commandMetadata: new Dictionary<string, string> { ["source"] = "test" },
            envelopeMetadata: new Dictionary<string, string> { ["transport"] = "in-process" });
    }

    private static ActivityExecutionState NewRunningState() =>
        new(
            Execution: new ActivityExecution(
                ActivityExecutionId: "actexec-1",
                WorkflowExecutionId: "wfexec-1",
                ExecutableNodeId: "node-start",
                AuthoredActivityId: "authored-node-start",
                ActivityType: "test/activity",
                ActivityTypeVersion: "1.0.0"),
            Status: ActivityExecutionStatus.Running,
            SubStatus: null,
            ScheduledAt: DateTimeOffset.UtcNow.AddMinutes(-2),
            StartedAt: DateTimeOffset.UtcNow.AddMinutes(-1),
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
            Metadata: new Dictionary<string, string> { ["runtime.startReason"] = "test" });

    private static WorkflowExecutable NewExecutable()
    {
        using var document = JsonDocument.Parse("""{"type":"test"}""");
        var start = NewNode("node-start", document.RootElement, LiteralTextBinding());
        var other = NewNode("node-other", document.RootElement);

        return new(
            identity: NewIdentity(),
            rootActivity: WithChildren(start, [other]),
            resumeTargets: new Dictionary<string, WorkflowExecutableResumeTarget>(),
            createdAt: DateTimeOffset.UtcNow,
            publishedAt: DateTimeOffset.UtcNow,
            compatibilityMetadata: new Dictionary<string, string>());
    }

    private static WorkflowExecutable NewExecutableWithInputBinding(RuntimeInputBinding inputBinding)
    {
        using var document = JsonDocument.Parse("""{"type":"test"}""");
        var start = NewNode("node-start", document.RootElement, inputBinding);

        return new(
            identity: NewIdentity(),
            rootActivity: start,
            resumeTargets: new Dictionary<string, WorkflowExecutableResumeTarget>(),
            createdAt: DateTimeOffset.UtcNow,
            publishedAt: DateTimeOffset.UtcNow,
            compatibilityMetadata: new Dictionary<string, string>());
    }

    private static WorkflowExecutable NewExecutableWithOutputCapture()
    {
        using var document = JsonDocument.Parse("""{"type":"test"}""");
        var start = NewNode(
            "node-start",
            document.RootElement,
            LiteralTextBinding(),
            new Dictionary<string, RuntimeOutputCapture>
            {
                ["customer"] = new(
                    outputName: "customer",
                    valueId: "customer",
                    type: new RuntimeValueTypeDescriptor("reference", "crm.customer", null),
                    lifecycle: DurableValueLifecycle.Instance,
                    storage: DurableValueStorage.Inline,
                    captureOnSuccessfulCompletion: true,
                    metadata: new Dictionary<string, string> { ["source"] = "activity" })
            });

        return new(
            identity: NewIdentity(),
            rootActivity: start,
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
                new ExecutableChildSlot("children", children)
            ]);

    private static ExecutableNode NewNode(
        string nodeId,
        JsonElement descriptorPayload,
        RuntimeInputBinding? inputBinding = null,
        IReadOnlyDictionary<string, RuntimeOutputCapture>? outputCaptures = null) =>
        new(
            executableNodeId: nodeId,
            authoredActivityId: $"authored-{nodeId}",
            activityType: "test/activity",
            activityTypeVersion: "1.0.0",
            descriptorType: "test",
            descriptorPayload: descriptorPayload.Clone(),
            inputBindings: inputBinding is null
                ? new Dictionary<string, RuntimeInputBinding>()
                : new Dictionary<string, RuntimeInputBinding> { ["Text"] = inputBinding },
            outputCaptures: outputCaptures ?? new Dictionary<string, RuntimeOutputCapture>(),
            metadata: new Dictionary<string, string>());

    private static RuntimeInputBinding LiteralTextBinding() =>
        new(
            inputName: "Text",
            source: RuntimeInputBindingSource.Literal,
            literalValue: JsonSerializer.SerializeToElement("hello"),
            metadata: new Dictionary<string, string> { [RuntimeActivityInputMaterializer.InputTypeMetadataKey] = $"{typeof(string).FullName}, {typeof(string).Assembly.GetName().Name}" });

    private static RuntimeInputBinding ActivityOutputTextBinding(string producerActivityExecutionId, string outputName) =>
        new(
            inputName: "Text",
            source: RuntimeInputBindingSource.ActivityOutput,
            activityOutput: new RuntimeActivityOutputReference(producerActivityExecutionId, "node-fetch", outputName),
            metadata: new Dictionary<string, string> { [RuntimeActivityInputMaterializer.InputTypeMetadataKey] = $"{typeof(string).FullName}, {typeof(string).Assembly.GetName().Name}" });

    private static RuntimeInputBinding DurableTextBinding(string valueId) =>
        new(
            inputName: "Text",
            source: RuntimeInputBindingSource.DurableValue,
            durableValue: new RuntimeDurableValueReference(valueId),
            metadata: new Dictionary<string, string> { [RuntimeActivityInputMaterializer.InputTypeMetadataKey] = $"{typeof(string).FullName}, {typeof(string).Assembly.GetName().Name}" });

    private static WorkflowExecutableIdentity NewIdentity() =>
        new("artifact-1", "definition-1", "version-1", "1.0.0", "sha256:test");

    private static JsonElement Json(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private sealed class RecordingActivityFactory(IActivity activity) : IActivityFactory
    {
        public int CreateCalls { get; private set; }
        public string? LastDescriptorType { get; private set; }
        public IDictionary<string, InputArgument> LastInputs { get; private set; } = new Dictionary<string, InputArgument>();
        public IDictionary<string, OutputArgument> LastOutputs { get; private set; } = new Dictionary<string, OutputArgument>();

        public ValueTask<IActivity> Create(
            string descriptorType,
            JsonElement payload,
            IDictionary<string, InputArgument>? inputs,
            IDictionary<string, OutputArgument>? outputs,
            CancellationToken cancellationToken = default)
        {
            CreateCalls++;
            LastDescriptorType = descriptorType;
            LastInputs = inputs ?? new Dictionary<string, InputArgument>();
            LastOutputs = outputs ?? new Dictionary<string, OutputArgument>();
            if (activity is RecordingActivity recordingActivity && LastInputs.TryGetValue("Text", out var text))
                recordingActivity.Text = (InputArgument<string>)text;
            if (activity is OutputProducingActivity outputProducingActivity && LastOutputs.TryGetValue("customer", out var customer))
                outputProducingActivity.Customer = (OutputArgument<object?>)customer;

            return ValueTask.FromResult(activity);
        }
    }

    private sealed class ThrowingActivityFactory(Exception exception) : IActivityFactory
    {
        public ValueTask<IActivity> Create(
            string descriptorType,
            JsonElement payload,
            IDictionary<string, InputArgument>? inputs,
            IDictionary<string, OutputArgument>? outputs,
            CancellationToken cancellationToken = default) =>
            throw exception;
    }

    private sealed class ThrowingSaveActivityExecutionStateStore(
        IActivityExecutionStateStore inner,
        ActivityExecutionStatus throwOnStatus,
        Exception exception) : IActivityExecutionStateStore
    {
        public List<ActivityExecutionStatus> AttemptedSaveStatuses { get; } = [];

        public ValueTask<ActivityExecutionState> SaveAsync(ActivityExecutionState state, CancellationToken cancellationToken = default)
        {
            AttemptedSaveStatuses.Add(state.Status);

            if (state.Status == throwOnStatus)
                throw exception;

            return inner.SaveAsync(state, cancellationToken);
        }

        public ValueTask<ActivityExecutionState?> FindAsync(string workflowExecutionId, string activityExecutionId, CancellationToken cancellationToken = default) =>
            inner.FindAsync(workflowExecutionId, activityExecutionId, cancellationToken);

        public ValueTask<IReadOnlyCollection<ActivityExecutionState>> ListAsync(string workflowExecutionId, CancellationToken cancellationToken = default) =>
            inner.ListAsync(workflowExecutionId, cancellationToken);
    }

    private class RecordingActivity : ActivityBase
    {
        public InputArgument<string> Text { get; set; } = null!;
        public bool ShouldExecute { get; set; } = true;
        public Exception? Exception { get; set; }
        public string[]? Outcomes { get; set; }
        public string? ObservedText { get; private set; }

        protected override ValueTask<bool> CanExecuteAsync(IActivityExecutionContext context) =>
            ValueTask.FromResult(ShouldExecute);

        protected override void Execute(IActivityExecutionContext context)
        {
            if (Exception is not null)
                throw Exception;

            ObservedText = context.Get(Text);
            if (Outcomes is not null)
                context.SetOutcomes(Outcomes);
        }
    }

    private sealed class BookmarkRequestingActivity(params ActivityBookmarkRequest[] requests) : RecordingActivity
    {
        protected override void Execute(IActivityExecutionContext context)
        {
            base.Execute(context);

            foreach (var request in requests)
                context.CreateBookmark(request);
        }
    }

    private class OutputProducingActivity(string customerJson) : RecordingActivity
    {
        public OutputArgument<object?> Customer { get; set; } = null!;

        protected override void Execute(IActivityExecutionContext context)
        {
            base.Execute(context);
            context.Set(Customer, Json(customerJson));
        }
    }

    private sealed class OutputBookmarkRequestingActivity(string customerJson, params ActivityBookmarkRequest[] requests) : OutputProducingActivity(customerJson)
    {
        protected override void Execute(IActivityExecutionContext context)
        {
            base.Execute(context);

            foreach (var request in requests)
                context.CreateBookmark(request);
        }
    }

    private sealed class ChildSchedulingActivity : RecordingActivity
    {
        protected override void Execute(IActivityExecutionContext context)
        {
            base.Execute(context);
            ((IRuntimeActivityExecutionContext)context).ScheduleChildActivity("node-other");
        }
    }

    private sealed class DuplicateBookmarkRequestActivity : ActivityBase
    {
        protected override void Execute(IActivityExecutionContext context)
        {
            context.CreateBookmark(new ActivityBookmarkRequest("bookmark-1", "resume-target:delivery", "event", "hash"));
            context.CreateBookmark(new ActivityBookmarkRequest("bookmark-1", "resume-target:delivery", "event", "hash"));
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class CaptureAllRuntimePayloadCapturePolicy : IRuntimePayloadCapturePolicy
    {
        public RuntimePayloadCaptureDecision Decide(RuntimePayloadCaptureRequest request) =>
            new(RuntimePayloadCaptureMode.Payload, "Test policy captures payloads.");
    }
}
