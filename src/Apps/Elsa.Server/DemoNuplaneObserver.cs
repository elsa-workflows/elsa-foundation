using Nuplane.Abstractions;

namespace Elsa.Server;

internal sealed class DemoNuplaneObserver(DemoPackageEventStore events) : INuplaneObserver
{
    public Task OnPackagesChangingAsync(PackageChangeSet changeSet, CancellationToken ct)
    {
        events.RecordChanging(changeSet);
        return Task.CompletedTask;
    }

    public Task OnPackagesChangedAsync(PackageChangeSet changeSet, CancellationToken ct)
    {
        events.RecordChanged(changeSet);
        return Task.CompletedTask;
    }

    public Task OnPackageFailedAsync(string packageId, Exception exception, CancellationToken ct)
    {
        events.RecordFailed(packageId, exception);
        return Task.CompletedTask;
    }

    public Task OnPackagesReconciledAsync(PackageChangeSet changeSet, IReadOnlyList<ResolvedPackage> appliedPackages, CancellationToken ct)
    {
        events.RecordReconciled(changeSet, appliedPackages);
        return Task.CompletedTask;
    }
}
