using Elsa.Persistence.EntityFramework;
using CShells.Features;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Elsa.Workflows.Runtime.Distributed.Persistence.EntityFrameworkCore.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.Distributed.Persistence.EntityFrameworkCore;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Workflows")]
[ManifestFeatureCategory("Runtime")]
[ManifestFeatureCategory("Persistence")]
[ShellFeature(
    name: "WorkflowsRuntimeDistributedEntityFrameworkCorePersistence",
    DisplayName = "Workflows Runtime Distributed EF Core Placement Persistence",
    Description = "Opt-in EF Core persistence for distributed execution placement only. It replaces IExecutionPlacementStore, leaves command transport ownership unchanged, applies or validates its own migrations on shell activation, and leaves all other distributed stores independently selectable.",
    DependsOn = new object[] { "WorkflowsRuntimeDistributed" })]
[UsesEfModule("Workflows.Runtime.Distributed.Placement")]
public class DistributedRuntimeExecutionPlacementEntityFrameworkCoreFeature : IShellFeature
{
    [ManifestSetting(DisplayName = "Provider", Description = "Relational provider: Sqlite, SqlServer, PostgreSql, or MySql. The host references the matching provider package.", Category = "Persistence")]
    public string Provider { get; set; } = "Sqlite";

    [ManifestSetting(DisplayName = "Connection string", Description = "Optional explicit provider connection string. SQLite defaults to a local file; non-SQLite providers require this or Connection name.", Category = "Persistence", Secret = true)]
    public string? ConnectionString { get; set; }

    [ManifestSetting(DisplayName = "Connection name", Description = "Named connection under ConnectionStrings when Connection string is omitted.", Category = "Persistence")]
    public string? ConnectionName { get; set; }

    [ManifestSetting(
        DisplayName = "Schema",
        Description = "Optional database schema for this module's tables and its own migrations history table. Falls back to Elsa:Persistence:EntityFramework:Schema, then to the provider's own default. Ignored on Sqlite, which has no schemas, and refused on MySql, where a schema is a database: name it in the connection string there instead.",
        Category = "Persistence")]
    public string? Schema { get; set; }

    [ManifestSetting(
        DisplayName = "Pooled contexts",
        Description = "Reuse DbContext instances from a pool instead of constructing one per scope. Safe for every first-party module context, which carries nothing but its options.",
        Category = "Persistence")]
    public bool Pooling { get; set; }

    public virtual void ConfigureServices(IServiceCollection services) => services.AddDistributedRuntimeExecutionPlacementEntityFrameworkCore(
        new DistributedRuntimeExecutionPlacementEntityFrameworkCoreOptions
        {
            Provider = Provider,
            ConnectionString = ConnectionString,
            ConnectionName = ConnectionName, Schema = Schema, Pooling = Pooling
        })
        .AddEfModuleMigrations<ExecutionPlacementDbContext>(Provider);
}
