using Elsa.Foundation.Identity.AspNetCoreIdentity.EntityFrameworkCore.Seeding;

namespace Elsa.Foundation.Identity.Tests;

/// <summary>
/// Well-known seed administrator used by the identity integration tests. Production code no longer carries any
/// credential constants — the seed account comes entirely from configuration — so the tests own their own
/// values here and feed them to the seeder through <see cref="SeedOptions"/>.
/// </summary>
internal static class TestAdmin
{
    public const string UserName = "admin";
    public const string Password = "Password123!";
    public const string Email = "admin@elsa.local";
    public const string RoleName = "administrator";

    /// <summary>The seed options a dev/demo test host passes to the identity EF Core registration.</summary>
    public static IdentitySeedOptions SeedOptions() => new()
    {
        UserName = UserName,
        Password = Password,
        Email = Email,
        RoleName = RoleName,
        IsDevelopmentSeed = true
    };
}
