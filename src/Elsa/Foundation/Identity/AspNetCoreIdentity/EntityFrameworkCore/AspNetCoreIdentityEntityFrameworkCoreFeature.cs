using Elsa.Persistence.EntityFramework;
using CShells.Features;
using Elsa.Foundation.Identity.AspNetCoreIdentity.EntityFrameworkCore.DependencyInjection;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Seeding;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Foundation.Identity.AspNetCoreIdentity.EntityFrameworkCore;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Identity")]
[ManifestFeatureCategory("Security")]
[ShellFeature(
    name: "FoundationIdentityAspNetCoreIdentityEntityFrameworkCore",
    DisplayName = "Foundation Identity ASP.NET Core Identity Entity Framework Core",
    Description = "Opt-in ASP.NET Core Identity stores over the shared Foundation Identity EF authority. It does not add IdentityDbContext or a separate framework schema.")]
public class AspNetCoreIdentityEntityFrameworkCoreFeature : IShellFeature
{
    [ManifestSetting(DisplayName = "Provider", Description = "Relational provider for the shared Identity IAM context: Sqlite, SqlServer, PostgreSql, or MySql.", Category = "Persistence")]
    public string Provider { get; set; } = "Sqlite";

    [ManifestSetting(DisplayName = "Connection string", Description = "Optional explicit connection string. When omitted, ConnectionName or ConnectionStrings:ElsaIdentity is used.", Category = "Persistence", Secret = true)]
    public string? ConnectionString { get; set; }

    [ManifestSetting(DisplayName = "Connection name", Description = "Named connection under ConnectionStrings when ConnectionString is omitted.", Category = "Persistence")]
    public string? ConnectionName { get; set; }

    [ManifestSetting(DisplayName = "Development or demo", Description = "Relaxes cookie transport to support local HTTP. Credentials remain explicitly configured.", Category = "Identity", DefaultValue = "false")]
    public bool IsDevelopmentOrDemo { get; set; }

    [ManifestSetting(DisplayName = "Seed admin username", Description = "Provisions this administrator at startup. Requires a password.", Category = "Identity")]
    public string? SeedAdminUserName { get; set; }

    [ManifestSetting(DisplayName = "Seed admin password", Description = "Password for the seeded administrator. Supply through a secret outside development.", Category = "Identity", Secret = true)]
    public string? SeedAdminPassword { get; set; }

    [ManifestSetting(DisplayName = "Seed admin email", Description = "Optional email for the seeded administrator.", Category = "Identity")]
    public string? SeedAdminEmail { get; set; }

    [ManifestSetting(DisplayName = "Seed admin role", Description = "Role granted to the seeded administrator.", Category = "Identity")]
    public string? SeedAdminRoleName { get; set; }

    public virtual void ConfigureServices(IServiceCollection services) =>
        services.AddFoundationAspNetCoreIdentityEntityFrameworkCore(
            new IdentityIamEntityFrameworkCoreOptions
            {
                Provider = Provider,
                ConnectionString = ConnectionString,
                ConnectionName = ConnectionName
            },
            BuildInitialAdmin(),
            IsDevelopmentOrDemo)
        .AddEfModuleMigrations<Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.IdentityIamDbContext>(Provider);

    private IdentitySeedOptions? BuildInitialAdmin()
    {
        var hasUserName = !string.IsNullOrWhiteSpace(SeedAdminUserName);
        var hasPassword = !string.IsNullOrWhiteSpace(SeedAdminPassword);
        if (!hasUserName && !hasPassword)
            return null;
        // Say which half is missing: this is the error an operator meets when a production overlay blanks the
        // seed password, so the failure names the missing setting.
        if (!hasPassword)
            throw new InvalidOperationException(
                "FoundationIdentityAspNetCoreIdentityEntityFrameworkCore:SeedAdminUserName is configured but SeedAdminPassword is not. " +
                "Supply the password (via committed config for development/demo, or a secret otherwise), or clear SeedAdminUserName to seed no admin.");
        if (!hasUserName)
            throw new InvalidOperationException(
                "FoundationIdentityAspNetCoreIdentityEntityFrameworkCore:SeedAdminPassword is configured but SeedAdminUserName is not. " +
                "Supply the user name, or clear SeedAdminPassword to seed no admin.");
        return new IdentitySeedOptions
        {
            UserName = SeedAdminUserName!,
            Password = SeedAdminPassword!,
            Email = string.IsNullOrWhiteSpace(SeedAdminEmail) ? $"{SeedAdminUserName}@elsa.local" : SeedAdminEmail!,
            RoleName = string.IsNullOrWhiteSpace(SeedAdminRoleName) ? IdentitySeedOptions.DefaultRoleName : SeedAdminRoleName!,
            IsDevelopmentSeed = IsDevelopmentOrDemo
        };
    }
}
