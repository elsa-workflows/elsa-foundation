using Elsa.Activities.Flowchart.Contracts;
using Elsa.Activities.Flowchart.Models;

namespace Elsa.Activities.Flowchart.Internal;

public sealed class FlowchartPolicyContext(
    FlowchartPolicyTrigger trigger,
    FlowchartExecutionState state,
    IReadOnlyCollection<FlowchartConnection> connections,
    string? currentNodeId,
    IReadOnlyCollection<string> outcomeNames)
    : IFlowchartPolicyContext
{
    public FlowchartPolicyTrigger Trigger { get; } = trigger;
    public FlowchartExecutionState State { get; } = state;
    public IReadOnlyCollection<FlowchartConnection> Connections { get; } = connections.ToArray();
    public string? CurrentNodeId { get; } = currentNodeId;
    public IReadOnlyCollection<string> OutcomeNames { get; } = outcomeNames.ToArray();
}
