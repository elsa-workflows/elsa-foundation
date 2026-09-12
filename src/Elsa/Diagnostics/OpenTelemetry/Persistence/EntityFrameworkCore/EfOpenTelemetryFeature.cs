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
    Description = "Opt-in EF Core persistence for OpenTelemetry. It does not provision schema or apply migrations; Groundwork remains the default.",
    DependsOn = new object[] { "DiagnosticsOpenTelemetry" })]
public class EfOpenTelemetryFeature : IShellFeature
{
    [ManifestSetting(DisplayName = "Provider", Description = "Relational provider: Sqlite, SqlServer, PostgreSql, or MySql.", Category = "Persistence")]
    public string Provider { get; set; } = "Sqlite";
    [ManifestSetting(DisplayName = "Connection string", Description = "Optional explicit connection string.", Category = "Persistence", Secret = true)]
    public string? ConnectionString { get; set; }
    [ManifestSetting(DisplayName = "Connection name", Description = "Named connection under ConnectionStrings.", Category = "Persistence")]
    public string? ConnectionName { get; set; }
    public string TenantId { get; set; } = "default";
    public string ScopeId { get; set; } = "default";
    public string SourceId { get; set; } = "opentelemetry";

    public void ConfigureServices(IServiceCollection services) => services.AddOpenTelemetryEntityFrameworkCore(new EfOpenTelemetryOptions
    {
        Provider = Provider,
        ConnectionString = ConnectionString,
        ConnectionName = ConnectionName,
        TenantId = TenantId,
        ScopeId = ScopeId,
        SourceId = SourceId
    });
}
