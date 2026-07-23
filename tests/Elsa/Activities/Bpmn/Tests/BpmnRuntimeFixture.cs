using System.Text.Json;
using Elsa.Activities.Bpmn.Internal;
using Elsa.Activities.Bpmn.Models;
using Elsa.Activities.Testing;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using BpmnProcessActivity = Elsa.Activities.Bpmn.Activities.BpmnProcess;

namespace Elsa.Activities.Bpmn.Tests;

public sealed class BpmnRuntimeFixture : IAsyncDisposable
{
    public const string ProcessNodeId = "node-bpmn";

    private static readonly JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web);

    private readonly WorkflowExecutionHarness _harness;

    private BpmnRuntimeFixture(WorkflowExecutionHarness harness)
    {
        _harness = harness;
        Provider = harness.Services;
    }

    public IServiceProvider Provider { get; }

    public static ValueTask<BpmnRuntimeFixture> CreateAsync(IEnumerable<string> activityExecutionIds, Action<IServiceCollection>? configureServices = null)
    {
        var builder = WorkflowExecutionHarness.Create()
            .WithFeature(services => new ActivitiesBpmnFeature().ConfigureServices(services))
            .WithProbeLeaf()
            .WithFaultingLeaf();
        if (configureServices is not null)
            builder.ConfigureServices(configureServices);

        return new ValueTask<BpmnRuntimeFixture>(new BpmnRuntimeFixture(builder.Build(activityExecutionIds)));
    }

    public ValueTask DisposeAsync() => _harness.DisposeAsync();

    public Task<WorkflowExecutionRun> RunAsync(WorkflowExecutable executable) => _harness.RunAsync(executable);

    /// <summary>
    /// Runs the process as a trigger-started run (spec 117): the matched binding's node id is the process node and
    /// its metadata names the event-defined start element to seed, mirroring how the stimulus router forwards
    /// <c>binding.Metadata</c> onto the start dispatch. Only the targeted event-defined start seeds a token.
    /// </summary>
    public Task<WorkflowExecutionRun> RunAsTriggerAsync(WorkflowExecutable executable, string startElementId) =>
        _harness.RunAsTriggerDeliveryAsync(
            executable,
            triggerNodeId: ProcessNodeId,
            triggerMetadata: new Dictionary<string, string> { [BpmnStartTrigger.StartElementIdMetadataKey] = startElementId });

    /// <summary>Lists the run's live bookmark resume handles (spec 116 catch-event scenarios).</summary>
    public async Task<IReadOnlyCollection<BookmarkState>> BookmarksAsync() =>
        await Provider.GetRequiredService<IBookmarkStateStore>()
            .ListAllBookmarkStatesAsync(WorkflowExecutionHarness.WorkflowExecutionId);

    /// <summary>
    /// Resumes a suspended child's bookmark through the underlying harness, mirroring how a matched
    /// stimulus (durable timer / raised event) resumes a waiting catch-event child: the typed
    /// trigger-delivery metadata is derived from the suspension's committed trigger registration, the
    /// way the production stimulus router builds it.
    /// </summary>
    public async Task<WorkflowExecutionRun> ResumeAsync(BookmarkState bookmark, JsonElement input)
    {
        var state = await Provider.GetRequiredService<IActivityExecutionStateStore>()
            .FindAsync(WorkflowExecutionHarness.WorkflowExecutionId, bookmark.ActivityExecutionId)
            ?? throw new InvalidOperationException($"Activity execution '{bookmark.ActivityExecutionId}' is missing.");
        var registration = (state.TriggerRegistrations ?? [])
            .Single(candidate => StringComparer.Ordinal.Equals(candidate.RegistrationId, bookmark.BookmarkId));

        return await _harness.ResumeAsync(
            pinnedExecutable: WorkflowExecutionHarness.Identity,
            bookmarkId: bookmark.BookmarkId,
            activityExecutionId: bookmark.ActivityExecutionId,
            executableNodeId: bookmark.ExecutableNodeId,
            resumeTargetId: bookmark.ResumeTargetId,
            stimulusType: bookmark.StimulusType,
            stimulusHash: bookmark.StimulusHash,
            input: input,
            triggerDelivery: new RuntimeTypedTriggerDeliveryMetadata(
                deliveryId: $"delivery:{bookmark.BookmarkId}",
                payloadType: registration.PayloadType,
                providerId: "test.stimulus",
                receivedAt: new DateTimeOffset(2026, 6, 12, 12, 0, 0, TimeSpan.Zero),
                deduplicationKey: $"dedupe:{bookmark.BookmarkId}"));
    }

    public async Task<BpmnExecutionState> GetBpmnStateAsync()
    {
        var states = await Provider.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync("wfexec-1");
        var processState = states.Single(state => state.Execution.ExecutableNodeId == ProcessNodeId);
        var lastCommittedPrivateState = Provider.GetRequiredService<InMemoryRuntimeCheckpointCommitStore>().ListCommits()
            .SelectMany(record => record.Commit.StateChanges.ActivityExecutions)
            .Where(change => StringComparer.Ordinal.Equals(change.StateId, processState.Execution.ActivityExecutionId))
            .Select(change => change.State.PrivateState?.Value.InlineValue?.GetRawText())
            .LastOrDefault(value => value is not null);
        var raw = processState.PrivateState?.Value.InlineValue?.GetRawText() ?? lastCommittedPrivateState
            ?? throw new InvalidOperationException($"BPMN private state is missing. Status: {processState.Status}/{processState.SubStatus}.");
        return JsonSerializer.Deserialize<BpmnExecutionState>(raw, new JsonSerializerOptions(JsonSerializerDefaults.Web))
               ?? throw new InvalidOperationException("BPMN execution state resolved to null.");
    }

    public WorkflowExecutable NewExecutable(
        IReadOnlyCollection<ExecutableNode> children,
        IReadOnlyCollection<BpmnElement> elements,
        IReadOnlyCollection<BpmnSequenceFlow> sequenceFlows,
        IReadOnlyCollection<WorkflowExecutableResumeTarget>? resumeTargets = null,
        IReadOnlyCollection<RuntimeVariableDeclaration>? variables = null,
        bool isTransaction = false)
    {
        // The runtime reads the container's declared variables as RuntimeVariableDeclaration; BpmnStructure carries
        // authored VariableDefinition, so a spec-123 collection variable is injected as the runtime shape here (the
        // real publish compiler does the VariableDefinition→RuntimeVariableDeclaration lowering).
        var structurePayload = JsonSerializer.SerializeToNode(new BpmnStructure(elements, sequenceFlows, isTransaction: isTransaction), WebOptions)!.AsObject();
        if (variables is { Count: > 0 })
            structurePayload["variables"] = JsonSerializer.SerializeToNode(variables, WebOptions);

        var root = new ExecutableNode(
            executableNodeId: ProcessNodeId,
            authoredActivityId: "authored-bpmn",
            activityType: typeof(BpmnProcessActivity).FullName!,
            activityTypeVersion: "1.0.0",
            descriptorType: typeof(BpmnDescriptor).FullName!,
            descriptorPayload: JsonSerializer.SerializeToElement(new BpmnDescriptor()),
            inputBindings: new Dictionary<string, RuntimeInputBinding>(),
            metadata: new Dictionary<string, string>(),
            childSlots:
            [
                new ExecutableChildSlot(BpmnProcessActivity.ActivitiesSlotName, children)
            ],
            structure: new ExecutableActivityStructure(
                BpmnProcessActivity.StructureKind,
                BpmnProcessActivity.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(structurePayload)));

        var executable = WorkflowExecutionHarness.NewExecutable(root);
        if (resumeTargets is not { Count: > 0 })
            return executable;

        // The real publish compiler indexes each suspending child's [ResumeTarget] handler into this map with
        // node-scoped keys (PR #911); harness executables declare the same entries by hand.
        return new WorkflowExecutable(
            identity: executable.Identity,
            rootActivity: executable.RootActivity,
            resumeTargets: resumeTargets.ToDictionary(target => target.ResumeTargetId, StringComparer.Ordinal),
            createdAt: executable.CreatedAt,
            compatibilityMetadata: executable.CompatibilityMetadata);
    }

    /// <summary>
    /// Declares a node-scoped resume target the way the publish compiler emits them: a global
    /// <c>{nodeId}:{localId}</c> key with the activity-local id as the <c>LocalResumeTargetId</c> fallback.
    /// </summary>
    public static WorkflowExecutableResumeTarget NodeResumeTarget(string nodeId, string localResumeTargetId) =>
        new(
            ResumeTargetId: $"{nodeId}:{localResumeTargetId}",
            ExecutableNodeId: nodeId,
            HandlerKey: "ResumeAsync",
            Metadata: new Dictionary<string, string>(),
            LocalResumeTargetId: localResumeTargetId);

    public ExecutableNode NewProbeNode(string nodeId, IReadOnlyCollection<string>? outcomes = null) =>
        WorkflowExecutionHarness.NewProbeNode(nodeId, outcomes);

    public ExecutableNode NewFaultingNode(string nodeId) =>
        WorkflowExecutionHarness.NewFaultingNode(nodeId);

    public static BpmnElement StartEvent(string elementId = "start") =>
        new(elementId, BpmnElementTypes.StartEvent);

    public static BpmnElement EndEvent(string elementId = "end") =>
        new(elementId, BpmnElementTypes.EndEvent);

    public static BpmnElement TerminateEndEvent(string elementId = "terminate") =>
        new(elementId, BpmnElementTypes.EndEvent, eventDefinitions: [new BpmnEventDefinition(BpmnEventDefinitionTypes.Terminate)]);

    /// <summary>An event-defined start event (spec 117). Only the definition type matters at runtime; the token behaves like a none start.</summary>
    public static BpmnElement EventStart(string elementId, string definitionType) =>
        new(elementId, BpmnElementTypes.StartEvent, eventDefinitions: [new BpmnEventDefinition(definitionType)]);

    public static BpmnElement IntermediateCatchEvent(string elementId, string definitionType, string? childNodeId = null) =>
        new(elementId, BpmnElementTypes.IntermediateCatchEvent, childNodeId: childNodeId, eventDefinitions: [new BpmnEventDefinition(definitionType)]);

    public static BpmnElement Task(string elementId, string? childNodeId = null, string? defaultFlowId = null) =>
        new(elementId, BpmnElementTypes.Task, childNodeId: childNodeId, defaultFlowId: defaultFlowId);

    /// <summary>A call activity (spec 133): a task-family element that binds a <c>DispatchWorkflow</c> child; its child's Faulted/DispatchFailed/Cancelled outcomes route through BPMN error handling (D3).</summary>
    public static BpmnElement CallActivity(string elementId, string? childNodeId = null, string? defaultFlowId = null) =>
        new(elementId, BpmnElementTypes.CallActivity, childNodeId: childNodeId, defaultFlowId: defaultFlowId);

    /// <summary>A multi-instance call activity (spec 133 D3 / spec 121): runs its bound <c>DispatchWorkflow</c> child <paramref name="cardinality"/> times.</summary>
    public static BpmnElement MultiInstanceCallActivity(string elementId, string childNodeId, int cardinality, bool isSequential, string? defaultFlowId = null) =>
        new(elementId, BpmnElementTypes.CallActivity, childNodeId: childNodeId, defaultFlowId: defaultFlowId,
            loopCharacteristics: new BpmnLoopCharacteristics(isSequential: isSequential, cardinality: cardinality));

    /// <summary>A multi-instance task (spec 121): runs its bound child <paramref name="cardinality"/> times, sequentially or in parallel.</summary>
    public static BpmnElement MultiInstanceTask(string elementId, string childNodeId, int cardinality, bool isSequential, string? defaultFlowId = null) =>
        new(elementId, BpmnElementTypes.Task, childNodeId: childNodeId, defaultFlowId: defaultFlowId,
            loopCharacteristics: new BpmnLoopCharacteristics(isSequential: isSequential, cardinality: cardinality));

    /// <summary>A collection-mode multi-instance task (spec 123): runs its bound child once per item of the declared <paramref name="collectionVariable"/>.</summary>
    public static BpmnElement MultiInstanceCollectionTask(string elementId, string childNodeId, string collectionVariable, bool isSequential, string? itemVariable = null, string? defaultFlowId = null) =>
        new(elementId, BpmnElementTypes.Task, childNodeId: childNodeId, defaultFlowId: defaultFlowId,
            loopCharacteristics: new BpmnLoopCharacteristics(isSequential: isSequential, collectionVariable: collectionVariable, itemVariable: itemVariable));

    /// <summary>
    /// A container-scoped variable declaration (spec 123) whose durable initial value is the given inline JSON,
    /// in the runtime <see cref="RuntimeVariableDeclaration"/> shape the frame projector reads (a collection
    /// variable seeds a JSON array). Typed as the canonical dynamic <c>Elsa.Any</c>.
    /// </summary>
    public static RuntimeVariableDeclaration InlineVariable(string name, JsonElement value)
    {
        var type = new ValueTypeDescriptor("Elsa.Any");
        var policy = ValueProtectionPolicy.InstanceInline;
        return new RuntimeVariableDeclaration(name, name, type, policy, new RuntimeInputBinding(
            inputKey: name,
            targetType: type,
            effectivePolicy: policy,
            source: RuntimeInputBindingSource.Literal,
            literal: ValueEnvelope.Inline(type, value, policy)));
    }

    /// <summary>An <c>Elsa.Any</c> variable declaration seeded with a JSON array of the given string items.</summary>
    public static RuntimeVariableDeclaration StringArrayVariable(string name, params string[] items) =>
        InlineVariable(name, JsonSerializer.SerializeToElement(items));

    /// <summary>An <c>Elsa.Any</c> variable declaration whose durable initial value is an explicit null (spec 123 empty-loop path).</summary>
    public static RuntimeVariableDeclaration NullVariable(string name)
    {
        var type = new ValueTypeDescriptor("Elsa.Any");
        var policy = ValueProtectionPolicy.InstanceInline;
        return new RuntimeVariableDeclaration(name, name, type, policy, new RuntimeInputBinding(
            inputKey: name,
            targetType: type,
            effectivePolicy: policy,
            source: RuntimeInputBindingSource.Literal,
            literal: ValueEnvelope.Null(type, policy)));
    }

    public static BpmnElement ExclusiveGateway(string elementId, string? childNodeId = null, string? defaultFlowId = null) =>
        new(elementId, BpmnElementTypes.ExclusiveGateway, childNodeId: childNodeId, defaultFlowId: defaultFlowId);

    public static BpmnElement ParallelGateway(string elementId) =>
        new(elementId, BpmnElementTypes.ParallelGateway);

    public static BpmnElement EventBasedGateway(string elementId) =>
        new(elementId, BpmnElementTypes.EventBasedGateway);

    public static BpmnElement InclusiveGateway(string elementId, string? childNodeId = null, string? defaultFlowId = null) =>
        new(elementId, BpmnElementTypes.InclusiveGateway, childNodeId: childNodeId, defaultFlowId: defaultFlowId);

    /// <summary>A boundary event (spec 120) attached to <paramref name="attachedToRef"/>. Catch boundaries (timer/message/signal) bind a listener child; error boundaries bind none.</summary>
    public static BpmnElement BoundaryEvent(string elementId, string attachedToRef, string definitionType, string? childNodeId = null, bool cancelActivity = true) =>
        new(elementId, BpmnElementTypes.BoundaryEvent, childNodeId: childNodeId, eventDefinitions: [new BpmnEventDefinition(definitionType)], attachedToRef: attachedToRef, cancelActivity: cancelActivity);

    /// <summary>A compensation boundary event (spec 124) attached to <paramref name="attachedToRef"/>, referencing its handler via association (no listener child, no outbound flows).</summary>
    public static BpmnElement CompensationBoundary(string elementId, string attachedToRef, string handlerElementId) =>
        new(elementId, BpmnElementTypes.BoundaryEvent, eventDefinitions: [new BpmnEventDefinition(BpmnEventDefinitionTypes.Compensation)], attachedToRef: attachedToRef, compensationHandlerElementId: handlerElementId);

    /// <summary>A compensation handler (spec 124): a task-family element that binds a child, participates in no sequence flows, and runs only during a compensation replay.</summary>
    public static BpmnElement CompensationHandler(string elementId, string childNodeId) =>
        new(elementId, BpmnElementTypes.Task, childNodeId: childNodeId, isForCompensation: true);

    /// <summary>A plain subprocess element that binds a nested <c>BpmnProcess</c> child (spec 127 escalation host).</summary>
    public static BpmnElement SubProcess(string elementId, string childNodeId, string? defaultFlowId = null) =>
        new(elementId, BpmnElementTypes.SubProcess, childNodeId: childNodeId, defaultFlowId: defaultFlowId);

    /// <summary>A multi-instance subprocess host (spec 121/127): runs its bound nested process <paramref name="cardinality"/> times.</summary>
    public static BpmnElement MultiInstanceSubProcess(string elementId, string childNodeId, int cardinality, bool isSequential, string? defaultFlowId = null) =>
        new(elementId, BpmnElementTypes.SubProcess, childNodeId: childNodeId, defaultFlowId: defaultFlowId,
            loopCharacteristics: new BpmnLoopCharacteristics(isSequential: isSequential, cardinality: cardinality));

    /// <summary>An escalation intermediate throw event (spec 127): raises escalation code <paramref name="code"/> to the parent scope, then routes onward (fire-and-continue).</summary>
    public static BpmnElement EscalationThrow(string elementId, string code, string? name = null) =>
        new(elementId, BpmnElementTypes.IntermediateThrowEvent, eventDefinitions: [new BpmnEventDefinition(BpmnEventDefinitionTypes.Escalation, EscalationProperties(code, name))]);

    /// <summary>An escalation end event (spec 127): raises escalation code <paramref name="code"/> to the parent scope, then consumes its token (none-end semantics).</summary>
    public static BpmnElement EscalationEnd(string elementId, string code, string? name = null) =>
        new(elementId, BpmnElementTypes.EndEvent, eventDefinitions: [new BpmnEventDefinition(BpmnEventDefinitionTypes.Escalation, EscalationProperties(code, name))]);

    /// <summary>An escalation boundary event (spec 127) attached to a subprocess host; dormant (no listener). A <c>null</c> <paramref name="code"/> is the code-less catch-all.</summary>
    public static BpmnElement EscalationBoundary(string elementId, string attachedToRef, string? code = null, bool cancelActivity = true) =>
        new(elementId, BpmnElementTypes.BoundaryEvent, eventDefinitions: [new BpmnEventDefinition(BpmnEventDefinitionTypes.Escalation, code is null ? null : EscalationProperties(code, null))], attachedToRef: attachedToRef, cancelActivity: cancelActivity);

    private static IReadOnlyDictionary<string, string> EscalationProperties(string code, string? name)
    {
        var properties = new Dictionary<string, string> { [BpmnEventDefinitionProperties.Code] = code };
        if (!string.IsNullOrWhiteSpace(name))
            properties[BpmnEventDefinitionProperties.Name] = name;
        return properties;
    }

    /// <summary>An event subprocess (spec 128): a flow-less <c>triggeredByEvent</c> subprocess binding a nested body whose event-start trigger activates it.</summary>
    public static BpmnElement EventSubprocess(string elementId, string childNodeId) =>
        new(elementId, BpmnElementTypes.SubProcess, childNodeId: childNodeId, triggeredByEvent: true);

    /// <summary>A message/signal/timer event subprocess (spec 134): a flow-less <c>triggeredByEvent</c> subprocess binding a nested body plus an armed scope-listener node (<paramref name="listenerNodeId"/>).</summary>
    public static BpmnElement EventSubprocessWithListener(string elementId, string childNodeId, string listenerNodeId) =>
        new(elementId, BpmnElementTypes.SubProcess, childNodeId: childNodeId, triggeredByEvent: true, listenerNodeId: listenerNodeId);

    /// <summary>A message event-subprocess body start (spec 134): resolves its stimulus from <paramref name="name"/>; <paramref name="interrupting"/> models the start event's <c>isInterrupting</c> flag.</summary>
    public static BpmnElement EventSubprocessMessageStart(string elementId, string name, bool interrupting = true) =>
        new(elementId, BpmnElementTypes.StartEvent,
            eventDefinitions: [new BpmnEventDefinition(BpmnEventDefinitionTypes.Message, new Dictionary<string, string> { [BpmnEventDefinitionProperties.Name] = name })],
            cancelActivity: interrupting);

    /// <summary>A signal event-subprocess body start (spec 134): resolves its stimulus from <paramref name="name"/>.</summary>
    public static BpmnElement EventSubprocessSignalStart(string elementId, string name, bool interrupting = true) =>
        new(elementId, BpmnElementTypes.StartEvent,
            eventDefinitions: [new BpmnEventDefinition(BpmnEventDefinitionTypes.Signal, new Dictionary<string, string> { [BpmnEventDefinitionProperties.Name] = name })],
            cancelActivity: interrupting);

    /// <summary>A timer event-subprocess body start (spec 134): a one-shot <paramref name="interval"/> (ISO-8601 duration), re-armed per non-interrupting fire.</summary>
    public static BpmnElement EventSubprocessTimerStart(string elementId, string interval, bool interrupting = true) =>
        new(elementId, BpmnElementTypes.StartEvent,
            eventDefinitions: [new BpmnEventDefinition(BpmnEventDefinitionTypes.Timer, new Dictionary<string, string> { [BpmnEventDefinitionProperties.Interval] = interval })],
            cancelActivity: interrupting);

    /// <summary>A durable <c>Delay</c> wait child (a timer scope-listener's synthesized node), mirroring the timer catch-event child.</summary>
    public static ExecutableNode DelayWaitNode(string nodeId, TimeSpan duration) =>
        NewClrNode(nodeId, typeof(Elsa.Activities.Scheduling.Activities.Delay), new Dictionary<string, object?>
        {
            [nameof(Elsa.Activities.Scheduling.Activities.Delay.Duration)] = duration
        });

    /// <summary>An escalation event-subprocess body start (spec 128); a <c>null</c> <paramref name="code"/> is the code-less catch-all, and <paramref name="interrupting"/> models the start event's <c>isInterrupting</c> flag.</summary>
    public static BpmnElement EventSubprocessEscalationStart(string elementId, string? code = null, bool interrupting = true) =>
        new(elementId, BpmnElementTypes.StartEvent,
            eventDefinitions: [new BpmnEventDefinition(BpmnEventDefinitionTypes.Escalation, code is null ? null : EscalationProperties(code, null))],
            cancelActivity: interrupting);

    /// <summary>An error event-subprocess body start (spec 128): catch-all, always interrupting (per BPMN).</summary>
    public static BpmnElement EventSubprocessErrorStart(string elementId, bool interrupting = true) =>
        new(elementId, BpmnElementTypes.StartEvent,
            eventDefinitions: [new BpmnEventDefinition(BpmnEventDefinitionTypes.Error)],
            cancelActivity: interrupting);

    /// <summary>
    /// Builds a nested <c>BpmnProcess</c> executable node (spec 127 escalation scenarios): its own element graph
    /// plus a child slot of inner activity nodes (probes/waiters/further nested processes). Mirrors how the publish
    /// compiler emits an embedded subprocess as a nested <c>BpmnProcess</c> activity node.
    /// </summary>
    public static ExecutableNode NestedProcessNode(
        string nodeId,
        IReadOnlyCollection<BpmnElement> elements,
        IReadOnlyCollection<BpmnSequenceFlow> flows,
        IReadOnlyCollection<ExecutableNode>? innerChildren = null,
        bool isTransaction = false) =>
        new(
            executableNodeId: nodeId,
            authoredActivityId: $"authored-{nodeId}",
            activityType: typeof(BpmnProcessActivity).FullName!,
            activityTypeVersion: "1.0.0",
            descriptorType: "InnerDescriptor",
            descriptorPayload: JsonSerializer.SerializeToElement(new { }),
            inputBindings: new Dictionary<string, RuntimeInputBinding>(),
            metadata: new Dictionary<string, string>(),
            childSlots: [new ExecutableChildSlot(BpmnProcessActivity.ActivitiesSlotName, innerChildren ?? [])],
            structure: new ExecutableActivityStructure(
                BpmnProcessActivity.StructureKind,
                BpmnProcessActivity.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(new BpmnStructure(elements, flows, isTransaction: isTransaction))));

    /// <summary>A mid-flow <c>Event</c> wait child (<c>CanStartWorkflow = false</c>) for building nested-process waiters.</summary>
    public static ExecutableNode EventWaitNode(string nodeId, string eventName) =>
        NewClrNode(nodeId, typeof(Elsa.Activities.Primitives.Activities.Event), new Dictionary<string, object?>
        {
            [nameof(Elsa.Activities.Primitives.Activities.Event.EventName)] = eventName,
            [nameof(Elsa.Activities.Primitives.Activities.Event.CanStartWorkflow)] = false
        });

    private static ExecutableNode NewClrNode(string nodeId, Type activityType, IReadOnlyDictionary<string, object?> literals) =>
        new(
            executableNodeId: nodeId,
            authoredActivityId: $"authored-{nodeId}",
            activityType: activityType.FullName!,
            activityTypeVersion: "1.0.0",
            descriptorType: "test/clr",
            descriptorPayload: JsonSerializer.SerializeToElement(new { type = "clr" }),
            inputBindings: literals.ToDictionary(
                literal => literal.Key,
                literal => new RuntimeInputBinding(
                    inputKey: literal.Key,
                    targetType: new ValueTypeDescriptor("String"),
                    effectivePolicy: ValueProtectionPolicy.InstanceInline,
                    source: RuntimeInputBindingSource.Literal,
                    literal: ValueEnvelope.Inline(
                        new ValueTypeDescriptor("String"),
                        JsonSerializer.SerializeToElement(literal.Value),
                        ValueProtectionPolicy.InstanceInline)),
                StringComparer.Ordinal),
            metadata: new Dictionary<string, string>());

    /// <summary>A transaction subprocess element (spec 125): a <c>subProcess</c> host marked <c>IsTransaction</c> that binds a nested transaction process.</summary>
    public static BpmnElement TransactionSubProcess(string elementId, string childNodeId, string? defaultFlowId = null) =>
        new(elementId, BpmnElementTypes.SubProcess, childNodeId: childNodeId, defaultFlowId: defaultFlowId, isTransaction: true);

    /// <summary>A cancel end event (spec 125); valid only inside a transaction, it cancels the transaction scope and completes it with the <c>Cancelled</c> outcome.</summary>
    public static BpmnElement CancelEnd(string elementId = "cancel-end") =>
        new(elementId, BpmnElementTypes.EndEvent, eventDefinitions: [new BpmnEventDefinition(BpmnEventDefinitionTypes.Cancel)]);

    /// <summary>A cancel boundary event (spec 125) attached to a transaction host; dormant (no listener), it fires on the transaction's <c>Cancelled</c> outcome and routes its outbound flows.</summary>
    public static BpmnElement CancelBoundary(string elementId, string attachedToRef) =>
        new(elementId, BpmnElementTypes.BoundaryEvent, eventDefinitions: [new BpmnEventDefinition(BpmnEventDefinitionTypes.Cancel)], attachedToRef: attachedToRef);

    /// <summary>A compensate intermediate throw event (spec 124); an optional <paramref name="activityRef"/> targets only that host's registrations.</summary>
    public static BpmnElement CompensateThrow(string elementId, string? activityRef = null) =>
        new(elementId, BpmnElementTypes.IntermediateThrowEvent, eventDefinitions: [new BpmnEventDefinition(BpmnEventDefinitionTypes.Compensation, CompensationProperties(activityRef))]);

    /// <summary>A compensate end event (spec 124); an optional <paramref name="activityRef"/> targets only that host's registrations.</summary>
    public static BpmnElement CompensateEnd(string elementId, string? activityRef = null) =>
        new(elementId, BpmnElementTypes.EndEvent, eventDefinitions: [new BpmnEventDefinition(BpmnEventDefinitionTypes.Compensation, CompensationProperties(activityRef))]);

    private static IReadOnlyDictionary<string, string>? CompensationProperties(string? activityRef) =>
        string.IsNullOrWhiteSpace(activityRef)
            ? null
            : new Dictionary<string, string> { [BpmnEventDefinitionProperties.ActivityRef] = activityRef };

    public static BpmnSequenceFlow Flow(string flowId, string sourceRef, string targetRef, string? conditionOutcome = null, bool isDefault = false) =>
        new(flowId, sourceRef, targetRef, conditionOutcome: conditionOutcome, isDefault: isDefault);

    private sealed record BpmnDescriptor;
}
