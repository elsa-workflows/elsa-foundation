using Elsa.Activities.Flowchart.Contracts;
using Elsa.Activities.Flowchart.Models;

namespace Elsa.Activities.Flowchart.Internal.Policies;

/// <summary>
/// Base for the built-in policies whose only decision is "schedule every outbound connection whose source
/// port matches a completed outcome" (<see cref="FlowchartPolicyConnectionSelector.ScheduleMatchingOutbound"/>).
/// The concrete policies differ only in their <see cref="PolicyKind"/>/<see cref="DisplayName"/> identity —
/// the real gateway behavior differences live in <c>FlowchartExecutionEngine</c>'s join accounting, not in
/// the policy body — so the shared <see cref="Execute"/> is factored here while each policy stays a distinct
/// named type (mirroring the shared-helper approach taken for the control-flow navigators in #449). The only
/// built-in that does not share this body is <see cref="ParallelForkFlowchartPolicy"/> (schedule <b>all</b>
/// outbound).
/// </summary>
public abstract class MatchingOutboundFlowchartPolicyBase(string policyKind, string displayName) : IFlowchartPolicy
{
    public string PolicyKind { get; } = policyKind;
    public string DisplayName { get; } = displayName;

    public FlowchartPolicyDecision Execute(IFlowchartPolicyContext context) =>
        new(FlowchartPolicyConnectionSelector.ScheduleMatchingOutbound(context));
}
