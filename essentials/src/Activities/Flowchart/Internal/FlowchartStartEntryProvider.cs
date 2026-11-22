using Elsa.Activities.Flowchart.Exceptions;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using FlowchartActivity = Elsa.Activities.Flowchart.Activities.Flowchart;

namespace Elsa.Activities.Flowchart.Internal;

/// <summary>
/// The Flowchart's answer to the start-path rule (spec 238). A run started by a trigger begins at that trigger when
/// it is an entry of this Flowchart (a direct child with no inbound connection, or the start node), and at the start
/// node when the trigger is deeper. See <see cref="FlowchartGraph.SelectTriggerStartChild"/>.
/// </summary>
public sealed class FlowchartStartEntryProvider : IStructuralStartEntryProvider
{
    public string StructureKind => FlowchartActivity.StructureKind;

    /// <exception cref="InvalidOperationException">The Flowchart's structure is malformed, so its entries cannot be read.</exception>
    public ExecutableNode? SelectStartChild(ExecutableNode composite, string triggerNodeId)
    {
        ArgumentNullException.ThrowIfNull(composite);
        ArgumentException.ThrowIfNullOrWhiteSpace(triggerNodeId);

        FlowchartGraph graph;
        try
        {
            graph = FlowchartGraph.From(composite);
        }
        catch (FlowchartExecutionException exception)
        {
            throw new InvalidOperationException($"Flowchart '{composite.ExecutableNodeId}' has a malformed structure: {exception.Message}", exception);
        }

        return graph.SelectTriggerStartChild(triggerNodeId);
    }
}
