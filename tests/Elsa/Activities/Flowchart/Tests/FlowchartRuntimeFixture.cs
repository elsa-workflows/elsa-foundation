using System.Text.Json;
using Elsa.Activities.Flowchart.Internal;
using Elsa.Activities.Flowchart.Models;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Testing;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using FlowchartActivity = Elsa.Activities.Flowchart.Activities.Flowchart;

namespace Elsa.Activities.Flowchart.Tests;

public sealed class FlowchartRuntimeFixture : IAsyncDisposable
{
    private readonly WorkflowExecutionHarness _harness;

    private FlowchartRuntimeFixture(WorkflowExecutionHarness harness)
    {
        _harness = harness;
        Provider = harness.Services;
    }

    public IServiceProvider Provider { get; }

    public static ValueTask<FlowchartRuntimeFixture> CreateAsync(IEnumerable<string> activityExecutionIds, Action<IServiceCollection>? configureServices = null)
    {
        var builder = WorkflowExecutionHarness.Create()
            .WithFeature(services => new ActivitiesFlowchartFeature().ConfigureServices(services))
            .WithProbeLeaf();
        if (configureServices is not null)
            builder.ConfigureServices(configureServices);

        return new ValueTask<FlowchartRuntimeFixture>(new FlowchartRuntimeFixture(builder.Build(activityExecutionIds)));
    }

    public ValueTask DisposeAsync() => _harness.DisposeAsync();

    public async Task ExecuteAsync(WorkflowExecutable executable)
    {
        await _harness.RunAsync(executable);
    }

    public async Task<FlowchartExecutionState> GetFlowchartStateAsync() =>
        JsonSerializer.Deserialize<FlowchartExecutionState>(await GetRawFlowchartStateAsync(), new JsonSerializerOptions(JsonSerializerDefaults.Web))
        ?? throw new InvalidOperationException("Flowchart execution state resolved to null.");

    public async Task<string> GetRawFlowchartStateAsync()
    {
        var states = await Provider.GetRequiredService<IActivityExecutionStateStore>().ListAsync("wfexec-1");
        var flowchartState = states.Single(state => state.Execution.ExecutableNodeId == "node-flowchart");
        var lastCommittedPrivateState = Provider.GetRequiredService<InMemoryRuntimeCheckpointCommitStore>().ListCommits()
            .SelectMany(record => record.Commit.StateChanges.ActivityExecutions)
            .Where(change => StringComparer.Ordinal.Equals(change.StateId, flowchartState.Execution.ActivityExecutionId))
            .Select(change => change.State.PrivateState?.Value.InlineValue?.GetRawText())
            .LastOrDefault(value => value is not null);
        return flowchartState.PrivateState?.Value.InlineValue?.GetRawText() ?? lastCommittedPrivateState
               ?? throw new InvalidOperationException(
                   $"Flowchart private state is missing. Status: {flowchartState.Status}/{flowchartState.SubStatus}. " +
                   $"Metadata: {string.Join(", ", flowchartState.Metadata.Select(item => $"{item.Key}={item.Value}"))}");
    }

    public WorkflowExecutable NewExecutable(
        IReadOnlyCollection<ExecutableNode> children,
        IReadOnlyCollection<FlowchartConnection> connections,
        string? startNodeId = null,
        IReadOnlyDictionary<string, FlowchartNodeMetadata>? nodeMetadata = null,
        IReadOnlyDictionary<string, FlowchartConnectionMetadata>? connectionMetadata = null)
    {
        var root = new ExecutableNode(
            executableNodeId: "node-flowchart",
            authoredActivityId: "authored-flowchart",
            activityType: typeof(FlowchartActivity).FullName!,
            activityTypeVersion: "1.0.0",
            descriptorType: typeof(FlowchartDescriptor).FullName!,
            descriptorPayload: JsonSerializer.SerializeToElement(new FlowchartDescriptor()),
            inputBindings: new Dictionary<string, RuntimeInputBinding>(),
            metadata: new Dictionary<string, string>(),
            childSlots:
            [
                new ExecutableChildSlot(
                    FlowchartActivity.ActivitiesSlotName,
                    children)
            ],
            structure: new ExecutableActivityStructure(
                FlowchartActivity.StructureKind,
                FlowchartActivity.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(new FlowchartStructure(connections, startNodeId, nodeMetadata, connectionMetadata))));

        return WorkflowExecutionHarness.NewExecutable(root);
    }

    public ExecutableNode NewProbeNode(string nodeId, IReadOnlyCollection<string>? outcomes = null) =>
        WorkflowExecutionHarness.NewProbeNode(nodeId, outcomes);

    public FlowchartConnection NewConnection(string sourceNodeId, string targetNodeId, string? sourcePort = null) =>
        new(new FlowchartEndpoint(sourceNodeId, sourcePort), new FlowchartEndpoint(targetNodeId));

    private sealed record FlowchartDescriptor;
}
