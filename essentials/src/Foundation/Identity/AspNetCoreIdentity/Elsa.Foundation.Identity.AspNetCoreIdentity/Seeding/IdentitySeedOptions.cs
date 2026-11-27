namespace Elsa.Foundation.Identity.AspNetCoreIdentity.Seeding;

/// <summary>
/// The administrator account a concrete ASP.NET Core Identity persistence provider can ensure at startup.
/// Credentials are supplied from configuration; there are no code-level credential defaults.
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
    /// When <c>true</c> the seed is the well-known development/demo admin and startup logging may include
    /// credentials with an explicit caveat. Production seed options must keep this <c>false</c>.
    /// </summary>
    public bool IsDevelopmentSeed { get; set; }
}
