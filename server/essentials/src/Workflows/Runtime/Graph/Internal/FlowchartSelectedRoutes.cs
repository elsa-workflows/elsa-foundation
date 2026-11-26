using Kernel.Graph.Models;

namespace Kernel.Graph.Internal;

/// <summary>The live routes of one completion. Both routing paths reject two live edges to one target.</summary>
internal sealed class FlowchartSelectedRoutes
{
    private readonly Dictionary<string, (FlowchartConnection Edge, ExecutionPath Arrival)> _byTarget = new(StringComparer.Ordinal);

    public void Record(FlowchartConnection edge, ExecutionPath arrival) => _byTarget.Add(edge.Target.NodeId, (edge, arrival));

    public (string Scope, string? Iteration)? UntakenPartition(FlowchartConnection edge, (string Scope, string? Iteration) fallback)
    {
        if (!_byTarget.TryGetValue(edge.Target.NodeId, out var selected))
            return fallback;

        // Equivalent declarations represent the already-taken answer. A different answer owed to
        // the same target must meet the live arrival where it actually landed, including scope overrides.
        return selected.Edge.GetAnswerKey() == edge.GetAnswerKey() ? null : (selected.Arrival.ExecutionScopeId, selected.Arrival.IterationKey);
    }
}
