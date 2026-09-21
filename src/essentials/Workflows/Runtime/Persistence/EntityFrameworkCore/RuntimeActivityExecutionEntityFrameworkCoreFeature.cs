using Elsa.Persistence.EntityFramework;
using CShells.Features;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Workflows.Runtime.Services.ActivityExecutions;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Workflows")]
[ManifestFeatureCategory("Runtime")]
[ManifestFeatureCategory("Persistence")]
[ShellFeature(
    name: "WorkflowsRuntimeActivityExecutionEntityFrameworkCorePersistence",
    DisplayName = "Workflows Runtime EF Core Activity Execution Persistence",
    Description = "Opt-in EF Core persistence for activity execution state, inspection and hierarchy.",
    DependsOn = new object[] { "WorkflowsRuntimeResumption" })]
[UsesEfModule("Workflows.Runtime")]
public class RuntimeActivityExecutionEntityFrameworkCoreFeature : IShellFeature
{
    [ManifestSetting(DisplayName = "Provider", Description = "Relational provider for the Runtime activity execution DbContext: Sqlite, SqlServer, PostgreSql, or MySql.", Category = "Persistence")]
    public string Provider { get; set; } = "Sqlite";

    [ManifestSetting(DisplayName = "Connection string", Description = "Optional explicit connection string. When omitted, ConnectionName or ConnectionStrings:Elsa is used. Sqlite defaults to Data Source=elsa.db.", Category = "Persistence", Secret = true)]
    public string? ConnectionString { get; set; }

    [ManifestSetting(DisplayName = "Connection name", Description = "Optional configuration connection-string name. When omitted, Elsa is used.", Category = "Persistence")]
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

    [ManifestSetting(DisplayName = "Hierarchy cursor signing key", Description = "At least 32 UTF-8 bytes shared by nodes that consume activity execution hierarchy pages.", Category = "Security", Secret = true)]
    public string? HierarchyCursorSigningKey { get; set; }

    [ManifestSetting(DisplayName = "Recovery continuation signing key", Description = "At least 32 UTF-8 bytes shared by nodes that consume durable activity execution state and inspection pages.", Category = "Security", Secret = true)]
    public string? RecoveryContinuationSigningKey { get; set; }

    public virtual void ConfigureServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (!string.IsNullOrWhiteSpace(HierarchyCursorSigningKey))
            services.Configure<ActivityExecutionHierarchyCursorOptions>(options => options.SigningKey = HierarchyCursorSigningKey);
        services.AddRuntimeActivityExecutionEntityFrameworkCore(new RuntimeActivityExecutionEntityFrameworkCoreOptions
        {
            Provider = Provider,
            ConnectionString = ConnectionString,
            ConnectionName = ConnectionName,
            Schema = Schema,
            Pooling = Pooling,
            HierarchyCursorSigningKey = HierarchyCursorSigningKey,
            RecoveryContinuationSigningKey = RecoveryContinuationSigningKey
        });
        services.AddEfModuleMigrations<RuntimeDbContext>(Provider);
    }
}
