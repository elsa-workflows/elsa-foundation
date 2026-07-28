using CShells.Features;
using Elsa.Persistence.Groundwork.PostgreSql.Unified.DependencyInjection;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Persistence.Groundwork.PostgreSql.Unified;

/// <summary>
/// Opt-in host feature that backs the six provider-level Elsa persistence families with one Groundwork
/// PostgreSQL target: workflow runtime, secrets, distributed runtime, workflows design, activities design,
/// and workflows publishing. Identity remains an explicit host selection.
/// </summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Workflows")]
[ManifestFeatureCategory("Runtime")]
[ManifestFeatureCategory("Persistence")]
[ShellFeature(
    name: "GroundworkUnifiedPersistencePostgreSql",
    DisplayName = "Groundwork PostgreSQL Unified Persistence",
    Description = "Backs the six provider-level Elsa persistence families with one admission-gated Groundwork PostgreSQL target; Identity remains an explicit host selection. Safe missing document structures and diagnostic streams can be auto-applied at startup; otherwise apply them through Groundwork.Tool. Compose alongside Workflows Runtime Resumption so durable work is re-driven after a restart.",
    DependsOn = new object[] { "WorkflowsRuntimeResumption" })]
public sealed class PostgreSqlGroundworkUnifiedPersistenceShellFeature : IShellFeature
{
    private readonly ShellFeatureContext _context;

    public PostgreSqlGroundworkUnifiedPersistenceShellFeature(ShellFeatureContext context) =>
        _context = context ?? throw new ArgumentNullException(nameof(context));

    public const string DefaultConnectionString = "Host=localhost;Port=5432;Database=elsa;Username=postgres;Password=postgres";

    [ManifestSetting(
        DisplayName = "Connection string",
        Description = "PostgreSQL connection string used by the single Groundwork document store backing every Elsa lane.",
        Category = "Persistence",
        Secret = true)]
    public string? ConnectionString { get; set; }

    [ManifestSetting(
        DisplayName = "Auto-apply schema on startup",
        Description = "When enabled, safe pending document-schema operations and missing diagnostic-record streams are applied automatically at startup instead of requiring Groundwork.Tool. Drift and destructive operations are never auto-applied.",
        Category = "Persistence")]
    public bool AutoApplySchemaOnStartup { get; set; } = true;

    public void ConfigureServices(IServiceCollection services)
    {
        var connectionString = string.IsNullOrWhiteSpace(ConnectionString) ? DefaultConnectionString : ConnectionString;
        services.AddGroundworkPostgreSqlUnifiedPersistence(connectionString, _context, AutoApplySchemaOnStartup);
    }
}
