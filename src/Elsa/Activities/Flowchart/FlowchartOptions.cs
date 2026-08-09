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
    /// ADR 0064 ships two: <see cref="FlowchartPolicyKinds.ReachabilityJoin"/>, the historical search over live
    /// work, and <see cref="FlowchartPolicyKinds.DeadPathJoin"/>, which decides from the arrivals a target
    /// already holds. Both stay registered so the conformance corpus can run a graph through each and assert
    /// where the two agree; a host that hits an unexpected divergence can fall back with one setting rather
    /// than a downgrade.
    /// </para>
    /// </summary>
    public string JoinPolicyKind { get; set; } = FlowchartPolicyKinds.ReachabilityJoin;
}
