using CShells.Features;
using Elsa.Activities.Sequence.Internal;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Elsa.Workflows.Design.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Activities.Sequence.Design;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Activities")]
[ManifestFeatureCategory("Design")]
[ShellFeature(
    name: "ActivitiesSequenceDesign",
    DisplayName = "Activities Sequence (Design)",
    Description = "Authored Sequence structure projection, child-slot replacement, and executable-structure compilation.",
    DependsOn = new object[] { "ActivitiesSequenceRuntime" })]
public class ActivitiesSequenceDesignFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IActivityStructureHandler, SequenceStructureHandler>();
    }
}
