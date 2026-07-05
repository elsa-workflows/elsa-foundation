namespace Elsa.Foundation.Identity.AspNetCoreIdentity.EntityFrameworkCore;

/// <summary>
/// Claim types persisted against ASP.NET Core Identity user/role claim tables to carry Elsa's
/// permission model. Roles and users store permissions as claims of type <see cref="Permission"/>; the EF
/// store adapters read them back into <c>RoleRecord.Permissions</c> / <c>UserRecord.DirectPermissions</c>.
/// </summary>
internal static class IdentityStoreClaimTypes
{
    internal const string Permission = "elsa:permission";
}
