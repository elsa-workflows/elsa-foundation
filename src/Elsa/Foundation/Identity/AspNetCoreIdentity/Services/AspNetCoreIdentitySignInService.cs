using Elsa.Foundation.Identity.Abstractions.Authentication;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Elsa.Foundation.Identity.AspNetCoreIdentity.Services;

/// <summary>
/// Validates credentials with <see cref="SignInManager{TUser}.CheckPasswordSignInAsync"/>, issues the
/// Identity cookie via <see cref="SignInManager{TUser}.SignInAsync"/> (which runs the registered
/// <see cref="IUserClaimsPrincipalFactory{TUser}"/> — the Elsa principal factory — so tenant/role/permission
/// claims flow into the cookie), and projects the resulting principal into an Elsa <see cref="AuthSession"/>.
/// </summary>
public sealed class AspNetCoreIdentitySignInService(
    SignInManager<AspNetCoreIdentityUser> signInManager,
    UserManager<AspNetCoreIdentityUser> userManager,
    IUserClaimsPrincipalFactory<AspNetCoreIdentityUser> principalFactory,
    IPasswordHasher<AspNetCoreIdentityUser> passwordHasher,
    IAuthSessionService sessionService,
    IHttpContextAccessor httpContextAccessor,
    IPersistenceAccessContextAccessor accessContextAccessor,
    IPersistenceAccessContextBinder accessContextBinder,
    IOptions<AspNetCoreIdentityOptions> options) : IIdentitySignInService
{
    // A precomputed PBKDF2 hash of a throwaway password, verified against when the user is not found so the
    // no-such-user path performs the same password-hashing work as the wrong-password path. This equalizes
    // response timing and closes the username-enumeration timing oracle (unknown users would otherwise return
    // before any hashing). The value is never a valid credential; the verify result is discarded.
    private static readonly string DummyPasswordHash =
        new PasswordHasher<AspNetCoreIdentityUser>().HashPassword(new AspNetCoreIdentityUser(), "timing-equalizer-not-a-real-password");

    public async ValueTask<SignInOutcome> PasswordSignInAsync(string username, string password, string? tenantId, CancellationToken cancellationToken = default)
    {
        var effectiveTenant = string.IsNullOrWhiteSpace(tenantId) ? options.Value.DefaultTenantId : tenantId;
        BindEffectiveTenant(effectiveTenant);

        var user = await FindUserAsync(username);
        if (user is null)
        {
            // Perform a dummy password verification against a fixed hash so an unknown username takes the same
            // time as a wrong password for a real one, then fail with the same generic outcome. Prevents
            // username enumeration via a timing side channel.
            passwordHasher.VerifyHashedPassword(new AspNetCoreIdentityUser(), DummyPasswordHash, password);
            return SignInOutcome.Failure;
        }

        var result = await signInManager.CheckPasswordSignInAsync(user, password, lockoutOnFailure: true);
        if (!result.Succeeded)
            return SignInOutcome.Failure;

        // Build the principal through the Elsa claims-principal factory so tenant/role/permission claims are
        // projected into the cookie (and the returned session).
        var principal = await principalFactory.CreateAsync(user);

        // Issue the cookie when running inside the HTTP pipeline, on our own cookie scheme. Sign in through
        // HttpContext (not SignInManager.SignInAsync, whose string overload parameter is the authentication
        // *method*, not the scheme — it would target the default Identity.Application scheme, which this
        // module does not register). Outside a request (e.g. unit tests) credential validation still succeeds
        // and the session is projected without a cookie write.
        if (httpContextAccessor.HttpContext is not null)
            await httpContextAccessor.HttpContext.SignInAsync(AspNetCoreIdentityDefaults.CookieScheme, principal);

        var session = await sessionService.GetAsync(principal, cancellationToken);
        return SignInOutcome.Success(session);
    }

    private async Task<AspNetCoreIdentityUser?> FindUserAsync(string username)
    {
        var byName = await userManager.FindByNameAsync(username);
        if (byName is not null)
            return byName;

        return await userManager.FindByEmailAsync(username);
    }

    private void BindEffectiveTenant(string tenantId)
    {
        var scope = new PersistenceScope(tenantId);
        if (accessContextAccessor.Current.Scope == scope)
            return;

        accessContextBinder.Bind(PersistenceAccessContext.Scoped(scope));
    }
}
