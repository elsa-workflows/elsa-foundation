using CShells.Features;
using Elsa.Persistence.Groundwork.DependencyInjection;
using Elsa.Persistence.Groundwork.PostgreSql.DependencyInjection;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Persistence.Groundwork.PostgreSql;

/// <summary>
/// Opt-in host feature that backs the runtime persistence seams with Groundwork over PostgreSQL. The
/// host owns the provider choice: composing this feature selects PostgreSQL and supplies its connection
/// string. Runtime and domain code never reference Groundwork or PostgreSQL. When this feature is not
/// composed, the runtime keeps its in-memory defaults.
/// </summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Workflows")]
[ManifestFeatureCategory("Runtime")]
[ManifestFeatureCategory("Persistence")]
[ShellFeature(
    name: "GroundworkRuntimePersistencePostgreSql",
    DisplayName = "Groundwork PostgreSQL Runtime Persistence",
    Description = "Backs the workflow runtime persistence seams with Groundwork over PostgreSQL. Durable storage keeps checkpoints, post-commit outbox items and queued scheduler work across a crash; compose alongside Workflows Runtime Resumption so a background pump re-drives that work after a restart.",
    DependsOn = new object[] { "WorkflowsRuntimeResumption" })]
public class PostgreSqlGroundworkRuntimePersistenceShellFeature : GroundworkRuntimePersistenceShellFeatureBase
{
    public const string DefaultConnectionString = "Host=localhost;Port=5432;Database=elsa;Username=postgres;Password=postgres";

    [ManifestSetting(
        DisplayName = "Connection string",
        Description = "PostgreSQL connection string used by the Groundwork runtime document store.",
        Category = "Persistence",
        Secret = true)]
    public string? ConnectionString { get; set; }

    public override void ConfigureServices(IServiceCollection services)
    {
        services.AddPostgreSqlGroundworkDocumentStore(
            ValueOrDefault(ConnectionString, DefaultConnectionString),
            AutoApplySchemaOnStartup);

        services.AddGroundworkRuntimeStores(CreateWorkflowExecutableCacheOptions());
    }
}
