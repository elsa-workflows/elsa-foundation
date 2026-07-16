using System.Text.Json;
using Elsa.Activities.Runtime.Contracts;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Attributes;
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

public sealed partial class WorkflowResumeBookmarkSchedulerWorkHandlerTests
{
    private readonly DateTimeOffset _now = new(2026, 6, 11, 17, 0, 0, TimeSpan.Zero);
    private readonly InMemoryWorkflowExecutableStore _executableStore = new();
    private readonly InMemoryActivityExecutionStateStore _activityStateStore = new();
    private readonly InMemoryBookmarkStateStore _bookmarkStateStore = new();
    private readonly InMemoryWorkflowSchedulerWorkQueue _schedulerWorkQueue = new();
    private readonly InMemoryIncidentStateStore _incidentStateStore = new();
    private readonly InMemoryActivityExecutionInspectionStore _inspectionStore = new();
    private readonly InMemoryDurableValueStateStore _durableValueStateStore = new();
    private readonly InMemoryRuntimeCheckpointCommitStore _checkpointWriter;

    public WorkflowResumeBookmarkSchedulerWorkHandlerTests()
    {
        _checkpointWriter = new InMemoryRuntimeCheckpointCommitStore(
            workflowExecutionStateStore: null,
            activityExecutionStateStore: _activityStateStore,
            bookmarkStateStore: _bookmarkStateStore,
            durableValueStateStore: null,
            incidentStateStore: _incidentStateStore,
            operationalStateStore: null,
            schedulerStateStore: null,
            activityExecutionInspectionWriter: _inspectionStore);
    }

    [Fact]
    public async Task HandleAsync_InvokesResumeTargetAndEnqueuesCompletionWork()
    {
        var activity = new ResumeTargetActivity { Outcomes = ["Resumed"] };
        var factory = new RecordingActivityFactory(activity);
        await _executableStore.SaveAsync(NewExecutable());
        await _activityStateStore.SaveAsync(NewSuspendedState());
        await SaveBookmarkAsync();
        await using var provider = NewProvider(factory);
        var handler = NewHandler(provider);

        await handler.HandleAsync(NewResumeWorkItem());

        Assert.True(activity.ContextResumeInvoked);
        Assert.Equal("actexec-1", activity.Id);
        Assert.Equal("node-wait", activity.NodeId);
        Assert.Equal("test", factory.LastDescriptorType);
        var state = await _activityStateStore.FindAsync("wfexec-1", "actexec-1");
        Assert.NotNull(state);
        Assert.Equal(ActivityExecutionStatus.Completed, state.Status);
        Assert.Null(state.SubStatus);
        Assert.Equal(_now, state.CompletedAt);
        Assert.Equal(RuntimeResumeBookmarkCommandPayload.StimulusMatchedReason, state.Metadata["runtime.resumeReason"]);
        Assert.Equal("resume-work", state.Metadata["runtime.resumeSchedulerWorkItemId"]);
        Assert.Equal("bookmark-1", state.Metadata["runtime.bookmarkId"]);
        Assert.Equal("resume-target:delivery", state.Metadata["runtime.resumeTargetId"]);
        var completionPayload = await AssertCompletionWorkAsync();
        Assert.Equal("actexec-1", completionPayload.ActivityExecutionId);
        Assert.Equal("node-wait", completionPayload.ExecutableNodeId);
        Assert.Equal(["Resumed"], completionPayload.OutcomeNames);
        await AssertBookmarkConsumedCheckpointAsync();
    }

    [Fact]
    public async Task HandleAsync_PopulatesResumedActivityCarrierIdentityFromDurableValues()
    {
        // The resumed activity's own execution-time carrier (ADR 0030) reads identity from the IdentityName-tagged
        // durable values the resume path already lists (spec 083 review), so getCorrelationId() /
        // getWorkflowInstanceName() are live inside the resume target too.
        var activity = new ResumeTargetActivity { Outcomes = ["Resumed"] };
        foreach (var change in RuntimeWorkflowStateSeed.BuildIdentityChanges(
                     "wfexec-1", correlationIdAssignmentRequested: true, "order-123",
                     instanceNameAssignmentRequested: true, "Order 123", _now))
            await _durableValueStateStore.SaveAsync(change.State!);
        await _executableStore.SaveAsync(NewExecutable());
        await _activityStateStore.SaveAsync(NewSuspendedState());
        await SaveBookmarkAsync();
        await using var provider = NewProvider(new RecordingActivityFactory(activity));
        var handler = NewHandler(provider);

        await handler.HandleAsync(NewResumeWorkItem());

        Assert.Equal("order-123", activity.ObservedCorrelationId);
        Assert.Equal("Order 123", activity.ObservedWorkflowName);
        await AssertCompletionWorkAsync();
    }

    [Fact]
    public async Task HandleAsync_PassesJsonInputToResumeTarget()
    {
        var activity = new ResumeTargetActivity();
        await _executableStore.SaveAsync(NewExecutable(resumeTargetId: "resume-target:input"));
        await _activityStateStore.SaveAsync(NewSuspendedState());
        await SaveBookmarkAsync(resumeTargetId: "resume-target:input");
        await using var provider = NewProvider(new RecordingActivityFactory(activity));
        var handler = NewHandler(provider);

        await handler.HandleAsync(NewResumeWorkItem(resumeTargetId: "resume-target:input"));

        Assert.Equal("order-123", activity.ObservedOrderId);
        await AssertCompletionWorkAsync();
    }

    [Fact]
    public async Task HandleAsync_CapturesOutputSnapshotsWhenResumeTargetCompletes()
    {
        var activity = new OutputProducingResumeTargetActivity("""{"customerId":"customer-123"}""");
        await _executableStore.SaveAsync(NewExecutable(includeOutputCapture: true));
        await _activityStateStore.SaveAsync(NewSuspendedState());
        await SaveBookmarkAsync();
        await using var provider = NewProvider(new RecordingActivityFactory(activity));
        var handler = NewHandler(provider);

        await handler.HandleAsync(NewResumeWorkItem());

        var projection = await _inspectionStore.FindAsync("wfexec-1", "actexec-1");
        Assert.NotNull(projection);
        var outputSnapshot = Assert.Single(projection.ValueSnapshots, snapshot => snapshot.Subject == ActivityExecutionInspectionValueSubject.ActivityOutput);
        Assert.Equal("customer", outputSnapshot.Name);
        Assert.Equal(RuntimePayloadCaptureMode.DiagnosticSnapshot, outputSnapshot.CaptureMode);
        var snapshot = Assert.IsType<JsonElement>(outputSnapshot.Payload);
        Assert.Equal("object", snapshot.GetProperty("kind").GetString());
        Assert.Equal("customer-123", snapshot.GetProperty("properties")[0].GetProperty("value").GetProperty("preview").GetString());
    }

    [Fact]
    public async Task HandleAsync_FaultsActivityWhenResumeTargetIsMissingOnActivityType()
    {
        await _executableStore.SaveAsync(NewExecutable());
        await _activityStateStore.SaveAsync(NewSuspendedState());
        await SaveBookmarkAsync();
        await using var provider = NewProvider(new RecordingActivityFactory(new ActivityWithoutResumeTarget()));
        var handler = NewHandler(provider);

        await handler.HandleAsync(NewResumeWorkItem());

        var state = await _activityStateStore.FindAsync("wfexec-1", "actexec-1");
        Assert.NotNull(state);
        Assert.Equal(ActivityExecutionStatus.Faulted, state.Status);
        Assert.Equal("ActivityResumeFaulted", state.SubStatus);
        Assert.Equal(1, state.FaultCount);
        Assert.Contains("does not declare resume target 'resume-target:delivery'", state.Metadata["runtime.faultMessage"]);
        await AssertIncidentRecordedAsync("ActivityResumeFaulted", message => Assert.Contains("does not declare resume target 'resume-target:delivery'", message));
        Assert.Empty(await _schedulerWorkQueue.ListAsync(new RuntimeSchedulerWorkQuery("wfexec-1")));
        Assert.NotNull(await _bookmarkStateStore.FindAsync("wfexec-1", "bookmark-1"));
    }

    [Fact]
    public async Task HandleAsync_FaultsActivityWhenResumeTargetSignatureIsInvalid()
    {
        await _executableStore.SaveAsync(NewExecutable(resumeTargetId: "resume-target:invalid"));
        await _activityStateStore.SaveAsync(NewSuspendedState());
        await SaveBookmarkAsync(resumeTargetId: "resume-target:invalid");
        await using var provider = NewProvider(new RecordingActivityFactory(new InvalidResumeTargetActivity()));
        var handler = NewHandler(provider);

        await handler.HandleAsync(NewResumeWorkItem(resumeTargetId: "resume-target:invalid"));

        var state = await _activityStateStore.FindAsync("wfexec-1", "actexec-1");
        Assert.NotNull(state);
        Assert.Equal(ActivityExecutionStatus.Faulted, state.Status);
        Assert.Equal("ActivityResumeFaulted", state.SubStatus);
        Assert.Contains("unsupported signature", state.Metadata["runtime.faultMessage"]);
        await AssertIncidentRecordedAsync("ActivityResumeFaulted", message => Assert.Contains("unsupported signature", message), resumeTargetId: "resume-target:invalid");
        Assert.Empty(await _schedulerWorkQueue.ListAsync(new RuntimeSchedulerWorkQuery("wfexec-1")));
        Assert.NotNull(await _bookmarkStateStore.FindAsync("wfexec-1", "bookmark-1"));
    }

    [Fact]
    public async Task HandleAsync_FaultsActivityWithInnerExceptionWhenResumeTargetThrows()
    {
        await _executableStore.SaveAsync(NewExecutable(resumeTargetId: "resume-target:throwing"));
        await _activityStateStore.SaveAsync(NewSuspendedState());
        await SaveBookmarkAsync(resumeTargetId: "resume-target:throwing");
        await using var provider = NewProvider(new RecordingActivityFactory(new ThrowingResumeTargetActivity()));
        var handler = NewHandler(provider);

        await handler.HandleAsync(NewResumeWorkItem(resumeTargetId: "resume-target:throwing"));

        var state = await _activityStateStore.FindAsync("wfexec-1", "actexec-1");
        Assert.NotNull(state);
        Assert.Equal(ActivityExecutionStatus.Faulted, state.Status);
        Assert.Equal("ActivityResumeFaulted", state.SubStatus);
        Assert.Equal(typeof(InvalidOperationException).FullName, state.Metadata["runtime.faultType"]);
        Assert.Equal("resume failed", state.Metadata["runtime.faultMessage"]);
        await AssertIncidentRecordedAsync("ActivityResumeFaulted", message => Assert.Equal("resume failed", message), resumeTargetId: "resume-target:throwing");
        Assert.Empty(await _schedulerWorkQueue.ListAsync(new RuntimeSchedulerWorkQuery("wfexec-1")));
        Assert.NotNull(await _bookmarkStateStore.FindAsync("wfexec-1", "bookmark-1"));
    }

    [Fact]
    public async Task HandleAsync_PropagatesFaultToParent_WhenResumeFaultsAndChildHasParent()
    {
        // A suspended branch that faults on resume must propagate the fault to its parent fork/join so the
        // join resolves deterministically (#308), mirroring the invoke-path propagation. The child keeps its
        // own blocking incident; a child-fault parent-evaluation work item rides along on the incident
        // checkpoint, tagged so the parent-completion handler routes it to OnChildFaultedAsync.
        await _executableStore.SaveAsync(NewExecutable(resumeTargetId: "resume-target:throwing"));
        await _activityStateStore.SaveAsync(NewParentState());
        await _activityStateStore.SaveAsync(NewSuspendedState() with { ParentActivityExecutionId = "actexec-parent" });
        await SaveBookmarkAsync(resumeTargetId: "resume-target:throwing");
        await using var provider = NewProvider(new RecordingActivityFactory(new ThrowingResumeTargetActivity()));
        var handler = NewHandler(provider);

        await handler.HandleAsync(NewResumeWorkItem(resumeTargetId: "resume-target:throwing"));

        var state = await _activityStateStore.FindAsync("wfexec-1", "actexec-1");
        Assert.NotNull(state);
        Assert.Equal(ActivityExecutionStatus.Faulted, state.Status);

        var parentEvaluation = AssertSchedulerPostCommitWork(WorkflowExecutionCommandKind.CompleteActivity);
        Assert.Equal(bool.TrueString, parentEvaluation.CommandMetadata[RuntimeMetadataKeys.ChildFaulted]);
        Assert.Equal("incident:resume-work:actexec-1:ActivityResumeFaulted", parentEvaluation.CommandMetadata[RuntimeMetadataKeys.IncidentId]);
        Assert.NotNull(parentEvaluation.Payload);
        var payload = parentEvaluation.Payload.Value.Deserialize<RuntimeCompleteActivityCommandPayload>()!;
        Assert.Equal(SchedulerCompletionKind.ParentCompletionEvaluation, payload.CompletionKind);
        Assert.Equal("actexec-parent", payload.ActivityExecutionId);
        Assert.Equal("node-parent", payload.ExecutableNodeId);
        Assert.Equal("actexec-1", payload.CompletedChildActivityExecutionId);
    }

    [Fact]
    public async Task HandleAsync_RecordsIncidentWhenInputMaterializationFails()
    {
        await _executableStore.SaveAsync(NewExecutable(inputBinding: new RuntimeInputBinding(
            inputName: "Text",
            source: RuntimeInputBindingSource.Expression,
            expression: new RuntimeExpressionBinding("JavaScript", "workflow.input"))));
        await _activityStateStore.SaveAsync(NewSuspendedState());
        await SaveBookmarkAsync();
        var factory = new RecordingActivityFactory(new ResumeTargetActivity());
        await using var provider = NewProvider(factory);
        var handler = NewHandler(provider);

        await handler.HandleAsync(NewResumeWorkItem());

        Assert.Equal(0, factory.CreateCalls);
        var state = await _activityStateStore.FindAsync("wfexec-1", "actexec-1");
        Assert.NotNull(state);
        Assert.Equal(ActivityExecutionStatus.Faulted, state.Status);
        Assert.Equal("InputMaterializationFailed", state.SubStatus);
        Assert.Equal(_now, state.CompletedAt);
        Assert.Contains("typeName", state.Metadata["runtime.faultMessage"]);
        await AssertIncidentRecordedAsync("InputMaterializationFailed", message => Assert.Contains("typeName", message));
        Assert.Empty(await _schedulerWorkQueue.ListAsync(new RuntimeSchedulerWorkQuery("wfexec-1")));
        Assert.NotNull(await _bookmarkStateStore.FindAsync("wfexec-1", "bookmark-1"));
    }

    [Fact]
    public async Task HandleAsync_RecordsFaultedStateWhenActivityConstructionFails()
    {
        await _executableStore.SaveAsync(NewExecutable());
        await _activityStateStore.SaveAsync(NewSuspendedState());
        await SaveBookmarkAsync();
        var factory = new ThrowingActivityFactory(new InvalidOperationException("missing constructor"));
        await using var provider = NewProvider(factory);
        var handler = NewHandler(provider);

        await handler.HandleAsync(NewResumeWorkItem());

        var state = await _activityStateStore.FindAsync("wfexec-1", "actexec-1");
        Assert.NotNull(state);
        Assert.Equal(ActivityExecutionStatus.Faulted, state.Status);
        Assert.Equal("ActivityResumeConstructionFailed", state.SubStatus);
        Assert.Equal(_now, state.CompletedAt);
        Assert.Equal(1, state.FaultCount);
        Assert.Equal(typeof(InvalidOperationException).FullName, state.Metadata["runtime.faultType"]);
        Assert.Equal("missing constructor", state.Metadata["runtime.faultMessage"]);
        await AssertIncidentRecordedAsync("ActivityResumeConstructionFailed", message => Assert.Equal("missing constructor", message));
        Assert.Empty(await _schedulerWorkQueue.ListAsync(new RuntimeSchedulerWorkQuery("wfexec-1")));
        Assert.NotNull(await _bookmarkStateStore.FindAsync("wfexec-1", "bookmark-1"));
    }

    [Fact]
    public async Task HandleAsync_RecordsFaultedStateWhenArgumentBindingThrows()
    {
        // Models a binder InvalidOperationException (the #313/#316 class of bug): the argument binder runs inside
        // activityFactory.Create, so a typed-binding failure on the resume path must fault the activity with a
        // blocking incident rather than escaping the fault boundary and stalling the run silently at Running (#325).
        await _executableStore.SaveAsync(NewExecutable());
        await _activityStateStore.SaveAsync(NewSuspendedState());
        await SaveBookmarkAsync();
        var factory = new ThrowingActivityFactory(new InvalidOperationException("cannot bind argument 'Value'"));
        await using var provider = NewProvider(factory);
        var handler = NewHandler(provider);

        await handler.HandleAsync(NewResumeWorkItem());

        var state = await _activityStateStore.FindAsync("wfexec-1", "actexec-1");
        Assert.NotNull(state);
        Assert.Equal(ActivityExecutionStatus.Faulted, state.Status);
        Assert.Equal("ActivityResumeConstructionFailed", state.SubStatus);
        await AssertIncidentRecordedAsync("ActivityResumeConstructionFailed", message => Assert.Contains("cannot bind argument", message));

        var projection = await _inspectionStore.FindAsync("wfexec-1", "actexec-1");
        Assert.NotNull(projection);
        Assert.Equal(ActivityExecutionStatus.Faulted, projection.Status);
        Assert.Equal("ActivityResumeConstructionFailed", projection.SubStatus);
        var incident = Assert.Single(projection.Incidents);
        Assert.Equal("ActivityResumeConstructionFailed", incident.FailureType);
        Assert.True(incident.IsBlocking);
        Assert.Empty(await _schedulerWorkQueue.ListAsync(new RuntimeSchedulerWorkQuery("wfexec-1")));
        Assert.NotNull(await _bookmarkStateStore.FindAsync("wfexec-1", "bookmark-1"));
    }

    [Fact]
    public async Task HandleAsync_ConsumesBookmarkAndReenqueuesCompletionWorkForExistingCompletedState()
    {
        var activity = new ResumeTargetActivity();
        var factory = new RecordingActivityFactory(activity);
        await _executableStore.SaveAsync(NewExecutable());
        await _activityStateStore.SaveAsync(NewSuspendedState() with
        {
            Status = ActivityExecutionStatus.Completed,
            CompletedAt = _now.AddMinutes(-1)
        });
        await SaveBookmarkAsync();
        await using var provider = NewProvider(factory);
        var handler = NewHandler(provider);

        await handler.HandleAsync(NewResumeWorkItem());

        Assert.Equal(0, factory.CreateCalls);
        Assert.False(activity.ContextResumeInvoked);
        var completionPayload = await AssertCompletionWorkAsync();
        Assert.Equal([ActivityOutcomes.Done], completionPayload.OutcomeNames);
        await AssertBookmarkConsumedCheckpointAsync();
    }

    [Fact]
    public async Task HandleAsync_DoesNotInvokeActivityForMissingBookmarkAndNonCompletedState()
    {
        var activity = new ResumeTargetActivity();
        var factory = new RecordingActivityFactory(activity);
        await _executableStore.SaveAsync(NewExecutable());
        await _activityStateStore.SaveAsync(NewSuspendedState());
        await using var provider = NewProvider(factory);
        var handler = NewHandler(provider);

        await handler.HandleAsync(NewResumeWorkItem());

        Assert.Equal(0, factory.CreateCalls);
        Assert.False(activity.ContextResumeInvoked);
        var state = await _activityStateStore.FindAsync("wfexec-1", "actexec-1");
        Assert.NotNull(state);
        Assert.Equal(ActivityExecutionStatus.Suspended, state.Status);
        Assert.Empty(await _schedulerWorkQueue.ListAsync(new RuntimeSchedulerWorkQuery("wfexec-1")));
        Assert.Empty(_checkpointWriter.ListCommits());
    }

    [Fact]
    public async Task HandleAsync_DoesNotInvokeActivityForRecoveredState()
    {
        var activity = new ResumeTargetActivity();
        var factory = new RecordingActivityFactory(activity);
        await _executableStore.SaveAsync(NewExecutable());
        await _activityStateStore.SaveAsync(NewSuspendedState() with { Status = ActivityExecutionStatus.Recovered });
        await SaveBookmarkAsync();
        await using var provider = NewProvider(factory);
        var handler = NewHandler(provider);

        await handler.HandleAsync(NewResumeWorkItem());

        Assert.Equal(0, factory.CreateCalls);
        Assert.False(activity.ContextResumeInvoked);
        Assert.NotNull(await _bookmarkStateStore.FindAsync("wfexec-1", "bookmark-1"));
        Assert.Empty(_checkpointWriter.ListCommits());
    }

    [Fact]
    public async Task HandleAsync_RejectsBookmarkIdentityMismatchBeforeInvokingActivity()
    {
        var activity = new ResumeTargetActivity();
        var factory = new RecordingActivityFactory(activity);
        await _executableStore.SaveAsync(NewExecutable());
        await _activityStateStore.SaveAsync(NewSuspendedState());
        await SaveBookmarkAsync(stimulusHash: "sha256:delivery-status:other-order");
        await using var provider = NewProvider(factory);
        var handler = NewHandler(provider);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => handler.HandleAsync(NewResumeWorkItem()).AsTask());

        Assert.Contains("references stimulus hash 'sha256:delivery-status:order-123'", exception.Message);
        Assert.Equal(0, factory.CreateCalls);
        Assert.NotNull(await _bookmarkStateStore.FindAsync("wfexec-1", "bookmark-1"));
        Assert.Empty(_checkpointWriter.ListCommits());
    }

    [Fact]
    public async Task HandleAsync_RejectsMissingArtifactResumeTargetBeforeInvokingActivity()
    {
        await _executableStore.SaveAsync(NewExecutable(includeResumeTarget: false));
        await _activityStateStore.SaveAsync(NewSuspendedState());
        var factory = new RecordingActivityFactory(new ResumeTargetActivity());
        await using var provider = NewProvider(factory);
        var handler = NewHandler(provider);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => handler.HandleAsync(NewResumeWorkItem()).AsTask());

        Assert.Contains("references resume target 'resume-target:delivery'", exception.Message);
        Assert.Equal(0, factory.CreateCalls);
    }

    [Fact]
    public void CanHandle_AcceptsOnlyResumeBookmarkWork()
    {
        using var provider = NewProvider(new RecordingActivityFactory(new ResumeTargetActivity()));
        var handler = NewHandler(provider);

        Assert.True(handler.CanHandle(NewResumeWorkItem()));
        Assert.False(handler.CanHandle(NewResumeWorkItem(commandKind: WorkflowExecutionCommandKind.InvokeActivity)));
    }

    private WorkflowResumeBookmarkSchedulerWorkHandler NewHandler(ServiceProvider provider) =>
        new(
            new RuntimeActivityInputMaterializer(),
            provider.GetRequiredService<IServiceScopeFactory>(),
            new FixedTimeProvider(_now));

    private Task<RuntimeCompleteActivityCommandPayload> AssertCompletionWorkAsync()
    {
        var completionWork = AssertSchedulerPostCommitWork(WorkflowExecutionCommandKind.CompleteActivity);
        Assert.Equal(WorkflowExecutionCommandKind.CompleteActivity, completionWork.CommandKind);
        Assert.Equal("resume-work:complete:actexec-1", completionWork.WorkItemId);
        Assert.Equal("command-1:complete:actexec-1", completionWork.CommandId);
        Assert.Equal("wfexec-1:resume:bookmark-1:complete:actexec-1", completionWork.IdempotencyKey);
        Assert.Equal(41, completionWork.Sequence);
        Assert.NotNull(completionWork.Payload);
        return Task.FromResult(completionWork.Payload.Value.Deserialize<RuntimeCompleteActivityCommandPayload>()!);
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

    private ServiceProvider NewProvider(IActivityFactory factory, IActivityActivator? activityActivator = null)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => factory);
        services.AddSingleton(_executableStore);
        services.AddSingleton<IWorkflowExecutableStore>(_ => _executableStore);
        services.AddSingleton(_activityStateStore);
        services.AddSingleton<IActivityExecutionStateStore>(_ => _activityStateStore);
        services.AddSingleton(_bookmarkStateStore);
        services.AddSingleton<IBookmarkStateStore>(_ => _bookmarkStateStore);
        services.AddSingleton(_schedulerWorkQueue);
        services.AddSingleton<IWorkflowSchedulerWorkQueue>(_ => _schedulerWorkQueue);
        services.AddSingleton<IRuntimeActivityOutputRegister, InMemoryRuntimeActivityOutputRegister>();
        services.AddSingleton<IDurableValueStateStore>(_ => _durableValueStateStore);
        services.AddSingleton<TimeProvider>(new FixedTimeProvider(_now));
        services.AddSingleton<IRuntimeCheckpointPersistencePolicy, ImmediateRuntimeCheckpointPersistencePolicy>();
        services.AddSingleton<IRuntimeCheckpointCommitStore>(_checkpointWriter);
        services.AddSingleton(_incidentStateStore);
        services.AddSingleton<IIncidentStateStore>(_ => _incidentStateStore);
        services.AddSingleton<IActivityExecutionInspectionStore>(_ => _inspectionStore);
        services.AddSingleton<IRuntimeActivityExecutionInspectionAccumulator, RuntimeActivityExecutionInspectionAccumulator>();
        services.AddSingleton<IRuntimePostCommitIntentDispatcher, RuntimeSchedulerPostCommitIntentDispatcher>();
        services.AddSingleton<RuntimeCheckpointCommitter>();
        services.AddSingleton<ActivityFaultIncidentRecorder>();
        services.AddSingleton<IBookmarkConsumptionCheckpointService, BookmarkConsumptionCheckpointService>();
        services.AddSingleton<ActivityCompletionProjector>();
        if (activityActivator is not null)
            services.AddSingleton(activityActivator);
        return services.BuildServiceProvider();
    }

    private ValueTask<BookmarkState> SaveBookmarkAsync(
        string resumeTargetId = "resume-target:delivery",
        string stimulusType = "delivery-status",
        string stimulusHash = "sha256:delivery-status:order-123") =>
        _bookmarkStateStore.SaveAsync(NewBookmark(resumeTargetId, stimulusType, stimulusHash));

    private async Task AssertBookmarkConsumedCheckpointAsync()
    {
        Assert.Null(await _bookmarkStateStore.FindAsync("wfexec-1", "bookmark-1"));
        var write = Assert.Single(_checkpointWriter.ListCommits());
        Assert.Equal(RuntimeCheckpointPersistenceMode.Immediate, write.Decision.Mode);
        Assert.Equal("commit:resume-work:bookmark-consumed:bookmark-1", write.Commit.CommitId);
        Assert.Equal("checkpoint:resume-work:bookmark-consumed:bookmark-1", write.Commit.Checkpoint.CheckpointId);
        Assert.Equal(RuntimeCheckpointNames.BookmarkConsumed, write.Commit.Checkpoint.Name);
        Assert.Equal(["actexec-1"], write.Commit.Checkpoint.ActivityExecutionIds);
        Assert.Equal("bookmark-1", write.Commit.Checkpoint.Metadata["runtime.bookmarkId"]);
        Assert.Equal(RuntimeMetadataKeys.CheckpointRequirementMandatory, write.Commit.Checkpoint.Metadata[RuntimeMetadataKeys.CheckpointRequirement]);

        var activityChange = Assert.Single(write.Commit.StateChanges.ActivityExecutions);
        Assert.Equal(RuntimeStateChangeOperation.Upsert, activityChange.Operation);
        Assert.Equal(ActivityExecutionStatus.Completed, activityChange.State.Status);

        var bookmarkChange = Assert.Single(write.Commit.StateChanges.Bookmarks);
        Assert.Equal(RuntimeStateChangeOperation.Delete, bookmarkChange.Operation);
        Assert.Equal("bookmark-1", bookmarkChange.State.BookmarkId);
        var inspectionChange = Assert.Single(write.Commit.StateChanges.ActivityExecutionInspections);
        Assert.Equal(ActivityExecutionStatus.Completed, inspectionChange.State.Status);
        Assert.Equal(new[] { "actexec-1" }, write.Commit.PostCommitIntents.Select(intent => intent.ActivityExecutionId).ToArray());

        var projection = await _inspectionStore.FindAsync("wfexec-1", "actexec-1");
        Assert.NotNull(projection);
        Assert.Equal(ActivityExecutionStatus.Completed, projection.Status);
    }

    private async Task AssertIncidentRecordedAsync(string failureType, Action<string> assertMessage, string resumeTargetId = "resume-target:delivery")
    {
        var incidentId = $"incident:resume-work:actexec-1:{failureType}";
        var state = await _activityStateStore.FindAsync("wfexec-1", "actexec-1");
        Assert.NotNull(state);
        Assert.Equal([incidentId], state.IncidentIds);
        Assert.Equal(incidentId, state.Metadata["runtime.incidentId"]);
        Assert.Equal("bookmark-1", state.Metadata["runtime.bookmarkId"]);
        Assert.Equal(resumeTargetId, state.Metadata["runtime.resumeTargetId"]);

        var incident = await _incidentStateStore.FindAsync("wfexec-1", incidentId);
        Assert.NotNull(incident);
        Assert.Equal("wfexec-1", incident.WorkflowExecutionId);
        Assert.Equal("actexec-1", incident.ActivityExecutionId);
        Assert.Equal("node-wait", incident.ExecutableNodeId);
        Assert.Equal(IncidentSeverity.Error, incident.Severity);
        Assert.Equal(IncidentStatus.Blocking, incident.Status);
        Assert.Equal(IncidentResolutionAction.WaitForIntervention, incident.ResolutionAction);
        Assert.Equal(failureType, incident.FailureType);
        Assert.Equal(_now, incident.CreatedAt);
        Assert.Null(incident.ResolvedAt);
        Assert.Equal("resume-work", incident.Metadata["runtime.schedulerWorkItemId"]);
        Assert.Equal("command-1", incident.Metadata["runtime.commandId"]);
        Assert.Equal(failureType, incident.Metadata["runtime.faultSubStatus"]);
        Assert.Equal("bookmark-1", incident.Metadata["runtime.bookmarkId"]);
        Assert.Equal(resumeTargetId, incident.Metadata["runtime.resumeTargetId"]);
        assertMessage(incident.Message);

        var write = Assert.Single(_checkpointWriter.ListCommits());
        Assert.Equal(RuntimeCheckpointNames.IncidentRecorded, write.Commit.Checkpoint.Name);
        Assert.Equal(incidentId, write.Commit.Checkpoint.Metadata["runtime.incidentId"]);
        Assert.Equal("bookmark-1", write.Commit.Checkpoint.Metadata["runtime.bookmarkId"]);
        Assert.Equal(resumeTargetId, write.Commit.Checkpoint.Metadata["runtime.resumeTargetId"]);
        Assert.Equal(["actexec-1"], write.Commit.Checkpoint.ActivityExecutionIds);
        Assert.Empty(write.Commit.StateChanges.Bookmarks);
    }

    private RuntimeSchedulerWorkItem NewResumeWorkItem(
        WorkflowExecutionCommandKind commandKind = WorkflowExecutionCommandKind.ResumeBookmark,
        string resumeTargetId = "resume-target:delivery",
        JsonElement? input = null,
        RuntimeTypedTriggerDeliveryMetadata? triggerDelivery = null)
    {
        var payload = JsonSerializer.SerializeToElement(new RuntimeResumeBookmarkCommandPayload(
            pinnedExecutable: NewIdentity(),
            bookmarkId: "bookmark-1",
            activityExecutionId: "actexec-1",
            executableNodeId: "node-wait",
            resumeTargetId: resumeTargetId,
            stimulusType: "delivery-status",
            stimulusHash: "sha256:delivery-status:order-123",
            input: input ?? Json("""{"orderId":"order-123"}"""),
            reason: RuntimeResumeBookmarkCommandPayload.StimulusMatchedReason,
            triggerDelivery: triggerDelivery));

        return new RuntimeSchedulerWorkItem(
            workItemId: "resume-work",
            workflowExecutionId: "wfexec-1",
            commandId: "command-1",
            commandKind: commandKind,
            envelopeId: "envelope-1",
            idempotencyKey: "wfexec-1:resume:bookmark-1",
            enqueuedAt: _now,
            recordedAt: _now,
            sequence: 40,
            payload: payload,
            commandMetadata: new Dictionary<string, string> { ["source"] = "test" },
            envelopeMetadata: new Dictionary<string, string> { ["transport"] = "in-process" });
    }

    private static ActivityExecutionState NewSuspendedState() =>
        new(
            Execution: new ActivityExecution(
                ActivityExecutionId: "actexec-1",
                WorkflowExecutionId: "wfexec-1",
                ExecutableNodeId: "node-wait",
                AuthoredActivityId: "authored-node-wait",
                ActivityType: "test/activity",
                ActivityTypeVersion: "1.0.0"),
            Status: ActivityExecutionStatus.Suspended,
            SubStatus: "BookmarkWaiting",
            ScheduledAt: DateTimeOffset.UtcNow.AddMinutes(-3),
            StartedAt: DateTimeOffset.UtcNow.AddMinutes(-2),
            CompletedAt: null,
            SchedulingActivityExecutionId: null,
            ParentActivityExecutionId: null,
            BranchId: null,
            IterationId: null,
            CallStackDepth: null,
            BookmarkIds: ["bookmark-1"],
            IncidentIds: [],
            FaultCount: 0,
            AggregateFaultCount: 0,
            Metadata: new Dictionary<string, string>());

    // A minimal Running parent execution for the faulted branch, so child-fault propagation can resolve the
    // parent's executable node when building the parent-evaluation work item.
    private static ActivityExecutionState NewParentState() =>
        new(
            Execution: new ActivityExecution(
                ActivityExecutionId: "actexec-parent",
                WorkflowExecutionId: "wfexec-1",
                ExecutableNodeId: "node-parent",
                AuthoredActivityId: "authored-node-parent",
                ActivityType: "test/parallel",
                ActivityTypeVersion: "1.0.0"),
            Status: ActivityExecutionStatus.Running,
            SubStatus: null,
            ScheduledAt: DateTimeOffset.UtcNow.AddMinutes(-4),
            StartedAt: DateTimeOffset.UtcNow.AddMinutes(-3),
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
            Metadata: new Dictionary<string, string>());

    private static BookmarkState NewBookmark(
        string resumeTargetId,
        string stimulusType,
        string stimulusHash) =>
        new(
            BookmarkId: "bookmark-1",
            WorkflowExecutionId: "wfexec-1",
            ActivityExecutionId: "actexec-1",
            ExecutableNodeId: "node-wait",
            ResumeTargetId: resumeTargetId,
            StimulusType: stimulusType,
            StimulusHash: stimulusHash,
            Payload: null,
            Metadata: new Dictionary<string, string>(),
            CreatedAt: DateTimeOffset.UtcNow.AddMinutes(-1),
            ExpiresAt: null);

    private static WorkflowExecutable NewExecutable(
        bool includeResumeTarget = true,
        string resumeTargetId = "resume-target:delivery",
        RuntimeInputBinding? inputBinding = null,
        bool includeOutputCapture = false,
        ActivityContract? activityContract = null)
    {
        using var document = JsonDocument.Parse("""{"type":"test"}""");
        var resumeTargets = includeResumeTarget
            ? new Dictionary<string, WorkflowExecutableResumeTarget>
            {
                [resumeTargetId] = new(
                    ResumeTargetId: resumeTargetId,
                    ExecutableNodeId: "node-wait",
                    HandlerKey: "test-handler",
                    Metadata: new Dictionary<string, string>())
            }
            : new Dictionary<string, WorkflowExecutableResumeTarget>();

        return new(
            identity: NewIdentity(),
            rootActivity: NewNode("node-wait", document.RootElement, inputBinding, includeOutputCapture, activityContract),
            resumeTargets: resumeTargets,
            createdAt: DateTimeOffset.UtcNow,
            compatibilityMetadata: new Dictionary<string, string>());
    }

    private static ExecutableNode NewNode(
        string nodeId,
        JsonElement descriptorPayload,
        RuntimeInputBinding? inputBinding = null,
        bool includeOutputCapture = false,
        ActivityContract? activityContract = null) =>
        new(
            executableNodeId: nodeId,
            authoredActivityId: $"authored-{nodeId}",
            activityType: "test/activity",
            activityTypeVersion: "1.0.0",
            descriptorType: "test",
            descriptorPayload: descriptorPayload.Clone(),
            inputBindings: inputBinding is null
                ? new Dictionary<string, RuntimeInputBinding>()
                : new Dictionary<string, RuntimeInputBinding> { [inputBinding.InputName] = inputBinding },
            outputCaptures: includeOutputCapture
                ? new Dictionary<string, RuntimeOutputCapture>
                {
                    ["customer"] = new(
                        outputName: "customer",
                        valueId: "customer",
                        type: new RuntimeValueTypeDescriptor("json", "object", null),
                        lifecycle: DurableValueLifecycle.Instance,
                        storage: DurableValueStorage.Inline,
                        captureOnSuccessfulCompletion: false,
                        metadata: new Dictionary<string, string>())
                }
                : new Dictionary<string, RuntimeOutputCapture>(),
            metadata: new Dictionary<string, string>(),
            activityContract: activityContract);

    private static WorkflowExecutableIdentity NewIdentity() =>
        new("artifact-1", "definition-1", "version-1", "1.0.0", "sha256:test");

    private static WorkflowExecutable NewTypedExecutable(string currentLiteral)
    {
        var descriptor = JsonSerializer.SerializeToElement(new { type = "typed-resume" });
        var valueType = new Elsa.Primitives.Models.ValueTypeDescriptor("String");
        var contract = new ActivityContract(
            "test/typed-resume",
            "1.0.0",
            "typed-resume",
            descriptor,
            [new ActivityInputContract("text", "Text", valueType, true, false, null, ActivityValuePolicy.Default)],
            new ActivityResultContract(new Elsa.Primitives.Models.ValueTypeDescriptor("Elsa.Unit"), true, ActivityValuePolicy.Default, []),
            ["Done"],
            new ActivityActivationRequirement("typed-resume", "test/typed-resume"));
        var binding = new RuntimeInputBinding(
            inputKey: "text",
            targetType: valueType,
            effectivePolicy: ValueProtectionPolicy.InstanceInline,
            source: RuntimeInputBindingSource.Literal,
            literal: ValueEnvelope.Inline(valueType, JsonSerializer.SerializeToElement(currentLiteral), ValueProtectionPolicy.InstanceInline));
        return NewExecutable(inputBinding: binding, activityContract: contract);
    }

    private ActivityExecutionState NewTypedSuspendedState(ActivityContract contract, string pinnedText)
    {
        var valueType = new Elsa.Primitives.Models.ValueTypeDescriptor("String");
        var snapshot = new ActivityInputSnapshot(
            "actexec-1",
            contract.SchemaFingerprint,
            "sha256:original-bindings",
            new Dictionary<string, ValueEnvelope>
            {
                ["text"] = ValueEnvelope.Inline(valueType, JsonSerializer.SerializeToElement(pinnedText), ValueProtectionPolicy.InstanceInline)
            },
            _now.AddMinutes(-2));
        var initialAttempt = new ActivityAttempt(
            "actexec-1:attempt:1",
            "actexec-1",
            1,
            ActivityAttemptReason.Initial,
            _now.AddMinutes(-2),
            _now.AddMinutes(-1),
            transitionKind: Elsa.Workflows.Runtime.Core.Models.ActivityTransitionKind.Suspend);
        return NewSuspendedState() with
        {
            ContractIdentity = new ActivityInvocationContractIdentity(contract.ActivityTypeKey, contract.ContractVersion, contract.SchemaFingerprint),
            InputSnapshot = snapshot,
            Attempts = [initialAttempt]
        };
    }

    private static JsonElement Json(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private sealed class RecordingActivityFactory(IActivity activity) : IActivityFactory
    {
        public int CreateCalls { get; private set; }
        public string? LastDescriptorType { get; private set; }

        public ValueTask<IActivity> Create(
            string descriptorType,
            JsonElement payload,
            IDictionary<string, InputArgument>? inputs,
            IDictionary<string, OutputArgument>? outputs,
            CancellationToken cancellationToken = default)
        {
            CreateCalls++;
            LastDescriptorType = descriptorType;
            if (activity is OutputProducingResumeTargetActivity outputProducingActivity && outputs is not null && outputs.TryGetValue("customer", out var customer))
                outputProducingActivity.Customer = (OutputArgument<object?>)customer;
            return ValueTask.FromResult(activity);
        }
    }

    private sealed class RecordingTypedResumeActivator : IActivityActivator
    {
        public List<ActivityActivationRequest> Requests { get; } = [];
        public List<TypedResumeTargetActivity> Activities { get; } = [];

        public ValueTask<ActivityActivationLease> ActivateAsync(ActivityActivationRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            var activity = new TypedResumeTargetActivity(request.Inputs.Values["text"].InlineValue!.Value.GetString()!);
            Activities.Add(activity);
            return ValueTask.FromResult(new ActivityActivationLease(activity));
        }
    }

    private sealed class TypedResumeTargetActivity(string text) : ActivityBase, IDisposable
    {
        public string? ObservedText { get; private set; }
        public string? ObservedAttemptId { get; private set; }
        public bool Disposed { get; private set; }

        [ResumeTarget("resume-target:delivery")]
        private void Resume(IActivityExecutionContext context)
        {
            ObservedText = text;
            ObservedAttemptId = ((SimpleActivityExecutionContext)context).AttemptId;
        }

        public void Dispose() => Disposed = true;
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

    private sealed class ResumeTargetActivity : ActivityBase
    {
        public string[]? Outcomes { get; set; }
        public bool ContextResumeInvoked { get; private set; }
        public string? ObservedOrderId { get; private set; }
        public string? ObservedCorrelationId { get; private set; }
        public string? ObservedWorkflowName { get; private set; }

        [ResumeTarget("resume-target:delivery")]
        private ValueTask ResumeAsync(IActivityExecutionContext context)
        {
            ContextResumeInvoked = true;
            var carrier = (IExecutionExpressionState)context;
            ObservedCorrelationId = carrier.CorrelationId;
            ObservedWorkflowName = carrier.WorkflowName;
            if (Outcomes is not null)
                context.SetOutcomes(Outcomes);

            return ValueTask.CompletedTask;
        }

        [ResumeTarget("resume-target:input")]
        public void ResumeWithInput(JsonElement input)
        {
            ObservedOrderId = input.GetProperty("orderId").GetString();
        }
    }

    private sealed class ActivityWithoutResumeTarget : ActivityBase;

    private sealed class OutputProducingResumeTargetActivity(string customerJson) : ActivityBase
    {
        public OutputArgument<object?> Customer { get; set; } = null!;

        [ResumeTarget("resume-target:delivery")]
        private void Resume(IActivityExecutionContext context) =>
            context.Set(Customer, Json(customerJson));
    }

    private sealed class InvalidResumeTargetActivity : ActivityBase
    {
        [ResumeTarget("resume-target:invalid")]
        public string Resume() => "invalid";
    }

    private sealed class ThrowingResumeTargetActivity : ActivityBase
    {
        [ResumeTarget("resume-target:throwing")]
        public void Resume() => throw new InvalidOperationException("resume failed");
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
