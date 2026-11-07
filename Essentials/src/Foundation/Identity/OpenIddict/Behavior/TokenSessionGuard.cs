using Elsa.Foundation.Identity.Core.Authentication;

namespace Elsa.Foundation.Identity.OpenIddict;

internal static class TokenSessionGuard
{
    public static async ValueTask<(bool Allowed, DateTimeOffset? ExpiresAt)> ValidateAsync(TokenSubject subject, string? id,
        IAuthenticationSessionStore? store, ICanonicalUserSignInService? canonical, CancellationToken token)
    {
        if (subject.CanonicalUserId is null) return (id is null, null);
        // Base token-only compositions do not acquire a managed session authority. A signed session ID
        // must nevertheless fail closed when its authority is removed, never become a legacy token.
        if (store is null) return (id is null, null);
        if (!store.IsDurable || canonical is null || !canonical.SupportsDurableSessions || id is not { Length: 64 } || !id.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f'))
            return (false, null);
        var session = await canonical.ReadSessionAsync(subject, id, token);
        if (session is null || session.TenantId != subject.TenantId || session.UserId != subject.CanonicalUserId ||
            session.RevokedAt is not null) return (false, null);
        return (true, session.ExpiresAt);
    }
}
