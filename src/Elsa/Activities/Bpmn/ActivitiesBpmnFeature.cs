using CShells.Features;
using Elsa.Activities.Bpmn.Contracts;
using Elsa.Activities.Bpmn.Internal;
using Elsa.Activities.Bpmn.Internal.Behaviors;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Elsa.Workflows.Design.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Activities.Bpmn;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Activities")]
[ManifestFeatureCategory("Composition")]
[ShellFeature(
    name: "ActivitiesBpmn",
    DisplayName = "Activities BPMN",
    Description = "BPMN 2.0 process composite activity and executable element-graph contracts."
)]
public class ActivitiesBpmnFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IActivityStructureHandler, BpmnStructureHandler>();
        services.AddSingleton<BpmnTokenCoordinator>();
        services.AddSingleton<BpmnStatePersister>();
        services.AddSingleton<BpmnExecutionEngine>();
        services.AddSingleton<IBpmnElementBehavior, NoneStartEventBehavior>();
        services.AddSingleton<IBpmnElementBehavior, NoneEndEventBehavior>();
        services.AddSingleton<IBpmnElementBehavior, TerminateEndEventBehavior>();
        services.AddSingleton<IBpmnElementBehavior, CatchEventBehavior>();
        services.AddSingleton<IBpmnElementBehavior, TaskBehavior>();
        services.AddSingleton<IBpmnElementBehavior, SubProcessBehavior>();
        services.AddSingleton<IBpmnElementBehavior, ExclusiveGatewayBehavior>();
        services.AddSingleton<IBpmnElementBehavior, ParallelGatewayBehavior>();
        services.AddSingleton<IBpmnElementBehavior, InclusiveGatewayBehavior>();
        services.AddSingleton<IBpmnBehaviorRegistry, BpmnBehaviorRegistry>();
    }
}
