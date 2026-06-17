using Elsa.Activities.Flowchart.Contracts;
using Elsa.Activities.Flowchart.Models;

namespace Elsa.Activities.Flowchart.Internal.Policies;

public sealed class DirectContinuationFlowchartPolicy : IFlowchartPolicy
{
    public string PolicyKind => FlowchartPolicyKinds.DirectContinuation;
    public string DisplayName => "Direct Continuation";

    public FlowchartPolicyDecision Execute(IFlowchartPolicyContext context) =>
        new(FlowchartPolicyConnectionSelector.ScheduleMatchingOutbound(context));
}
