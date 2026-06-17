using Elsa.Activities.Flowchart.Contracts;
using Elsa.Activities.Flowchart.Models;

namespace Elsa.Activities.Flowchart.Internal.Policies;

public sealed class InclusiveForkFlowchartPolicy : IFlowchartPolicy
{
    public string PolicyKind => FlowchartPolicyKinds.InclusiveFork;
    public string DisplayName => "Inclusive Fork";

    public FlowchartPolicyDecision Execute(IFlowchartPolicyContext context) =>
        new(FlowchartPolicyConnectionSelector.ScheduleMatchingOutbound(context));
}
