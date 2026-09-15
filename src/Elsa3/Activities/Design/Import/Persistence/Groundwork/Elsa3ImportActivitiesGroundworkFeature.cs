using CShells.Features;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Elsa3.Activities.Design.Import.Persistence.Groundwork.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa3.Activities.Design.Import.Persistence.Groundwork;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Elsa3")]
[ManifestFeatureCategory("Import")]
[ShellFeature(
    name: "Elsa3ImportActivitiesGroundwork",
    DisplayName = "Elsa 3 Activity Import Groundwork",
    Description = "Commits reviewed Elsa 3 reusable-activity collection closures atomically across Activity and Workflow Design documents.",
    // GroundworkActivityManagementProjectionWriter and GroundworkReusableActivityImportCommand take
    // GroundworkV2ActivityDesignStore as a required dependency, and this feature does not register it --
    // the activity-design lane owns it, along with the units it reads and writes. Without the
    // declaration, selecting this feature alone composes a shell that fails when those services
    // resolve. CShells auto-enables a DependsOn, so naming the lane is what makes the pairing real.
    DependsOn = new object[] { "ActivitiesDesignGroundworkPersistence" })]
public class Elsa3ImportActivitiesGroundworkFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services) => services.AddElsa3ImportGroundworkPersistence();
}
