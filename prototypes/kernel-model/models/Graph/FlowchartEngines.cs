using Kernel.Graph.Contracts;
using Kernel.Graph.Internal;
using Kernel.Graph.Internal.Policies;

namespace Kernel.Graph;

/// <summary>
/// Wires the engine without a container, as main's <c>ActivitiesFlowchartFeature</c> wires it with one: the same
/// collaborators, the same built-in policies, both join semantics registered, and <see cref="FlowchartOptions"/>
/// choosing between them (dead-path join by default).
/// </summary>
public static class FlowchartEngines
{
    /// <summary>Main's built-in policies, in the order the feature registers them.</summary>
    public static IReadOnlyList<IFlowchartPolicy> BuiltInPolicies() =>
    [
        new DirectContinuationFlowchartPolicy(),
        new ImplicitActivationJoinFlowchartPolicy(),
        new DecisionFlowchartPolicy(),
        new ParallelForkFlowchartPolicy(),
        new ParallelJoinFlowchartPolicy(),
        new InclusiveForkFlowchartPolicy(),
        new InclusiveJoinFlowchartPolicy(),
        new FirstWinsFlowchartPolicy(),
        new MergeFlowchartPolicy(),
        new ReachabilityJoinFlowchartPolicy(),
        new DeadPathJoinFlowchartPolicy(),
    ];

    /// <summary>An engine over the built-in policies plus <paramref name="extraPolicies"/>.</summary>
    public static FlowchartExecutionEngine Create(FlowchartOptions? options = null, params IFlowchartPolicy[] extraPolicies)
    {
        var registry = new FlowchartPolicyRegistry([.. BuiltInPolicies(), .. extraPolicies]);
        options ??= new FlowchartOptions();
        var analyzer = new FlowchartReachabilityAnalyzer { Flaws = options.Flaws };
        var joinCoordinator = new FlowchartJoinCoordinator(registry, options);
        return new FlowchartExecutionEngine(analyzer, registry, joinCoordinator, new FlowchartPolicyApplier(analyzer, joinCoordinator), new FlowchartStatePersister());
    }
}
