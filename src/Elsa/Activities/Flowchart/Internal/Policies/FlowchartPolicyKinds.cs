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
}
