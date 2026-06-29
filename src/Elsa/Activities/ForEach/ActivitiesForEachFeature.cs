using CShells.Features;
using Elsa.Activities.ForEach.Internal;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Elsa.Workflows.Design.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Activities.ForEach;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Activities")]
[ManifestFeatureCategory("Composition")]
[ShellFeature(
    name: "ActivitiesForEach",
    DisplayName = "Activities ForEach",
    Description = "ForEach looping composite activity that runs a body once per collection item and its body child slot contract."
)]
public class ActivitiesForEachFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IActivityStructureHandler, ForEachStructureHandler>();
    }
}
