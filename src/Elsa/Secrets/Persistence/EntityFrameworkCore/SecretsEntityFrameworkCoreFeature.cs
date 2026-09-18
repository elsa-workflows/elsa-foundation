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
    Description = "EF Core persistence for the secrets repository."
)]
public class SecretsEntityFrameworkCoreFeature : IShellFeature
{
    [ManifestSetting(
        DisplayName = "Provider",
        Description = "Relational provider for the derived Secrets DbContext: Sqlite, SqlServer, PostgreSql, or MySql. The host must reference that provider package.",
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
            Schema = Schema,
            Pooling = Pooling,
            MigratePolicy = MigratePolicy
        });
}
