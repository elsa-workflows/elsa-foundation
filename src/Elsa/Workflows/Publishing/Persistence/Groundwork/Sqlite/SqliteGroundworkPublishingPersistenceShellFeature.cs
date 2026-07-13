using CShells.Features;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Elsa.Workflows.Publishing.Persistence.Groundwork.Sqlite.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Publishing.Persistence.Groundwork.Sqlite;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Workflows")]
[ManifestFeatureCategory("Publishing")]
[ManifestFeatureCategory("Persistence")]
[ShellFeature(
    name: "GroundworkPublishingPersistenceSqlite",
    DisplayName = "Groundwork SQLite Publishing Persistence",
    Description = "Adds a dedicated durable SQLite lane for publication slots, records, policies, projection intents and review tokens without replacing Runtime or Design persistence.")]
public sealed class SqliteGroundworkPublishingPersistenceShellFeature : IShellFeature
{
    public const string DefaultConnectionString = "Data Source=elsa-groundwork-publishing.db";

    [ManifestSetting(
        DisplayName = "Connection string",
        Description = "SQLite connection string used only by the Groundwork Publishing document store.",
        Category = "Persistence",
        Secret = true)]
    public string? ConnectionString { get; set; }

    public void ConfigureServices(IServiceCollection services)
    {
        var connectionString = string.IsNullOrWhiteSpace(ConnectionString) ? DefaultConnectionString : ConnectionString;
        services.AddGroundworkPublishingSqlitePersistence(connectionString);
    }
}
