using CShells.Features;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Elsa.Workflows.Publishing.Persistence.Groundwork.DependencyInjection;
using Elsa.Workflows.Publishing.Persistence.Groundwork.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Publishing.Persistence.Groundwork;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Workflows")]
[ManifestFeatureCategory("Publishing")]
[ManifestFeatureCategory("Persistence")]
[ShellFeature(
    name: "WorkflowsPublishingGroundwork",
    DisplayName = "Workflows Publishing (Groundwork)",
    Description = "Atomic reusable-activity publication across Design and Runtime Groundwork units.",
    // The publication commands write through the Activities Design Groundwork stores, which that
    // feature owns and must register first.
    DependsOn = new object[] { "ActivitiesDesignGroundworkPersistence" })]
public sealed class PublishingGroundworkFeature : IShellFeature
{
    [ManifestSetting(
        DisplayName = "Target",
        Description = "The Groundwork target holding publication records, policies and receipts. Activation slots are Runtime-owned and use the Runtime target. Defaults to 'default'. Reusable-activity publication commits design, runtime and publishing documents together, so those lanes must currently agree on one target.",
        Category = "Persistence")]
    public string? Target { get; set; }

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddGroundworkPublishingStores(Target);
        services.AddGroundworkActivityPublicationCommands();
        services.AddScoped<GroundworkActivityUpgradePlanStore>();
        services.AddScoped<IActivityUpgradeDiscoverySource>(sp => sp.GetRequiredService<GroundworkActivityUpgradePlanStore>());
        services.AddScoped<IActivityUpgradePlanMutationStore>(sp => sp.GetRequiredService<GroundworkActivityUpgradePlanStore>());
        services.AddScoped<IActivityUpgradePublishedDraftResolver>(sp => sp.GetRequiredService<GroundworkActivityUpgradePlanStore>());
        services.AddScoped<IActivityDependencyProjectionRebuildCoordinator, GroundworkActivityDependencyProjectionRebuildCoordinator>();
    }
}
