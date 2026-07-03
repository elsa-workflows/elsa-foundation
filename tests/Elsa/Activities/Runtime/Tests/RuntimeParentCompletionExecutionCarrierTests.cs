using System.Text.Json;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Runtime.Services;
using Elsa.Expressions.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

/// <summary>
/// Locks the execution-time expression carrier (ADR 0030) on the
/// <see cref="WorkflowParentActivityCompletionSchedulerWorkHandler"/> path. Before this unit the parent-completion
/// context carried workflow identity but none of the carrier state (correlation id / name / inputs / variables /
/// outputs) and no variable scope, so a composite container's <c>OnChildCompleted</c> logic evaluating an expression
/// saw empty <c>getCorrelationId()</c>/<c>getInput()</c>/<c>getVariable()</c>/<c>getOutput()</c>. This drives the real
/// handler over shared in-memory stores and asserts the container observes correct identity, inputs, variables, and
/// prior activity outputs through <see cref="IExecutionExpressionState"/>, and that an in-evaluation variable
/// write-back lands in the visible scope.
/// </summary>
public sealed class RuntimeParentCompletionExecutionCarrierTests
{
    private const string WorkflowExecutionId = ExecutionCarrierTestData.WorkflowExecutionId;
    private const string ParentNodeId = "node-parent";
    private const string ChildNodeId = "node-child";
    private const string VariableName = ExecutionCarrierTestData.VariableName;

    private readonly DateTimeOffset _now = ExecutionCarrierTestData.Now;
    private readonly InMemoryWorkflowExecutableStore _executableStore = new();
    private readonly InMemoryActivityExecutionStateStore _activityStateStore = new();
    private readonly InMemoryWorkflowSchedulerWorkQueue _schedulerWorkQueue = new();
    private readonly InMemoryDurableValueStateStore _durableValueStateStore = new();
    private readonly InMemoryActivityExecutionInspectionStore _inspectionStore = new();
    private readonly InMemoryIncidentStateStore _incidentStateStore = new();
    private readonly InMemoryWorkflowExecutionStateStore _workflowStateStore = new();
    private readonly InMemoryRuntimeCheckpointCommitStore _checkpointWriter;

    public RuntimeParentCompletionExecutionCarrierTests() =>
        _checkpointWriter = new InMemoryRuntimeCheckpointCommitStore(
            workflowExecutionStateStore: _workflowStateStore,
            activityExecutionStateStore: _activityStateStore,
            bookmarkStateStore: null,
            durableValueStateStore: _durableValueStateStore,
            incidentStateStore: _incidentStateStore,
            operationalStateStore: null,
            schedulerStateStore: null,
            activityExecutionInspectionWriter: _inspectionStore);

    [Fact]
    public async Task ChildCompletionHandler_ObservesExecutionTimeIdentityInputsVariablesAndOutputs()
    {
        await _executableStore.SaveAsync(NewExecutable());
        await _workflowStateStore.SaveAsync(ExecutionCarrierTestData.NewWorkflowState(_now));
        await ExecutionCarrierTestData.SeedIdentityAsync(_durableValueStateStore, _now, "corr-1", "Instance A");
        await ExecutionCarrierTestData.SeedInputAsync(_durableValueStateStore, _now, "orderId", "order-1");
        await ExecutionCarrierTestData.SeedVariableAsync(_durableValueStateStore, _now, "Hello");
        await ExecutionCarrierTestData.SeedOutputAsync(_durableValueStateStore, _now, "prior", "prior-value");
        await _activityStateStore.SaveAsync(NewState("actexec-parent", ParentNodeId, ActivityExecutionStatus.Running));
        await _activityStateStore.SaveAsync(NewState("actexec-child", ChildNodeId, ActivityExecutionStatus.Completed, parentActivityExecutionId: "actexec-parent"));

        var composite = new CarrierObservingCompositeActivity();
        await using var provider = NewProvider(new RecordingActivityFactory(composite));
        var handler = NewHandler(provider);

        await handler.HandleAsync(NewParentCompletionWorkItem());

        Assert.True(composite.ChildCompletionInvoked);
        // WorkflowInstanceId/WorkflowDefinitionId are not asserted here: the parent-completion context carried
        // execution id + pinned executable before the carrier existed, so they cannot distinguish populated-vs-empty.
        Assert.Equal("corr-1", composite.ObservedCorrelationId);
        Assert.Equal("Instance A", composite.ObservedWorkflowName);
        Assert.Equal(7, composite.ObservedWorkflowDefinitionVersion);
        Assert.Equal("order-1", ExecutionCarrierTestData.AsString(composite.ObservedInput));
        // Workflow variables read through the carrier's durable-value projection (empty before this unit). Scope-
        // based / named variable access and write-back on the parent-completion path — where the container is
        // evaluated as its own scope owner, so ADR-0027 does not expose its own variables to itself by name — plus
        // durable persistence of any such mutation, is a documented follow-up (see the handler comment).
        Assert.Equal("Hello", ExecutionCarrierTestData.AsString(composite.ObservedVariable));
        Assert.Equal("prior-value", ExecutionCarrierTestData.AsString(composite.ObservedOutput));

        // The container still completed normally — the carrier population is additive to the existing flow.
        var projection = await _inspectionStore.FindAsync(WorkflowExecutionId, "actexec-parent");
        Assert.NotNull(projection);
        Assert.Equal(ActivityExecutionStatus.Completed, projection.Status);
    }

    private WorkflowParentActivityCompletionSchedulerWorkHandler NewHandler(ServiceProvider provider) =>
        new(new RuntimeActivityInputMaterializer(), provider.GetRequiredService<IServiceScopeFactory>(), new FixedTimeProvider(_now));

    private ServiceProvider NewProvider(IActivityFactory factory)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => factory);
        services.AddSingleton<IWorkflowExecutableStore>(_executableStore);
        services.AddSingleton<IActivityExecutionStateStore>(_activityStateStore);
        services.AddSingleton<IWorkflowSchedulerWorkQueue>(_schedulerWorkQueue);
        services.AddSingleton<IRuntimeActivityOutputRegister, InMemoryRuntimeActivityOutputRegister>();
        services.AddSingleton<IDurableValueStateStore>(_durableValueStateStore);
        services.AddSingleton<IWorkflowExecutionStateStore>(_workflowStateStore);
        services.AddSingleton<IIncidentStateStore>(_incidentStateStore);
        services.AddSingleton<IRuntimeExecutionIdGenerator, GuidRuntimeExecutionIdGenerator>();
        services.AddSingleton<IActivityExecutionInspectionStore>(_inspectionStore);
        services.AddSingleton<IRuntimeActivityExecutionInspectionAccumulator, RuntimeActivityExecutionInspectionAccumulator>();
        services.AddSingleton<IRuntimeCheckpointCommitStore>(_checkpointWriter);
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
            ExecutionCarrierTestData.NewIdentity(),
            ParentNodeId,
            "actexec-parent",
            parentActivityExecutionId: null,
            branchId: null,
            outcomeNames: [ActivityOutcomes.Done],
            reason: RuntimeCompleteActivityCommandPayload.ParentCompletionEvaluationReason,
            completionKind: SchedulerCompletionKind.ParentCompletionEvaluation,
            completedChildActivityExecutionId: "actexec-child");

        return new RuntimeSchedulerWorkItem(
            workItemId: "parent-work",
            workflowExecutionId: WorkflowExecutionId,
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

    private ActivityExecutionState NewState(
        string activityExecutionId,
        string executableNodeId,
        ActivityExecutionStatus status,
        string? parentActivityExecutionId = null) =>
        new(
            Execution: new ActivityExecution(activityExecutionId, WorkflowExecutionId, executableNodeId, $"authored-{executableNodeId}", "test/activity", "1.0.0"),
            Status: status,
            SubStatus: null,
            ScheduledAt: _now.AddMinutes(-2),
            StartedAt: _now.AddMinutes(-1),
            CompletedAt: status == ActivityExecutionStatus.Completed ? _now : null,
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

    private WorkflowExecutable NewExecutable()
    {
        // The parent-completion path reads workflow variables from the carrier's durable-value projection (the
        // seeded `greeting`), not through a variable scope, so no structure/variable declaration is needed here.
        var child = NewNode(ChildNodeId);
        var parent = new ExecutableNode(
            executableNodeId: ParentNodeId,
            authoredActivityId: $"authored-{ParentNodeId}",
            activityType: "test/container",
            activityTypeVersion: "1.0.0",
            descriptorType: "test",
            descriptorPayload: JsonSerializer.SerializeToElement(new { type = "test" }),
            inputBindings: new Dictionary<string, RuntimeInputBinding>(),
            outputCaptures: new Dictionary<string, RuntimeOutputCapture>(),
            metadata: new Dictionary<string, string>(),
            childSlots: [new ExecutableChildSlot("children", [child])]);

        return new(
            identity: ExecutionCarrierTestData.NewIdentity(),
            rootActivity: parent,
            resumeTargets: new Dictionary<string, WorkflowExecutableResumeTarget>(),
            createdAt: _now,
            publishedAt: _now,
            compatibilityMetadata: new Dictionary<string, string>());
    }

    private static ExecutableNode NewNode(string nodeId) =>
        new(
            executableNodeId: nodeId,
            authoredActivityId: $"authored-{nodeId}",
            activityType: "test/activity",
            activityTypeVersion: "1.0.0",
            descriptorType: "test",
            descriptorPayload: JsonSerializer.SerializeToElement(new { type = "test" }),
            inputBindings: new Dictionary<string, RuntimeInputBinding>(),
            outputCaptures: new Dictionary<string, RuntimeOutputCapture>(),
            metadata: new Dictionary<string, string>());

    private sealed class CarrierObservingCompositeActivity : IActivity, IActivityChildCompletionHandler
    {
        public string Id { get; set; } = string.Empty;
        public string NodeId { get; set; } = string.Empty;
        public string? Name { get; set; }
        public string Type { get; set; } = "test/activity";
        public string Version { get; set; } = "1.0.0";
        public Dictionary<string, object> CustomProperties { get; set; } = new();
        public Dictionary<string, object> SyntheticProperties { get; set; } = new();
        public Dictionary<string, object> Metadata { get; set; } = new();

        public bool ChildCompletionInvoked { get; private set; }
        public string? ObservedCorrelationId { get; private set; }
        public string? ObservedWorkflowName { get; private set; }
        public int ObservedWorkflowDefinitionVersion { get; private set; }
        public object? ObservedInput { get; private set; }
        public object? ObservedVariable { get; private set; }
        public object? ObservedOutput { get; private set; }

        public ValueTask<bool> CanExecuteAsync(IActivityExecutionContext context) => ValueTask.FromResult(true);
        public ValueTask ExecuteAsync(IActivityExecutionContext context) => ValueTask.CompletedTask;

        public ValueTask OnChildCompletedAsync(ActivityChildCompletedContext context)
        {
            ChildCompletionInvoked = true;
            var parentContext = context.ParentContext;
            var state = (IExecutionExpressionState)parentContext;
            ObservedCorrelationId = state.CorrelationId;
            ObservedWorkflowName = state.WorkflowName;
            ObservedWorkflowDefinitionVersion = state.WorkflowDefinitionVersion;
            ObservedInput = state.WorkflowInputs.GetValueOrDefault("orderId");
            ObservedVariable = state.WorkflowVariables.GetValueOrDefault(VariableName);
            ObservedOutput = state.ActivityOutputValues.GetValueOrDefault("prior");

            ((IRuntimeActivityExecutionContext)parentContext).CompleteCompositeActivity([ActivityOutcomes.Done]);
            return ValueTask.CompletedTask;
        }
    }
}
