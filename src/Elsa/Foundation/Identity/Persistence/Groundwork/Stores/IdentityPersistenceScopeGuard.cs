using Elsa.Persistence.Core;

namespace Elsa.Foundation.Identity.Persistence.Groundwork.Stores;

internal static class IdentityPersistenceScopeGuard
{
    public static void EnsureCurrentScope(
        this IPersistenceAccessContextAccessor accessContextAccessor,
        string tenantId) =>
        accessContextAccessor.Current.EnsureScope(new PersistenceScope(tenantId));
}
