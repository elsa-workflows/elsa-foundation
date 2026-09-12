using CShells.Features;
using Elsa.Diagnostics.StructuredLogs.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Diagnostics.StructuredLogs.Persistence.EntityFrameworkCore;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Diagnostics")]
[ManifestFeatureCategory("Persistence")]
[ShellFeature(
    name: "DiagnosticsStructuredLogsEntityFrameworkCore",
    DisplayName = "Diagnostics Structured Logs Entity Framework Core Persistence",
    Description = "Opt-in EF Core persistence for Structured Logs. It does not provision schema or apply migrations; Groundwork remains the default.",
    DependsOn = new object[] { "DiagnosticsStructuredLogs" })]
public class StructuredLogsEntityFrameworkCoreFeature : IShellFeature
{
    [ManifestSetting(
        DisplayName = "Provider",
        Description = "Relational provider for Structured Logs: Sqlite, SqlServer, PostgreSql, or MySql.",
        Category = "Persistence")]
    public string Provider { get; set; } = "Sqlite";

    [ManifestSetting(
        DisplayName = "Connection string",
        Description = "Optional explicit connection string. When omitted, ConnectionName or ConnectionStrings:ElsaStructuredLogs is used.",
        Category = "Persistence",
        Secret = true)]
    public string? ConnectionString { get; set; }

    [ManifestSetting(
        DisplayName = "Connection name",
        Description = "Named connection under ConnectionStrings when ConnectionString is omitted.",
        Category = "Persistence")]
    public string? ConnectionName { get; set; }

    public void ConfigureServices(IServiceCollection services) =>
        services.AddStructuredLogsEntityFrameworkCore(new StructuredLogsEntityFrameworkCoreOptions
        {
            Provider = Provider,
            ConnectionString = ConnectionString,
            ConnectionName = ConnectionName
        });
}
