using Kernel.Foundation.Hosting;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.EntityFramework;

internal sealed partial class SqliteRootAdmission
{
    internal Task<PreparedWaitReceipt> DeliverPreparedWaitsAsync(Contract.BoundScope scope, string operation, string sourceDigest,
        IReadOnlyList<PreparedWaitDelivery> deliveries, bool allowNew, RootAdmissionPressure.Attempt? attempt, CancellationToken token)
    {
        if (scope.IsHostIsolated) throw new ArgumentException("A prepared external wait source requires an ordinary tenant.", nameof(scope));
        var selected = RootEventAdmission.SnapshotPreparedWaitDeliveries(operation, sourceDigest, deliveries);
        return WriteAsync((db, now, ct) => RootEventAdmission.DeliverPreparedWaitsAsync(db, scope.Tenant, operation, sourceDigest,
            selected, allowNew, now, deduplicationWindow, attempt, bind: null, ct), token, attempt);
    }
}
