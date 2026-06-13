using CShells.Features;
using Elsa.Platform.PackageManifest.Generator.Hints;
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
    }
}
