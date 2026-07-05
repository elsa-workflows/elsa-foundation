namespace Elsa.Foundation.Identity.AspNetCoreIdentity.EntityFrameworkCore.Entities;

/// <summary>
/// Persists an Elsa tenant-membership record. ASP.NET Core Identity has no native tenant-membership table,
/// so this dedicated entity backs <c>ITenantMembershipStore</c>. Role ids and direct permissions are stored
/// as newline-separated values.
/// </summary>
public sealed class TenantMembershipEntity
{
    public string Id { get; set; } = default!;

    public string TenantId { get; set; } = default!;

    public string UserId { get; set; } = default!;

    public int Status { get; set; }

    public string RoleIds { get; set; } = string.Empty;

    public string DirectPermissions { get; set; } = string.Empty;
}
