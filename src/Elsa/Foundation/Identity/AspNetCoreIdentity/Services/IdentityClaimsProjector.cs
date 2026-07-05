using System.Security.Claims;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Foundation.Identity.Abstractions.Iam;

namespace Elsa.Foundation.Identity.AspNetCoreIdentity.Services;

/// <summary>
/// Shared projection of a persisted <see cref="UserRecord"/> (plus its tenant membership and role-granted
/// permissions) onto a <see cref="ClaimsIdentity"/>. Used by both the external-OIDC principal factory and
/// the first-party claims-principal factory so the two paths emit identical tenant/role/permission claims.
/// </summary>
internal static class IdentityClaimsProjector
{
    public static async Task ApplyUserClaimsAsync(
        ClaimsIdentity identity,
        UserRecord user,
        IRoleStore roles,
        ITenantMembershipStore memberships,
        CancellationToken cancellationToken)
    {
        AddIfMissing(identity, ClaimTypes.NameIdentifier, user.Id);
        AddIfMissing(identity, "sub", user.Id);
        AddIfMissing(identity, IdentityClaimTypes.TenantId, user.TenantId);

        if (!string.IsNullOrWhiteSpace(user.DisplayName))
            AddIfMissing(identity, ClaimTypes.Name, user.DisplayName);

        if (!string.IsNullOrWhiteSpace(user.Email))
            AddIfMissing(identity, ClaimTypes.Email, user.Email);

        var roleIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var role in user.RoleIds)
        {
            roleIds.Add(role);
            AddIfMissing(identity, IdentityClaimTypes.Role, role);
        }

        foreach (var permission in user.DirectPermissions)
            AddIfMissing(identity, IdentityClaimTypes.Permission, permission);

        var membership = await memberships.FindAsync(user.TenantId, user.Id, cancellationToken);
        if (membership is not null && membership.Status == TenantMembershipStatus.Active)
        {
            foreach (var role in membership.RoleIds)
            {
                roleIds.Add(role);
                AddIfMissing(identity, IdentityClaimTypes.Role, role);
            }

            foreach (var permission in membership.DirectPermissions)
                AddIfMissing(identity, IdentityClaimTypes.Permission, permission);
        }

        // Expand role-granted permissions so that permissions assigned to a role (RoleRecord.Permissions)
        // are honoured by permission-based authorization, not just permissions assigned directly to the user.
        foreach (var roleId in roleIds)
        {
            var role = await roles.FindAsync(user.TenantId, roleId, cancellationToken);
            if (role is null)
                continue;

            foreach (var permission in role.Permissions)
                AddIfMissing(identity, IdentityClaimTypes.Permission, permission);
        }
    }

    public static void AddIfMissing(ClaimsIdentity identity, string type, string value)
    {
        if (!identity.HasClaim(type, value))
            identity.AddClaim(new Claim(type, value));
    }
}
