using CShells.Features;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Elsa3.Activities.Design.Import.Contracts;
using Elsa3.Activities.Design.Import.Persistence.Groundwork.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa3.Activities.Design.Import.Persistence.Groundwork;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Elsa3")]
[ManifestFeatureCategory("Import")]
[ShellFeature(
    name: "Elsa3ImportActivitiesGroundwork",
    DisplayName = "Elsa 3 Activity Import Groundwork",
    Description = "Commits reviewed Elsa 3 reusable-activity collection closures atomically across Activity and Workflow Design documents.")]
public sealed class Elsa3ImportActivitiesGroundworkFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services) =>
        services.AddScoped<IReusableActivityImportCommand, GroundworkReusableActivityImportCommand>();
}
