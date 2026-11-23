using Kernel.Graph.Contracts;
using Kernel.Graph.Models;

namespace Kernel.Graph.Internal.Policies;

public sealed class ParallelForkFlowchartPolicy : IFlowchartPolicy
{
    public string PolicyKind => FlowchartPolicyKinds.ParallelFork;
    public string DisplayName => "Parallel Fork";

    public FlowchartPolicyDecision Execute(IFlowchartPolicyContext context) =>
        new(FlowchartPolicyConnectionSelector.ScheduleAllOutbound(context));
}
