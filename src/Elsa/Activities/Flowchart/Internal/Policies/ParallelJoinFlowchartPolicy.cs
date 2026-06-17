using Elsa.Activities.Flowchart.Contracts;
using Elsa.Activities.Flowchart.Models;

namespace Elsa.Activities.Flowchart.Internal.Policies;

public sealed class ParallelJoinFlowchartPolicy : IFlowchartPolicy
{
    public string PolicyKind => FlowchartPolicyKinds.ParallelJoin;
    public string DisplayName => "Parallel Join";

    public FlowchartPolicyDecision Execute(IFlowchartPolicyContext context) =>
        new(FlowchartPolicyConnectionSelector.ScheduleMatchingOutbound(context));
}
