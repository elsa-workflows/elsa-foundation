using Elsa.Activities.Flowchart.Contracts;
using Elsa.Activities.Flowchart.Internal.Policies;

namespace Elsa.Activities.Flowchart;

/// <summary>
/// Host-level Flowchart engine settings.
/// </summary>
public sealed class FlowchartOptions
{
    /// <summary>
    /// Which registered <see cref="IFlowchartJoinPolicy"/> decides implicit joins.
    /// <para>
    /// ADR 0064 ships two. The default, <see cref="FlowchartPolicyKinds.DeadPathJoin"/>, decides from the
    /// arrivals a target already holds. <see cref="FlowchartPolicyKinds.ReachabilityJoin"/> is the historical
    /// search over live work, kept registered rather than deleted: the conformance corpus runs every scenario
    /// through both and asserts they agree, and a host that hits an unexpected divergence can fall back with
    /// one setting rather than a downgrade.
    /// </para>
    /// </summary>
    public string JoinPolicyKind { get; set; } = FlowchartPolicyKinds.DeadPathJoin;
}
