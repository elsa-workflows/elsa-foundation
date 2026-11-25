using Kernel.Graph.Models;

namespace Kernel.Graph.Contracts;

public interface IFlowchartPolicy
{
    string PolicyKind { get; }
    string DisplayName { get; }
    FlowchartPolicyDecision Execute(IFlowchartPolicyContext context);
}
