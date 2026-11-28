using CShells.Features;
using Elsa.Activities.Design.Reconciliation.Json.Contracts;
using Elsa.Activities.Design.Reconciliation.Json.Services;
using Elsa.Workflows.Design.Reconciliation.Json.Contracts;
using Elsa.Workflows.Design.Reconciliation.Json.Services;
using Elsa3.Activities.Design.Import.Reconciliation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa3.Migration.Tooling;

/// <summary>
/// The migration tooling's shell feature (spec 1237 T013, research R-5): registers the shared Elsa 3 import run inside the
/// shell, after the publishing authorization context it decorates, and the JSON catalog writers the migrated export
/// writes through. It does not enable the Elsa 3 folder import, and it leaves the JSON reconciliation features' own
/// registrations unchanged (R-10). It is composed only by the migration CLI, through its addition list.
/// </summary>
[ShellFeature(
    name: FeatureName,
    DisplayName = "Elsa 3 migration import run",
    Description = "Runs the Elsa 3 import and publish sequence for the migration CLI's test migration.",
    // The same publisher dependencies as Elsa3JsonReconciliation, and Tasks, whose startup tasks build the catalog.
    DependsOn = new object[] { "WorkflowsPublishingApi", "WorkflowsPublishing", "Elsa3ImportActivitiesPublishing", "Tasks" })]
public sealed class Elsa3MigrationImportRunFeature : IShellFeature
{
    /// <summary>The feature name the addition list enables.</summary>
    public const string FeatureName = "Elsa3MigrationImportRun";

    public void ConfigureServices(IServiceCollection services)
    {
        Elsa3ImportRunRegistration.Add(services);
        services.TryAddScoped<IJsonWorkflowCatalogWriter, JsonWorkflowCatalogWriter>();
        services.TryAddScoped<IJsonActivityCatalogWriter, JsonActivityCatalogWriter>();
    }
}
