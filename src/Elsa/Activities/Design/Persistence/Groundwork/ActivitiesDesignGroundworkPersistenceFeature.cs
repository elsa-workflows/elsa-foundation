using CShells.Features;
using Elsa.Activities.Design.Persistence.Groundwork.DependencyInjection;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Activities.Design.Persistence.Groundwork;

/// <summary>
/// Backs the reusable-activity design persistence ports with Groundwork, on a target of the host's choosing.
/// <para>
/// This lane owns a private <c>activityDesignOperation</c> ledger. The workflow-design lane owns a
/// separate <c>workflowDesignOperation</c> ledger; the two catalogs may bind to different Groundwork
/// targets (split-database topology). Cross-lane publication stages design, runtime, and publishing
/// rows in one transaction and refuses a split-target host rather than using a shared ledger.
/// </para>
/// </summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Activities")]
[ManifestFeatureCategory("Design")]
[ManifestFeatureCategory("Persistence")]
[ShellFeature(
    name: "ActivitiesDesignGroundworkPersistence",
    DisplayName = "Activities Design Groundwork Persistence",
    Description = "Persists reusable-activity definitions, versions, drafts, dependencies and management projections through Groundwork. Binds to a named Groundwork target; the workflow-design lane may use a different target.")]
public class ActivitiesDesignGroundworkPersistenceFeature : IShellFeature
{
    [ManifestSetting(
        DisplayName = "Target",
        Description = "The Groundwork target holding the reusable-activity catalog. Defaults to 'default'. Independent of the workflows-design lane's target; each catalog owns its own operation ledger.",
        Category = "Persistence")]
    public string? Target { get; set; }

    public void ConfigureServices(IServiceCollection services) =>
        services.AddGroundworkActivitiesDesignStores(Target);
}
