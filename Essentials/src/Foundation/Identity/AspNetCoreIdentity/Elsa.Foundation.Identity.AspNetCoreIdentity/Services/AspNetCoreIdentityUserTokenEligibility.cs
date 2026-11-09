using Elsa.Foundation.Identity.Core.Authentication;
using Elsa.Foundation.Identity.Core.Iam;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Foundation.Identity.AspNetCoreIdentity.Services;

/// <summary>
/// Re-reads the user from the persisted Elsa IAM records: a missing or disabled user may not hold tokens. The same
/// check password sign-in and cookie validation apply, so a refresh is refused exactly where a new sign-in would be.
/// The refresh endpoint is anonymous and binds no tenant, so the user's tenant is bound here the way cookie
/// validation binds it; a request already bound to another tenant is refused rather than crossed.
/// </summary>
internal sealed class AspNetCoreIdentityUserTokenEligibility(
    IUserStore users,
    IPersistenceAccessContextAccessor accessContextAccessor,
    IPersistenceAccessContextBinder accessContextBinder) : IUserTokenEligibility
{
    public async ValueTask<bool> IsEligibleAsync(string tenantId, string userId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        var scope = new PersistenceScope(tenantId);
        if (accessContextAccessor.Current.Scope != scope)
        {
            try { accessContextBinder.Bind(PersistenceAccessContext.Scoped(scope)); }
            catch (InvalidOperationException) { return false; }
        }

        var user = await users.FindAsync(tenantId, userId, cancellationToken);
        return user is not null &&
               string.Equals(user.Id, userId, StringComparison.Ordinal) &&
               string.Equals(user.TenantId, tenantId, StringComparison.Ordinal) &&
               user.Status != UserStatus.Disabled;
    }
}
