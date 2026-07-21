using System.Text.Json;
using Elsa.Activities.Bpmn.Internal;
using Elsa.Activities.Bpmn.Models;
using Elsa.Activities.Testing;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using BpmnProcessActivity = Elsa.Activities.Bpmn.Activities.BpmnProcess;

namespace Elsa.Activities.Bpmn.Tests;

public sealed class BpmnRuntimeFixture : IAsyncDisposable
{
    public const string ProcessNodeId = "node-bpmn";

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
        IReadOnlyCollection<WorkflowExecutableResumeTarget>? resumeTargets = null)
    {
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
                JsonSerializer.SerializeToElement(new BpmnStructure(elements, sequenceFlows))));

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

    public static BpmnElement IntermediateCatchEvent(string elementId, string definitionType, string? childNodeId = null) =>
        new(elementId, BpmnElementTypes.IntermediateCatchEvent, childNodeId: childNodeId, eventDefinitions: [new BpmnEventDefinition(definitionType)]);

    public static BpmnElement Task(string elementId, string? childNodeId = null, string? defaultFlowId = null) =>
        new(elementId, BpmnElementTypes.Task, childNodeId: childNodeId, defaultFlowId: defaultFlowId);

    public static BpmnElement ExclusiveGateway(string elementId, string? childNodeId = null, string? defaultFlowId = null) =>
        new(elementId, BpmnElementTypes.ExclusiveGateway, childNodeId: childNodeId, defaultFlowId: defaultFlowId);

    public static BpmnElement ParallelGateway(string elementId) =>
        new(elementId, BpmnElementTypes.ParallelGateway);

    public static BpmnElement InclusiveGateway(string elementId, string? childNodeId = null, string? defaultFlowId = null) =>
        new(elementId, BpmnElementTypes.InclusiveGateway, childNodeId: childNodeId, defaultFlowId: defaultFlowId);

    public static BpmnSequenceFlow Flow(string flowId, string sourceRef, string targetRef, string? conditionOutcome = null, bool isDefault = false) =>
        new(flowId, sourceRef, targetRef, conditionOutcome: conditionOutcome, isDefault: isDefault);

    private sealed record BpmnDescriptor;
}
