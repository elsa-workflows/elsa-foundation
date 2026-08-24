using CShells.Features;
using Elsa.Activities.Design.Persistence.Groundwork.Services;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Core;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Elsa.Workflows.Design.Persistence.Groundwork;
using Elsa3.Activities.Design.Import.Contracts;
using Elsa3.Activities.Design.Import.Persistence.Groundwork.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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
    public void ConfigureServices(IServiceCollection services)
    {
        foreach (var unit in Elsa3ImportStorageManifest.CreateUnits())
            services.AddGroundworkStorageUnit(unit);
        services.TryAddScoped<GroundworkDesignStorage>(provider => new(
            provider.GetRequiredService<IGroundworkStorageSessionSource>(),
            provider.GetRequiredService<IPersistenceAccessContextAccessor>(),
            auditSink: provider.GetService<IGroundworkPrivilegedQueryAuditSink>()));
        services.TryAddScoped<GroundworkActivityManagementProjectionWriter>();
        services.RemoveAll<IReusableActivityImportOperationStore>();
        services.AddScoped<IReusableActivityImportOperationStore, GroundworkReusableActivityImportOperationStore>();
        services.RemoveAll<IReusableActivityImportCommand>();
        services.AddScoped<IReusableActivityImportCommand, GroundworkReusableActivityImportCommand>();
    }
}
