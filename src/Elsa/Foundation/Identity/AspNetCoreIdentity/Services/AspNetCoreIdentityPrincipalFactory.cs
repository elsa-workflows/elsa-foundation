using System.Security.Claims;
using Elsa.Foundation.Identity.Abstractions.Authentication;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Foundation.Identity.Abstractions.Iam;

namespace Elsa.Foundation.Identity.AspNetCoreIdentity.Services;

public sealed class AspNetCoreIdentityPrincipalFactory(
    IUserStore users,
    IRoleStore roles,
    IExternalIdentityStore externalIdentities,
    ITenantMembershipStore memberships,
    IUserManager userManager,
    IClaimsNormalizer normalizer) : IPrincipalFactory
{
    public async ValueTask<ClaimsPrincipal> CreateAsync(PrincipalFactoryContext context, CancellationToken cancellationToken = default)
    {
        var externalIdentity = await externalIdentities.FindBySubjectAsync(context.TenantId, context.Provider, context.ProviderSubject, cancellationToken);
        var user = externalIdentity is not null
            ? await users.FindAsync(context.TenantId, externalIdentity.UserId, cancellationToken)
            : null;

        if (user is null)
        {
            user = await userManager.CreateAsync(new CreateUserRequest(
                context.TenantId,
                ResolveUserName(context.ExternalPrincipal, context.ProviderSubject),
                ResolveEmail(context.ExternalPrincipal),
                ResolveDisplayName(context.ExternalPrincipal),
                ResourceOwnership.External), cancellationToken);

            await userManager.LinkExternalIdentityAsync(new LinkExternalIdentityRequest(
                context.TenantId,
                context.Provider,
                context.ProviderSubject,
                user.Id,
                ExternalIdentityLinkPolicy.Auto,
                DuplicateEmailWasExplicitlyApproved: false), cancellationToken);
        }

        var normalized = await normalizer.NormalizeAsync(new ClaimsNormalizationContext(
            context.ExternalPrincipal,
            context.TenantId,
            context.Provider,
            context.MappingRules), cancellationToken);

        var identity = new ClaimsIdentity(normalized.Principal.Claims, "Elsa.Foundation.Identity");
        await IdentityClaimsProjector.ApplyUserClaimsAsync(identity, user, roles, memberships, cancellationToken);

        return new ClaimsPrincipal(identity);
    }

    private static string ResolveUserName(ClaimsPrincipal principal, string providerSubject) =>
        principal.FindFirst(ClaimTypes.Name)?.Value ??
        principal.FindFirst("preferred_username")?.Value ??
        principal.FindFirst(ClaimTypes.Email)?.Value ??
        providerSubject;

    private static string? ResolveEmail(ClaimsPrincipal principal) => principal.FindFirst(ClaimTypes.Email)?.Value ?? principal.FindFirst("email")?.Value;

    private static string? ResolveDisplayName(ClaimsPrincipal principal) => principal.FindFirst(ClaimTypes.Name)?.Value ?? principal.Identity?.Name;
}
