using System.Text.Json;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Runtime.Services;
using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
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
    private const string OuterNodeId = "node-outer";
    private const string ParentNodeId = "node-parent";
    private const string ChildNodeId = "node-child";
    private const string VariableName = ExecutionCarrierTestData.VariableName;
    private const string OuterVariableReferenceKey = "var-greeting";
    private const string IterationItemName = "loopItem";

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
        // based / named resolution of enclosing-container and workflow variables now lands too, through the threaded
        // self-owner scope — see ChildCompletionHandler_ResolvesEnclosingContainerVariableByName_AndSuppressesTheIterationLayer;
        // durable named-variable write-back lands too — see ChildCompletionHandler_AssignsWorkflowRootVariableByName_IsDurablyPersisted.
        Assert.Equal("Hello", ExecutionCarrierTestData.AsString(composite.ObservedVariable));
        Assert.Equal("prior-value", ExecutionCarrierTestData.AsString(composite.ObservedOutput));

        // The container still completed normally — the carrier population is additive to the existing flow.
        var projection = await _inspectionStore.FindAsync(WorkflowExecutionId, "actexec-parent");
        Assert.NotNull(projection);
        Assert.Equal(ActivityExecutionStatus.Completed, projection.Status);
    }

    [Fact]
    public async Task ChildCompletionHandler_ResolvesEnclosingContainerVariableByName_AndSuppressesTheIterationLayer()
    {
        // ADR 0030 follow-up: the parent-completion context now threads a self-owner variable scope, so a nested
        // container's OnChildCompleted resolves an enclosing-container/workflow variable *by name* through the
        // visible chain (previously it could only read the flat carrier projection). The `evaluateAsSelf` mode
        // suppresses the per-iteration loop layer: the container's own scheduling provenance carries the iteration
        // values of the loop it is a body of (ADR 0028), which is the wrong innermost scope when the container
        // evaluates itself and broke loop/break control flow during development — so that iteration variable must
        // NOT be visible to the container's self-evaluation.
        await _executableStore.SaveAsync(NewNestedExecutable());
        await _workflowStateStore.SaveAsync(ExecutionCarrierTestData.NewWorkflowState(_now));
        await ExecutionCarrierTestData.SeedVariableAsync(_durableValueStateStore, _now, "enclosing-hello");
        await _activityStateStore.SaveAsync(NewState("actexec-outer", OuterNodeId, ActivityExecutionStatus.Running));
        await _activityStateStore.SaveAsync(NewLoopBodyParentState("actexec-parent", "actexec-outer"));
        await _activityStateStore.SaveAsync(NewState("actexec-child", ChildNodeId, ActivityExecutionStatus.Completed, parentActivityExecutionId: "actexec-parent"));

        var composite = new NamedVariableObservingCompositeActivity();
        await using var provider = NewProvider(new RecordingActivityFactory(composite));
        var handler = NewHandler(provider);

        await handler.HandleAsync(NewParentCompletionWorkItem());

        Assert.True(composite.ChildCompletionInvoked);
        // The enclosing container's variable resolves by name through the threaded self-owner scope.
        Assert.True(composite.EnclosingVariableResolved);
        Assert.Equal("enclosing-hello", ExecutionCarrierTestData.AsString(composite.ObservedEnclosingVariable));
        // The iteration layer is suppressed on the self-evaluation path, so the loop item the container's own
        // provenance carries is not visible to itself — the exact interaction that regressed loop/break control flow.
        Assert.False(composite.IterationVariableResolved);

        var projection = await _inspectionStore.FindAsync(WorkflowExecutionId, "actexec-parent");
        Assert.NotNull(projection);
        Assert.Equal(ActivityExecutionStatus.Completed, projection.Status);
    }

    [Fact]
    public async Task ChildCompletionHandler_AssignsWorkflowRootVariableByName_IsDurablyPersisted()
    {
        // ADR 0030 follow-up to #448: a workflow-root (workflow-level) variable assigned by name inside
        // OnChildCompleted lands in the threaded self-owner scope's in-memory store, but was previously dropped at
        // the checkpoint boundary because the workflow-root write-back was not folded into the commit's durableValues.
        // The handler now captures BuildWorkflowScopeWriteBackChanges output and threads it into the completion
        // commit, so the assignment is durable and re-projected on the next materialization.
        await _executableStore.SaveAsync(NewNestedExecutable());
        await _workflowStateStore.SaveAsync(ExecutionCarrierTestData.NewWorkflowState(_now));
        await ExecutionCarrierTestData.SeedVariableAsync(_durableValueStateStore, _now, "start-value");
        await _activityStateStore.SaveAsync(NewState("actexec-outer", OuterNodeId, ActivityExecutionStatus.Running));
        await _activityStateStore.SaveAsync(NewLoopBodyParentState("actexec-parent", "actexec-outer"));
        await _activityStateStore.SaveAsync(NewState("actexec-child", ChildNodeId, ActivityExecutionStatus.Completed, parentActivityExecutionId: "actexec-parent"));

        var composite = new WorkflowRootVariableAssigningCompositeActivity("reassigned-value");
        await using var provider = NewProvider(new RecordingActivityFactory(composite));
        var handler = NewHandler(provider);

        await handler.HandleAsync(NewParentCompletionWorkItem());

        Assert.True(composite.ChildCompletionInvoked);
        Assert.True(composite.AssignmentAccepted);

        // Re-project the workflow variables from durable-value state as the next materialization would: the
        // by-name assignment must be visible, proving it survived the checkpoint boundary rather than staying
        // in the in-memory scope only.
        var durableValues = await _durableValueStateStore.ListAsync(WorkflowExecutionId);
        var workflowVariables = RuntimeInputBindingStateProjection.ProjectWorkflowVariables(durableValues);
        Assert.Equal("reassigned-value", ExecutionCarrierTestData.AsString(workflowVariables.GetValueOrDefault(VariableName)));

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

    private WorkflowExecutable NewExecutable() =>
        // The parent-completion path reads workflow variables from the carrier's durable-value projection (the
        // seeded `greeting`), not through a variable scope, so no structure/variable declaration is needed here.
        NewExecutable(ContainerNode(ParentNodeId, NewNode(ChildNodeId)));

    // A three-level nesting — enclosing container (workflow root, declares `greeting`) → composite parent → child —
    // so the parent-completion path evaluates the composite as its own scope owner with a real enclosing scope to
    // resolve by name. The enclosing container is the workflow root, so its value is anchored from the durable-value
    // variable projection the handler passes into BuildScopeAsync (#286), not a local snapshot.
    private WorkflowExecutable NewNestedExecutable()
    {
        var greetingStructure = new ExecutableActivityStructure("test.structure", "1.0.0", JsonSerializer.SerializeToElement(
            new
            {
                variables = new[]
                {
                    new VariableDefinition(OuterVariableReferenceKey, VariableName, new TypeReference("String"), null, new ArgumentValue("default-greeting", "Literal"))
                }
            },
            new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        var parent = ContainerNode(ParentNodeId, NewNode(ChildNodeId));
        return NewExecutable(ContainerNode(OuterNodeId, parent, greetingStructure));
    }

    private WorkflowExecutable NewExecutable(ExecutableNode root) =>
        new(
            identity: ExecutionCarrierTestData.NewIdentity(),
            rootActivity: root,
            resumeTargets: new Dictionary<string, WorkflowExecutableResumeTarget>(),
            createdAt: _now,
            publishedAt: _now,
            compatibilityMetadata: new Dictionary<string, string>());

    private static ExecutableNode ContainerNode(string nodeId, ExecutableNode child, ExecutableActivityStructure? structure = null) =>
        new(
            executableNodeId: nodeId,
            authoredActivityId: $"authored-{nodeId}",
            activityType: "test/container",
            activityTypeVersion: "1.0.0",
            descriptorType: "test",
            descriptorPayload: JsonSerializer.SerializeToElement(new { type = "test" }),
            inputBindings: new Dictionary<string, RuntimeInputBinding>(),
            outputCaptures: new Dictionary<string, RuntimeOutputCapture>(),
            metadata: new Dictionary<string, string>(),
            childSlots: [new ExecutableChildSlot("children", [child])],
            structure: structure);

    // The composite parent's execution state carrying the scheduling provenance of the loop it is a body of
    // (ADR 0028): an IterationId plus the loop owner's published per-pass item. On the leaf/resume path this
    // layers an iteration scope; on the self-evaluation path `evaluateAsSelf` must suppress it.
    private ActivityExecutionState NewLoopBodyParentState(string activityExecutionId, string parentActivityExecutionId)
    {
        var iterationMetadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [RuntimeMetadataKeys.LoopIterationOwnerNodeId] = OuterNodeId,
            [RuntimeMetadataKeys.LoopIterationItemName] = IterationItemName,
            [RuntimeMetadataKeys.LoopIterationItemValue] = "\"pass-value\""
        };
        var provenance = ActivitySchedulingProvenance.From(
            WorkflowExecutionId,
            parentActivityExecutionId: parentActivityExecutionId,
            schedulingActivityExecutionId: null,
            branchId: null,
            iterationId: "loop:iteration:0",
            executionPathId: null,
            executionScopeId: null,
            schedulingCause: null,
            metadata: iterationMetadata);

        return NewState(activityExecutionId, ParentNodeId, ActivityExecutionStatus.Running, parentActivityExecutionId) with
        {
            IterationId = "loop:iteration:0",
            Provenance = provenance
        };
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

    // Shared IActivity stub for the child-completion test doubles: subclasses implement only the observation
    // performed inside OnChildCompleted (each completes the composite so the handler advances normally).
    private abstract class ObservingCompositeActivityBase : IActivity, IActivityChildCompletionHandler
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

        public ValueTask<bool> CanExecuteAsync(IActivityExecutionContext context) => ValueTask.FromResult(true);
        public ValueTask ExecuteAsync(IActivityExecutionContext context) => ValueTask.CompletedTask;

        public ValueTask OnChildCompletedAsync(ActivityChildCompletedContext context)
        {
            ChildCompletionInvoked = true;
            Observe(context.ParentContext);
            ((IRuntimeActivityExecutionContext)context.ParentContext).CompleteCompositeActivity([ActivityOutcomes.Done]);
            return ValueTask.CompletedTask;
        }

        protected abstract void Observe(IActivityExecutionContext parentContext);
    }

    private sealed class CarrierObservingCompositeActivity : ObservingCompositeActivityBase
    {
        public string? ObservedCorrelationId { get; private set; }
        public string? ObservedWorkflowName { get; private set; }
        public int ObservedWorkflowDefinitionVersion { get; private set; }
        public object? ObservedInput { get; private set; }
        public object? ObservedVariable { get; private set; }
        public object? ObservedOutput { get; private set; }

        protected override void Observe(IActivityExecutionContext parentContext)
        {
            var state = (IExecutionExpressionState)parentContext;
            ObservedCorrelationId = state.CorrelationId;
            ObservedWorkflowName = state.WorkflowName;
            ObservedWorkflowDefinitionVersion = state.WorkflowDefinitionVersion;
            ObservedInput = state.WorkflowInputs.GetValueOrDefault("orderId");
            ObservedVariable = state.WorkflowVariables.GetValueOrDefault(VariableName);
            ObservedOutput = state.ActivityOutputValues.GetValueOrDefault("prior");
        }
    }

    // Reads variables by name through the parent context's visible scope chain (IScopedVariableProvider) rather
    // than the flat carrier projection, so the assertions exercise the threaded self-owner scope directly.
    private sealed class NamedVariableObservingCompositeActivity : ObservingCompositeActivityBase
    {
        public bool EnclosingVariableResolved { get; private set; }
        public object? ObservedEnclosingVariable { get; private set; }
        public bool IterationVariableResolved { get; private set; }

        protected override void Observe(IActivityExecutionContext parentContext)
        {
            var scoped = (IScopedVariableProvider)parentContext;
            EnclosingVariableResolved = scoped.TryGetVariableValueByName(VariableName, out var enclosing);
            ObservedEnclosingVariable = enclosing;
            IterationVariableResolved = scoped.TryGetVariableValueByName(IterationItemName, out _);
        }
    }

    // Assigns a workflow-root variable by name inside OnChildCompleted through the visible scope chain, exercising
    // the workflow-root write-back fold into the completion checkpoint.
    private sealed class WorkflowRootVariableAssigningCompositeActivity(string newValue) : ObservingCompositeActivityBase
    {
        public bool AssignmentAccepted { get; private set; }

        protected override void Observe(IActivityExecutionContext parentContext) =>
            AssignmentAccepted = ((IScopedVariableProvider)parentContext).TrySetVariableValueByName(VariableName, newValue);
    }
}
