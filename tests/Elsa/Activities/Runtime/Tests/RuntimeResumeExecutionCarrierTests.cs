using System.Text.Json;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Attributes;
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
/// Locks the execution-time expression carrier (ADR 0030) on the <see cref="WorkflowResumeBookmarkSchedulerWorkHandler"/>
/// path. Before this unit the resume context was built with no identity carrier and no variable scope, so a resume
/// callback that evaluated an expression saw empty <c>getWorkflowInstanceId()</c>/<c>getCorrelationId()</c>/
/// <c>getInput()</c>/<c>getVariable()</c>/<c>getOutput()</c> and had no scope to write variables into. This drives the
/// real handler over shared in-memory stores and asserts the resumed activity observes correct identity, inputs,
/// variables, and prior activity outputs through <see cref="IExecutionExpressionState"/>, and that an in-evaluation
/// variable write-back lands in the visible scope — the same scope the reference
/// <c>RunJavaScriptExecutionAccessorsTests</c> exercises through the JavaScript pipeline.
/// </summary>
public sealed class RuntimeResumeExecutionCarrierTests
{
    private const string WorkflowExecutionId = ExecutionCarrierTestData.WorkflowExecutionId;
    private const string WaitNodeId = "node-wait";
    private const string RootNodeId = "node-root";
    private const string VariableName = ExecutionCarrierTestData.VariableName;

    private readonly DateTimeOffset _now = ExecutionCarrierTestData.Now;
    private readonly InMemoryWorkflowExecutableStore _executableStore = new();
    private readonly InMemoryActivityExecutionStateStore _activityStateStore = new();
    private readonly InMemoryBookmarkStateStore _bookmarkStateStore = new();
    private readonly InMemoryWorkflowSchedulerWorkQueue _schedulerWorkQueue = new();
    private readonly InMemoryIncidentStateStore _incidentStateStore = new();
    private readonly InMemoryActivityExecutionInspectionStore _inspectionStore = new();
    private readonly InMemoryDurableValueStateStore _durableValueStateStore = new();
    private readonly InMemoryWorkflowExecutionStateStore _workflowStateStore = new();
    private readonly InMemoryRuntimeCheckpointCommitStore _checkpointWriter;

    public RuntimeResumeExecutionCarrierTests() =>
        _checkpointWriter = new InMemoryRuntimeCheckpointCommitStore(
            workflowExecutionStateStore: _workflowStateStore,
            activityExecutionStateStore: _activityStateStore,
            bookmarkStateStore: _bookmarkStateStore,
            durableValueStateStore: _durableValueStateStore,
            incidentStateStore: _incidentStateStore,
            operationalStateStore: null,
            schedulerStateStore: null,
            activityExecutionInspectionWriter: _inspectionStore);

    [Fact]
    public async Task ResumeCallback_ObservesExecutionTimeIdentityInputsVariablesAndOutputs()
    {
        await _executableStore.SaveAsync(NewExecutable());
        await _workflowStateStore.SaveAsync(ExecutionCarrierTestData.NewWorkflowState(_now));
        await ExecutionCarrierTestData.SeedInputAsync(_durableValueStateStore, _now, "orderId", "order-1");
        await ExecutionCarrierTestData.SeedVariableAsync(_durableValueStateStore, _now, "Hello");
        await ExecutionCarrierTestData.SeedOutputAsync(_durableValueStateStore, _now, "prior", "prior-value");
        await _activityStateStore.SaveAsync(NewRunningState(RootNodeId, "actexec-root", parentActivityExecutionId: null));
        await _activityStateStore.SaveAsync(NewSuspendedWaitState());
        await SaveBookmarkAsync();

        var activity = new CarrierObservingResumeTargetActivity();
        await using var provider = NewProvider(new RecordingActivityFactory(activity));
        var handler = NewHandler(provider);

        await handler.HandleAsync(NewResumeWorkItem());

        Assert.True(activity.ResumeInvoked);
        // Identity — sourced from the workflow-execution state + pinned executable, empty before this unit.
        Assert.Equal(WorkflowExecutionId, activity.ObservedWorkflowInstanceId);
        Assert.Equal("corr-1", activity.ObservedCorrelationId);
        Assert.Equal("Instance A", activity.ObservedWorkflowName);
        Assert.Equal("definition-1", activity.ObservedWorkflowDefinitionId);
        Assert.Equal(7, activity.ObservedWorkflowDefinitionVersion); // artifact version "7.0.0" -> major 7
        // Inputs / variables / prior outputs — the durable-value projections carried on the carrier.
        Assert.Equal("order-1", ExecutionCarrierTestData.AsString(activity.ObservedInput));
        Assert.Equal("Hello", ExecutionCarrierTestData.AsString(activity.ObservedVariable));
        Assert.Equal("prior-value", ExecutionCarrierTestData.AsString(activity.ObservedOutput));
        // In-evaluation variable write-back lands in the visible scope (impossible before: no scope was passed).
        Assert.Equal("Hi", activity.ReadBackAfterWrite);
    }

    private WorkflowResumeBookmarkSchedulerWorkHandler NewHandler(ServiceProvider provider) =>
        new(new RuntimeActivityInputMaterializer(), provider.GetRequiredService<IServiceScopeFactory>(), new FixedTimeProvider(_now));

    private ValueTask<BookmarkState> SaveBookmarkAsync() =>
        _bookmarkStateStore.SaveAsync(new BookmarkState(
            BookmarkId: "bookmark-1",
            WorkflowExecutionId: WorkflowExecutionId,
            ActivityExecutionId: "actexec-wait",
            ExecutableNodeId: WaitNodeId,
            ResumeTargetId: "resume-target:delivery",
            StimulusType: "delivery-status",
            StimulusHash: "sha256:delivery-status:order-1",
            Payload: null,
            Metadata: new Dictionary<string, string>(),
            CreatedAt: _now.AddMinutes(-1),
            ExpiresAt: null));

    private ServiceProvider NewProvider(IActivityFactory factory)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => factory);
        services.AddSingleton<IWorkflowExecutableStore>(_executableStore);
        services.AddSingleton<IActivityExecutionStateStore>(_activityStateStore);
        services.AddSingleton<IBookmarkStateStore>(_bookmarkStateStore);
        services.AddSingleton<IWorkflowSchedulerWorkQueue>(_schedulerWorkQueue);
        services.AddSingleton<IRuntimeActivityOutputRegister, InMemoryRuntimeActivityOutputRegister>();
        services.AddSingleton<IDurableValueStateStore>(_durableValueStateStore);
        services.AddSingleton<IWorkflowExecutionStateStore>(_workflowStateStore);
        services.AddSingleton<IIncidentStateStore>(_incidentStateStore);
        services.AddSingleton<IActivityExecutionInspectionStore>(_inspectionStore);
        services.AddSingleton<TimeProvider>(new FixedTimeProvider(_now));
        services.AddSingleton<IRuntimeCheckpointPersistencePolicy, ImmediateRuntimeCheckpointPersistencePolicy>();
        services.AddSingleton<IRuntimeCheckpointCommitStore>(_checkpointWriter);
        services.AddSingleton<IRuntimePostCommitIntentDispatcher, RuntimeSchedulerPostCommitIntentDispatcher>();
        services.AddSingleton<IRuntimeActivityExecutionInspectionAccumulator, RuntimeActivityExecutionInspectionAccumulator>();
        services.AddSingleton<IRuntimePayloadCapturePolicy, DefaultRuntimePayloadCapturePolicy>();
        services.AddSingleton<RuntimeCheckpointCommitter>();
        services.AddSingleton<ActivityFaultIncidentRecorder>();
        services.AddSingleton<IRuntimeExecutionIdGenerator, GuidRuntimeExecutionIdGenerator>();
        services.AddSingleton<IBookmarkConsumptionCheckpointService, BookmarkConsumptionCheckpointService>();
        return services.BuildServiceProvider();
    }

    private RuntimeSchedulerWorkItem NewResumeWorkItem() =>
        new(
            workItemId: "resume-work",
            workflowExecutionId: WorkflowExecutionId,
            commandId: "command-resume",
            commandKind: WorkflowExecutionCommandKind.ResumeBookmark,
            envelopeId: "envelope-1",
            idempotencyKey: $"{WorkflowExecutionId}:resume:bookmark-1",
            enqueuedAt: _now,
            recordedAt: _now,
            sequence: 40,
            payload: JsonSerializer.SerializeToElement(new RuntimeResumeBookmarkCommandPayload(
                pinnedExecutable: ExecutionCarrierTestData.NewIdentity(),
                bookmarkId: "bookmark-1",
                activityExecutionId: "actexec-wait",
                executableNodeId: WaitNodeId,
                resumeTargetId: "resume-target:delivery",
                stimulusType: "delivery-status",
                stimulusHash: "sha256:delivery-status:order-1",
                input: null,
                reason: RuntimeResumeBookmarkCommandPayload.StimulusMatchedReason)),
            commandMetadata: new Dictionary<string, string> { ["source"] = "test" },
            envelopeMetadata: new Dictionary<string, string> { ["transport"] = "in-process" });

    private ActivityExecutionState NewSuspendedWaitState() =>
        NewState(WaitNodeId, "actexec-wait", "actexec-root", ActivityExecutionStatus.Suspended, "BookmarkWaiting", ["bookmark-1"]);

    private ActivityExecutionState NewRunningState(string executableNodeId, string activityExecutionId, string? parentActivityExecutionId) =>
        NewState(executableNodeId, activityExecutionId, parentActivityExecutionId, ActivityExecutionStatus.Running, null, []);

    private ActivityExecutionState NewState(
        string executableNodeId,
        string activityExecutionId,
        string? parentActivityExecutionId,
        ActivityExecutionStatus status,
        string? subStatus,
        IReadOnlyCollection<string> bookmarkIds) =>
        new(
            Execution: new ActivityExecution(activityExecutionId, WorkflowExecutionId, executableNodeId, $"authored-{executableNodeId}", "test/activity", "1.0.0"),
            Status: status,
            SubStatus: subStatus,
            ScheduledAt: _now.AddMinutes(-2),
            StartedAt: _now.AddMinutes(-1),
            CompletedAt: null,
            SchedulingActivityExecutionId: parentActivityExecutionId,
            ParentActivityExecutionId: parentActivityExecutionId,
            BranchId: null,
            IterationId: null,
            CallStackDepth: null,
            BookmarkIds: bookmarkIds,
            IncidentIds: [],
            FaultCount: 0,
            AggregateFaultCount: 0,
            Metadata: new Dictionary<string, string>());

    private WorkflowExecutable NewExecutable()
    {
        // The root container declares the workflow-scope variable `greeting`, so the resume path's scope build
        // resolves it (its current value comes from the seeded durable value) and an in-evaluation write-back lands.
        var structurePayload = JsonSerializer.SerializeToElement(
            new
            {
                variables = new[]
                {
                    new VariableDefinition("var-greeting", VariableName, new TypeReference("String"), null, new ArgumentValue("seed", "Literal"))
                }
            },
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        var wait = NewNode(WaitNodeId);
        var root = new ExecutableNode(
            executableNodeId: RootNodeId,
            authoredActivityId: $"authored-{RootNodeId}",
            activityType: "test/container",
            activityTypeVersion: "1.0.0",
            descriptorType: "test",
            descriptorPayload: JsonSerializer.SerializeToElement(new { type = "test" }),
            inputBindings: new Dictionary<string, RuntimeInputBinding>(),
            outputCaptures: new Dictionary<string, RuntimeOutputCapture>(),
            metadata: new Dictionary<string, string>(),
            childSlots: [new ExecutableChildSlot("children", [wait])],
            structure: new ExecutableActivityStructure("test.structure", "1.0.0", structurePayload));

        return new(
            identity: ExecutionCarrierTestData.NewIdentity(),
            rootActivity: root,
            resumeTargets: new Dictionary<string, WorkflowExecutableResumeTarget>
            {
                ["resume-target:delivery"] = new(
                    ResumeTargetId: "resume-target:delivery",
                    ExecutableNodeId: WaitNodeId,
                    HandlerKey: "test-handler",
                    Metadata: new Dictionary<string, string>())
            },
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

    private sealed class CarrierObservingResumeTargetActivity : ActivityBase
    {
        public bool ResumeInvoked { get; private set; }
        public string? ObservedWorkflowInstanceId { get; private set; }
        public string? ObservedCorrelationId { get; private set; }
        public string? ObservedWorkflowName { get; private set; }
        public string? ObservedWorkflowDefinitionId { get; private set; }
        public int ObservedWorkflowDefinitionVersion { get; private set; }
        public object? ObservedInput { get; private set; }
        public object? ObservedVariable { get; private set; }
        public object? ObservedOutput { get; private set; }
        public object? ReadBackAfterWrite { get; private set; }

        [ResumeTarget("resume-target:delivery")]
        private void Resume(IActivityExecutionContext context)
        {
            ResumeInvoked = true;
            var state = (IExecutionExpressionState)context;
            ObservedWorkflowInstanceId = state.WorkflowInstanceId;
            ObservedCorrelationId = state.CorrelationId;
            ObservedWorkflowName = state.WorkflowName;
            ObservedWorkflowDefinitionId = state.WorkflowDefinitionId;
            ObservedWorkflowDefinitionVersion = state.WorkflowDefinitionVersion;
            ObservedInput = state.WorkflowInputs.GetValueOrDefault("orderId");
            ObservedVariable = state.WorkflowVariables.GetValueOrDefault(VariableName);
            ObservedOutput = state.ActivityOutputValues.GetValueOrDefault("prior");

            context.ExpressionExecutionContext.SetVariable(VariableName, "Hi");
            ReadBackAfterWrite = ((IScopedVariableProvider)context).TryGetVariableValueByName(VariableName, out var written) ? written : null;
        }
    }
}
