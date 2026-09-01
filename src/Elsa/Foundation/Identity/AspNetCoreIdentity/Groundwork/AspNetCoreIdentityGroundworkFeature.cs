using CShells.Features;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.DependencyInjection;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Seeding;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Identity")]
[ManifestFeatureCategory("Security")]
[ShellFeature(
    name: "FoundationIdentityAspNetCoreIdentityGroundwork",
    DisplayName = "Foundation Identity ASP.NET Core Identity Groundwork",
    Description = "Registers the Groundwork-backed ASP.NET Core Identity stores and Elsa IAM adapters.")]
public class AspNetCoreIdentityGroundworkFeature : IShellFeature
{
    [ManifestSetting(DisplayName = "Development or demo", Description = "Seeds an admin account for a fresh checkout when credentials are configured.", Category = "Identity", DefaultValue = "false")]
    public bool IsDevelopmentOrDemo { get; set; }

    [ManifestSetting(DisplayName = "Seed admin username", Description = "Provisions this administrator at startup. Requires a password.", Category = "Identity")]
    public string? SeedAdminUserName { get; set; }

    [ManifestSetting(DisplayName = "Seed admin password", Description = "Password for the seeded administrator. Outside development/demo, supply via a secret (user-secrets/environment variable), never in committed config.", Category = "Identity", Secret = true)]
    public string? SeedAdminPassword { get; set; }

    [ManifestSetting(DisplayName = "Seed admin email", Description = "Email for the seeded administrator. Defaults to <username>@elsa.local.", Category = "Identity")]
    public string? SeedAdminEmail { get; set; }

    [ManifestSetting(DisplayName = "Seed admin role", Description = "Role granted to the seeded administrator. Defaults to 'administrator'.", Category = "Identity")]
    public string? SeedAdminRoleName { get; set; }

    public virtual void ConfigureServices(IServiceCollection services) =>
        services.AddFoundationAspNetCoreIdentityGroundwork(BuildInitialAdmin(), IsDevelopmentOrDemo);

    // A half-configured admin is a deployment error. The dev/demo path supplies its well-known values
    // through committed config, while production credentials can be supplied through a secret overlay.
    private IdentitySeedOptions? BuildInitialAdmin()
    {
        var hasUserName = !string.IsNullOrWhiteSpace(SeedAdminUserName);
        var hasPassword = !string.IsNullOrWhiteSpace(SeedAdminPassword);

        if (!hasUserName && !hasPassword)
            return null;

        if (!hasPassword)
            throw new InvalidOperationException(
                "FoundationIdentityAspNetCoreIdentityGroundwork:SeedAdminUserName is configured but SeedAdminPassword is not. " +
                "Supply the password (via committed config for development/demo, or a secret otherwise), or clear SeedAdminUserName to seed no admin.");

        if (!hasUserName)
            throw new InvalidOperationException(
                "FoundationIdentityAspNetCoreIdentityGroundwork:SeedAdminPassword is configured but SeedAdminUserName is not. " +
                "Set SeedAdminUserName, or clear SeedAdminPassword to seed no admin.");

        return new IdentitySeedOptions
        {
            UserName = SeedAdminUserName!,
            Password = SeedAdminPassword!,
            Email = string.IsNullOrWhiteSpace(SeedAdminEmail) ? $"{SeedAdminUserName}@elsa.local" : SeedAdminEmail,
            RoleName = string.IsNullOrWhiteSpace(SeedAdminRoleName) ? IdentitySeedOptions.DefaultRoleName : SeedAdminRoleName,
            IsDevelopmentSeed = IsDevelopmentOrDemo
        };
    }
}
