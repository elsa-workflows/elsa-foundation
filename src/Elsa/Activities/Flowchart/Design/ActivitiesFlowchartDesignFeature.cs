using CShells.Features;
using Elsa.Activities.Flowchart.Internal;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Elsa.Workflows.Design.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Activities.Flowchart.Design;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Activities")]
[ManifestFeatureCategory("Design")]
[ShellFeature(
    name: "ActivitiesFlowchartDesign",
    DisplayName = "Activities Flowchart (Design)",
    Description = "Authored Flowchart structure projection, connection replacement, and executable-structure compilation.",
    DependsOn = new object[] { "ActivitiesFlowchartRuntime" })]
public class ActivitiesFlowchartDesignFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IActivityStructureHandler, FlowchartStructureHandler>();
    }
}
