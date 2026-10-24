using System.Security.Claims;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Foundation.Identity.Core.Iam;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Elsa.Foundation.Identity.AspNetCoreIdentity.EntityFrameworkCore.Authentication;

/// <summary>Rejects first-party cookies whose tenant or security stamp no longer resolves, or whose user is disabled.</summary>
public sealed class EfCoreIdentityCookieEvents(
    UserManager<AspNetCoreIdentityUser> userManager,
    IUserStore users,
    IPersistenceAccessContextAccessor accessContextAccessor,
    IPersistenceAccessContextBinder accessContextBinder,
    IOptions<IdentityOptions> identityOptions) : CookieAuthenticationEvents
{
    public override async Task ValidatePrincipal(CookieValidatePrincipalContext context)
    {
        if (context.Principal?.Identity?.IsAuthenticated != true)
            return;

        var subject = context.Principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? context.Principal.FindFirstValue("sub");
        var tenantId = context.Principal.FindFirstValue(IdentityClaimTypes.TenantId);
        var stamp = context.Principal.FindFirstValue(identityOptions.Value.ClaimsIdentity.SecurityStampClaimType);
        if (string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(tenantId) || string.IsNullOrWhiteSpace(stamp))
        {
            await RejectAsync(context);
            return;
        }

        if (accessContextAccessor.Current.Scope != new PersistenceScope(tenantId))
        {
            try { accessContextBinder.Bind(PersistenceAccessContext.Scoped(new PersistenceScope(tenantId))); }
            catch (InvalidOperationException) { await RejectAsync(context); return; }
        }

        var user = await userManager.FindByIdAsync(subject);
        if (user is null || !string.Equals(user.TenantId, tenantId, StringComparison.Ordinal) ||
            !string.Equals(await userManager.GetSecurityStampAsync(user), stamp, StringComparison.Ordinal) ||
            (await users.FindAsync(user.TenantId, user.Id, context.HttpContext.RequestAborted))?.Status == UserStatus.Disabled)
            await RejectAsync(context);
    }

    private static async Task RejectAsync(CookieValidatePrincipalContext context)
    {
        context.RejectPrincipal();
        await context.HttpContext.SignOutAsync(context.Scheme.Name);
    }
}
