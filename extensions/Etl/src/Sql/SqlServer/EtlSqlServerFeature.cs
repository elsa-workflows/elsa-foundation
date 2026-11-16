using CShells.Features;
using Elsa.Etl.Core.Runtime;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Etl.Sql.SqlServer;

/// <summary>Installs the SQL Server engine that ETL SQL bindings with provider <c>sqlserver</c> resolve (spec 1197).</summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("ETL")]
[ManifestFeatureCategory("Runtime")]
[ShellFeature(name: "EtlSqlServer", DisplayName = "ETL SQL Server delivery", Description = "Keyed, transactional ETL delivery to SQL Server tables, applied once per effect through a ledger.", DependsOn = new object[] { "EtlRuntime" })]
public class EtlSqlServerFeature : IShellFeature
{
    /// <inheritdoc />
    public virtual void ConfigureServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IEtlSqlEngine, SqlServerEtlSqlEngine>();
    }
}
