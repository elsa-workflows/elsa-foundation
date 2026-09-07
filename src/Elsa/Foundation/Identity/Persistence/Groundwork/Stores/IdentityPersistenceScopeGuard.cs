using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Foundation.Identity.Persistence.Groundwork.Stores;

internal static class IdentityPersistenceScopeGuard
{
    public static void EnsureCurrentScope(
        this IPersistenceAccessContextAccessor accessContextAccessor,
        string tenantId) =>
        accessContextAccessor.Current.EnsureScope(new PersistenceScope(tenantId));

    public static void EnsureGlobalAccess(this IPersistenceAccessContextAccessor accessContextAccessor)
    {
        if (!accessContextAccessor.Current.IsGlobal)
            throw new InvalidOperationException("The requested resource requires explicit global persistence access.");
    }

    public static void EnsurePrivilegedGlobalAccess(this IPersistenceAccessContextAccessor accessContextAccessor)
    {
        var current = accessContextAccessor.Current;
        if (!current.IsGlobal || current.AccessPolicy != PersistenceAccessPolicy.Privileged)
            throw new InvalidOperationException("The requested resource requires explicit privileged global persistence access.");
    }
}
