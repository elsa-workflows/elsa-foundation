using Elsa.Studio.Hosting.Nuplane;
using global::Nuplane.Abstractions;
using global::Nuplane.Abstractions.PackageStoreProtection;
using global::Nuplane.Admin;
using Microsoft.Extensions.Logging.Abstractions;

namespace Elsa.Studio.Tests.Administration;

public sealed class PackageContentReaderTests
{
    [Fact]
    public async Task A_catalog_read_waits_out_a_busy_enrolled_store()
    {
        // An enrolled store's reconciliation holds the non-waiting root lock for its cycle (#5778).
        var admin = new BusyAdmin(busyReads: 3);
        using var reader = new PackageContentReader(admin, null!, null!, NullLogger<PackageContentReader>.Instance);

        var packages = await reader.GetPackagesAsync(CancellationToken.None);

        Assert.Same(BusyAdmin.Snapshot, packages);
        Assert.Equal(4, admin.Reads);
    }

    [Fact]
    public async Task A_catalog_read_that_stays_refused_fails_after_its_bounded_wait()
    {
        var admin = new BusyAdmin(busyReads: int.MaxValue);
        using var reader = new PackageContentReader(admin, null!, null!, NullLogger<PackageContentReader>.Instance);

        await Assert.ThrowsAsync<PackageStoreAdmissionException>(() => reader.GetPackagesAsync(CancellationToken.None));
        Assert.Equal(PackageContentReader.BusyRetryAttempts, admin.Reads);
    }

    private sealed class BusyAdmin(int busyReads) : INuplaneAdminOperations
    {
        public static readonly ActivePackagesSnapshot Snapshot = new(DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, [], "catalog");

        public int Reads { get; private set; }

        public Task<ActivePackagesSnapshot> GetPackagesAsync(CancellationToken cancellationToken = default)
        {
            Reads++;
            return Reads <= busyReads
                ? Task.FromException<ActivePackagesSnapshot>(new PackageStoreAdmissionException(PackageStoreAdmissionReason.UnknownAuthority, "The required lock file 'root.lock' is busy.", null, null))
                : Task.FromResult(Snapshot);
        }

        public Task<global::Nuplane.Operational.OperationalStateSnapshot> GetStateAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<global::Nuplane.Reconciliation.ManualReconcileOutcome> TriggerReconcileAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
