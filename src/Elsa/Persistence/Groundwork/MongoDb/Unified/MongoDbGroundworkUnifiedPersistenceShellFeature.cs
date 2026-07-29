using CShells.Features;
using Elsa.Persistence.Groundwork.MongoDb.Unified.DependencyInjection;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Persistence.Groundwork.MongoDb.Unified;

/// <summary>
/// Chooses one admission-gated MongoDB target for the six provider-level Elsa persistence families:
/// workflow runtime, secrets, distributed runtime, workflows design, activities design, and workflows
/// publishing. Identity remains an explicit host selection.
/// </summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Workflows")]
[ManifestFeatureCategory("Runtime")]
[ManifestFeatureCategory("Persistence")]
[ShellFeature(
    name: "GroundworkUnifiedPersistenceMongoDb",
    DisplayName = "Groundwork MongoDB Unified Persistence",
    Description = "Backs the six provider-level Elsa persistence families with one deployment-owned Groundwork MongoDB target: workflow runtime, secrets, distributed runtime, workflows design, activities design and workflows publishing. Identity remains an explicit host selection. A writable transaction-capable replica set is required; safe missing document structures and diagnostic streams can be auto-applied at startup.",
    DependsOn = new object[] { "WorkflowsRuntimeResumption" })]
public class MongoDbGroundworkUnifiedPersistenceShellFeature : IShellFeature
{
    private readonly ShellFeatureContext _context;

    public MongoDbGroundworkUnifiedPersistenceShellFeature(ShellFeatureContext context) =>
        _context = context ?? throw new ArgumentNullException(nameof(context));

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

    [ManifestSetting(
        DisplayName = "Auto-apply schema on startup",
        Description = "When enabled, safe pending document-schema operations and missing diagnostic-record streams are applied automatically at startup instead of requiring Groundwork.Tool. Drift and destructive operations are never auto-applied.",
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
        services.AddGroundworkMongoDbUnifiedPersistence(
            connectionString,
            databaseName,
            _context,
            new WorkflowExecutableCacheOptions
            {
                Enabled = CacheWorkflowExecutables,
                Capacity = WorkflowExecutableCacheCapacity
            },
            AutoApplySchemaOnStartup);
    }
}
