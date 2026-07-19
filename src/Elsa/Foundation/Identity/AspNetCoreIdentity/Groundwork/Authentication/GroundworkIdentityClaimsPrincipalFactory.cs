using System.Security.Claims;
using Elsa.Foundation.Identity.Abstractions.Iam;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Models;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Authentication;

/// <summary>Adds the persisted security stamp required by the Groundwork cookie replay validator.</summary>
public sealed class GroundworkIdentityClaimsPrincipalFactory(
    IUserStore users,
    IRoleStore roles,
    ITenantMembershipStore memberships,
    IOptions<IdentityOptions> identityOptions)
    : AspNetCoreIdentityUserClaimsPrincipalFactory(users, roles, memberships)
{
    public override async Task<ClaimsPrincipal> CreateAsync(AspNetCoreIdentityUser user)
    {
        var principal = await base.CreateAsync(user);
        if (principal.Identity is ClaimsIdentity identity && !string.IsNullOrWhiteSpace(user.SecurityStamp))
        {
            identity.AddClaim(new Claim(
                identityOptions.Value.ClaimsIdentity.SecurityStampClaimType,
                user.SecurityStamp));
        }

        return principal;
    }
}
