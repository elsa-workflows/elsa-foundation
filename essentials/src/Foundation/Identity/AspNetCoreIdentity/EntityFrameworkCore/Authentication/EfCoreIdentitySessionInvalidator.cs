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
    public string? AuthenticationScheme => AspNetCoreIdentityDefaults.CookieScheme;

    public async ValueTask InvalidateAsync(AuthenticationSessionInvalidationContext context, CancellationToken cancellationToken = default)
    {
        if (context.Principal.Identity?.IsAuthenticated != true)
            return;

        // An external subject that happens to equal an Elsa user ID owns no local cookie. Only a
        // provider-projected canonical user can rotate that user's security stamp.
        var canonicalUsers = context.Principal.FindAll(IdentityClaimTypes.UserId).ToArray();
        if (canonicalUsers.Length == 0)
            return;
        if (canonicalUsers.Length != 1)
            throw new InvalidOperationException("The authenticated Identity session has an ambiguous canonical user.");
        var subject = canonicalUsers[0].Value;
        var tenantId = context.Principal.FindFirstValue(IdentityClaimTypes.TenantId);
        if (string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(tenantId))
            throw new InvalidOperationException("The authenticated Identity session is missing its subject or tenant binding.");

        BindTenant(tenantId);
        // A subject that is no tenant-bound Identity user (a bearer issued to another subject kind) holds no
        // Identity session, so there is no stamp to rotate.
        var user = await userManager.FindByIdAsync(subject);
        if (user is null || !string.Equals(user.TenantId, tenantId, StringComparison.Ordinal))
            return;

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
