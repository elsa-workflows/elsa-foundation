using Elsa.Foundation.Identity.Abstractions.Iam;

namespace Elsa.Foundation.Identity.Persistence.Groundwork.Stores;

internal static class GroundworkIdentityListGuard
{
    public static void EnsureWithinMaterializationLimit<TPagedContract>(long totalCount)
    {
        if (totalCount <= IdentityStorageManifest.MaxMaterializedListEntries)
            return;

        throw new InvalidOperationException(
            $"The IAM list contains {totalCount} entries, which exceeds the finite materialization limit of " +
            $"{IdentityStorageManifest.MaxMaterializedListEntries}. Use {typeof(TPagedContract).Name} to read bounded pages.");
    }
}
