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
/// Locks the suspend/handoff write-back boundary (#286): an activity that mutates a workflow-scope variable
/// and then suspends (creates a bookmark) must have its dirty-tracked write-back flushed to durable state on
/// the bookmark continuation path — the path that does not reach the completion checkpoint. After the bookmark
/// is resumed, a downstream activity reads the variable and sees the mutated value, not the start-time seed.
/// Drives the real <see cref="WorkflowInvokeActivitySchedulerWorkHandler"/>,
/// <see cref="WorkflowResumeBookmarkSchedulerWorkHandler"/>, and materializer over shared in-memory stores —
/// no mock evaluator.
/// </summary>
public sealed class WorkflowVariableSuspendResumeWriteBackTests
{
    private const string WorkflowExecutionId = "wfexec-1";
    private const string RootNodeId = "node-root";
    private const string WaitNodeId = "node-wait";
    private const string DownstreamNodeId = "node-down";
    private const string VariableName = "counter";
    private const string VariableReferenceKey = "var-counter";
    private const string VariableValueId = "variable:counter";

    private readonly DateTimeOffset _now = new(2026, 6, 12, 12, 0, 0, TimeSpan.Zero);
    private readonly InMemoryWorkflowExecutableStore _executableStore = new();
    private readonly InMemoryActivityExecutionStateStore _activityStateStore = new();
    private readonly InMemoryBookmarkStateStore _bookmarkStateStore = new();
    private readonly InMemoryWorkflowSchedulerWorkQueue _schedulerWorkQueue = new();
    private readonly InMemoryRuntimeActivityOutputRegister _activityOutputRegister = new();
    private readonly InMemoryDurableValueStateStore _durableValueStateStore = new();
    private readonly InMemoryIncidentStateStore _incidentStateStore = new();
    private readonly InMemoryActivityExecutionInspectionStore _inspectionStore = new();
    private readonly InMemoryRuntimeCheckpointCommitStore _checkpointWriter;

    public WorkflowVariableSuspendResumeWriteBackTests() =>
        _checkpointWriter = new InMemoryRuntimeCheckpointCommitStore(
            workflowExecutionStateStore: null,
            activityExecutionStateStore: _activityStateStore,
            bookmarkStateStore: _bookmarkStateStore,
            durableValueStateStore: _durableValueStateStore,
            incidentStateStore: _incidentStateStore,
            operationalStateStore: null,
            schedulerStateStore: null,
            activityExecutionInspectionWriter: _inspectionStore);

    [Fact]
    public async Task MutatingSuspendingActivity_FlushesWriteBack_AndDownstreamReadAfterResumeSeesMutatedValue()
    {
        await _executableStore.SaveAsync(NewExecutable());
        // The seed durable value: counter = 1 at start (what variables.* would project before any mutation).
        await SeedVariableAsync(1);
        // The variable-declaring root container is the workflow scope's owning execution; both children walk
        // up to it.
        await _activityStateStore.SaveAsync(NewRunningState(RootNodeId, "actexec-root", parentActivityExecutionId: null));

        // --- Phase 1: a child mutates counter -> 9 and suspends on a bookmark (the boundary under test). ---
        var waitActivity = new MutatingSuspendingActivity(VariableName, 9, NewBookmarkRequest());
        await _activityStateStore.SaveAsync(NewRunningState(WaitNodeId, "actexec-wait", parentActivityExecutionId: "actexec-root"));
        await using (var invokeProvider = NewProvider(new RecordingActivityFactory(waitActivity)))
        {
            await NewInvokeHandler(invokeProvider).HandleAsync(NewInvokeWorkItem(WaitNodeId, "actexec-wait"));
        }

        // The activity suspended (no completion), and the dirty-tracked write-back was flushed on the bookmark
        // path: durable state now projects the mutated value, superseding the seed.
        var waitState = await _activityStateStore.FindAsync(WorkflowExecutionId, "actexec-wait");
        Assert.Equal(ActivityExecutionStatus.Running, waitState!.Status); // suspended awaiting bookmark, not completed
        Assert.Single(await _schedulerWorkQueue.ListAsync(new RuntimeSchedulerWorkQuery(WorkflowExecutionId)),
            work => work.CommandKind == WorkflowExecutionCommandKind.CreateBookmark);
        Assert.Equal(9, ProjectedVariableValue());

        // --- Phase 2: resume the bookmark through the real resume handler. ---
        await _activityStateStore.SaveAsync(NewSuspendedState(WaitNodeId, "actexec-wait", parentActivityExecutionId: "actexec-root"));
        await _bookmarkStateStore.SaveAsync(NewBookmark());
        await using (var resumeProvider = NewProvider(new RecordingActivityFactory(new ResumeTargetActivity())))
        {
            await NewResumeHandler(resumeProvider).HandleAsync(NewResumeWorkItem());
        }
        Assert.Null(await _bookmarkStateStore.FindAsync(WorkflowExecutionId, "bookmark-1")); // bookmark consumed

        // --- Phase 3: a downstream child reads counter and must see the mutated value persisted across the
        // suspend/resume boundary, not the start seed. ---
        var downstreamActivity = new RecordingCounterActivity();
        await _activityStateStore.SaveAsync(NewRunningState(DownstreamNodeId, "actexec-down", parentActivityExecutionId: "actexec-root"));
        await using (var downProvider = NewProvider(new RecordingActivityFactory(downstreamActivity)))
        {
            await NewInvokeHandler(downProvider).HandleAsync(NewInvokeWorkItem(DownstreamNodeId, "actexec-down"));
        }

        Assert.Equal(9, downstreamActivity.ObservedValue);
    }

    private int ProjectedVariableValue()
    {
        var durableValues = _durableValueStateStore.ListAsync(WorkflowExecutionId).AsTask().GetAwaiter().GetResult();
        var projected = RuntimeInputBindingStateProjection.ProjectWorkflowVariables(durableValues);
        return ((JsonElement)projected[VariableName]!).GetInt32();
    }

    private async Task SeedVariableAsync(int value) =>
        await _durableValueStateStore.SaveAsync(new DurableValueState(
            durableValueId: $"durable-{VariableValueId}",
            workflowExecutionId: WorkflowExecutionId,
            valueId: VariableValueId,
            type: new RuntimeValueTypeDescriptor("clr", typeof(int).FullName, null),
            lifecycle: DurableValueLifecycle.Instance,
            storage: DurableValueStorage.Inline,
            inlineValue: JsonSerializer.SerializeToElement(value),
            externalReference: null,
            sourceActivityExecutionId: null,
            capturedAt: _now,
            metadata: new Dictionary<string, string> { [RuntimeMetadataKeys.VariableName] = VariableName }));

    private WorkflowInvokeActivitySchedulerWorkHandler NewInvokeHandler(ServiceProvider provider) =>
        new(new RuntimeActivityInputMaterializer(), provider.GetRequiredService<IServiceScopeFactory>(), new FixedTimeProvider(_now));

    private WorkflowResumeBookmarkSchedulerWorkHandler NewResumeHandler(ServiceProvider provider) =>
        new(new RuntimeActivityInputMaterializer(), provider.GetRequiredService<IServiceScopeFactory>(), new FixedTimeProvider(_now));

    private ServiceProvider NewProvider(IActivityFactory factory)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => factory);
        services.AddSingleton<IWorkflowExecutableStore>(_executableStore);
        services.AddSingleton<IActivityExecutionStateStore>(_activityStateStore);
        services.AddSingleton<IBookmarkStateStore>(_bookmarkStateStore);
        services.AddSingleton<IWorkflowSchedulerWorkQueue>(_schedulerWorkQueue);
        services.AddSingleton<IRuntimeActivityOutputRegister>(_activityOutputRegister);
        services.AddSingleton<IDurableValueStateStore>(_durableValueStateStore);
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

    private RuntimeSchedulerWorkItem NewInvokeWorkItem(string executableNodeId, string activityExecutionId) =>
        new(
            workItemId: $"invoke-work:{activityExecutionId}",
            workflowExecutionId: WorkflowExecutionId,
            commandId: $"command:{activityExecutionId}",
            commandKind: WorkflowExecutionCommandKind.InvokeActivity,
            envelopeId: "envelope-1",
            idempotencyKey: $"{WorkflowExecutionId}:invoke:{activityExecutionId}",
            enqueuedAt: _now,
            recordedAt: _now,
            sequence: 30,
            payload: JsonSerializer.SerializeToElement(new RuntimeInvokeActivityCommandPayload(
                NewIdentity(), executableNodeId, activityExecutionId, RuntimeInvokeActivityCommandPayload.StartedActivityReason)),
            commandMetadata: new Dictionary<string, string> { ["source"] = "test" },
            envelopeMetadata: new Dictionary<string, string> { ["transport"] = "in-process" });

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
                pinnedExecutable: NewIdentity(),
                bookmarkId: "bookmark-1",
                activityExecutionId: "actexec-wait",
                executableNodeId: WaitNodeId,
                resumeTargetId: "resume-target:delivery",
                stimulusType: "delivery-status",
                stimulusHash: "sha256:delivery-status:order-123",
                input: null,
                reason: RuntimeResumeBookmarkCommandPayload.StimulusMatchedReason)),
            commandMetadata: new Dictionary<string, string> { ["source"] = "test" },
            envelopeMetadata: new Dictionary<string, string> { ["transport"] = "in-process" });

    private ActivityBookmarkRequest NewBookmarkRequest() =>
        new(
            bookmarkId: "bookmark-1",
            resumeTargetId: "resume-target:delivery",
            stimulusType: "delivery-status",
            stimulusHash: "sha256:delivery-status:order-123",
            payload: null,
            expiresAt: _now.AddMinutes(10),
            metadata: new Dictionary<string, string>());

    private BookmarkState NewBookmark() =>
        new(
            BookmarkId: "bookmark-1",
            WorkflowExecutionId: WorkflowExecutionId,
            ActivityExecutionId: "actexec-wait",
            ExecutableNodeId: WaitNodeId,
            ResumeTargetId: "resume-target:delivery",
            StimulusType: "delivery-status",
            StimulusHash: "sha256:delivery-status:order-123",
            Payload: null,
            Metadata: new Dictionary<string, string>(),
            CreatedAt: _now.AddMinutes(-1),
            ExpiresAt: null);

    private ActivityExecutionState NewRunningState(string executableNodeId, string activityExecutionId, string? parentActivityExecutionId) =>
        NewState(executableNodeId, activityExecutionId, parentActivityExecutionId, ActivityExecutionStatus.Running, subStatus: null, bookmarkIds: []);

    private ActivityExecutionState NewSuspendedState(string executableNodeId, string activityExecutionId, string? parentActivityExecutionId) =>
        NewState(executableNodeId, activityExecutionId, parentActivityExecutionId, ActivityExecutionStatus.Suspended, subStatus: "BookmarkWaiting", bookmarkIds: ["bookmark-1"]);

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
        // The compiled root structure carries the workflow's authored variables in the generic `variables`
        // shape RuntimeVariableScopeFactory reads (Web JSON options); a single `counter` defaulting to 0.
        var structurePayload = JsonSerializer.SerializeToElement(
            new
            {
                variables = new[]
                {
                    new VariableDefinition(VariableReferenceKey, VariableName, TypeInformation.Integer, null, new ArgumentValue(0, "Literal"))
                }
            },
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        var wait = LeafNode(WaitNodeId);
        var down = LeafNode(DownstreamNodeId, DurableCounterBinding(VariableValueId));

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
            childSlots: [new ExecutableChildSlot("children", [wait, down])],
            structure: new ExecutableActivityStructure("test.structure", "1.0.0", structurePayload));

        return new(
            identity: NewIdentity(),
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

    private static ExecutableNode LeafNode(string nodeId, RuntimeInputBinding? inputBinding = null) =>
        new(
            executableNodeId: nodeId,
            authoredActivityId: $"authored-{nodeId}",
            activityType: "test/activity",
            activityTypeVersion: "1.0.0",
            descriptorType: "test",
            descriptorPayload: JsonSerializer.SerializeToElement(new { type = "test" }),
            inputBindings: inputBinding is null
                ? new Dictionary<string, RuntimeInputBinding>()
                : new Dictionary<string, RuntimeInputBinding> { ["Value"] = inputBinding },
            outputCaptures: new Dictionary<string, RuntimeOutputCapture>(),
            metadata: new Dictionary<string, string>());

    private static RuntimeInputBinding DurableCounterBinding(string valueId) =>
        new(
            inputName: "Value",
            source: RuntimeInputBindingSource.DurableValue,
            durableValue: new RuntimeDurableValueReference(valueId),
            metadata: new Dictionary<string, string> { [RuntimeActivityInputMaterializer.InputTypeMetadataKey] = $"{typeof(int).FullName}, {typeof(int).Assembly.GetName().Name}" });

    private static WorkflowExecutableIdentity NewIdentity() =>
        new("artifact-1", "definition-1", "version-1", "1.0.0", "sha256:test");

    private sealed class RecordingActivityFactory(IActivity activity) : IActivityFactory
    {
        public ValueTask<IActivity> Create(
            string descriptorType,
            JsonElement payload,
            IDictionary<string, InputArgument>? inputs,
            IDictionary<string, OutputArgument>? outputs,
            CancellationToken cancellationToken = default)
        {
            if (activity is RecordingCounterActivity counterActivity && inputs is not null && inputs.TryGetValue("Value", out var value))
                counterActivity.Value = (InputArgument<int>)value;
            return ValueTask.FromResult(activity);
        }
    }

    // Mutates a workflow-scope variable by name through the visible scope chain, then suspends on a bookmark —
    // the exact shape the suspend-boundary write-back must persist.
    private sealed class MutatingSuspendingActivity(string variableName, int value, ActivityBookmarkRequest request) : ActivityBase
    {
        protected override void Execute(IActivityExecutionContext context)
        {
            context.ExpressionExecutionContext.SetVariable(variableName, value);
            context.CreateBookmark(request);
        }
    }

    private sealed class RecordingCounterActivity : ActivityBase
    {
        public InputArgument<int> Value { get; set; } = null!;
        public int? ObservedValue { get; private set; }

        protected override void Execute(IActivityExecutionContext context) => ObservedValue = context.Get(Value);
    }

    private sealed class ResumeTargetActivity : ActivityBase
    {
        [ResumeTarget("resume-target:delivery")]
        private ValueTask ResumeAsync(IActivityExecutionContext context) => ValueTask.CompletedTask;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
