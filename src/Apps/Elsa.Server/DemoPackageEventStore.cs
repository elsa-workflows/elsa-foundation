using Nuplane.Abstractions;

namespace Elsa.Server;

internal sealed class DemoPackageEventStore
{
    private readonly Lock _lock = new();
    private readonly List<DemoPackageEvent> _events = [];
    private long _nextSequence;

    public IReadOnlyList<DemoPackageEvent> GetEvents(long? afterSequence = null)
    {
        lock (_lock)
        {
            return _events
                .Where(x => afterSequence is null || x.Sequence > afterSequence)
                .OrderBy(x => x.Sequence)
                .ToArray();
        }
    }

    public void RecordChanging(PackageChangeSet changeSet) =>
        Add("changing", "Package reconciliation started.", changeSet, []);

    public void RecordChanged(PackageChangeSet changeSet) =>
        Add("changed", "New package detected. Reload shells?", changeSet, []);

    public void RecordFailed(string packageId, Exception exception) =>
        Add(
            "failed",
            $"Package '{packageId}' failed reconciliation: {exception.Message}",
            null,
            [],
            packageId,
            exception.Message);

    public void RecordReconciled(PackageChangeSet changeSet, IReadOnlyList<ResolvedPackage> appliedPackages) =>
        Add("reconciled", "Package reconciliation completed.", changeSet, appliedPackages);

    private void Add(
        string kind,
        string message,
        PackageChangeSet? changeSet,
        IReadOnlyList<ResolvedPackage> appliedPackages,
        string? packageId = null,
        string? error = null)
    {
        lock (_lock)
        {
            var added = changeSet?.Added.Select(DemoPackageSummary.FromResolvedPackage).ToArray() ?? [];
            var updated = changeSet?.Updated.Select(DemoPackageSummary.FromResolvedPackage).ToArray() ?? [];
            var active = appliedPackages.Select(DemoPackageSummary.FromResolvedPackage).ToArray();
            var hasPackageChanges = added.Length > 0 || updated.Length > 0 || changeSet?.Removed.Count > 0;

            _events.Add(new DemoPackageEvent(
                ++_nextSequence,
                DateTimeOffset.UtcNow,
                kind,
                message,
                changeSet?.CorrelationId,
                hasPackageChanges,
                added,
                updated,
                changeSet?.Removed.ToArray() ?? [],
                active,
                packageId,
                error));

            if (_events.Count > 200)
                _events.RemoveRange(0, _events.Count - 200);
        }
    }
}

internal sealed record DemoPackageEvent(
    long Sequence,
    DateTimeOffset Timestamp,
    string Kind,
    string Message,
    string? CorrelationId,
    bool HasPackageChanges,
    IReadOnlyList<DemoPackageSummary> Added,
    IReadOnlyList<DemoPackageSummary> Updated,
    IReadOnlyList<string> Removed,
    IReadOnlyList<DemoPackageSummary> ActivePackages,
    string? PackageId,
    string? Error);

internal sealed record DemoPackageSummary(
    string Id,
    string Version,
    string FeedName,
    string SourceName,
    string InstallPath)
{
    public static DemoPackageSummary FromResolvedPackage(ResolvedPackage package) =>
        new(package.Id, package.Version, package.FeedName ?? "", package.SourceName ?? "", package.InstallPath);

    public static DemoPackageSummary FromActivePackage(ActivePackage package) =>
        new(package.PackageId, package.Version, package.FeedName ?? "", package.SourceName ?? "", package.InstallPath);
}
