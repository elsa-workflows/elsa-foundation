using CShells.Features;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Elsa.Workflows.Design.Persistence.Groundwork.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Design.Persistence.Groundwork;

/// <summary>
/// Backs the workflow-design persistence ports with Groundwork, on a target of the host's choosing.
/// <para>
/// This is what makes the design lane reachable without composing a unified provider that drags every other
/// lane's schema into the same database. Point <see cref="Target"/> at a database of its own and the
/// authoring catalog lives there alone; leave it unset and design shares the default target as before.
/// </para>
/// <para>
/// This lane owns a private <c>workflowDesignOperation</c> ledger. The activities-design lane owns a
/// separate <c>activityDesignOperation</c> ledger, so the two catalogs may bind to different targets.
/// </para>
/// </summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Workflows")]
[ManifestFeatureCategory("Design")]
[ManifestFeatureCategory("Persistence")]
[ShellFeature(
    name: "WorkflowsDesignGroundworkPersistence",
    DisplayName = "Workflows Design Groundwork Persistence",
    Description = "Persists workflow definitions, versions, drafts and layouts through Groundwork. Binds to a named Groundwork target, so the authoring catalog can live in its own database.")]
public class WorkflowsDesignGroundworkPersistenceFeature : IShellFeature
{
    [ManifestSetting(
        DisplayName = "Target",
        Description = "The Groundwork target holding the workflow-design catalog. Defaults to 'default'. Independent of the activities-design lane's target; each catalog owns its own operation ledger.",
        Category = "Persistence")]
    public string? Target { get; set; }

    public void ConfigureServices(IServiceCollection services) =>
        services.AddGroundworkWorkflowsDesignStores(Target);
}
