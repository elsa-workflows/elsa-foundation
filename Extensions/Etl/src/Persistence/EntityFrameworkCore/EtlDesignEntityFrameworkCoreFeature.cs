using CShells.Features;
using Elsa.Etl.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Persistence.EntityFramework;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Etl.Persistence.EntityFrameworkCore;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("ETL")]
[ManifestFeatureCategory("Persistence")]
[ShellFeature(
    name: "EtlDesignEntityFrameworkCore",
    DisplayName = "ETL Design EF Core Persistence",
    Description = "SQLite persistence for ETL drafts and immutable published artifacts.",
    DependsOn = new object[] { "EtlDesign" })]
[UsesEfModule("EtlDesign")]
public class EtlDesignEntityFrameworkCoreFeature : IShellFeature
{
    [ManifestSetting(DisplayName = "Provider", Description = "Relational provider. This initial ETL design module supports SQLite.", Category = "Persistence")]
    public string Provider { get; set; } = "Sqlite";

    [ManifestSetting(DisplayName = "Connection string", Description = "Optional ETL persistence connection string. If omitted, the shared Elsa connection is used.", Category = "Persistence", Secret = true)]
    public string? ConnectionString { get; set; }

    [ManifestSetting(DisplayName = "Connection name", Description = "Optional named connection under ConnectionStrings.", Category = "Persistence")]
    public string? ConnectionName { get; set; }

    [ManifestSetting(DisplayName = "Schema", Description = "Optional database schema. SQLite ignores schemas.", Category = "Persistence")]
    public string? Schema { get; set; }

    [ManifestSetting(DisplayName = "Pooled contexts", Description = "Reuse the ETL design DbContext from a pool.", Category = "Persistence")]
    public bool Pooling { get; set; }

    public virtual void ConfigureServices(IServiceCollection services)
    {
        var options = new EtlDesignEntityFrameworkCoreOptions
        {
            Provider = Provider,
            ConnectionString = ConnectionString,
            ConnectionName = ConnectionName,
            Schema = Schema,
            Pooling = Pooling
        };
        services.AddEtlDesignEntityFrameworkCore(options);
        services.AddEfModuleMigrations<EtlDesignDbContext>(options.Provider);
    }
}
