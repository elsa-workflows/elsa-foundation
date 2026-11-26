using global::Nuplane.Abstractions;
using global::Nuplane.Abstractions.PackageStoreProtection;

namespace Elsa.Studio.Hosting.Nuplane;

internal sealed class FallbackPackageSource(IDesiredPackageSource inner, Func<bool> selected) : IScopedDesiredPackageSource
{
    public Task<IReadOnlyList<PackageRequest>> GetDesiredAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return selected() ? inner.GetDesiredAsync(ct) : Task.FromResult<IReadOnlyList<PackageRequest>>([]);
    }

    public Task<IReadOnlyList<PackageRequest>> GetDesiredAsync(PackageStoreOperationBorrow borrow, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return selected()
            ? ((IScopedDesiredPackageSource)inner).GetDesiredAsync(borrow, ct)
            : Task.FromResult<IReadOnlyList<PackageRequest>>([]);
    }
}
