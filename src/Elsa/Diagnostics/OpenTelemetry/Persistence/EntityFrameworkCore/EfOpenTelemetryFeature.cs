using Elsa.Persistence.EntityFramework;
using CShells.Features;
using Elsa.Diagnostics.OpenTelemetry.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Diagnostics.OpenTelemetry.Persistence.EntityFrameworkCore;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Diagnostics")]
[ManifestFeatureCategory("Persistence")]
[ShellFeature(
    name: "DiagnosticsOpenTelemetryEntityFrameworkCore",
    DisplayName = "Diagnostics OpenTelemetry Entity Framework Core Persistence",
    Description = "Opt-in EF Core persistence for OpenTelemetry. It applies or validates its own migrations on shell activation;",
    DependsOn = new object[] { "DiagnosticsOpenTelemetry" })]
public class EfOpenTelemetryFeature : IShellFeature
{
    [ManifestSetting(DisplayName = "Provider", Description = "Relational provider: Sqlite, SqlServer, PostgreSql, or MySql.", Category = "Persistence")]
    public string Provider { get; set; } = "Sqlite";
    [ManifestSetting(DisplayName = "Connection string", Description = "Optional explicit connection string. When omitted, ConnectionName or ConnectionStrings:ElsaOpenTelemetry is used — this module does not fall back to the shared Elsa connection every other module defaults to, so a host that keeps telemetry in the shared database sets ConnectionName to Elsa. Sqlite defaults to Data Source=elsa-opentelemetry.db.", Category = "Persistence", Secret = true)]
    public string? ConnectionString { get; set; }
    [ManifestSetting(DisplayName = "Connection name", Description = "Named connection under ConnectionStrings when ConnectionString is omitted. Defaults to ElsaOpenTelemetry rather than the shared Elsa.", Category = "Persistence")]
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
    public string TenantId { get; set; } = "default";
    public string ScopeId { get; set; } = "default";
    public string SourceId { get; set; } = "opentelemetry";

    public void ConfigureServices(IServiceCollection services) => services.AddOpenTelemetryEntityFrameworkCore(new EfOpenTelemetryOptions
    {
        Provider = Provider,
        ConnectionString = ConnectionString,
        ConnectionName = ConnectionName,
        Schema = Schema,
        Pooling = Pooling,
        TenantId = TenantId,
        ScopeId = ScopeId,
        SourceId = SourceId
    })
        .AddEfModuleMigrations<OpenTelemetryDbContext>(Provider);
}
