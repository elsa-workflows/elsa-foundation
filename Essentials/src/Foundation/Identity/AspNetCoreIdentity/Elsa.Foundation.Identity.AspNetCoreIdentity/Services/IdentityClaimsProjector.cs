using System.Security.Claims;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Foundation.Identity.Core.Iam;

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
        // Provider-owned projection must never carry an incoming normalized marker forward. The marker is
        // trusted only when this complete projection succeeds, so emit one fresh marker at the end below.
        foreach (var marker in identity.FindAll(IdentityClaimTypes.Normalized).ToArray())
            identity.RemoveClaim(marker);

        // The canonical Elsa user replaces anything an incoming principal claimed as its identity: the user id,
        // the subject (NameIdentifier and sub) and the tenant. Appending after a provider's own NameIdentifier would
        // leave the provider's subject first, so tokens would be issued and revoked under a provider's sub (#2848).
        Replace(identity, IdentityClaimTypes.UserId, user.Id);
        Replace(identity, ClaimTypes.NameIdentifier, user.Id);
        Replace(identity, "sub", user.Id);
        Replace(identity, IdentityClaimTypes.TenantId, user.TenantId);
        // An Elsa user is keyed by its id, never by a provider's subject, so no subject provider may ride along.
        foreach (var subjectProvider in identity.FindAll(IdentityClaimTypes.SubjectProvider).ToArray())
            identity.RemoveClaim(subjectProvider);

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

        identity.AddClaim(new Claim(IdentityClaimTypes.Normalized, "v1"));
    }

    public static void AddIfMissing(ClaimsIdentity identity, string type, string value)
    {
        if (!identity.HasClaim(type, value))
            identity.AddClaim(new Claim(type, value));
    }

    /// <summary>Leaves exactly one claim of <paramref name="type"/>, carrying <paramref name="value"/>.</summary>
    private static void Replace(ClaimsIdentity identity, string type, string value)
    {
        foreach (var claim in identity.FindAll(type).ToArray())
            identity.RemoveClaim(claim);
        identity.AddClaim(new Claim(type, value));
    }
}
