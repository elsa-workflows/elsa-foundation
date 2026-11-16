using System.Security.Claims;
using Elsa.Foundation.Identity.Core.Authentication;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Foundation.Identity.Core.Iam;

namespace Elsa.Foundation.Identity.AspNetCoreIdentity.Services;

/// <summary>Projects only stored Elsa authority after admission has selected the canonical user.</summary>
public sealed class AspNetCoreIdentityPrincipalFactory(IUserStore users, IRoleStore roles, ITenantMembershipStore memberships) : IPrincipalFactory
{
    public async ValueTask<ClaimsPrincipal> CreateAsync(PrincipalFactoryContext context, CancellationToken cancellationToken = default)
    {
        var user = await users.FindAsync(context.TenantId, context.UserId, cancellationToken);
        if (user is null || user.Status != UserStatus.Active)
            throw new InvalidOperationException("The Elsa user is unavailable.");
        var membership = await memberships.FindAsync(user.TenantId, user.Id, cancellationToken);
        if (membership is not null && membership.Status != TenantMembershipStatus.Active)
            throw new InvalidOperationException("The Elsa user is unavailable.");
        var identity = new ClaimsIdentity("Elsa.Foundation.Identity");
        identity.AddClaim(new Claim(IdentityClaimTypes.Provider, context.Provider ?? AspNetCoreIdentityDefaults.ProviderId));
        await IdentityClaimsProjector.ApplyUserClaimsAsync(identity, user, roles, memberships, cancellationToken);
        return new ClaimsPrincipal(identity);
    }
}
