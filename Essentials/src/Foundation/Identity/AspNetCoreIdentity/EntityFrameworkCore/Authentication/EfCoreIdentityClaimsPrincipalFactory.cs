using System.Security.Claims;
using Elsa.Foundation.Identity.Core.Iam;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Models;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Elsa.Foundation.Identity.AspNetCoreIdentity.EntityFrameworkCore.Authentication;

/// <summary>Adds the persisted security stamp to the first-party cookie principal.</summary>
public sealed class EfCoreIdentityClaimsPrincipalFactory(
    IUserStore users,
    IRoleStore roles,
    ITenantMembershipStore memberships,
    IOptions<IdentityOptions> identityOptions) : AspNetCoreIdentityUserClaimsPrincipalFactory(users, roles, memberships)
{
    public override async Task<ClaimsPrincipal> CreateAsync(AspNetCoreIdentityUser user)
    {
        var principal = await base.CreateAsync(user);
        if (principal.Identity is ClaimsIdentity identity && !string.IsNullOrWhiteSpace(user.SecurityStamp))
            identity.AddClaim(new Claim(identityOptions.Value.ClaimsIdentity.SecurityStampClaimType, user.SecurityStamp));
        return principal;
    }
}
