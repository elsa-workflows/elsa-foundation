using CShells.Features;
using Elsa.Persistence.Groundwork;
using Elsa.Persistence.Groundwork.DependencyInjection;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Groundwork.Documents.Store;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Persistence.Groundwork.Sqlite;

/// <summary>
/// Opt-in host feature that backs the runtime persistence seams with Groundwork over SQLite. The
/// host owns the provider choice: composing this feature selects SQLite and supplies its connection
/// string. Runtime and domain code never reference Groundwork or SQLite. When this feature is not
/// composed, the runtime keeps its in-memory defaults.
/// </summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Workflows")]
[ManifestFeatureCategory("Runtime")]
[ManifestFeatureCategory("Persistence")]
[ShellFeature(
    name: "GroundworkRuntimePersistenceSqlite",
    DisplayName = "Groundwork SQLite Runtime Persistence",
    Description = "Backs the workflow runtime persistence seams with Groundwork over SQLite.")]
public sealed class SqliteGroundworkRuntimePersistenceShellFeature : IShellFeature
{
    public const string DefaultConnectionString = "Data Source=elsa-groundwork-runtime.db";

    [ManifestSetting(
        DisplayName = "Connection string",
        Description = "SQLite connection string used by the Groundwork runtime document store.",
        Category = "Persistence",
        Secret = true)]
    public string? ConnectionString { get; set; }

    public void ConfigureServices(IServiceCollection services)
    {
        var connectionString = string.IsNullOrWhiteSpace(ConnectionString) ? DefaultConnectionString : ConnectionString;

        services.RemoveAll<IDocumentStore>();
        services.AddSingleton<IDocumentStore>(_ =>
            new SqliteGroundworkDocumentStore(connectionString, ElsaRuntimeStorageManifest.Create()));

        services.AddGroundworkRuntimeStores();
    }
}
