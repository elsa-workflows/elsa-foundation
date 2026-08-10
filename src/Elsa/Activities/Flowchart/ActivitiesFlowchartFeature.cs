using CShells.Features;
using Elsa.Activities.Flowchart.Contracts;
using Elsa.Activities.Flowchart.Internal;
using Elsa.Activities.Flowchart.Internal.Policies;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Activities.Flowchart;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Activities")]
[ManifestFeatureCategory("Composition")]
[ShellFeature(
    name: "ActivitiesFlowchart",
    DisplayName = "Activities Flowchart",
    Description = "Flowchart composite activity and executable-node graph contracts."
)]
public class ActivitiesFlowchartFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IActivityStructureHandler, FlowchartStructureHandler>();
        services.AddSingleton(new FlowchartOptions());
        services.AddSingleton<FlowchartReachabilityAnalyzer>();
        services.AddSingleton<FlowchartJoinCoordinator>();
        services.AddSingleton<FlowchartPolicyApplier>();
        services.AddSingleton<FlowchartStatePersister>();
        services.AddSingleton<FlowchartExecutionEngine>();
        services.AddSingleton<IFlowchartPolicy, DirectContinuationFlowchartPolicy>();
        services.AddSingleton<IFlowchartPolicy, ImplicitActivationJoinFlowchartPolicy>();
        services.AddSingleton<IFlowchartPolicy, DecisionFlowchartPolicy>();
        services.AddSingleton<IFlowchartPolicy, ParallelForkFlowchartPolicy>();
        services.AddSingleton<IFlowchartPolicy, ParallelJoinFlowchartPolicy>();
        services.AddSingleton<IFlowchartPolicy, InclusiveForkFlowchartPolicy>();
        services.AddSingleton<IFlowchartPolicy, InclusiveJoinFlowchartPolicy>();
        services.AddSingleton<IFlowchartPolicy, FirstWinsFlowchartPolicy>();
        services.AddSingleton<IFlowchartPolicy, MergeFlowchartPolicy>();
        // ADR 0064: both join semantics stay registered so the A/B is FlowchartOptions.JoinPolicyKind rather
        // than a fork of the engine, and the reachability walk stays available for validation after the flip.
        services.AddSingleton<IFlowchartPolicy, ReachabilityJoinFlowchartPolicy>();
        services.AddSingleton<IFlowchartPolicy, DeadPathJoinFlowchartPolicy>();
        services.AddSingleton<IFlowchartPolicyRegistry, FlowchartPolicyRegistry>();
        // spec 123 D2 (ADR 0047 resolution #1): lets the ReplaySafe fused completion pump prove a flowchart
        // successor edge single-predecessor via the spec-119 routing memo; fan-in/join edges keep the discrete cascade.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IReplaySafeSuccessorRoutingProbe, FlowchartReplaySafeSuccessorRoutingProbe>());
    }
}
