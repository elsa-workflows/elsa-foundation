using CShells.Features;
using Elsa.Weaver.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Persistence.EntityFramework;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Weaver.Persistence.EntityFrameworkCore;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Weaver")]
[ManifestFeatureCategory("Persistence")]
[ShellFeature(name: "WeaverEntityFrameworkCore", DisplayName = "Foundation Agent Entity Framework Core Persistence",
    Description = "Opt-in durable Weaver sessions: the session record store and owner-scoped audit in a relational database, with owner, fence and database-time lease checks and protected payloads.",
    DependsOn = new object[] { "Weaver", "Secrets" })]
[UsesEfModule("Weaver")]
public class WeaverEntityFrameworkCoreFeature : IShellFeature
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
    [ManifestSetting(DisplayName = "Lease seconds", Description = "How long a host keeps a session's work after its last write or renewal, in database time; 15 to 86400.", Category = "Runtime")]
    public int LeaseSeconds { get; set; } = 30;

    public virtual void ConfigureServices(IServiceCollection services) => services.AddAgentEntityFrameworkCore(new()
    {
        Provider = Provider, ConnectionString = ConnectionString, ConnectionName = ConnectionName, Schema = Schema, Pooling = Pooling,
        LeaseDuration = TimeSpan.FromSeconds(LeaseSeconds)
    });
}
