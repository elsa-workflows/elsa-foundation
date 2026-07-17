using Elsa.Persistence.Core;
using Groundwork.Core.Scoping;
using Groundwork.Documents.Scoping;

namespace Elsa.Persistence.Groundwork.Scoping;

/// <summary>Maps an authorized Elsa persistence context to one immutable Groundwork access session.</summary>
public static class GroundworkPersistenceAccessMapper
{
    public static DocumentStoreAccess Map(
        PersistenceAccessContext context,
        PersistenceAccessPolicy requiredPolicy)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!Enum.IsDefined(requiredPolicy))
            throw new ArgumentOutOfRangeException(nameof(requiredPolicy), requiredPolicy, null);

        if (requiredPolicy == PersistenceAccessPolicy.Privileged &&
            context.AccessPolicy != PersistenceAccessPolicy.Privileged)
        {
            throw new InvalidOperationException("The persistence operation requires privileged access, but the current context is ordinary.");
        }

        if (context.AcrossScopes)
        {
            if (requiredPolicy != PersistenceAccessPolicy.Privileged || context.Purpose is null)
                throw new InvalidOperationException("Across-scope persistence access always requires a named privileged purpose.");

            return DocumentStoreAccess.PrivilegedAcrossScopes(Privilege(context.Purpose));
        }

        if (requiredPolicy == PersistenceAccessPolicy.Privileged)
        {
            var privilege = Privilege(context.Purpose!);
            return context.Scope is null
                ? DocumentStoreAccess.PrivilegedGlobal(privilege)
                : DocumentStoreAccess.PrivilegedScoped(privilege, Scope(context.Scope));
        }

        return context.Scope is null
            ? DocumentStoreAccess.Global
            : DocumentStoreAccess.Scoped(Scope(context.Scope));
    }

    private static StorageScope Scope(PersistenceScope scope) => new(scope.Value);

    private static PrivilegedStorageAccess Privilege(PersistenceAccessPurpose purpose) => new(purpose.Value);
}
