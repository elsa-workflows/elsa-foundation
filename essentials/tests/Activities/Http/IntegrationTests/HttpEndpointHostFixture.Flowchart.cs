using System.Text.Json;
using Elsa.Activities.Flowchart;
using Elsa.Activities.Flowchart.Models;
using Elsa.Activities.Http.Activities;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Testing;
using Elsa.Serialization.Core;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using FlowchartActivity = Elsa.Activities.Flowchart.Activities.Flowchart;

namespace Elsa.Activities.Http.IntegrationTests;

/// <summary>
/// The Flowchart parts of the host fixture (spec 238). Only the integration tests compile this file. The command-cost
/// tests link <c>HttpEndpointHostFixture.cs</c> without a Flowchart reference.
/// </summary>
public sealed partial class HttpEndpointHostFixture
{
    static partial void ConfigureOptionalCompositeFeatures(IServiceCollection services) =>
        new ActivitiesFlowchartFeature().ConfigureServices(services);

    /// <summary>
    /// Publishes a Flowchart with two start-trigger <see cref="HttpEndpoint"/>s, each heading its own branch: the first
    /// (<c>node-first-endpoint</c>, the Flowchart's start node) on <paramref name="firstPath"/> and the second
    /// (<c>node-second-endpoint</c>) on <paramref name="secondPath"/>. Both are indexed. A request to either path starts
    /// a run at that endpoint (spec 238); the other endpoint never runs.
    /// </summary>
    public Task PublishTwoHttpEndpointFlowchartWorkflowAsync(string artifactId, string firstPath, string secondPath, string method)
    {
        var first = NewStartEndpointNode(artifactId, firstPath, method, "node-first-endpoint");
        var second = NewStartEndpointNode(artifactId, secondPath, method, "node-second-endpoint");
        return PublishRootAsync(artifactId, NewFlowchartNode("node-flowchart", [first, second], [], first.ExecutableNodeId));
    }

    /// <summary>A real Flowchart node over <paramref name="children"/>, with its connections and start node.</summary>
    public ExecutableNode NewFlowchartNode(
        string nodeId,
        IReadOnlyList<ExecutableNode> children,
        IReadOnlyCollection<(string Source, string Target)> connections,
        string startNodeId)
    {
        var descriptorPayload = ClrConstruction.Payload(Services.GetRequiredService<IPayloadSerializer>(), typeof(FlowchartActivity));
        var flowchartConnections = connections
            .Select(connection => new FlowchartConnection(new FlowchartEndpoint(connection.Source, null), new FlowchartEndpoint(connection.Target)))
            .ToArray();
        return new ExecutableNode(
            executableNodeId: nodeId,
            authoredActivityId: $"authored-{nodeId}",
            activityType: typeof(FlowchartActivity).FullName!,
            activityTypeVersion: "1.0.0",
            descriptor: ClrRuntimeDescriptor(descriptorPayload),
            inputBindings: new Dictionary<string, RuntimeInputBinding>(),
            metadata: new Dictionary<string, string>(),
            childSlots: [new ExecutableChildSlot(FlowchartActivity.ActivitiesSlotName, children)],
            structure: new ExecutableActivityStructure(
                FlowchartActivity.StructureKind,
                FlowchartActivity.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(new FlowchartStructure(flowchartConnections, startNodeId))),
            activityContract: UnitActivityContract(typeof(FlowchartActivity), descriptorPayload, [], [ActivityOutcomes.Done, ActivityOutcomes.Break]));
    }
}
