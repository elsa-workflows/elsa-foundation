using CShells.Features;
using Elsa.Activities.Parallel.Internal;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Elsa.Workflows.Design.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Activities.Parallel;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Activities")]
[ManifestFeatureCategory("Composition")]
[ShellFeature(
    name: "ActivitiesParallel",
    DisplayName = "Activities Parallel",
    Description = "Parallel fork/join composite activity and its per-branch child slot contracts."
)]
public class ActivitiesParallelFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IActivityStructureHandler, ParallelStructureHandler>();
    }
}
