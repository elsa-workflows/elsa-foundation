using CShells.Features;
using Elsa.Activities.Design.Persistence.Core.Contracts;
using Elsa.Activities.Design.Core.Contracts;
using GroundworkActivityManagementProjectionWriter = Elsa.Activities.Design.Persistence.Groundwork.Services.GroundworkActivityManagementProjectionWriter;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Elsa.Workflows.Publishing.Persistence.Groundwork.Services;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Publishing.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Workflows.Publishing.Persistence.Groundwork;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Workflows")]
[ManifestFeatureCategory("Publishing")]
[ManifestFeatureCategory("Persistence")]
[ShellFeature(
    name: "WorkflowsPublishingGroundwork",
    DisplayName = "Workflows Publishing (Groundwork)",
    Description = "Atomic reusable-activity publication across Design and Runtime Groundwork units.")]
public sealed class PublishingGroundworkFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.TryAddScoped<GroundworkActivityManagementProjectionWriter>();
        services.AddScoped<ICommitActivityPublicationCommand<ExecutableActivityTemplate, WorkflowExecutableSourceReference, ActivityPublicationReceipt>, GroundworkActivityPublicationCommand>();
        services.AddScoped<ICommitSourceActivityPublicationCommand<ExecutableActivityTemplate, WorkflowExecutableSourceReference>, GroundworkSourceActivityPublicationCommand>();
        services.AddScoped<GroundworkActivityUpgradePlanStore>();
        services.AddScoped<IActivityUpgradeDiscoverySource>(sp => sp.GetRequiredService<GroundworkActivityUpgradePlanStore>());
        services.AddScoped<IActivityUpgradePlanMutationStore>(sp => sp.GetRequiredService<GroundworkActivityUpgradePlanStore>());
        services.AddScoped<IActivityUpgradePublishedDraftResolver>(sp => sp.GetRequiredService<GroundworkActivityUpgradePlanStore>());
        services.AddScoped<IActivityDependencyProjectionRebuildCoordinator, GroundworkActivityDependencyProjectionRebuildCoordinator>();
    }
}
