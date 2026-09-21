namespace Elsa.Activities.Flowchart.Internal.Policies;

public static class FlowchartPolicyKinds
{
    public const string Decision = "decision";
    public const string ParallelFork = "parallelFork";
    public const string ParallelJoin = "parallelJoin";
    public const string InclusiveFork = "inclusiveFork";
    public const string InclusiveJoin = "inclusiveJoin";
    public const string FirstWins = "firstWins";
    public const string Merge = "merge";
    public const string ImplicitActivationJoin = "implicitActivationJoin";
    public const string DirectContinuation = "directContinuation";

    /// <summary>
    /// ADR 0064: the join semantics that searches the graph for live work that could still reach an un-arrived
    /// inbound. Selected through <see cref="FlowchartOptions.JoinPolicyKind"/>, not through node metadata.
    /// </summary>
    public const string ReachabilityJoin = "reachabilityJoin";

    /// <summary>
    /// ADR 0064: the join semantics that decides from arrivals the target already holds, with untaken outbound
    /// connections emitting dead arrivals. Also selected through <see cref="FlowchartOptions.JoinPolicyKind"/>.
    /// </summary>
    public const string DeadPathJoin = "deadPathJoin";
}
