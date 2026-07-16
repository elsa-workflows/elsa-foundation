using CShells.Features;
using Elsa.Persistence.Groundwork.PostgreSql.Unified.DependencyInjection;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Persistence.Groundwork.PostgreSql.Unified;

/// <summary>
/// Opt-in host feature that backs all seven shipped Elsa persistence families with one Groundwork
/// PostgreSQL target: workflow runtime, identity, secrets, distributed runtime, workflows design,
/// activities design, and workflows publishing. Runtime, design, and domain code remain
/// provider-neutral.
/// </summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Workflows")]
[ManifestFeatureCategory("Runtime")]
[ManifestFeatureCategory("Persistence")]
[ShellFeature(
    name: "GroundworkUnifiedPersistencePostgreSql",
    DisplayName = "Groundwork PostgreSQL Unified Persistence",
    Description = "Backs all seven shipped Elsa persistence families with one admission-gated Groundwork PostgreSQL target. Apply schema through Groundwork.Tool before host startup; compose alongside Workflows Runtime Resumption so durable work is re-driven after a restart.",
    DependsOn = new object[] { "WorkflowsRuntimeResumption" })]
public sealed class PostgreSqlGroundworkUnifiedPersistenceShellFeature : IShellFeature
{
    public const string DefaultConnectionString = "Host=localhost;Port=5432;Database=elsa;Username=postgres;Password=postgres";

    [ManifestSetting(
        DisplayName = "Connection string",
        Description = "PostgreSQL connection string used by the single Groundwork document store backing every Elsa lane.",
        Category = "Persistence",
        Secret = true)]
    public string? ConnectionString { get; set; }

    [ManifestSetting(
        DisplayName = "Auto-apply schema on startup",
        Description = "When enabled, safe pending schema operations are applied automatically at startup instead of requiring Groundwork.Tool. Destructive operations are never auto-applied.",
        Category = "Persistence")]
    public bool AutoApplySchemaOnStartup { get; set; } = true;

    public void ConfigureServices(IServiceCollection services)
    {
        var connectionString = string.IsNullOrWhiteSpace(ConnectionString) ? DefaultConnectionString : ConnectionString;
        services.AddGroundworkPostgreSqlUnifiedPersistence(connectionString, AutoApplySchemaOnStartup);
    }
}
