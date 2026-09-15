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
    name: "WorkflowsRuntimeDistributedCommandTransportEntityFrameworkCorePersistence",
    DisplayName = "Workflows Runtime Distributed EF Core Command Transport Persistence",
    Description = "Opt-in EF Core persistence for distributed command stream heads and execution command transport. It replaces IExecutionCommandTransport only, leaves execution placement independently selectable, applies or validates its own migrations on shell activation, and keeps Groundwork as the default for all other distributed stores.",
    DependsOn = new object[] { "WorkflowsRuntimeDistributed" })]
public class DistributedRuntimeExecutionCommandTransportEntityFrameworkCoreFeature : IShellFeature
{
    [ManifestSetting(DisplayName = "Provider", Description = "Relational provider: Sqlite, SqlServer, PostgreSql, or MySql. The host references the matching provider package.", Category = "Persistence")]
    public string Provider { get; set; } = "Sqlite";

    [ManifestSetting(DisplayName = "Connection string", Description = "Optional explicit provider connection string. SQLite defaults to a local file; non-SQLite providers require this or Connection name.", Category = "Persistence", Secret = true)]
    public string? ConnectionString { get; set; }

    [ManifestSetting(DisplayName = "Connection name", Description = "Named connection under ConnectionStrings when Connection string is omitted.", Category = "Persistence")]
    public string? ConnectionName { get; set; }

    public virtual void ConfigureServices(IServiceCollection services) => services.AddDistributedRuntimeExecutionCommandTransportEntityFrameworkCore(
        new DistributedRuntimeExecutionCommandTransportEntityFrameworkCoreOptions
        {
            Provider = Provider,
            ConnectionString = ConnectionString,
            ConnectionName = ConnectionName
        })
        .AddEfModuleMigrations<ExecutionCommandTransportDbContext>(Provider);
}
