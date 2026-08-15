using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace Elsa.Foundation.Identity.Abstractions.Authorization;

public sealed class NormalizedPrincipalValidator(IOptions<FoundationIdentityOptions> options)
{
    public bool TryGetNormalizedPrincipal(ClaimsPrincipal principal, out ClaimsPrincipal normalizedPrincipal)
    {
        var trustedTypes = options.Value.NormalizedAuthenticationTypes;
        var identities = principal.Identities
            .Where(identity => identity.IsAuthenticated)
            .Where(identity => identity.AuthenticationType is not null &&
                               trustedTypes.Any(type => string.Equals(type, identity.AuthenticationType, StringComparison.Ordinal)))
            .Where(HasExactlyOneValidMarker)
            .ToArray();

        if (identities.Length != 1)
        {
            normalizedPrincipal = new ClaimsPrincipal();
            return false;
        }

        var identity = identities[0];
        normalizedPrincipal = new ClaimsPrincipal(new ClaimsIdentity(
            identity.Claims,
            identity.AuthenticationType,
            identity.NameClaimType,
            identity.RoleClaimType));
        return true;
    }

    private static bool HasExactlyOneValidMarker(ClaimsIdentity identity)
    {
        var markers = identity.Claims
            .Where(claim => claim.Type == IdentityClaimTypes.Normalized)
            .ToArray();
        return markers.Length == 1 && string.Equals(markers[0].Value, "v1", StringComparison.Ordinal);
    }
}

internal sealed class NormalizedPermissionPrincipalHandler(NormalizedPrincipalValidator validator)
    : AuthorizationHandler<NormalizedPermissionPrincipalRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        NormalizedPermissionPrincipalRequirement requirement)
    {
        if (validator.TryGetNormalizedPrincipal(context.User, out _))
            context.Succeed(requirement);

        return Task.CompletedTask;
    }
}
