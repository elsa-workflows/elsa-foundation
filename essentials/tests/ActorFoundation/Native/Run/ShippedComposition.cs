using Kernel.EntityFramework;

namespace Kernel.Native.Run;

// S7 (#4229): the composed-set guards (S2 to S5a) also run against the shipped format's capability set. Their own unshipped
// format numbers stay, so a shipped profile still refuses their markers; the capabilities are format 24's.
internal static class ShippedComposition
{
    /// <summary>Binds an owner's partition, first turning on the job capability of a profile that carries jobs.</summary>
    internal static async Task<(EfStore.WorkerRoots Scope, int Count)> BindWorkerPartitionAsync(EfStore store, Func<KernelDb> open, int partition,
        CancellationToken stop)
    {
        bool jobs;
        await using (var db = open()) jobs = db.UsesRootJobProtocol;
        if (jobs) await store.EnableRootJobsAsync(stop);
        return await store.BindWorkerPartitionAsync(partition, stop);
    }
}
