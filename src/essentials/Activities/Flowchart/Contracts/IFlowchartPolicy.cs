using Elsa.Activities.Flowchart.Models;

namespace Elsa.Activities.Flowchart.Contracts;

public interface IFlowchartPolicy
{
    string PolicyKind { get; }
    string DisplayName { get; }
    FlowchartPolicyDecision Execute(IFlowchartPolicyContext context);
}
