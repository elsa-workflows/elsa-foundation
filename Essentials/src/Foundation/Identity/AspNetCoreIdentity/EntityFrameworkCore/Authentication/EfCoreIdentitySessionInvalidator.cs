using System.Security.Claims;
using Elsa.Foundation.Identity.Core.Authentication;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Elsa.Foundation.Identity.AspNetCoreIdentity.EntityFrameworkCore.Authentication;

/// <summary>Rotates the persisted security stamp to invalidate all existing first-party cookies.</summary>
public sealed class EfCoreIdentitySessionInvalidator(
    UserManager<AspNetCoreIdentityUser> userManager,
    IPersistenceAccessContextAccessor accessContextAccessor,
    IPersistenceAccessContextBinder accessContextBinder,
    IOptions<AspNetCoreIdentityOptions> options) : IAuthenticationSessionInvalidator
{
    public string ProviderId => options.Value.ProviderId;

    public async ValueTask InvalidateAsync(AuthenticationSessionInvalidationContext context, CancellationToken cancellationToken = default)
    {
        if (context.Principal.Identity?.IsAuthenticated != true)
            return;

        var subject = context.Principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? context.Principal.FindFirstValue("sub");
        var tenantId = context.Principal.FindFirstValue(IdentityClaimTypes.TenantId);
        if (string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(tenantId))
            throw new InvalidOperationException("The authenticated Identity session is missing its subject or tenant binding.");

        BindTenant(tenantId);
        var user = await userManager.FindByIdAsync(subject);
        if (user is null || !string.Equals(user.TenantId, tenantId, StringComparison.Ordinal))
            throw new InvalidOperationException("The authenticated Identity session no longer resolves to its tenant-bound user.");

        var result = await userManager.UpdateSecurityStampAsync(user);
        if (!result.Succeeded)
            throw new InvalidOperationException("The authenticated Identity session could not be invalidated: " +
                string.Join("; ", result.Errors.Select(error => $"{error.Code}: {error.Description}")));
    }

    private void BindTenant(string tenantId)
    {
        var scope = new PersistenceScope(tenantId);
        if (accessContextAccessor.Current.Scope == scope)
            return;
        try { accessContextBinder.Bind(PersistenceAccessContext.Scoped(scope)); }
        catch (InvalidOperationException exception) { throw new InvalidOperationException("The authenticated Identity session does not match the request persistence scope.", exception); }
    }
}
