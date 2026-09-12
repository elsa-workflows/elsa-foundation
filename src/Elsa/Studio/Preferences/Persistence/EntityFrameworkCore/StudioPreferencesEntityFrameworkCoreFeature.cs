using CShells.Features;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Elsa.Studio.Preferences.Persistence.EntityFrameworkCore.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Studio.Preferences.Persistence.EntityFrameworkCore;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Studio")]
[ManifestFeatureCategory("Persistence")]
[ShellFeature(
    name: "StudioPreferencesEntityFrameworkCore",
    DisplayName = "Studio Preferences Entity Framework Core Persistence",
    Description = "Opt-in EF Core repository binding for Studio Preferences. It does not provision schema and is non-operational on a fresh database until the deferred migration and lifecycle work lands; keep it out of production composition until then. It has no Groundwork fallback and rejects a conflicting preference backend registration.",
    DependsOn = new object[] { "StudioPreferences" })]
public sealed class StudioPreferencesEntityFrameworkCoreFeature : IShellFeature
{
    [ManifestSetting(
        DisplayName = "Provider",
        Description = "Relational provider for the Studio Preferences DbContext: Sqlite, SqlServer, PostgreSql, or MySql. The host must reference that provider package; this binding only configures the context and does not apply migrations.",
        Category = "Persistence")]
    public string Provider { get; set; } = "Sqlite";

    [ManifestSetting(
        DisplayName = "Connection string",
        Description = "Optional explicit connection string. When omitted, ConnectionName or ConnectionStrings:ElsaStudioPreferences is used. Sqlite defaults to Data Source=elsa-studio-preferences.db.",
        Category = "Persistence",
        Secret = true)]
    public string? ConnectionString { get; set; }

    [ManifestSetting(
        DisplayName = "Connection name",
        Description = "Named connection under ConnectionStrings when ConnectionString is omitted.",
        Category = "Persistence")]
    public string? ConnectionName { get; set; }

    public void ConfigureServices(IServiceCollection services) =>
        services.AddStudioPreferencesEntityFrameworkCore(new StudioPreferencesEntityFrameworkCoreOptions
        {
            Provider = Provider,
            ConnectionString = ConnectionString,
            ConnectionName = ConnectionName
        });
}
