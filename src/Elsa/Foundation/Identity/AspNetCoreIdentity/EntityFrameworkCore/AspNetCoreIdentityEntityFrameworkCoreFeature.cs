using CShells.Features;
using Elsa.Foundation.Identity.AspNetCoreIdentity.EntityFrameworkCore.Extensions;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Seeding;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Foundation.Identity.AspNetCoreIdentity.EntityFrameworkCore;

/// <summary>
/// Replaces the in-memory ASP.NET Core Identity stores with a durable EF Core store over
/// <see cref="ApplicationIdentityDbContext"/> and wires ASP.NET Core Identity core + cookie sign-in. When
/// <see cref="IsDevelopmentOrDemo"/> is set, an EF in-memory database is used and an admin account is seeded.
/// </summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Identity")]
[ManifestFeatureCategory("Security")]
[ManifestFeatureCategory("Persistence")]
[ShellFeature(
    name: "FoundationIdentityAspNetCoreIdentityEntityFrameworkCore",
    DisplayName = "Foundation Identity ASP.NET Core Identity EF Core",
    Description = "Durable EF Core persistence and SignInManager-backed cookie sign-in for the ASP.NET Core Identity provider."
)]
public sealed class AspNetCoreIdentityEntityFrameworkCoreFeature : IShellFeature
{
    [ManifestSetting(DisplayName = "Development or demo", Description = "Uses an in-memory database and seeds an admin account for a fresh checkout.", Category = "Identity", DefaultValue = "false")]
    public bool IsDevelopmentOrDemo { get; set; }

    [ManifestSetting(DisplayName = "Connection string", Description = "Sqlite connection string for the identity database.", Category = "Persistence", Secret = true)]
    public string? ConnectionString { get; set; }

    [ManifestSetting(DisplayName = "Seed admin username", Description = "Provisions this administrator at startup. Requires a password.", Category = "Identity")]
    public string? SeedAdminUserName { get; set; }

    [ManifestSetting(DisplayName = "Seed admin password", Description = "Password for the seeded administrator. Outside development/demo, supply via a secret (user-secrets/environment variable), never in committed config.", Category = "Identity", Secret = true)]
    public string? SeedAdminPassword { get; set; }

    [ManifestSetting(DisplayName = "Seed admin email", Description = "Email for the seeded administrator. Defaults to <username>@elsa.local.", Category = "Identity")]
    public string? SeedAdminEmail { get; set; }

    [ManifestSetting(DisplayName = "Seed admin role", Description = "Role granted to the seeded administrator. Defaults to 'administrator'.", Category = "Identity")]
    public string? SeedAdminRoleName { get; set; }

    public void ConfigureServices(IServiceCollection services)
    {
        var connectionString = ConnectionString;
        var isDev = IsDevelopmentOrDemo;

        services.AddFoundationAspNetCoreIdentityEntityFrameworkCore(
            isDevelopmentOrDemo: isDev,
            configureDbContext: string.IsNullOrWhiteSpace(connectionString)
                ? null
                : builder => builder.UseSqlite(connectionString, sqlite =>
                    sqlite.MigrationsAssembly(typeof(ApplicationIdentityDbContext).Assembly.GetName().Name)),
            initialAdmin: BuildInitialAdmin());
    }

    // The admin is seeded — on both the dev/demo and durable-store paths — only when both a username and
    // password are configured. A half-configured admin (one of the two present) is a deployment error: fail
    // fast rather than silently booting with no way to sign in. The credentials come entirely from the
    // SeedAdmin* settings; the dev/demo path supplies its well-known values through committed config.
    private IdentitySeedOptions? BuildInitialAdmin()
    {
        var hasUserName = !string.IsNullOrWhiteSpace(SeedAdminUserName);
        var hasPassword = !string.IsNullOrWhiteSpace(SeedAdminPassword);

        if (!hasUserName && !hasPassword)
            return null;

        if (!hasPassword)
            throw new InvalidOperationException(
                "FoundationIdentityAspNetCoreIdentityEntityFrameworkCore:SeedAdminUserName is configured but SeedAdminPassword is not. " +
                "Supply the password (via committed config for development/demo, or a secret otherwise), or clear SeedAdminUserName to seed no admin.");

        if (!hasUserName)
            throw new InvalidOperationException(
                "FoundationIdentityAspNetCoreIdentityEntityFrameworkCore:SeedAdminPassword is configured but SeedAdminUserName is not. " +
                "Set SeedAdminUserName, or clear SeedAdminPassword to seed no admin.");

        return new IdentitySeedOptions
        {
            // Both are non-empty here (validated above); the compiler cannot narrow through the local flags.
            UserName = SeedAdminUserName!,
            Password = SeedAdminPassword!,
            Email = string.IsNullOrWhiteSpace(SeedAdminEmail) ? $"{SeedAdminUserName}@elsa.local" : SeedAdminEmail,
            RoleName = string.IsNullOrWhiteSpace(SeedAdminRoleName) ? IdentitySeedOptions.DefaultRoleName : SeedAdminRoleName,
            // Only the well-known dev/demo seed echoes its password to the startup log.
            IsDevelopmentSeed = IsDevelopmentOrDemo
        };
    }
}
