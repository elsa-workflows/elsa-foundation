using System.Security.Claims;
using Elsa.Foundation.Identity.Abstractions.Iam;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Models;
using Microsoft.AspNetCore.Identity;

namespace Elsa.Foundation.Identity.AspNetCoreIdentity.Services;

/// <summary>
/// ASP.NET Core Identity's <see cref="IUserClaimsPrincipalFactory{TUser}"/> for first-party cookie sign-in.
/// Rather than emitting only the framework's default name/id claims, it projects the persisted Elsa
/// <see cref="UserRecord"/> (tenant, roles, direct and role-granted permissions, tenant membership) via the
/// shared <see cref="IdentityClaimsProjector"/>, so the cookie principal carries the same claims the OIDC
/// path produces.
/// </summary>
public sealed class AspNetCoreIdentityUserClaimsPrincipalFactory(
    IUserStore users,
    IRoleStore roles,
    ITenantMembershipStore memberships) : IUserClaimsPrincipalFactory<AspNetCoreIdentityUser>
{
    public async Task<ClaimsPrincipal> CreateAsync(AspNetCoreIdentityUser user)
    {
        var identity = new ClaimsIdentity(
            authenticationType: AspNetCoreIdentityDefaults.CookieScheme,
            nameType: ClaimTypes.Name,
            roleType: ClaimTypes.Role);

        var record = await users.FindAsync(user.TenantId, user.Id)
                     ?? new UserRecord(
                         user.Id,
                         user.TenantId,
                         user.UserName ?? user.Id,
                         user.Email,
                         user.DisplayName,
                         UserStatus.Active,
                         ResourceOwnership.Foundation,
                         new HashSet<string>(),
                         new HashSet<string>());

        await IdentityClaimsProjector.ApplyUserClaimsAsync(identity, record, roles, memberships, CancellationToken.None);

        if (!string.IsNullOrWhiteSpace(user.UserName))
            IdentityClaimsProjector.AddIfMissing(identity, ClaimTypes.Name, user.DisplayName ?? user.UserName);

        return new ClaimsPrincipal(identity);
    }
}
