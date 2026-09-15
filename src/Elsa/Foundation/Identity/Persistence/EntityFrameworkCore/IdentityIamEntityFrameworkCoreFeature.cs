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
    name: "IdentityIamEntityFrameworkCore",
    DisplayName = "Identity IAM Entity Framework Core Persistence",
    Description = "Opt-in EF Core persistence for the complete tenant-local Identity IAM authority, including users, roles, applications, credentials, mappings, external identities, memberships, relationships, reservations, and mutation receipts. Schema provisioning remains host-owned.")]
public class IdentityIamEntityFrameworkCoreFeature : IShellFeature
{
    [ManifestSetting(DisplayName = "Provider", Description = "Relational provider for Identity IAM persistence: Sqlite, SqlServer, PostgreSql, or MySql. The host must reference that provider package.", Category = "Persistence")]
    public string Provider { get; set; } = "Sqlite";

    [ManifestSetting(DisplayName = "Connection string", Description = "Optional explicit connection string. When omitted, ConnectionName or ConnectionStrings:ElsaIdentity is used. Sqlite defaults to Data Source=elsa-identity.db.", Category = "Persistence", Secret = true)]
    public string? ConnectionString { get; set; }

    [ManifestSetting(DisplayName = "Connection name", Description = "Named connection under ConnectionStrings when ConnectionString is omitted.", Category = "Persistence")]
    public string? ConnectionName { get; set; }

    public virtual void ConfigureServices(IServiceCollection services) =>
        services.AddIdentityIamEntityFrameworkCore(new IdentityIamEntityFrameworkCoreOptions
        {
            Provider = Provider,
            ConnectionString = ConnectionString,
            ConnectionName = ConnectionName
        })
        .AddEfModuleMigrations<IdentityIamDbContext>(Provider);
}
