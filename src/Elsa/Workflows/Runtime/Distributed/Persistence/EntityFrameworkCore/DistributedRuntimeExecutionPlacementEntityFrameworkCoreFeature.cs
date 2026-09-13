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
    Description = "Opt-in EF Core persistence for distributed execution placement only. It replaces IExecutionPlacementStore, leaves command transport ownership unchanged, does not provision schema, and keeps Groundwork as the default for all other distributed stores.",
    DependsOn = new object[] { "WorkflowsRuntimeDistributed" })]
public class DistributedRuntimeExecutionPlacementEntityFrameworkCoreFeature : IShellFeature
{
    [ManifestSetting(DisplayName = "Provider", Description = "Relational provider: Sqlite, SqlServer, PostgreSql, or MySql. The host references the matching provider package.", Category = "Persistence")]
    public string Provider { get; set; } = "Sqlite";

    [ManifestSetting(DisplayName = "Connection string", Description = "Optional explicit provider connection string. SQLite defaults to a local file; non-SQLite providers require this or Connection name.", Category = "Persistence", Secret = true)]
    public string? ConnectionString { get; set; }

    [ManifestSetting(DisplayName = "Connection name", Description = "Named connection under ConnectionStrings when Connection string is omitted.", Category = "Persistence")]
    public string? ConnectionName { get; set; }

    public void ConfigureServices(IServiceCollection services) => services.AddDistributedRuntimeExecutionPlacementEntityFrameworkCore(
        new DistributedRuntimeExecutionPlacementEntityFrameworkCoreOptions
        {
            Provider = Provider,
            ConnectionString = ConnectionString,
            ConnectionName = ConnectionName
        });
}
