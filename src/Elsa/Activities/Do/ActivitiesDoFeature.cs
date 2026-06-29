using CShells.Features;
using Elsa.Activities.Do.Internal;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Elsa.Workflows.Design.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Activities.Do;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Activities")]
[ManifestFeatureCategory("Composition")]
[ShellFeature(
    name: "ActivitiesDo",
    DisplayName = "Activities Do",
    Description = "Do/DoWhile post-test conditional-loop composite activity and its body child slot contract."
)]
public class ActivitiesDoFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IActivityStructureHandler, DoStructureHandler>();
    }
}
