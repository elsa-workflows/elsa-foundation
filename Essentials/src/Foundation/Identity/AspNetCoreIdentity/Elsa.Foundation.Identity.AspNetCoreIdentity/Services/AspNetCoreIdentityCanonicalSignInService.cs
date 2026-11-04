using System.Security.Claims;
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

namespace Elsa.Foundation.Identity.AspNetCoreIdentity.Services;

/// <summary>Uses the selected framework user/principal authority and its ordinary cookie, after external admission.</summary>
internal sealed class AspNetCoreIdentityCanonicalSignInService(UserManager<AspNetCoreIdentityUser> userManager,
    SignInManager<AspNetCoreIdentityUser> signInManager, IUserClaimsPrincipalFactory<AspNetCoreIdentityUser> principalFactory,
    IUserStore users, ITenantMembershipStore memberships, ITokenSubjectResolver subjects, IAuthSessionService sessions,
    IHttpContextAccessor http, IPersistenceAccessContextAccessor access, IPersistenceAccessContextBinder binder,
    IOptions<AspNetCoreIdentityOptions> options) : ICanonicalUserSignInService
{
    public string AuthenticationScheme => AspNetCoreIdentityDefaults.CookieScheme;

    public async ValueTask<AuthSession?> SignInAsync(PrincipalFactoryContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (http.HttpContext is null || string.IsNullOrWhiteSpace(context.TenantId) || string.IsNullOrWhiteSpace(context.UserId)) return null;
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
        var principal = await principalFactory.CreateAsync(user);
        if (principal.Identities.Count() != 1 || principal.Identity is not ClaimsIdentity identity ||
            identity.AuthenticationType != AuthenticationScheme || !subjects.TryResolve(principal, out var subject) ||
            subject.TenantId != context.TenantId || subject.CanonicalUserId != context.UserId) return null;
        foreach (var existing in identity.FindAll(IdentityClaimTypes.Provider).ToArray()) identity.RemoveClaim(existing);
        identity.AddClaim(new(IdentityClaimTypes.Provider, context.Provider ?? options.Value.ProviderId));
        var session = await sessions.GetAsync(principal, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        await http.HttpContext.SignInAsync(AuthenticationScheme, principal, new AuthenticationProperties { IsPersistent = false });
        return session;
    }
}
