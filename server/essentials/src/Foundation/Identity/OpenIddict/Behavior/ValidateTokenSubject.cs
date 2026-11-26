using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;
using Elsa.Foundation.Identity.Core.Authentication;
using Elsa.Foundation.Identity.Core.Authorization;
using OpenIddict.Abstractions;
using OpenIddict.Validation;

namespace Elsa.Foundation.Identity.OpenIddict;

/// <summary>Verifies first-party identity scope against its signed token id and persisted subject.</summary>
internal sealed class ValidateTokenSubject(ITokenSubjectResolver subjects, IOpenIddictTokenManager tokens,
    IAuthenticationSessionStore? sessions = null, ICanonicalUserSignInService? canonical = null)
    : IOpenIddictValidationHandler<OpenIddictValidationEvents.ValidateTokenContext>
{
    public async ValueTask HandleAsync(OpenIddictValidationEvents.ValidateTokenContext context)
    {
        try
        {
            var principal = context.Principal;
            if (principal is null || !TryClaim(principal, IdentityClaimTypes.TokenSubjectKey, out var declaredKey) ||
                !TryClaim(principal, OpenIddictConstants.Claims.Private.TokenId, out var entryId) || entryId is null)
            {
                Reject(context);
                return;
            }

            // Main's existing scoped user tokens have no explicit key claim. Some carry an upstream sub
            // beside the canonical user id; normalize that signed legacy projection only after proving its
            // persisted user key below. This never guesses a user from a bare upstream subject.
            if (!subjects.TryResolve(principal, out var subject))
            {
                if (declaredKey is not null || !TryLegacyUserPrincipal(principal, out principal) ||
                    !subjects.TryResolve(principal, out subject))
                {
                    Reject(context);
                    return;
                }
            }

            var key = TokenSubjectKey.Create(subject);
            if (declaredKey is null && subject.UserId is null ||
                declaredKey is not null && !string.Equals(declaredKey, key, StringComparison.Ordinal))
            {
                Reject(context);
                return;
            }

            var entry = await tokens.FindByIdAsync(entryId, context.CancellationToken);
            if (entry is null || !string.Equals(await tokens.GetSubjectAsync(entry, context.CancellationToken), key, StringComparison.Ordinal))
            {
                Reject(context);
                return;
            }

            if (!TryClaim(principal, IdentityClaimTypes.SessionId, out var sessionId) ||
                !(await TokenSessionGuard.ValidateAsync(subject, sessionId, sessions, canonical, context.CancellationToken)).Allowed)
            {
                Reject(context);
                return;
            }

            context.Principal = principal;
        }
        catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // Store and projection failures are authentication failures, never an unscoped principal.
            Reject(context);
        }
    }

    private static bool TryLegacyUserPrincipal(ClaimsPrincipal source, [NotNullWhen(true)] out ClaimsPrincipal? principal)
    {
        principal = null;
        var identities = source.Identities.ToArray();
        if (identities.Length != 1 || !TryClaim(source, IdentityClaimTypes.UserId, out var userId) || userId is null ||
            !TryClaim(source, ClaimTypes.NameIdentifier, out var nameIdentifier) ||
            !TryClaim(source, OpenIddictConstants.Claims.Subject, out var sub) ||
            nameIdentifier is null && sub is null ||
            nameIdentifier is not null && sub is not null && nameIdentifier != sub)
            return false;

        var identity = new ClaimsIdentity(identities[0]);
        foreach (var claim in identity.Claims.Where(claim => claim.Type == ClaimTypes.NameIdentifier || claim.Type == OpenIddictConstants.Claims.Subject).ToArray())
            identity.RemoveClaim(claim);
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, userId));
        identity.AddClaim(new Claim(OpenIddictConstants.Claims.Subject, userId));
        principal = new ClaimsPrincipal(identity);
        return true;
    }

    private static bool TryClaim(ClaimsPrincipal principal, string type, out string? value)
    {
        value = null;
        var claims = principal.Claims.Where(claim => string.Equals(claim.Type, type, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (claims.Length == 0)
            return true;
        if (claims.Length != 1 || claims[0].Type != type || string.IsNullOrWhiteSpace(claims[0].Value))
            return false;
        value = claims[0].Value;
        return true;
    }

    private static void Reject(OpenIddictValidationEvents.ValidateTokenContext context) =>
        context.Reject(OpenIddictConstants.Errors.InvalidToken, "The token identity scope is invalid.");
}
