using CShells.Features;
using Elsa.Foundation.Identity.AspNetCoreIdentity.EntityFrameworkCore.Extensions;
using Elsa.Foundation.Identity.AspNetCoreIdentity.EntityFrameworkCore.Seeding;
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

    [ManifestSetting(DisplayName = "Seed admin username", Description = "Provisions this administrator on the durable store when development/demo is off. Requires a password.", Category = "Identity")]
    public string? SeedAdminUserName { get; set; }

    [ManifestSetting(DisplayName = "Seed admin password", Description = "Password for the seeded durable-store administrator. Supply via a secret (user-secrets/environment variable), never in committed config.", Category = "Identity", Secret = true)]
    public string? SeedAdminPassword { get; set; }

    [ManifestSetting(DisplayName = "Seed admin email", Description = "Email for the seeded durable-store administrator. Defaults to <username>@elsa.local.", Category = "Identity")]
    public string? SeedAdminEmail { get; set; }

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

    // A durable-store admin is seeded only when both a username and password are configured; the dev/demo
    // path ignores this and seeds its own well-known admin instead. A half-configured admin (one of the two
    // present) is a deployment error — fail fast rather than silently booting with no way to sign in.
    private IdentitySeedOptions? BuildInitialAdmin()
    {
        var hasUserName = !string.IsNullOrWhiteSpace(SeedAdminUserName);
        var hasPassword = !string.IsNullOrWhiteSpace(SeedAdminPassword);

        if (!hasUserName && !hasPassword)
            return null;

        if (!hasPassword)
            throw new InvalidOperationException(
                "FoundationIdentityAspNetCoreIdentityEntityFrameworkCore:SeedAdminUserName is configured but SeedAdminPassword is not. " +
                "Supply the password via a secret (user-secrets or environment variable), or clear SeedAdminUserName to seed no admin.");

        if (!hasUserName)
            throw new InvalidOperationException(
                "FoundationIdentityAspNetCoreIdentityEntityFrameworkCore:SeedAdminPassword is configured but SeedAdminUserName is not. " +
                "Set SeedAdminUserName, or clear SeedAdminPassword to seed no admin.");

        return new IdentitySeedOptions
        {
            UserName = SeedAdminUserName,
            Password = SeedAdminPassword,
            Email = string.IsNullOrWhiteSpace(SeedAdminEmail) ? $"{SeedAdminUserName}@elsa.local" : SeedAdminEmail,
            IsDevelopmentSeed = false
        };
    }
}
