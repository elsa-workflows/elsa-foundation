using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Foundation.Identity.Persistence.EntityFrameworkCore;

internal static class IdentityEntityFrameworkAccessGuard
{
    public static void EnsureTenant(IPersistenceAccessContextAccessor accessor, string tenantId) =>
        accessor.Current.EnsureScope(new PersistenceScope(tenantId));

    public static void EnsureGlobal(IPersistenceAccessContextAccessor accessor)
    {
        if (!accessor.Current.IsGlobal)
            throw new InvalidOperationException("The requested resource requires explicit global persistence access.");
    }

    public static void EnsurePrivilegedGlobal(IPersistenceAccessContextAccessor accessor)
    {
        var current = accessor.Current;
        if (!current.IsGlobal || current.AccessPolicy != PersistenceAccessPolicy.Privileged)
            throw new InvalidOperationException("The requested resource requires explicit privileged global persistence access.");
    }
}
