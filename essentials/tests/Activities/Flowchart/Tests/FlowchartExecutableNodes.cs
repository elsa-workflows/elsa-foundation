using System.Text.Json;
using Elsa.Activities.Flowchart.Models;
using Elsa.Workflows.Runtime.Core.Models;
using FlowchartActivity = Elsa.Activities.Flowchart.Activities.Flowchart;

namespace Elsa.Activities.Flowchart.Tests;

internal static class FlowchartExecutableNodes
{
    public static ExecutableNode Create(
        string id,
        IReadOnlyList<string> childIds,
        IReadOnlyList<FlowchartConnection> connections,
        string? startNodeId = null)
    {
        var children = childIds.Select(NewLeaf).ToArray();
        return new ExecutableNode(
            $"node-flowchart-{id}",
            $"authored-flowchart-{id}",
            typeof(FlowchartActivity).FullName!,
            "1.0.0",
            "elsa.flowchart",
            JsonSerializer.SerializeToElement(new { }),
            new Dictionary<string, RuntimeInputBinding>(),
            new Dictionary<string, string>(),
            childSlots: [new ExecutableChildSlot(FlowchartActivity.ActivitiesSlotName, children)],
            structure: new ExecutableActivityStructure(
                FlowchartActivity.StructureKind,
                FlowchartActivity.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(new FlowchartStructure(connections: connections, startNodeId: startNodeId))));
    }

    private static ExecutableNode NewLeaf(string id) =>
        new(
            id,
            $"authored-{id}",
            "leaf",
            "1.0.0",
            "clr",
            JsonSerializer.SerializeToElement(new { }),
            new Dictionary<string, RuntimeInputBinding>(),
            new Dictionary<string, string>());
}
