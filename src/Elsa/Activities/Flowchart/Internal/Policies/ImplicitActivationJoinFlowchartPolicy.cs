using Elsa.Activities.Flowchart.Contracts;
using Elsa.Activities.Flowchart.Models;

namespace Elsa.Activities.Flowchart.Internal.Policies;

public sealed class ImplicitActivationJoinFlowchartPolicy : IFlowchartPolicy
{
    public string PolicyKind => FlowchartPolicyKinds.ImplicitActivationJoin;
    public string DisplayName => "Implicit Activation-Aware Join";

    public FlowchartPolicyDecision Execute(IFlowchartPolicyContext context) =>
        new(FlowchartPolicyConnectionSelector.ScheduleMatchingOutbound(context));
}
