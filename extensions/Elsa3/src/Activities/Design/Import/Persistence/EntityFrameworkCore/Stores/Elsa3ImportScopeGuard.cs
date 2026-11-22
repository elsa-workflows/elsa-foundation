using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa3.Activities.Design.Import.Models;

namespace Elsa3.Activities.Design.Import.Persistence.EntityFrameworkCore.Stores;

/// <summary>
/// Authorizes an operation scope against the ambient persistence scope before any row is read or written.
/// A mismatch on a read is indistinguishable from absence; on a write it is a persistence failure.
/// </summary>
internal static class Elsa3ImportScopeGuard
{
    public static void EnsureCurrent(
        IPersistenceAccessContextAccessor accessor,
        ReusableActivityImportAccessScope accessScope,
        bool hideMismatch,
        string identity)
    {
        ArgumentNullException.ThrowIfNull(accessScope);
        try
        {
            if (accessScope.TenantId is { } tenantId)
            {
                accessor.Current.EnsureScope(new PersistenceScope(tenantId));
                return;
            }

            if (!accessor.Current.IsGlobal)
                throw new InvalidOperationException("The requested resource does not belong to the current persistence scope.");
        }
        catch (InvalidOperationException exception)
        {
            if (hideMismatch)
                throw new ReusableActivityImportNotFoundException("The Elsa 3 import resource was not found.");
            throw new ReusableActivityImportPersistenceException("validate persistence scope", identity, exception);
        }
    }
}
