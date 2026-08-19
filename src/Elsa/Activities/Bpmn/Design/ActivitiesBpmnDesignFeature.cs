using CShells.Features;
using Elsa.Activities.Bpmn.Internal;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Elsa.Workflows.Design.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Activities.Bpmn.Design;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Activities")]
[ManifestFeatureCategory("Design")]
[ShellFeature(
    name: "ActivitiesBpmnDesign",
    DisplayName = "Activities BPMN (Design)",
    Description = "Authored BPMN structure projection and compilation, including the pool, lane and diagram metadata stripped from the executable form.",
    DependsOn = new object[] { "ActivitiesBpmnRuntime" })]
public class ActivitiesBpmnDesignFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IActivityStructureHandler, BpmnStructureHandler>();
    }
}
