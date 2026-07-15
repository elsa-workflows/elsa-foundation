using CShells.Features;
using Elsa.Persistence.Groundwork.MongoDb.Unified.DependencyInjection;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Persistence.Groundwork.MongoDb.Unified;

/// <summary>
/// Chooses one admission-gated MongoDB target for all seven shipped Elsa persistence families:
/// workflow runtime, identity, secrets, distributed runtime, workflows design, activities design,
/// and workflows publishing.
/// </summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Workflows")]
[ManifestFeatureCategory("Runtime")]
[ManifestFeatureCategory("Persistence")]
[ShellFeature(
    name: "GroundworkUnifiedPersistenceMongoDb",
    DisplayName = "Groundwork MongoDB Unified Persistence",
    Description = "Backs all seven shipped Elsa persistence families with one deployment-owned Groundwork MongoDB target: workflow runtime, identity, secrets, distributed runtime, workflows design, activities design and workflows publishing. A writable transaction-capable replica set and the exact pre-applied schema are required at startup.",
    DependsOn = new object[] { "WorkflowsRuntimeResumption" })]
public class MongoDbGroundworkUnifiedPersistenceShellFeature : IShellFeature
{
    public const string DefaultConnectionString = "mongodb://localhost:27017/?replicaSet=rs0";
    public const string DefaultDatabaseName = "elsa";

    [ManifestSetting(
        DisplayName = "Connection string",
        Description = "MongoDB replica-set connection string for the unified Groundwork document store.",
        Category = "Persistence",
        Secret = true)]
    public string? ConnectionString { get; set; }

    [ManifestSetting(
        DisplayName = "Database name",
        Description = "MongoDB database containing the deployment-owned Groundwork physical schema.",
        Category = "Persistence")]
    public string? DatabaseName { get; set; }

    public void ConfigureServices(IServiceCollection services)
    {
        var connectionString = string.IsNullOrWhiteSpace(ConnectionString)
            ? DefaultConnectionString
            : ConnectionString;
        var databaseName = string.IsNullOrWhiteSpace(DatabaseName)
            ? DefaultDatabaseName
            : DatabaseName;
        services.AddGroundworkMongoDbUnifiedPersistence(connectionString, databaseName);
    }
}
