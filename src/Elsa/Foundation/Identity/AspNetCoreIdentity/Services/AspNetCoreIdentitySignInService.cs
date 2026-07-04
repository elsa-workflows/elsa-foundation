using Elsa.Foundation.Identity.Abstractions.Authentication;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Models;
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
    IAuthSessionService sessionService,
    IHttpContextAccessor httpContextAccessor,
    IOptions<AspNetCoreIdentityOptions> options) : IIdentitySignInService
{
    public async ValueTask<SignInOutcome> PasswordSignInAsync(string username, string password, string? tenantId, CancellationToken cancellationToken = default)
    {
        var effectiveTenant = string.IsNullOrWhiteSpace(tenantId) ? options.Value.DefaultTenantId : tenantId;

        var user = await FindUserAsync(username, effectiveTenant);
        if (user is null)
            return SignInOutcome.Failure;

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

    private async Task<AspNetCoreIdentityUser?> FindUserAsync(string username, string tenantId)
    {
        var byName = await userManager.FindByNameAsync(username);
        if (byName is not null && TenantMatches(byName, tenantId))
            return byName;

        var byEmail = await userManager.FindByEmailAsync(username);
        return byEmail is not null && TenantMatches(byEmail, tenantId) ? byEmail : null;
    }

    private static bool TenantMatches(AspNetCoreIdentityUser user, string tenantId) =>
        string.IsNullOrEmpty(user.TenantId) || string.Equals(user.TenantId, tenantId, StringComparison.OrdinalIgnoreCase);
}
