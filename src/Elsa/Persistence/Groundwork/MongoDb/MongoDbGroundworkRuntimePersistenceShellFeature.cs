using CShells.Features;
using Elsa.Persistence.Groundwork.DependencyInjection;
using Elsa.Persistence.Groundwork.MongoDb.DependencyInjection;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Persistence.Groundwork.MongoDb;

/// <summary>Chooses the admission-gated MongoDB Groundwork leaf for workflow runtime persistence.</summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Workflows")]
[ManifestFeatureCategory("Runtime")]
[ManifestFeatureCategory("Persistence")]
[ShellFeature(
    name: "GroundworkRuntimePersistenceMongoDb",
    DisplayName = "Groundwork MongoDB Runtime Persistence",
    Description = "Backs workflow runtime persistence with one deployment-owned Groundwork MongoDB target. A writable transaction-capable replica set and the exact pre-applied schema are required at startup.",
    DependsOn = new object[] { "WorkflowsRuntimeResumption", "WorkflowsRuntimeRecurringTriggers" })]
public class MongoDbGroundworkRuntimePersistenceShellFeature : GroundworkRuntimePersistenceShellFeatureBase
{
    public const string DefaultConnectionString = "mongodb://localhost:27017/?replicaSet=rs0";
    public const string DefaultDatabaseName = "elsa";

    [ManifestSetting(
        DisplayName = "Connection string",
        Description = "MongoDB replica-set connection string for the Groundwork runtime document store.",
        Category = "Persistence",
        Secret = true)]
    public string? ConnectionString { get; set; }

    [ManifestSetting(
        DisplayName = "Database name",
        Description = "MongoDB database containing the deployment-owned Groundwork physical schema.",
        Category = "Persistence")]
    public string? DatabaseName { get; set; }

    public override void ConfigureServices(IServiceCollection services)
    {
        services.AddGroundworkRuntimeStores(CreateWorkflowExecutableCacheOptions());
        services.AddMongoDbGroundworkDocumentStore(
            ValueOrDefault(ConnectionString, DefaultConnectionString),
            ValueOrDefault(DatabaseName, DefaultDatabaseName),
            AutoApplySchemaOnStartup);
    }
}
