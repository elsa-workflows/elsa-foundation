using Kernel.Graph.Contracts;
using Kernel.Graph.Internal.Policies;

namespace Kernel.Graph;

/// <summary>
/// Host-level Flowchart engine settings.
/// </summary>
public sealed class FlowchartOptions
{
    /// <summary>
    /// Which registered <see cref="IFlowchartJoinPolicy"/> decides implicit joins.
    /// <para>
    /// Two are registered. The default, <see cref="FlowchartPolicyKinds.DeadPathJoin"/>, decides from the
    /// arrivals a target already holds. <see cref="FlowchartPolicyKinds.ReachabilityJoin"/> is the historical
    /// search over live work, kept registered rather than deleted: the conformance corpus runs every scenario
    /// through both and asserts they agree, and a host that hits an unexpected divergence can fall back with
    /// one setting rather than a downgrade.
    /// </para>
    /// </summary>
    public string JoinPolicyKind { get; set; } = FlowchartPolicyKinds.DeadPathJoin;

    /// <summary>
    /// Not main's: deliberate defects, the four of the design sketch's plan, for the graph invariants G1, G2, G3 and G5 of the kernel prototype's simulation, so that
    /// its mutation check can show each is caught. <see cref="FlowchartFlaws.None"/> in every real use.
    /// </summary>
    public FlowchartFlaws Flaws { get; set; }
}

/// <summary>Deliberate defects of the engine, for the prototype's mutation check. Each is marked MUTATION where it acts.</summary>
[Flags]
public enum FlowchartFlaws
{
    None = 0,
    /// <summary>An untaken connection emits no dead arrival: an All join after a decision cannot hear from the untaken side.</summary>
    NoDeadPaths = 1 << 0,
    /// <summary>A loop-closing connection opens no new round: arrivals and joins of every round share one key.</summary>
    NoRoundReset = 1 << 1,
    /// <summary>A node whose every inbound arrived dead is run instead of passing the deadness on.</summary>
    DeadPathRunsNode = 1 << 2,
    /// <summary>A first-wins race completes without cancelling its losing paths.</summary>
    CancelLeavesPaths = 1 << 3,
}
