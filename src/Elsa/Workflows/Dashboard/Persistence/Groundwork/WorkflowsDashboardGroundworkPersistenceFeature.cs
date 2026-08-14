using CShells.Features;
using Elsa.Persistence.Groundwork;
using Elsa.Persistence.Groundwork.Targets;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Elsa.Serialization.Core;
using Elsa.Workflows.Design.Persistence.Groundwork;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Workflows.Dashboard.Persistence.Groundwork;

/// <summary>
/// Backs the workflow dashboard tiles with direct reads over the Groundwork tables — the physical entity
/// tables the manifests declare for definitions, drafts and executions, plus the shared documents table.
/// <para>
/// Run health reads runtime documents only. The portfolio tile counts design-lane definitions and drafts and
/// how many of them are published, which is a runtime-lane fact — so when a host puts those lanes in
/// different databases the tile queries each target and correlates the definition ids in memory. When they
/// share a target it stays a single statement.
/// </para>
/// <para>
/// This resolves both connections from the named targets, so unlike the unified presets it does not need a
/// connection string of its own.
/// </para>
/// </summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Workflows")]
[ManifestFeatureCategory("Persistence")]
[ShellFeature(
    name: "WorkflowsDashboardGroundworkPersistence",
    DisplayName = "Workflows Dashboard Groundwork Persistence",
    Description = "Serves the dashboard run-health and portfolio tiles from the Groundwork document tables, resolving each lane's database from its named target. Supports design and runtime living in separate databases.")]
public class WorkflowsDashboardGroundworkPersistenceFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.TryAddScoped<IWorkflowPortfolioDataSource>(serviceProvider =>
        {
            var laneTargets = serviceProvider.GetRequiredService<GroundworkLaneTargets>();
            var designTarget = laneTargets.For(typeof(WorkflowsDesignGroundworkStorageManifestSource));
            var runtimeTarget = laneTargets.For(typeof(RuntimeGroundworkStorageManifestSource));
            var design = RequireConnectionSource(serviceProvider, designTarget, "workflow portfolio (design lane)");
            var colocated = string.Equals(designTarget, runtimeTarget, StringComparison.Ordinal);
            var runtime = colocated
                ? null
                : RequireConnectionSource(serviceProvider, runtimeTarget, "workflow portfolio (runtime lane)");

            return new GroundworkWorkflowPortfolioDataSource(
                design.CreateConnection,
                DialectOf(design),
                serviceProvider.GetRequiredService<IPayloadSerializer>(),
                runtime is null ? null : runtime.CreateConnection);
        });

        services.TryAddScoped<IWorkflowRunHealthDataSource>(serviceProvider =>
        {
            var runtimeTarget = serviceProvider.GetRequiredService<GroundworkLaneTargets>()
                .For(typeof(RuntimeGroundworkStorageManifestSource));
            var runtime = RequireConnectionSource(serviceProvider, runtimeTarget, "workflow run health");
            return new GroundworkWorkflowRunHealthDataSource(runtime.CreateConnection, DialectOf(runtime));
        });
    }

    private static IGroundworkTargetConnectionSource RequireConnectionSource(
        IServiceProvider serviceProvider,
        string targetName,
        string tile)
    {
        return serviceProvider.GetKeyedService<IGroundworkTargetConnectionSource>(targetName)
               ?? throw new InvalidOperationException(
                   $"The {tile} tile reads Groundwork tables directly and needs a connection to target " +
                   $"'{targetName}', but its provider leaf supplies none. Document-database providers do not " +
                   "offer one; use a relational target for the dashboard, or disable this feature.");
    }

    /// <summary>Maps the provider leaf identity onto the SQL dialect the tiles render.</summary>
    private static GroundworkRunHealthDialect DialectOf(IGroundworkTargetConnectionSource source) =>
        source.ProviderIdentity switch
        {
            "sqlite" => GroundworkRunHealthDialect.Sqlite,
            "sqlserver" => GroundworkRunHealthDialect.SqlServer,
            "postgresql" => GroundworkRunHealthDialect.PostgreSql,
            _ => throw new InvalidOperationException(
                $"Groundwork provider '{source.ProviderIdentity}' has no dashboard SQL dialect.")
        };
}
