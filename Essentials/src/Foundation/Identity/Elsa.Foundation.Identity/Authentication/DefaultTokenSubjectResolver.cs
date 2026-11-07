using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;
using Elsa.Foundation.Identity.Core.Authentication;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Foundation.Identity.Authorization;

namespace Elsa.Foundation.Identity.Authentication;

internal sealed class DefaultTokenSubjectResolver(NormalizedPrincipalValidator validator) : ITokenSubjectResolver
{
    public bool TryResolve(ClaimsPrincipal principal, [NotNullWhen(true)] out TokenSubject? subject)
    {
        subject = null;
        if (!validator.TryGetNormalizedPrincipal(principal, out principal) ||
            !TrySingle(principal, IdentityClaimTypes.TenantId, out var tenant) ||
            !TryOptional(principal, ClaimTypes.NameIdentifier, out var nameIdentifier) ||
            !TryOptional(principal, "sub", out var sub) ||
            !TryOptional(principal, IdentityClaimTypes.UserId, out var userId) ||
            nameIdentifier is not null && sub is not null && nameIdentifier != sub)
            return false;

        var identifier = nameIdentifier ?? sub;
        if (identifier is null)
            return false;

        if (userId is not null)
        {
            if (userId != identifier)
                return false;
            subject = new TokenSubject(tenant, userId);
            return true;
        }

        // External adapters stamp these only after validating the upstream token. Requiring both also
        // prevents old unscoped principals from being silently treated as local users.
        if (!TrySingle(principal, IdentityClaimTypes.SubjectProvider, out var provider) ||
            !TrySingle(principal, IdentityClaimTypes.SubjectIssuer, out var issuer) ||
            !TryOptional(principal, IdentityClaimTypes.ExternalSubject, out var externalSubject))
            return false;

        subject = new TokenSubject(tenant, externalSubject ?? identifier, provider, issuer);
        return true;
    }

    private static bool TrySingle(ClaimsPrincipal principal, string type, [NotNullWhen(true)] out string? value) =>
        TryOptional(principal, type, out value) && value is not null;

    private static bool TryOptional(ClaimsPrincipal principal, string type, out string? value)
    {
        var claims = principal.Claims.Where(claim => string.Equals(claim.Type, type, StringComparison.OrdinalIgnoreCase)).ToArray();
        value = null;
        if (claims.Length == 0)
            return true;
        if (claims.Length != 1 || claims[0].Type != type || string.IsNullOrWhiteSpace(claims[0].Value))
            return false;
        value = claims[0].Value;
        return true;
    }
}
