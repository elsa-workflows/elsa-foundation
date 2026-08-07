using CShells.Features;
using Elsa.Activities.Design.Persistence.Groundwork.DependencyInjection;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Activities.Design.Persistence.Groundwork;

/// <summary>
/// Backs the reusable-activity design persistence ports with Groundwork, on a target of the host's choosing.
/// <para>
/// The activities-design and workflows-design lanes share one design-operation ledger, so both must name the
/// same target. Naming different ones is rejected when the manifest binding is recorded.
/// </para>
/// </summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Activities")]
[ManifestFeatureCategory("Design")]
[ManifestFeatureCategory("Persistence")]
[ShellFeature(
    name: "ActivitiesDesignGroundworkPersistence",
    DisplayName = "Activities Design Groundwork Persistence",
    Description = "Persists reusable-activity definitions, versions, drafts, dependencies and management projections through Groundwork. Binds to a named Groundwork target, which must be the same one the workflows-design lane names.")]
public class ActivitiesDesignGroundworkPersistenceFeature : IShellFeature
{
    [ManifestSetting(
        DisplayName = "Target",
        Description = "The Groundwork target holding the reusable-activity catalog. Defaults to 'default'. Must match the workflows-design lane's target, because the two share one design-operation ledger.",
        Category = "Persistence")]
    public string? Target { get; set; }

    public void ConfigureServices(IServiceCollection services) =>
        services.AddGroundworkActivitiesDesignStores(Target);
}
