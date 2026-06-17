using CShells.Features;
using Elsa.Activities.Flowchart.Contracts;
using Elsa.Activities.Flowchart.Internal;
using Elsa.Activities.Flowchart.Internal.Policies;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Elsa.Workflows.Design.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;

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
        services.AddSingleton<FlowchartReachabilityAnalyzer>();
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
        services.AddSingleton<IFlowchartPolicyRegistry, FlowchartPolicyRegistry>();
    }
}
