using Elsa.Persistence.EntityFramework;
using CShells.Features;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Foundation.Identity.Persistence.EntityFrameworkCore;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Identity")]
[ManifestFeatureCategory("Persistence")]
[ShellFeature(
    name: "IdentityProviderConfigurationEntityFrameworkCore",
    DisplayName = "Identity Provider Configuration Entity Framework Core Persistence",
    Description = "Opt-in EF Core persistence for tenant and global Identity provider configurations. It replaces only the provider-configuration stores; schema provisioning is deferred.")]
public class IdentityProviderConfigurationEntityFrameworkCoreFeature : IShellFeature
{
    [ManifestSetting(DisplayName = "Provider", Description = "Relational provider for provider-configuration persistence: Sqlite, SqlServer, PostgreSql, or MySql. The host must reference that provider package.", Category = "Persistence")]
    public string Provider { get; set; } = "Sqlite";

    [ManifestSetting(DisplayName = "Connection string", Description = "Optional explicit connection string. When omitted, ConnectionName or the shared ConnectionStrings:Elsa is used. Sqlite defaults to Data Source=elsa-identity.db.", Category = "Persistence", Secret = true)]
    public string? ConnectionString { get; set; }

    [ManifestSetting(DisplayName = "Connection name", Description = "Named connection under ConnectionStrings when ConnectionString is omitted.", Category = "Persistence")]
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

    public virtual void ConfigureServices(IServiceCollection services) =>
        services.AddIdentityProviderConfigurationEntityFrameworkCore(new IdentityProviderConfigurationEntityFrameworkCoreOptions
        {
            Provider = Provider,
            ConnectionString = ConnectionString,
            ConnectionName = ConnectionName, Schema = Schema, Pooling = Pooling
        })
        .AddEfModuleMigrations<IdentityProviderConfigurationDbContext>(Provider);
}
