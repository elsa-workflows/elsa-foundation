using Elsa.Activities.Flowchart.Models;

namespace Elsa.Activities.Flowchart.Contracts;

public interface IFlowchartPolicyContext
{
    FlowchartPolicyTrigger Trigger { get; }
    FlowchartExecutionState State { get; }
    IReadOnlyCollection<FlowchartConnection> Connections { get; }
    string? CurrentNodeId { get; }
    IReadOnlyCollection<string> OutcomeNames { get; }
}

public enum FlowchartPolicyTrigger
{
    Start,
    ChildCompleted,
    ChildCanceled,
    ChildFaulted,
    Resume
}
