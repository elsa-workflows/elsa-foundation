using System.Globalization;
using Kernel.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace Kernel.Ef.Conformance;

internal static class FormatMigrationChecks
{
    internal static async Task Run(Func<KernelDb> create, EfStore store, EfStore oldBuild)
    {
        var current = StoreFormat.Current;
        var next = current with { JournalCodec = 2 };
        var lease = TimeSpan.FromMinutes(2);
        await using (var db = create())
        {
            db.Journal.Add(new JournalEntity { Partition = 0, Position = 1, Instance = "0/migrated", Revision = 1, Entries = "[]" });
            await db.SaveChangesAsync();
        }
        Check(await store.TakeAsync(0, "original", lease) is { Epoch: 1 }, "current reader owns a partition");
        await VersionTakeChecks.Refuses(() => store.AdvanceStoreFormatAsync(current, next, UnexpectedMigration), "Live node 'original'",
            "migration with a live incompatible node");
        Check(await ReadVersion() == current, "a refused migration keeps the version and ownership");

        await SetNode(node =>
        {
            node.ReadableStoreSchema = "";
            node.ReadableMessageProtocol = "";
            node.ReadableJournalCodec = "";
        });
        await VersionTakeChecks.Refuses(() => store.AdvanceStoreFormatAsync(current, next), "Live node 'original'",
            "migration with an unadvertised live node");
        await SetNode(node =>
        {
            node.ReadableStoreSchema = current.StoreSchema.ToString(CultureInfo.InvariantCulture);
            node.ReadableMessageProtocol = current.MessageProtocol.ToString(CultureInfo.InvariantCulture);
            node.ReadableJournalCodec = "12";
        });
        await VersionTakeChecks.Refuses(() => store.AdvanceStoreFormatAsync(current, next), "journal codec=[12]",
            "migration checks a whole version in each readable set");

        var beforeHeartbeat = await ReadLease();
        await SetNode(node => node.LeaseUntil = 0);
        try
        {
            await store.AdvanceStoreFormatAsync(current, next, async (db, token) =>
            {
                await WriteJournal(db, token);
                throw new OperationCanceledException("migration fixture interrupted after changing data");
            });
            throw new InvalidOperationException("Expected the fixture migration to stop.");
        }
        catch (OperationCanceledException) { }
        Check(await ReadVersion() == current && await ReadJournal() == "[]",
            "an interrupted data change rolls back with the format marker");
        Check(await store.AdvanceStoreFormatAsync(current, next, WriteJournal),
            "an expired incompatible node does not prevent a coordinated advance");
        Check(await ReadVersion() == next && await ReadLease() == 0 && beforeHeartbeat > 0 && await ReadJournal() == "[2]",
            "the advance publishes the data and format together without changing leases");
        Check(!await store.AdvanceStoreFormatAsync(current, next, UnexpectedMigration),
            "a retry observes the same advance without executing the data change again");
        await VersionTakeChecks.Refuses(() => oldBuild.TakeAsync(1, "original", lease).AsTask(), "journal codec=2",
            "an old cold owner");
        await VersionTakeChecks.Refuses(() => oldBuild.NodeHeartbeatAsync("original", lease, lease).AsTask(), "journal codec=2",
            "an old owner renewing after the format advanced");
        await VersionTakeChecks.Refuses(() => oldBuild.SweepAsync().AsTask(), "journal codec=2", "an old sweeper");
        Check(await ReadLease() == 0, "the old process cannot renew a lease or acquire a partition");

        await SetNode(node =>
        {
            node.LeaseUntil = long.MaxValue;
            node.ReadableMessageProtocol = "1,2";
            node.ReadableJournalCodec = "1,2";
        });
        var newer = next with { MessageProtocol = 2 };
        Check(await store.AdvanceStoreFormatAsync(next, newer) && await ReadVersion() == newer,
            "a live node that advertises every target version can remain present");

        await using var snapshot = create();
        Check((await snapshot.Partitions.OrderBy(row => row.Id).ToArrayAsync()) is
            [{ Owner: "original", Epoch: 1 }, { Owner: null, Epoch: 0 }],
            "only the first compatible take changed the partition records");

        async Task<StoreFormat> ReadVersion()
        {
            await using var db = create();
            return (await db.StoreVersions.SingleAsync()).Format;
        }

        async Task<long> ReadLease()
        {
            await using var db = create();
            return await db.Nodes.Select(node => node.LeaseUntil).SingleAsync();
        }

        async Task<string> ReadJournal()
        {
            await using var db = create();
            return await db.Journal.Select(row => row.Entries).SingleAsync();
        }

        static async Task WriteJournal(KernelDb db, CancellationToken token)
        {
            var row = await db.Journal.SingleAsync(token);
            row.Entries = "[2]";
            await db.SaveChangesAsync(token);
        }

        static Task UnexpectedMigration(KernelDb db, CancellationToken token) =>
            throw new InvalidOperationException("This outcome should never execute the data migration.");

        async Task SetNode(Action<NodeEntity> change)
        {
            await using var db = create();
            change(await db.Nodes.SingleAsync());
            await db.SaveChangesAsync();
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        Console.WriteLine($"ok {message}");
    }
}
