using CShells.Features;
using Elsa.Persistence.EntityFramework;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Elsa.Secrets.Persistence.EntityFrameworkCore.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Secrets.Persistence.EntityFrameworkCore;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Secrets")]
[ManifestFeatureCategory("Persistence")]
[ShellFeature(
    name: "SecretsEntityFrameworkCore",
    DisplayName = "Secrets Entity Framework Core Persistence",
    Description = "Opt-in EF Core replacement for the secrets repository. Groundwork remains the default; do not enable both in one shell."
)]
public class SecretsEntityFrameworkCoreFeature : IShellFeature
{
    [ManifestSetting(
        DisplayName = "Provider",
        Description = "Relational provider for the derived Secrets DbContext: Sqlite, SqlServer, or PostgreSql. The host must reference that provider package.",
        Category = "Persistence")]
    public string Provider { get; set; } = "Sqlite";

    [ManifestSetting(
        DisplayName = "Connection string",
        Description = "Optional explicit connection string. When omitted, ConnectionName or ConnectionStrings:ElsaSecrets is used. Sqlite defaults to Data Source=elsa-secrets.db.",
        Category = "Persistence",
        Secret = true)]
    public string? ConnectionString { get; set; }

    [ManifestSetting(
        DisplayName = "Connection name",
        Description = "Named connection under ConnectionStrings when ConnectionString is omitted.",
        Category = "Persistence")]
    public string? ConnectionName { get; set; }

    [ManifestSetting(
        DisplayName = "Migrate policy",
        Description = "AutoMigrate runs Database.MigrateAsync (EF 9 lock) on feature enable and CShells reload. Validate fails when migrations are pending.",
        Category = "Persistence")]
    public EfMigratePolicy MigratePolicy { get; set; } = EfMigratePolicy.AutoMigrate;

    public void ConfigureServices(IServiceCollection services) =>
        services.AddSecretsEntityFrameworkCore(new SecretsEntityFrameworkCoreOptions
        {
            Provider = Provider,
            ConnectionString = ConnectionString,
            ConnectionName = ConnectionName,
            MigratePolicy = MigratePolicy
        });
}
