using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Models;
using Elsa.Foundation.Identity.Core.Authentication;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Foundation.Identity.Core.Iam;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Foundation.Identity.AspNetCoreIdentity.Services;

/// <summary>Uses the selected framework user/principal authority and its ordinary cookie, after external admission.</summary>
internal sealed class AspNetCoreIdentityCanonicalSignInService(UserManager<AspNetCoreIdentityUser> userManager,
    SignInManager<AspNetCoreIdentityUser> signInManager, IUserClaimsPrincipalFactory<AspNetCoreIdentityUser> principalFactory,
    IUserStore users, ITenantMembershipStore memberships, ITokenSubjectResolver subjects, IAuthSessionService sessions,
    IHttpContextAccessor http, IPersistenceAccessContextAccessor access, IPersistenceAccessContextBinder binder,
    IOptions<AspNetCoreIdentityOptions> options, IAuthenticationSessionStore? sessionStore = null, IServiceScopeFactory? scopes = null,
    IExternalIdentityStore? links = null, TimeProvider? clock = null,
    IRevisionAwareExternalIdentityStore? linkRevisions = null) : ICanonicalUserSignInService
{
    public string AuthenticationScheme => AspNetCoreIdentityDefaults.CookieScheme;
    public bool SupportsDurableSessions => sessionStore?.IsDurable == true && userManager.SupportsUserSecurityStamp &&
        options.Value.SessionLifetime >= TimeSpan.FromMinutes(1) && options.Value.SessionLifetime <= TimeSpan.FromDays(30);

    public async ValueTask<bool> CanSignInAsync(PrincipalFactoryContext context, bool requiresPassword = false, CancellationToken cancellationToken = default) =>
        (sessionStore is null || SupportsDurableSessions) && await EligibleUser(context, requiresPassword, cancellationToken) is not null;

    public async ValueTask<AuthSession?> SignInAsync(PrincipalFactoryContext context, CancellationToken cancellationToken = default)
    {
        if (http.HttpContext is null) return null;
        var user = await EligibleUser(context, false, cancellationToken);
        if (user is null) return null;
        // Older/programmatically seeded IAM accounts may have no framework epoch yet. Initialize it
        // only after verified admission, never from readiness/preview or a read-only eligibility probe.
        if (sessionStore is not null && string.IsNullOrWhiteSpace(user.SecurityStamp) &&
            !(await userManager.UpdateSecurityStampAsync(user)).Succeeded) return null;
        var principal = await principalFactory.CreateAsync(user);
        if (principal.Identities.Count() != 1 || principal.Identity is not ClaimsIdentity identity ||
            identity.AuthenticationType != AuthenticationScheme || !subjects.TryResolve(principal, out var subject) ||
            subject.TenantId != context.TenantId || subject.CanonicalUserId != context.UserId) return null;
        foreach (var existing in identity.FindAll(IdentityClaimTypes.Provider).ToArray()) identity.RemoveClaim(existing);
        identity.AddClaim(new(IdentityClaimTypes.Provider, context.Provider ?? options.Value.ProviderId));
        var properties = new AuthenticationProperties { IsPersistent = false };
        if (sessionStore is not null)
        {
            if (!sessionStore.IsDurable || string.IsNullOrWhiteSpace(user.SecurityStamp) || options.Value.SessionLifetime < TimeSpan.FromMinutes(1) ||
                options.Value.SessionLifetime > TimeSpan.FromDays(30)) return null;
            if (context.ExternalIdentity is { } origin && origin.TenantId != context.TenantId) return null;
            IamRevisionedRecord<ExternalIdentityRecord>? linked = null;
            if (context.ExternalIdentity is { } key)
            {
                if (links?.SupportsQualifiedKeys != true || linkRevisions?.SupportsQualifiedKeys != true) return null;
                linked = await linkRevisions.FindByKeyWithRevisionAsync(key, cancellationToken);
                if (linked is null || (await users.FindAsync(context.TenantId, linked.Record.UserId, cancellationToken))?.Id != context.UserId) return null;
            }
            var now = DateTimeOffset.FromUnixTimeMilliseconds((clock ?? TimeProvider.System).GetUtcNow().ToUnixTimeMilliseconds());
            var id = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
            var record = new AuthenticationSessionRecord(id, context.TenantId, context.UserId, context.Provider ?? options.Value.ProviderId,
                SecurityVersion(user.SecurityStamp), now, now + options.Value.SessionLifetime)
            {
                ConnectionKey = context.ExternalIdentity?.Provider, ConnectionRecordId = context.ConnectionRecordId,
                Issuer = context.ExternalIdentity?.Issuer, ExternalSubject = context.ExternalIdentity?.Subject,
                ExternalLinkRevision = linked?.Revision,
                ConnectionMaterialRevision = context.ConnectionMaterialRevision,
                ConnectionCredentialBinding = context.ConnectionCredentialBinding,
                ConnectionRedirectUri = context.ConnectionRedirectUri
            };
            if (!await sessionStore.TryCreateAsync(record, cancellationToken)) return null;
            foreach (var previous in identity.FindAll(IdentityClaimTypes.SessionId).ToArray()) identity.RemoveClaim(previous);
            identity.AddClaim(new(IdentityClaimTypes.SessionId, id));
            properties.ExpiresUtc = record.ExpiresAt; properties.AllowRefresh = false;
        }
        else if (context.ExternalIdentity is not null) return null;
        var session = await sessions.GetAsync(principal, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        await http.HttpContext.SignInAsync(AuthenticationScheme, principal, properties);
        return session with { AuthenticationSessionId = identity.FindFirst(IdentityClaimTypes.SessionId)?.Value };
    }

    public async ValueTask<bool> ValidateSessionAsync(AuthenticationSessionRecord session, CancellationToken cancellationToken = default)
    {
        var now = (clock ?? TimeProvider.System).GetUtcNow();
        if (session.RevokedAt is not null || session.CreatedAt > now || session.ExpiresAt <= now) return false;
        var user = await EligibleUser(new(session.TenantId, session.UserId, session.Provider), false, cancellationToken);
        if (user is null || string.IsNullOrWhiteSpace(user.SecurityStamp) ||
            !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(SecurityVersion(user.SecurityStamp)), Encoding.UTF8.GetBytes(session.SecurityVersion))) return false;
        if (session.ConnectionKey is null) return true;
        if (links?.SupportsQualifiedKeys != true || linkRevisions?.SupportsQualifiedKeys != true || session.Issuer is null || session.ExternalSubject is null || session.ExternalLinkRevision is null) return false;
        var link = await linkRevisions.FindByKeyWithRevisionAsync(new(session.TenantId, session.ConnectionKey, session.Issuer, session.ExternalSubject), cancellationToken);
        return link?.Revision == session.ExternalLinkRevision &&
            (await users.FindAsync(session.TenantId, link.Record.UserId, cancellationToken))?.Id == session.UserId;
    }

    public async ValueTask<AuthenticationSessionRecord?> ReadSessionAsync(TokenSubject subject, string id, CancellationToken cancellationToken = default)
    {
        if (!SupportsDurableSessions || scopes is null || subject.CanonicalUserId is null || id.Length != 64 ||
            !id.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f')) return null;
        await using var scope = scopes.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<IPersistenceAccessContextBinder>().Bind(PersistenceAccessContext.Scoped(new PersistenceScope(subject.TenantId)));
        var record = await scope.ServiceProvider.GetRequiredService<IAuthenticationSessionStore>().FindAsync(subject.TenantId, id, cancellationToken);
        var now = (clock ?? TimeProvider.System).GetUtcNow();
        if (record is null || record.TenantId != subject.TenantId || record.UserId != subject.CanonicalUserId || record.RevokedAt is not null ||
            record.CreatedAt > now || record.ExpiresAt <= now ||
            !await scope.ServiceProvider.GetRequiredService<ICanonicalUserSignInService>().ValidateSessionAsync(record, cancellationToken)) return null;
        return record;
    }

    private static string SecurityVersion(string stamp) => Convert.ToHexStringLower(SHA256.HashData(Encoding.Unicode.GetBytes(stamp)));

    private async Task<AspNetCoreIdentityUser?> EligibleUser(PrincipalFactoryContext context, bool requiresPassword, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (string.IsNullOrWhiteSpace(context.TenantId) || string.IsNullOrWhiteSpace(context.UserId) ||
            requiresPassword && (!options.Value.Enabled || options.Value.DefaultTenantId != context.TenantId)) return null;
        var scope = new PersistenceScope(context.TenantId);
        if (access.Current.Scope != scope) binder.Bind(PersistenceAccessContext.Scoped(scope));
        var record = await users.FindAsync(context.TenantId, context.UserId, cancellationToken);
        if (record is null || record.Status != UserStatus.Active) return null;
        var membership = await memberships.FindAsync(record.TenantId, record.Id, cancellationToken);
        if (membership is not null && membership.Status != TenantMembershipStatus.Active) return null;
        var user = await userManager.FindByIdAsync(context.UserId);
        if (user is null || user.Id != context.UserId || user.TenantId != context.TenantId ||
            !await signInManager.CanSignInAsync(user) || userManager.SupportsUserLockout && await userManager.IsLockedOutAsync(user)) return null;
        // This adapter has no local second-factor completion protocol. Do not turn an externally
        // authenticated identity into a bypass of an existing account's required local second factor.
        if (userManager.SupportsUserTwoFactor && await userManager.GetTwoFactorEnabledAsync(user)) return null;
        if (requiresPassword && (!userManager.SupportsUserPassword || !await userManager.HasPasswordAsync(user))) return null;
        cancellationToken.ThrowIfCancellationRequested();
        return user;
    }
}
