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
        IReadOnlyCollection<BpmnSequenceFlow> sequenceFlows)
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

        return WorkflowExecutionHarness.NewExecutable(root);
    }

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
