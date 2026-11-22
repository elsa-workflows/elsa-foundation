using CShells.Features;
using Elsa.Etl.Core.Runtime;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Etl.Sql.PostgreSql;

/// <summary>Installs the PostgreSQL engine that ETL SQL bindings with provider <c>postgresql</c> resolve (spec 1197).</summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("ETL")]
[ManifestFeatureCategory("Runtime")]
[ShellFeature(name: "EtlSqlPostgreSql", DisplayName = "ETL PostgreSQL delivery", Description = "Keyed, transactional ETL delivery to PostgreSQL tables, applied once per effect through a ledger.", DependsOn = new object[] { "EtlRuntime" })]
public class EtlSqlPostgreSqlFeature : IShellFeature
{
    /// <inheritdoc />
    public virtual void ConfigureServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IEtlSqlEngine, PostgreSqlEtlSqlEngine>();
    }
}
