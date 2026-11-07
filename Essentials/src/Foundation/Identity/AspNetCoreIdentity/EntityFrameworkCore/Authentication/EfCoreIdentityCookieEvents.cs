using System.Security.Claims;
using Elsa.Foundation.Identity.Core.Authentication;
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
    ITenantMembershipStore memberships,
    ITokenSubjectResolver subjects,
    IPersistenceAccessContextAccessor accessContextAccessor,
    IPersistenceAccessContextBinder accessContextBinder,
    IOptions<IdentityOptions> identityOptions, IAuthenticationSessionStore? sessions = null,
    ICanonicalUserSignInService? canonical = null) : CookieAuthenticationEvents
{
    public override async Task ValidatePrincipal(CookieValidatePrincipalContext context)
    {
        if (context.Principal?.Identity?.IsAuthenticated != true)
            return;

        if (!subjects.TryResolve(context.Principal, out var identity) || identity.CanonicalUserId is null)
        { await RejectAsync(context); return; }
        var subject = identity.CanonicalUserId;
        var tenantId = identity.TenantId;
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
            (await users.FindAsync(user.TenantId, user.Id, context.HttpContext.RequestAborted))?.Status != UserStatus.Active)
        { await RejectAsync(context); return; }
        var membership = await memberships.FindAsync(tenantId, subject, context.HttpContext.RequestAborted);
        if (membership is not null && membership.Status != TenantMembershipStatus.Active) { await RejectAsync(context); return; }
        var ids = context.Principal.FindAll(IdentityClaimTypes.SessionId).ToArray();
        if (sessions is not null || ids.Length != 0)
        {
            try
            {
                if (sessions is null || canonical is null || ids.Length != 1 || ids[0].Value.Length != 64 || !sessions.IsDurable)
                { await RejectAsync(context); return; }
                var session = await sessions.FindAsync(tenantId, ids[0].Value, context.HttpContext.RequestAborted);
                var now = DateTimeOffset.UtcNow;
                if (session is null || session.TenantId != tenantId || session.UserId != subject || session.RevokedAt is not null ||
                    session.CreatedAt > now || session.ExpiresAt <= now || !await canonical.ValidateSessionAsync(session, context.HttpContext.RequestAborted))
                    await RejectAsync(context);
            }
            catch (Exception exception) when (!(exception is OperationCanceledException && context.HttpContext.RequestAborted.IsCancellationRequested))
            { await RejectAsync(context); }
        }
    }

    private static async Task RejectAsync(CookieValidatePrincipalContext context)
    {
        context.RejectPrincipal();
        await context.HttpContext.SignOutAsync(context.Scheme.Name);
    }
}
