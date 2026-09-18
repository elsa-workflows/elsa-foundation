using CShells.Features;
using Elsa.Persistence.EntityFramework;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Workflows.Runtime.Services.ActivityExecutions;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;

/// <summary>
/// Backs the complete workflow-runtime persistence family with EF Core: the workflow
/// runtime feature. It applies or validates the shared Runtime migrations on shell activation.
/// </summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Workflows")]
[ManifestFeatureCategory("Runtime")]
[ManifestFeatureCategory("Persistence")]
[ShellFeature(
    name: "WorkflowsRuntimeEntityFrameworkCore",
    DisplayName = "Workflows Runtime EF Core Persistence",
    Description = "Persists the complete workflow runtime state (R01-R29) through EF Core on one relational database, and applies or validates its migrations on shell activation.",
    DependsOn = new object[] { "WorkflowsRuntimeResumption" })]
public class RuntimeEntityFrameworkCoreFeature : IShellFeature
{
    [ManifestSetting(
        DisplayName = "Provider",
        Description = "Relational provider: Sqlite, SqlServer, PostgreSql, or MySql. The host must reference that provider package.",
        Category = "Persistence")]
    public string Provider { get; set; } = "Sqlite";

    [ManifestSetting(
        DisplayName = "Connection string",
        Description = "Optional explicit connection string. When omitted, ConnectionName or ConnectionStrings:Elsa is used. Sqlite defaults to Data Source=elsa.db.",
        Category = "Persistence",
        Secret = true)]
    public string? ConnectionString { get; set; }

    [ManifestSetting(
        DisplayName = "Connection name",
        Description = "Optional configuration connection-string name. When omitted, Elsa is used.",
        Category = "Persistence")]
    public string? ConnectionName { get; set; }

    [ManifestSetting(
        DisplayName = "Schema",
        Description = "Optional database schema for this module's tables and its own migrations history table. Falls back to Elsa:Persistence:EntityFramework:Schema, then to the provider's own default. Ignored on Sqlite, which has no schemas, and refused on MySql, where a schema is a database: name it in the connection string there instead.",
        Category = "Persistence")]
    public string? Schema { get; set; }

    [ManifestSetting(
        DisplayName = "Pooled contexts",
        Description = "Reuse DbContext instances from a pool instead of constructing one per scope. Safe for every first-party module context, which carries nothing but its options.",
        Category = "Persistence")]
    public bool Pooling { get; set; }

    [ManifestSetting(
        DisplayName = "Cache workflow executables",
        Description = "Retain a bounded shell-local cache of immutable workflow executable artifacts loaded from the database, isolated by persistence scope.",
        Category = "Performance")]
    public bool CacheWorkflowExecutables { get; set; } = true;

    [ManifestSetting(
        DisplayName = "Workflow executable cache capacity",
        Description = "Maximum number of immutable workflow executable artifacts retained by this shell. Must be positive when caching is enabled.",
        Category = "Performance")]
    public int WorkflowExecutableCacheCapacity { get; set; } = WorkflowExecutableCacheOptions.DefaultCapacity;

    [ManifestSetting(
        DisplayName = "Recovery continuation signing key",
        Description = "At least 32 UTF-8 bytes shared by nodes that consume durable recovery and runtime pages. Required for durable runtime paging.",
        Category = "Security",
        Secret = true)]
    public string? RecoveryContinuationSigningKey { get; set; }

    [ManifestSetting(
        DisplayName = "Hierarchy cursor signing key",
        Description = "At least 32 UTF-8 bytes shared by nodes that consume activity execution hierarchy pages. Required for durable hierarchy paging.",
        Category = "Security",
        Secret = true)]
    public string? HierarchyCursorSigningKey { get; set; }

    public virtual void ConfigureServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddRuntimeEntityFrameworkCore(new RuntimeEntityFrameworkCoreOptions
        {
            Provider = Provider,
            ConnectionString = ConnectionString,
            ConnectionName = ConnectionName,
            Schema = Schema,
            Pooling = Pooling,
            CacheWorkflowExecutables = CacheWorkflowExecutables,
            WorkflowExecutableCacheCapacity = WorkflowExecutableCacheCapacity,
            RecoveryContinuationSigningKey = RecoveryContinuationSigningKey,
            HierarchyCursorSigningKey = HierarchyCursorSigningKey
        });
        // The runtime API feature assigns its own hierarchy key even when that key is unset, so a shell that composes
        // it after this feature would erase this key and fail closed. Post-configuring keeps the key that was
        // configured here effective in either feature order.
        if (!string.IsNullOrWhiteSpace(HierarchyCursorSigningKey))
            services.PostConfigure<ActivityExecutionHierarchyCursorOptions>(options => options.SigningKey = HierarchyCursorSigningKey);
        services.AddEfModuleMigrations<BookmarkStateDbContext>(Provider);
    }
}
