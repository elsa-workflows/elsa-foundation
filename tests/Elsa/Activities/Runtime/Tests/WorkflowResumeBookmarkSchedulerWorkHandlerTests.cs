using System.Text.Json;
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

public sealed class WorkflowResumeBookmarkSchedulerWorkHandlerTests
{
    private readonly DateTimeOffset _now = new(2026, 6, 11, 17, 0, 0, TimeSpan.Zero);
    private readonly InMemoryWorkflowExecutableStore _executableStore = new();
    private readonly InMemoryActivityExecutionStateStore _activityStateStore = new();
    private readonly InMemoryBookmarkStateStore _bookmarkStateStore = new();
    private readonly InMemoryWorkflowSchedulerWorkQueue _schedulerWorkQueue = new();
    private readonly InMemoryIncidentStateStore _incidentStateStore = new();
    private readonly InMemoryRuntimeCheckpointWriter _checkpointWriter;

    public WorkflowResumeBookmarkSchedulerWorkHandlerTests()
    {
        _checkpointWriter = new InMemoryRuntimeCheckpointWriter(
            workflowExecutionStateStore: null,
            activityExecutionStateStore: _activityStateStore,
            bookmarkStateStore: _bookmarkStateStore,
            durableValueStateStore: null,
            incidentStateStore: _incidentStateStore);
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
        Assert.Contains("not a supported materialized value binding", state.Metadata["runtime.faultMessage"]);
        await AssertIncidentRecordedAsync("InputMaterializationFailed", message => Assert.Contains("not a supported materialized value binding", message));
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
        Assert.Empty(_checkpointWriter.ListWrites());
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
        Assert.Empty(_checkpointWriter.ListWrites());
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

    private async Task<RuntimeCompleteActivityCommandPayload> AssertCompletionWorkAsync()
    {
        var completionWork = Assert.Single(await _schedulerWorkQueue.ListAsync(new RuntimeSchedulerWorkQuery("wfexec-1")));
        Assert.Equal(WorkflowExecutionCommandKind.CompleteActivity, completionWork.CommandKind);
        Assert.Equal("resume-work:complete:actexec-1", completionWork.WorkItemId);
        Assert.Equal("command-1:complete:actexec-1", completionWork.CommandId);
        Assert.Equal("wfexec-1:resume:bookmark-1:complete:actexec-1", completionWork.IdempotencyKey);
        Assert.Equal(41, completionWork.Sequence);
        Assert.NotNull(completionWork.Payload);
        return completionWork.Payload.Value.Deserialize<RuntimeCompleteActivityCommandPayload>()!;
    }

    private ServiceProvider NewProvider(IActivityFactory factory)
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
        services.AddSingleton<TimeProvider>(new FixedTimeProvider(_now));
        services.AddSingleton<IRuntimeCheckpointPersistencePolicy, ImmediateRuntimeCheckpointPersistencePolicy>();
        services.AddSingleton<IRuntimeCheckpointWriter>(_checkpointWriter);
        services.AddSingleton(_incidentStateStore);
        services.AddSingleton<IIncidentStateStore>(_ => _incidentStateStore);
        services.AddSingleton<IRuntimePostCommitIntentDispatcher, NoopRuntimePostCommitIntentDispatcher>();
        services.AddSingleton<RuntimeCheckpointCommitter>();
        services.AddSingleton<ActivityFaultIncidentRecorder>();
        services.AddSingleton<IBookmarkConsumptionCheckpointService, BookmarkConsumptionCheckpointService>();
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
        var write = Assert.Single(_checkpointWriter.ListWrites());
        Assert.Equal(RuntimeCheckpointPersistenceMode.Immediate, write.Decision.Mode);
        Assert.Equal("commit:resume-work:bookmark-consumed:bookmark-1", write.Commit.CommitId);
        Assert.Equal("checkpoint:resume-work:bookmark-consumed:bookmark-1", write.Commit.Checkpoint.CheckpointId);
        Assert.Equal(RuntimeCheckpointNames.BookmarkConsumed, write.Commit.Checkpoint.Name);
        Assert.Equal(["actexec-1"], write.Commit.Checkpoint.ActivityExecutionIds);
        Assert.Equal("bookmark-1", write.Commit.Checkpoint.Metadata["runtime.bookmarkId"]);

        var activityChange = Assert.Single(write.Commit.StateChanges.ActivityExecutions);
        Assert.Equal(RuntimeStateChangeOperation.Upsert, activityChange.Operation);
        Assert.Equal(ActivityExecutionStatus.Completed, activityChange.State.Status);

        var bookmarkChange = Assert.Single(write.Commit.StateChanges.Bookmarks);
        Assert.Equal(RuntimeStateChangeOperation.Delete, bookmarkChange.Operation);
        Assert.Equal("bookmark-1", bookmarkChange.State.BookmarkId);
        Assert.Empty(write.Commit.PostCommitIntents);
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

        var write = Assert.Single(_checkpointWriter.ListWrites());
        Assert.Equal(RuntimeCheckpointNames.IncidentRecorded, write.Commit.Checkpoint.Name);
        Assert.Equal(incidentId, write.Commit.Checkpoint.Metadata["runtime.incidentId"]);
        Assert.Equal("bookmark-1", write.Commit.Checkpoint.Metadata["runtime.bookmarkId"]);
        Assert.Equal(resumeTargetId, write.Commit.Checkpoint.Metadata["runtime.resumeTargetId"]);
        Assert.Equal(["actexec-1"], write.Commit.Checkpoint.ActivityExecutionIds);
        Assert.Empty(write.Commit.StateChanges.Bookmarks);
    }

    private RuntimeSchedulerWorkItem NewResumeWorkItem(
        WorkflowExecutionCommandKind commandKind = WorkflowExecutionCommandKind.ResumeBookmark,
        string resumeTargetId = "resume-target:delivery")
    {
        var payload = JsonSerializer.SerializeToElement(new RuntimeResumeBookmarkCommandPayload(
            pinnedExecutable: NewIdentity(),
            bookmarkId: "bookmark-1",
            activityExecutionId: "actexec-1",
            executableNodeId: "node-wait",
            resumeTargetId: resumeTargetId,
            stimulusType: "delivery-status",
            stimulusHash: "sha256:delivery-status:order-123",
            input: Json("""{"orderId":"order-123"}"""),
            reason: RuntimeResumeBookmarkCommandPayload.StimulusMatchedReason));

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
        RuntimeInputBinding? inputBinding = null)
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
            rootActivity: NewNode("node-wait", document.RootElement, inputBinding),
            resumeTargets: resumeTargets,
            createdAt: DateTimeOffset.UtcNow,
            publishedAt: DateTimeOffset.UtcNow,
            compatibilityMetadata: new Dictionary<string, string>());
    }

    private static ExecutableNode NewNode(string nodeId, JsonElement descriptorPayload, RuntimeInputBinding? inputBinding = null) =>
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
            outputCaptures: new Dictionary<string, RuntimeOutputCapture>(),
            metadata: new Dictionary<string, string>());

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

        public ValueTask<IActivity> Create(
            string descriptorType,
            JsonElement payload,
            IDictionary<string, InputArgument>? inputs,
            IDictionary<string, OutputArgument>? outputs,
            CancellationToken cancellationToken = default)
        {
            CreateCalls++;
            LastDescriptorType = descriptorType;
            return ValueTask.FromResult(activity);
        }
    }

    private sealed class ResumeTargetActivity : ActivityBase
    {
        public string[]? Outcomes { get; set; }
        public bool ContextResumeInvoked { get; private set; }
        public string? ObservedOrderId { get; private set; }

        [ResumeTarget("resume-target:delivery")]
        private ValueTask ResumeAsync(IActivityExecutionContext context)
        {
            ContextResumeInvoked = true;
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
