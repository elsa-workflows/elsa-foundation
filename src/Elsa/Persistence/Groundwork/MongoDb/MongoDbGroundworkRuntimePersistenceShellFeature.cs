using CShells.Features;
using Elsa.Persistence.Groundwork.DependencyInjection;
using Elsa.Persistence.Groundwork.MongoDb.DependencyInjection;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Elsa.Workflows.Runtime.Core.Models;
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
    DependsOn = new object[] { "WorkflowsRuntimeResumption" })]
public class MongoDbGroundworkRuntimePersistenceShellFeature : IShellFeature
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

    [ManifestSetting(
        DisplayName = "Auto-apply schema on startup",
        Description = "When enabled, safe pending schema operations are applied automatically at startup instead of requiring Groundwork.Tool. Destructive operations are never auto-applied.",
        Category = "Persistence")]
    public bool AutoApplySchemaOnStartup { get; set; } = true;

    [ManifestSetting(
        DisplayName = "Cache workflow executables",
        Description = "Retain a bounded shell-local cache of immutable workflow executable artifacts loaded from durable storage, isolated by persistence scope.",
        Category = "Performance")]
    public bool CacheWorkflowExecutables { get; set; } = true;

    [ManifestSetting(
        DisplayName = "Workflow executable cache capacity",
        Description = "Maximum number of immutable workflow executable artifacts retained by this shell. Must be positive when caching is enabled.",
        Category = "Performance")]
    public int WorkflowExecutableCacheCapacity { get; set; } = WorkflowExecutableCacheOptions.DefaultCapacity;

    public virtual void ConfigureServices(IServiceCollection services)
    {
        var connectionString = string.IsNullOrWhiteSpace(ConnectionString)
            ? DefaultConnectionString
            : ConnectionString;
        var databaseName = string.IsNullOrWhiteSpace(DatabaseName)
            ? DefaultDatabaseName
            : DatabaseName;

        services.AddGroundworkRuntimeStores(new WorkflowExecutableCacheOptions
        {
            Enabled = CacheWorkflowExecutables,
            Capacity = WorkflowExecutableCacheCapacity
        });
        services.AddMongoDbGroundworkDocumentStore(connectionString, databaseName, AutoApplySchemaOnStartup);
    }
}
