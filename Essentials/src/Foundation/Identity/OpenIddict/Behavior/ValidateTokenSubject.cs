using Elsa.Foundation.Identity.Core.Authentication;
using Elsa.Foundation.Identity.Core.Authorization;
using OpenIddict.Abstractions;
using OpenIddict.Validation;

namespace Elsa.Foundation.Identity.OpenIddict;

/// <summary>Rejects legacy/unscoped first-party credentials before they reach any API consumer.</summary>
internal sealed class ValidateTokenSubject(ITokenSubjectResolver subjects, IAuthenticationSessionStore? sessions = null, ICanonicalUserSignInService? canonical = null)
    : IOpenIddictValidationHandler<OpenIddictValidationEvents.ValidateTokenContext>
{
    public async ValueTask HandleAsync(OpenIddictValidationEvents.ValidateTokenContext context)
    {
        var principal = context.Principal;
        var keys = principal?.FindAll(IdentityClaimTypes.TokenSubjectKey).ToArray();
        if (principal is null || keys is not { Length: 1 } ||
            !subjects.TryResolve(principal, out var subject) ||
            !string.Equals(keys[0].Value, TokenSubjectKey.Create(subject), StringComparison.Ordinal))
        { context.Reject(OpenIddictConstants.Errors.InvalidToken, "The token identity scope is invalid."); return; }
        var ids = principal.FindAll(IdentityClaimTypes.SessionId).ToArray();
        try
        {
            if (ids.Length > 1 || !(await TokenSessionGuard.ValidateAsync(subject, ids.SingleOrDefault()?.Value, sessions, canonical, context.CancellationToken)).Allowed)
                context.Reject(OpenIddictConstants.Errors.InvalidToken, "The authentication session is unavailable.");
        }
        catch (Exception exception) when (!(exception is OperationCanceledException && context.CancellationToken.IsCancellationRequested))
        { context.Reject(OpenIddictConstants.Errors.InvalidToken, "The authentication session is unavailable."); }
    }
}
