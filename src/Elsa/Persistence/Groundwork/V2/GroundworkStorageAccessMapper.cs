using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Kernel;
using Groundwork.Store;

namespace Elsa.Persistence.Groundwork.Composition;

/// <summary>
/// Maps Elsa's immutable persistence authority to the least-privileged public Groundwork access mode
/// accepted by one storage unit.
/// </summary>
public static class GroundworkStorageAccessMapper
{
    /// <summary>
    /// Maps <paramref name="context"/> to the access mode required by <paramref name="unitScope"/>.
    /// The stable <paramref name="auditIdentity"/> identifies the Elsa subsystem performing a privileged
    /// cross-scope query; the caller-supplied persistence purpose remains the operation-specific audit reason.
    /// </summary>
    public static StorageAccess Map(
        PersistenceAccessContext context,
        ScopePolicy unitScope,
        string auditIdentity,
        IStorageAccessObserver? observer = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!Enum.IsDefined(unitScope))
            throw new ArgumentOutOfRangeException(nameof(unitScope), unitScope, null);

        if (unitScope == ScopePolicy.Global)
        {
            if (context.Scope is not null || context.AcrossScopes)
            {
                throw new InvalidOperationException(
                    "A global Groundwork unit requires explicit global persistence access without a tenant scope.");
            }

            return StorageAccess.Global;
        }

        if (context.AcrossScopes)
        {
            if (context.AccessPolicy != PersistenceAccessPolicy.Privileged || context.Purpose is null)
            {
                throw new InvalidOperationException(
                    "Across-scope Groundwork access requires a named privileged persistence purpose.");
            }

            return StorageAccess.PrivilegedAcrossScopes(
                new StorageAccessAudit(auditIdentity, context.Purpose.Value, observer));
        }

        if (context.Scope is null)
        {
            throw new InvalidOperationException(
                "A scoped Groundwork unit requires one explicit persistence scope.");
        }

        return StorageAccess.Scoped(new StorageScope(context.Scope.Value));
    }
}
