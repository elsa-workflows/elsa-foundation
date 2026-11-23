using Kernel.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace Kernel.Native.Run;

internal static class PostgresPreviewFailover
{
    private sealed record LeaseSample(long Until, long Now);

    private static Task<LeaseSample> ReadLeaseAsync(KernelDb db, int partition, string owner, long epoch, CancellationToken stop) =>
        db.Database.SqlQuery<LeaseSample>($"""
            SELECT n."LeaseUntil" AS "Until", floor(extract(epoch from clock_timestamp()) * 1000)::bigint AS "Now"
            FROM "Partitions" p JOIN "Nodes" n ON p."Owner" = n."Id"
            WHERE p."Id" = {partition} AND p."Epoch" = {epoch} AND n."Id" = {owner}
            """).SingleAsync(stop);

    internal static async Task HoldAsync(PostgresNativeRootOwner prepared, Worker worker,
        DbContextOptions<KernelDb> diagnostics, string node, CancellationToken stop)
    {
        await using var db = new KernelDb(diagnostics);
        var epoch = worker.Fences.Single().Epoch;
        var initial = await ReadLeaseAsync(db, prepared.Partition, node, epoch, stop);
        if (initial.Now >= initial.Until) throw new InvalidOperationException("The original owner lease already expired.");
        var previous = initial;
        var announced = false;
        while (true)
        {
            await Task.Delay(Defaults.Lease / 4, stop);
            await worker.RunAsync(stop);
            if (!worker.Owns || worker.Fences.Single().Epoch != epoch || await prepared.HeartbeatAsync(stop) is null)
                throw new InvalidOperationException("The waiting worker failed to renew its exact ownership.");
            var renewed = await ReadLeaseAsync(db, prepared.Partition, node, epoch, stop);
            if (renewed.Until <= previous.Until || renewed.Now >= renewed.Until)
                throw new InvalidOperationException("The waiting owner's persisted lease failed to advance.");
            if (!announced && renewed.Now > initial.Until)
            {
                Console.WriteLine($"owner-ready {epoch} {initial.Until}");
                announced = true;
            }
            previous = renewed;
        }
    }

    internal static async Task<IScopeActivation> TakeAfterExitAsync(PostgresNativeRootOwner standby, EfStore store,
        DbContextOptions<KernelDb> diagnostics, string node, long priorEpoch, int priorPid, long originalUntil, CancellationToken stop)
    {
        var partition = standby.Partition;
        await using var db = new KernelDb(diagnostics);
        var before = await db.Partitions.AsNoTracking().SingleAsync(p => p.Id == partition, stop);
        if (before.Owner != $"preview-{priorPid}" || before.Owner == node || before.Epoch != priorEpoch)
            throw new InvalidOperationException("The first host did not retain the expected partition and epoch.");

        var live = await ReadLeaseAsync(db, partition, before.Owner, priorEpoch, stop);
        if (live.Now <= originalUntil || live.Until <= live.Now)
            throw new InvalidOperationException("The first owner must still hold a renewed lease beyond its original deadline.");
        Console.WriteLine($"renewed-owner {priorPid} epoch {priorEpoch} original-until {originalUntil} now {live.Now} renewed-until {live.Until}");

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
