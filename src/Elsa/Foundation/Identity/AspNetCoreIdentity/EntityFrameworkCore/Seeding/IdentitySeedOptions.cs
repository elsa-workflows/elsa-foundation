namespace Elsa.Foundation.Identity.AspNetCoreIdentity.EntityFrameworkCore.Seeding;

/// <summary>
/// The administrator account <see cref="IdentitySeeder"/> ensures at startup. The username, password and
/// email are supplied from configuration (the feature's <c>SeedAdmin*</c> settings) — there are no code-level
/// credential defaults. <see cref="RoleName"/> keeps a structural fallback that the configuration overrides.
/// </summary>
public sealed class IdentitySeedOptions
{
    /// <summary>Role granted to the seeded administrator when no role name is configured.</summary>
    public const string DefaultRoleName = "administrator";

    public string UserName { get; set; } = "";

    public string Password { get; set; } = "";

    public string Email { get; set; } = "";

    public string RoleName { get; set; } = DefaultRoleName;

    /// <summary>
    /// When <c>true</c> the seed is the well-known development/demo admin: the startup log includes the
    /// credentials and the "development/demo only" caveat. A configured production admin sets this to
    /// <c>false</c> so the password is never written to the log.
    /// </summary>
    public bool IsDevelopmentSeed { get; set; }
}
