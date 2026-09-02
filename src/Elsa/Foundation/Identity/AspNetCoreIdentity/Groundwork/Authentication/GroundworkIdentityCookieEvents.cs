using System.Security.Claims;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Authentication;

/// <summary>
/// Validates the tenant-bound security stamp on every Groundwork-backed first-party Identity cookie request.
/// There is no validation interval, so a logout stamp rotation rejects replay immediately.
/// </summary>
public sealed class GroundworkIdentityCookieEvents(
    UserManager<AspNetCoreIdentityUser> userManager,
    IPersistenceAccessContextAccessor accessContextAccessor,
    IPersistenceAccessContextBinder accessContextBinder,
    IOptions<IdentityOptions> identityOptions) : CookieAuthenticationEvents
{
    public override async Task ValidatePrincipal(CookieValidatePrincipalContext context)
    {
        if (context.Principal?.Identity?.IsAuthenticated != true)
            return;

        var subject = context.Principal.FindFirstValue(ClaimTypes.NameIdentifier)
                      ?? context.Principal.FindFirstValue("sub");
        var tenantId = context.Principal.FindFirstValue(IdentityClaimTypes.TenantId);
        var securityStamp = context.Principal.FindFirstValue(identityOptions.Value.ClaimsIdentity.SecurityStampClaimType);
        if (string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(tenantId) || string.IsNullOrWhiteSpace(securityStamp))
        {
            await RejectAsync(context);
            return;
        }

        var tenantScope = new PersistenceScope(tenantId);
        if (accessContextAccessor.Current.Scope != tenantScope)
        {
            try
            {
                accessContextBinder.Bind(PersistenceAccessContext.Scoped(tenantScope));
            }
            catch (InvalidOperationException)
            {
                await RejectAsync(context);
                return;
            }
        }

        var user = await userManager.FindByIdAsync(subject);
        if (user is null ||
            !string.Equals(user.TenantId, tenantId, StringComparison.Ordinal) ||
            !string.Equals(await userManager.GetSecurityStampAsync(user), securityStamp, StringComparison.Ordinal))
        {
            await RejectAsync(context);
        }
    }

    private static async Task RejectAsync(CookieValidatePrincipalContext context)
    {
        context.RejectPrincipal();
        await context.HttpContext.SignOutAsync(context.Scheme.Name);
    }
}
