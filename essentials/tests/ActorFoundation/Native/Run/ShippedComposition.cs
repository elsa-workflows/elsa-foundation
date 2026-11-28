using Kernel.EntityFramework;

namespace Kernel.Native.Run;

// S7 (#4229): the composed-set guards (S2 to S5a) also run against the shipped format's capability set. Their own unshipped
// format numbers stay, so a shipped profile still refuses their markers; the checks use the current shipped capabilities.
internal static class ShippedComposition
{
    internal static async Task InstallHistoryAsync(EfStore store, PostgresRootAdmission admission, bool shipped, CancellationToken token)
    {
        if (shipped)
        {
            await store.InstallPreviewRootSchemaAsync(token);
            return;
        }
        await admission.InstallAsync(token);
        await store.InstallRootWorkflowHistoryAsync(token);
    }

    /// <summary>Binds an owner's partition after initializing the selected profile's self-send, job and child capabilities.</summary>
    internal static async Task<(EfStore.WorkerRoots Scope, int Count)> BindWorkerPartitionAsync(EfStore store, Func<KernelDb> open, int partition,
        CancellationToken stop)
    {
        bool jobs;
        bool selfSends;
        await using (var db = open())
        {
            jobs = db.UsesRootJobProtocol;
            selfSends = db.UsesRootSelfProtocol;
        }
        if (selfSends && store.RootSelfIssuer is null) await store.EnableRootSelfSendsAsync(stop);
        if (jobs) await store.EnableRootJobsAsync(stop);
        await store.EnableRootChildrenAsync(stop);
        return await store.BindWorkerPartitionAsync(partition, stop);
    }
}
