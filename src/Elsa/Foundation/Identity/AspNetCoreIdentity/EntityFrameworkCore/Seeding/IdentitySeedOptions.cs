namespace Elsa.Foundation.Identity.AspNetCoreIdentity.EntityFrameworkCore.Seeding;

/// <summary>
/// The administrator account <see cref="IdentitySeeder"/> ensures at startup. Defaults reproduce the
/// well-known development/demo admin; a durable-store deployment overrides <see cref="UserName"/>/
/// <see cref="Password"/>/<see cref="Email"/> to provision its own first administrator.
/// </summary>
public sealed class IdentitySeedOptions
{
    public string UserName { get; set; } = IdentitySeeder.AdminUserName;

    public string Password { get; set; } = IdentitySeeder.AdminPassword;

    public string Email { get; set; } = IdentitySeeder.AdminEmail;

    public string RoleName { get; set; } = IdentitySeeder.AdminRoleName;

    /// <summary>
    /// When <c>true</c> the seed is the well-known development/demo admin: the startup log includes the
    /// credentials and the "development/demo only" caveat. A configured production admin sets this to
    /// <c>false</c> so the password is never written to the log.
    /// </summary>
    public bool IsDevelopmentSeed { get; set; }
}
