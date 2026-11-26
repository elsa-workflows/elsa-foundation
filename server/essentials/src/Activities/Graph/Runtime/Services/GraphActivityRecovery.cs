using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;

namespace Elsa.Activities.Graph.Runtime.Services;

/// <summary>Creates stable causal failures at a graph boundary while retaining the original child incident.</summary>
public sealed class GraphActivityRecovery
{
    public GraphActivityBoundaryFaultException NewBoundaryFault(ActivityChildFaultedContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (string.IsNullOrWhiteSpace(context.IncidentId))
            throw new InvalidOperationException("A graph child fault must carry its recorded incident identity.");

        return new GraphActivityBoundaryFaultException(
            context.IncidentId,
            context.FaultedChildActivityExecutionId,
            context.FaultedChildExecutableNodeId);
    }
}

public sealed class GraphActivityBoundaryFaultException(
    string causalIncidentId,
    string causalActivityExecutionId,
    string causalExecutableNodeId)
    : Exception($"A descendant activity faulted inside the graph boundary. Causal incident: '{causalIncidentId}'."), IActivityFaultCausation
{
    public string CausalIncidentId { get; } = causalIncidentId;
    public string CausalActivityExecutionId { get; } = causalActivityExecutionId;
    public string CausalExecutableNodeId { get; } = causalExecutableNodeId;
    public string CausationKind => "graph-descendant-fault";
}
