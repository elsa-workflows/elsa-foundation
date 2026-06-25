using System.Text.Json;
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

public sealed class WorkflowParentActivityCompletionSchedulerWorkHandlerTests
{
    private readonly DateTimeOffset _now = new(2026, 6, 11, 12, 0, 0, TimeSpan.Zero);
    private readonly InMemoryWorkflowExecutableStore _executableStore = new();
    private readonly InMemoryActivityExecutionStateStore _activityStateStore = new();
    private readonly InMemoryWorkflowSchedulerWorkQueue _schedulerWorkQueue = new();
    private readonly InMemoryDurableValueStateStore _durableValueStateStore = new();
    private readonly InMemoryActivityExecutionInspectionStore _inspectionStore = new();
    private readonly InMemoryIncidentStateStore _incidentStateStore = new();
    private readonly InMemoryRuntimeCheckpointCommitStore _checkpointWriter;

    public WorkflowParentActivityCompletionSchedulerWorkHandlerTests()
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
    public async Task HandleAsync_CheckpointsCompletedParentInspectionAndPostCommitCompletionWork()
    {
        await _executableStore.SaveAsync(NewExecutable());
        await _activityStateStore.SaveAsync(NewState("actexec-parent", "node-parent", ActivityExecutionStatus.Running));
        await _activityStateStore.SaveAsync(NewState("actexec-child", "node-child", ActivityExecutionStatus.Completed, parentActivityExecutionId: "actexec-parent"));
        await using var provider = NewProvider(new RecordingActivityFactory(new CompletingCompositeActivity(["Approved"])));
        var handler = NewHandler(provider);

        await handler.HandleAsync(NewParentCompletionWorkItem());

        var write = Assert.Single(_checkpointWriter.ListCommits());
        Assert.Equal(RuntimeCheckpointNames.ActivityCompleted, write.Commit.Checkpoint.Name);
        Assert.Equal(RuntimeMetadataKeys.CheckpointRequirementMandatory, write.Commit.Checkpoint.Metadata[RuntimeMetadataKeys.CheckpointRequirement]);
        Assert.Single(write.Commit.StateChanges.ActivityExecutions);
        Assert.Single(write.Commit.StateChanges.ActivityExecutionInspections);
        Assert.Single(write.Commit.PostCommitIntents);

        var projection = await _inspectionStore.FindAsync("wfexec-1", "actexec-parent");
        Assert.NotNull(projection);
        Assert.Equal(ActivityExecutionStatus.Completed, projection.Status);
        Assert.Equal(["Approved"], projection.OutcomeNames);

        var completionWork = Assert.Single(await _schedulerWorkQueue.ListAsync(new RuntimeSchedulerWorkQuery("wfexec-1")));
        Assert.Equal(WorkflowExecutionCommandKind.CompleteActivity, completionWork.CommandKind);
        Assert.Equal("parent-work:complete:actexec-parent", completionWork.WorkItemId);
        Assert.Equal("command-parent:complete:actexec-parent", completionWork.CommandId);
        Assert.NotNull(completionWork.Payload);
        var completionPayload = completionWork.Payload.Value.Deserialize<RuntimeCompleteActivityCommandPayload>()!;
        Assert.Equal("actexec-parent", completionPayload.ActivityExecutionId);
        Assert.Equal(["Approved"], completionPayload.OutcomeNames);
        Assert.Equal(RuntimeCompleteActivityCommandPayload.ActivityInvocationCompletedReason, completionPayload.Reason);
    }

    [Fact]
    public async Task HandleAsync_CheckpointsParentInspectionBeforePostCommitChildScheduling()
    {
        await _executableStore.SaveAsync(NewExecutable());
        await _activityStateStore.SaveAsync(NewState("actexec-parent", "node-parent", ActivityExecutionStatus.Running));
        await _activityStateStore.SaveAsync(NewState("actexec-child", "node-child", ActivityExecutionStatus.Completed, parentActivityExecutionId: "actexec-parent"));
        await using var provider = NewProvider(new RecordingActivityFactory(new SchedulingCompositeActivity()));
        var handler = NewHandler(provider);

        await handler.HandleAsync(NewParentCompletionWorkItem());

        var write = Assert.Single(_checkpointWriter.ListCommits());
        Assert.Equal(RuntimeCheckpointNames.ActivityInspectionCaptured, write.Commit.Checkpoint.Name);
        Assert.Empty(write.Commit.StateChanges.ActivityExecutions);
        Assert.Single(write.Commit.StateChanges.ActivityExecutionInspections);
        Assert.Single(write.Commit.PostCommitIntents);

        var projection = await _inspectionStore.FindAsync("wfexec-1", "actexec-parent");
        Assert.NotNull(projection);
        Assert.Equal(ActivityExecutionStatus.Running, projection.Status);

        var scheduleWork = Assert.Single(await _schedulerWorkQueue.ListAsync(new RuntimeSchedulerWorkQuery("wfexec-1")));
        Assert.Equal(WorkflowExecutionCommandKind.ScheduleActivity, scheduleWork.CommandKind);
    }

    [Fact]
    public async Task HandleAsync_RecordsFaultedParentInspectionWhenChildCompletionHandlerThrows()
    {
        await _executableStore.SaveAsync(NewExecutable());
        await _activityStateStore.SaveAsync(NewState("actexec-parent", "node-parent", ActivityExecutionStatus.Running));
        await _activityStateStore.SaveAsync(NewState("actexec-child", "node-child", ActivityExecutionStatus.Completed, parentActivityExecutionId: "actexec-parent"));
        await using var provider = NewProvider(new RecordingActivityFactory(new ThrowingCompositeActivity(new InvalidOperationException("parent failed"))));
        var handler = NewHandler(provider);

        await handler.HandleAsync(NewParentCompletionWorkItem());

        var state = await _activityStateStore.FindAsync("wfexec-1", "actexec-parent");
        Assert.NotNull(state);
        Assert.Equal(ActivityExecutionStatus.Faulted, state.Status);
        Assert.Equal("ParentCompletionFaulted", state.SubStatus);
        Assert.Equal("parent failed", state.Metadata["runtime.faultMessage"]);

        var incident = Assert.Single(await _incidentStateStore.ListAsync("wfexec-1"));
        Assert.Equal("actexec-parent", incident.ActivityExecutionId);
        Assert.Equal("ParentCompletionFaulted", incident.FailureType);

        var projection = await _inspectionStore.FindAsync("wfexec-1", "actexec-parent");
        Assert.NotNull(projection);
        Assert.Equal(ActivityExecutionStatus.Faulted, projection.Status);
        Assert.Single(projection.Incidents);
        Assert.Empty(await _schedulerWorkQueue.ListAsync(new RuntimeSchedulerWorkQuery("wfexec-1")));
    }

    private WorkflowParentActivityCompletionSchedulerWorkHandler NewHandler(ServiceProvider provider) =>
        new(
            new RuntimeActivityInputMaterializer(),
            provider.GetRequiredService<IServiceScopeFactory>(),
            new FixedTimeProvider(_now));

    private ServiceProvider NewProvider(IActivityFactory factory)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => factory);
        services.AddSingleton(_executableStore);
        services.AddSingleton<IWorkflowExecutableStore>(_ => _executableStore);
        services.AddSingleton(_activityStateStore);
        services.AddSingleton<IActivityExecutionStateStore>(_ => _activityStateStore);
        services.AddSingleton(_schedulerWorkQueue);
        services.AddSingleton<IWorkflowSchedulerWorkQueue>(_ => _schedulerWorkQueue);
        services.AddSingleton<IRuntimeActivityOutputRegister, InMemoryRuntimeActivityOutputRegister>();
        services.AddSingleton(_durableValueStateStore);
        services.AddSingleton<IDurableValueStateStore>(_ => _durableValueStateStore);
        services.AddSingleton(_incidentStateStore);
        services.AddSingleton<IIncidentStateStore>(_ => _incidentStateStore);
        services.AddSingleton<IRuntimeExecutionIdGenerator, GuidRuntimeExecutionIdGenerator>();
        services.AddSingleton<IActivityExecutionInspectionStore>(_ => _inspectionStore);
        services.AddSingleton<IRuntimeActivityExecutionInspectionAccumulator, RuntimeActivityExecutionInspectionAccumulator>();
        services.AddSingleton<IRuntimeCheckpointCommitStore>(_ => _checkpointWriter);
        services.AddSingleton<IRuntimeCheckpointPersistencePolicy, ImmediateRuntimeCheckpointPersistencePolicy>();
        services.AddSingleton<IRuntimePostCommitIntentDispatcher, RuntimeSchedulerPostCommitIntentDispatcher>();
        services.AddSingleton<RuntimeCheckpointCommitter>();
        services.AddSingleton<TimeProvider>(new FixedTimeProvider(_now));
        services.AddSingleton<ActivityFaultIncidentRecorder>();
        return services.BuildServiceProvider();
    }

    private RuntimeSchedulerWorkItem NewParentCompletionWorkItem()
    {
        var payload = new RuntimeCompleteActivityCommandPayload(
            NewIdentity(),
            "node-parent",
            "actexec-parent",
            parentActivityExecutionId: null,
            branchId: null,
            outcomeNames: [ActivityOutcomes.Done],
            reason: RuntimeCompleteActivityCommandPayload.ParentCompletionEvaluationReason,
            completionKind: SchedulerCompletionKind.ParentCompletionEvaluation,
            completedChildActivityExecutionId: "actexec-child");

        return new RuntimeSchedulerWorkItem(
            workItemId: "parent-work",
            workflowExecutionId: "wfexec-1",
            commandId: "command-parent",
            commandKind: WorkflowExecutionCommandKind.CompleteActivity,
            envelopeId: "envelope-1",
            idempotencyKey: "wfexec-1:complete-parent:actexec-parent",
            enqueuedAt: _now,
            recordedAt: _now,
            sequence: 30,
            payload: JsonSerializer.SerializeToElement(payload),
            commandMetadata: new Dictionary<string, string> { ["source"] = "test" },
            envelopeMetadata: new Dictionary<string, string>());
    }

    private static ActivityExecutionState NewState(
        string activityExecutionId,
        string executableNodeId,
        ActivityExecutionStatus status,
        string? parentActivityExecutionId = null) =>
        new(
            Execution: new ActivityExecution(
                ActivityExecutionId: activityExecutionId,
                WorkflowExecutionId: "wfexec-1",
                ExecutableNodeId: executableNodeId,
                AuthoredActivityId: $"authored-{executableNodeId}",
                ActivityType: "test/activity",
                ActivityTypeVersion: "1.0.0"),
            Status: status,
            SubStatus: null,
            ScheduledAt: DateTimeOffset.UtcNow.AddMinutes(-2),
            StartedAt: DateTimeOffset.UtcNow.AddMinutes(-1),
            CompletedAt: status == ActivityExecutionStatus.Completed ? DateTimeOffset.UtcNow : null,
            SchedulingActivityExecutionId: null,
            ParentActivityExecutionId: parentActivityExecutionId,
            BranchId: null,
            IterationId: null,
            CallStackDepth: null,
            BookmarkIds: [],
            IncidentIds: [],
            FaultCount: 0,
            AggregateFaultCount: 0,
            Metadata: new Dictionary<string, string>());

    private static WorkflowExecutable NewExecutable()
    {
        using var document = JsonDocument.Parse("""{"type":"test"}""");
        var child = NewNode("node-child", document.RootElement);
        var parent = WithChildren(NewNode("node-parent", document.RootElement), [child]);

        return new(
            identity: NewIdentity(),
            rootActivity: parent,
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

    private sealed class RecordingActivityFactory(IActivity activity) : IActivityFactory
    {
        public ValueTask<IActivity> Create(
            string descriptorType,
            JsonElement payload,
            IDictionary<string, InputArgument>? inputs,
            IDictionary<string, OutputArgument>? outputs,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(activity);
    }

    private sealed class CompletingCompositeActivity(IReadOnlyCollection<string> outcomeNames) : IActivity, IActivityChildCompletionHandler
    {
        public string Id { get; set; } = string.Empty;
        public string NodeId { get; set; } = string.Empty;
        public string? Name { get; set; }
        public string Type { get; set; } = "test/activity";
        public string Version { get; set; } = "1.0.0";
        public Dictionary<string, object> CustomProperties { get; set; } = new();
        public Dictionary<string, object> SyntheticProperties { get; set; } = new();
        public Dictionary<string, object> Metadata { get; set; } = new();

        public ValueTask<bool> CanExecuteAsync(IActivityExecutionContext context) => ValueTask.FromResult(true);
        public ValueTask ExecuteAsync(IActivityExecutionContext context) => ValueTask.CompletedTask;

        public ValueTask OnChildCompletedAsync(ActivityChildCompletedContext context)
        {
            var runtimeContext = Assert.IsAssignableFrom<IRuntimeActivityExecutionContext>(context.ParentContext);
            runtimeContext.CompleteCompositeActivity(outcomeNames);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class SchedulingCompositeActivity : IActivity, IActivityChildCompletionHandler
    {
        public string Id { get; set; } = string.Empty;
        public string NodeId { get; set; } = string.Empty;
        public string? Name { get; set; }
        public string Type { get; set; } = "test/activity";
        public string Version { get; set; } = "1.0.0";
        public Dictionary<string, object> CustomProperties { get; set; } = new();
        public Dictionary<string, object> SyntheticProperties { get; set; } = new();
        public Dictionary<string, object> Metadata { get; set; } = new();

        public ValueTask<bool> CanExecuteAsync(IActivityExecutionContext context) => ValueTask.FromResult(true);
        public ValueTask ExecuteAsync(IActivityExecutionContext context) => ValueTask.CompletedTask;

        public ValueTask OnChildCompletedAsync(ActivityChildCompletedContext context)
        {
            var runtimeContext = Assert.IsAssignableFrom<IRuntimeActivityExecutionContext>(context.ParentContext);
            runtimeContext.ScheduleChildActivity("node-child");
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ThrowingCompositeActivity(Exception exception) : IActivity, IActivityChildCompletionHandler
    {
        public string Id { get; set; } = string.Empty;
        public string NodeId { get; set; } = string.Empty;
        public string? Name { get; set; }
        public string Type { get; set; } = "test/activity";
        public string Version { get; set; } = "1.0.0";
        public Dictionary<string, object> CustomProperties { get; set; } = new();
        public Dictionary<string, object> SyntheticProperties { get; set; } = new();
        public Dictionary<string, object> Metadata { get; set; } = new();

        public ValueTask<bool> CanExecuteAsync(IActivityExecutionContext context) => ValueTask.FromResult(true);
        public ValueTask ExecuteAsync(IActivityExecutionContext context) => ValueTask.CompletedTask;
        public ValueTask OnChildCompletedAsync(ActivityChildCompletedContext context) => throw exception;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
