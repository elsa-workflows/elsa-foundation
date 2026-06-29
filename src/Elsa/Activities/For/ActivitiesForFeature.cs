using CShells.Features;
using Elsa.Activities.For.Internal;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Elsa.Workflows.Design.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Activities.For;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Activities")]
[ManifestFeatureCategory("Composition")]
[ShellFeature(
    name: "ActivitiesFor",
    DisplayName = "Activities For",
    Description = "For counted-loop composite activity and its body child slot contract."
)]
public class ActivitiesForFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IActivityStructureHandler, ForStructureHandler>();
    }
}
