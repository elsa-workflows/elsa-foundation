using Kernel.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace Kernel.Ef.Conformance;

internal static class NodeAdvertisementChecks
{
    internal static async Task Run(Func<KernelDb> create, IKernelStore store)
    {
        var lease = TimeSpan.FromMinutes(1);
        const string owner = "format-owner";
        const string old = "format-legacy";

        await using (var db = create())
        {
            var sql = db.GetService<ISqlGenerationHelper>();
            string Q(string name) => sql.DelimitIdentifier(name);
            // A writer from before the additive schema change does not mention any capability columns.
            var insert = $"INSERT INTO {Q("Nodes")} ({Q("Id")}, {Q("SeenAt")}) VALUES ({{0}}, 0)";
            await db.Database.ExecuteSqlRawAsync(insert, old);
        }
        Check(await Read(old) is { ReadableStoreSchema: "", ReadableMessageProtocol: "", ReadableJournalCodec: "", LeaseUntil: 0 },
            "an old writer has an explicitly empty advertisement and no lease");

        await store.HeartbeatAsync(owner, lease);
        var joined = await Read(owner);
        Check(Advertises(joined) && joined.LeaseUntil == 0,
            "membership insert advertises all three families without acquiring a lease");

        await Stale(owner);
        var take = await store.TakeAsync(0, owner, lease);
        Check(take is { Epoch: 1 } && Renewed(await Read(owner)),
            "a take replaces all three stale values and renews from the store clock");

        await Stale(owner);
        var until = (await Read(owner)).LeaseUntil;
        await store.HeartbeatAsync(owner, lease);
        var membership = await Read(owner);
        Check(Advertises(membership) && membership.LeaseUntil == until,
            "membership update replaces all three values and preserves the existing lease deadline");

        await Stale(owner);
        var beat = await store.NodeHeartbeatAsync(owner, lease, lease);
        var renewed = await Read(owner);
        Check(Renewed(renewed) && beat.Now == renewed.SeenAt && beat.Owned is [{ Partition: 0, Epoch: 1 }],
            "node heartbeat replaces all three values, renews the lease, and reads the owned epoch");

        await Stale(owner);
        var retake = await store.RetakeAsync(0, owner, 1, lease);
        Check(retake is { Epoch: 2 } && Renewed(await Read(owner)),
            "retake replaces all three stale values and preserves the renewed node deadline");

        var other = await store.TakeAsync(0, old, lease);
        Check(other is null && Renewed(await Read(old)), "a refused contender advertises its abilities on its own renewal");
        await using var final = create();
        Check(await final.Partitions.AsNoTracking().SingleAsync(p => p.Id == 0) is { Owner: owner, Epoch: 2 },
            "the active owner keeps its partition and epoch after the contender is refused");

        async Task<NodeEntity> Read(string id)
        {
            await using var db = create();
            return await db.Nodes.AsNoTracking().SingleAsync(row => row.Id == id);
        }

        async Task Stale(string id)
        {
            await using var db = create();
            await db.Nodes.Where(row => row.Id == id).ExecuteUpdateAsync(set => set
                .SetProperty(row => row.ReadableStoreSchema, "1,2")
                .SetProperty(row => row.ReadableMessageProtocol, "2,3")
                .SetProperty(row => row.ReadableJournalCodec, "3,4"));
        }

        bool Renewed(NodeEntity row) => Advertises(row) && row.SeenAt > 0 &&
            row.LeaseUntil == row.SeenAt + (long)lease.TotalMilliseconds;
    }

    private static bool Advertises(NodeEntity row) =>
        row.ReadableStoreSchema == StoreVersionEntity.StoreSchemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture) &&
        row.ReadableMessageProtocol == StoreVersionEntity.MessageProtocolVersion.ToString(System.Globalization.CultureInfo.InvariantCulture) &&
        row.ReadableJournalCodec == StoreVersionEntity.JournalCodecVersion.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static void Check(bool result, string message)
    {
        if (!result) throw new InvalidOperationException(message);
        Console.WriteLine($"ok {message}");
    }
}
