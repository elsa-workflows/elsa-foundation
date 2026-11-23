using Kernel.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace Kernel.Native.Run;

internal static class PostgresPreviewFailover
{
    internal static async Task<IScopeActivation> TakeAfterExitAsync(PostgresNativeRootOwner standby, EfStore store,
        DbContextOptions<KernelDb> diagnostics, string node, long priorEpoch, CancellationToken stop)
    {
        var partition = standby.Partition;
        await using var db = new KernelDb(diagnostics);
        var before = await db.Partitions.AsNoTracking().SingleAsync(p => p.Id == partition, stop);
        if (before.Owner is null || before.Owner == node || before.Epoch != priorEpoch)
            throw new InvalidOperationException("The first host did not retain the expected partition and epoch.");

        // This direct store take returns null only on a concluded refusal. A database error propagates.
        if (await store.TakeAsync(partition, node, Defaults.Lease, stop) is not null)
            throw new InvalidOperationException("The standby took a live owner's partition.");
        var after = await db.Partitions.AsNoTracking().SingleAsync(p => p.Id == partition, stop);
        if (after.Owner != before.Owner || after.Epoch != priorEpoch)
            throw new InvalidOperationException("The live-owner refusal changed the partition.");
        Console.WriteLine($"standby-refused {Environment.ProcessId} {priorEpoch}");
        if (await Console.In.ReadLineAsync(stop) != "owner-exited")
            throw new InvalidOperationException("Missing confirmation that the fixture owner died.");

        IScopeActivation? activation;
        do
        {
            await standby.HeartbeatAsync(stop);
            activation = await standby.TryActivateAsync(stop);
            if (activation is null) await Task.Delay(250, stop);
        } while (activation is null);

        var fence = ((Worker)activation).Fences.Single();
        var current = await db.Partitions.AsNoTracking().SingleAsync(p => p.Id == partition, stop);
        var previousLease = await db.Nodes.AsNoTracking().Where(n => n.Id == before.Owner).Select(n => n.LeaseUntil).SingleAsync(stop);
        var takenAt = await db.Nodes.AsNoTracking().Where(n => n.Id == node).Select(n => n.SeenAt).SingleAsync(stop);
        if (current.Owner != node || current.Epoch != fence.Epoch || fence.Epoch <= priorEpoch || previousLease >= takenAt)
            throw new InvalidOperationException("The standby must own a later epoch after the previous owner's database lease expires.");
        Console.WriteLine($"standby-acquired {Environment.ProcessId} {fence.Epoch} expired-by-ms {takenAt - previousLease}");
        return activation;
    }
}
