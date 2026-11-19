using CShells.Features;
using Elsa.Connections.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Persistence.EntityFramework;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Connections.Persistence.EntityFrameworkCore;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Connections")]
[ManifestFeatureCategory("Persistence")]
[ShellFeature(name: "ConnectionsEntityFrameworkCore", DisplayName = "Connections Entity Framework Core Persistence",
    Description = "Persists scoped service connection metadata with database version checks.", DependsOn = new object[] { "Connections" })]
[UsesEfModule("Connections")]
public class ConnectionsEntityFrameworkCoreFeature : IShellFeature
{
    [ManifestSetting(DisplayName = "Provider", Description = "Sqlite, SqlServer, PostgreSql or MySql; the host supplies the selected provider package.", Category = "Persistence")]
    public string Provider { get; set; } = "Sqlite";
    [ManifestSetting(DisplayName = "Connection string", Description = "Optional explicit database connection string.", Category = "Persistence", Secret = true)]
    public string? ConnectionString { get; set; }
    [ManifestSetting(DisplayName = "Connection name", Description = "Named database connection when the explicit string is omitted.", Category = "Persistence")]
    public string? ConnectionName { get; set; }
    [ManifestSetting(DisplayName = "Schema", Description = "Optional module schema. Ignored on Sqlite and refused on MySql, where the database is named in the connection string.", Category = "Persistence")]
    public string? Schema { get; set; }
    [ManifestSetting(DisplayName = "Pooled contexts", Description = "Reuse contexts carrying only their options.", Category = "Persistence")]
    public bool Pooling { get; set; }

    public virtual void ConfigureServices(IServiceCollection services) => services.AddConnectionsEntityFrameworkCore(new()
    {
        Provider = Provider, ConnectionString = ConnectionString, ConnectionName = ConnectionName, Schema = Schema, Pooling = Pooling
    });
}
