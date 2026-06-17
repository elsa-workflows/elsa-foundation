using Elsa.Activities.Flowchart.Contracts;
using Elsa.Activities.Flowchart.Models;

namespace Elsa.Activities.Flowchart.Internal.Policies;

public sealed class MergeFlowchartPolicy : IFlowchartPolicy
{
    public string PolicyKind => FlowchartPolicyKinds.Merge;
    public string DisplayName => "Merge";

    public FlowchartPolicyDecision Execute(IFlowchartPolicyContext context) =>
        new(FlowchartPolicyConnectionSelector.ScheduleMatchingOutbound(context));
}
