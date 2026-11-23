using Kernel.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace Kernel.Ef.Conformance;

internal static class RootJournalChecks
{
    internal static async Task Run(Func<KernelDb> create, EfStore store, EfStore reader)
    {
        const string tenant = "preview";
        var ingress = new PostgresRootAdmission(create, TimeSpan.FromMinutes(1));
        await ingress.InstallAsync();
        var spec = new StartSpec("workflow", "definition");
        var first = await ingress.AdmitAsync(tenant, "first", spec);
        var second = await ingress.AdmitAsync(tenant, "second", spec);
        var pending = await reader.ReadRootJournalAsync(tenant, first.Reference, 0, 1);
        Check(pending.Rows.Count == 0 && pending.AsOf > 0, "a pending creator has no committed journal");
        var firstPosition = await Start(first.Reference, """["first:waiting","frame:1"]""");
        await Start(second.Reference, """["second:waiting"]""");

        var own = await reader.ReadRootJournalAsync(tenant, first.Reference, 0, 1);
        Check(own.Rows is [{ Revision: 1, Entries: """["first:waiting","frame:1"]""" } entry] &&
              entry.Position == firstPosition && own.AsOf >= pending.AsOf,
            "a fresh qualified reader observes only its committed checkpoint entries");
        Check((await reader.ReadRootJournalAsync("other", first.Reference, 0, 1)).Rows.Count == 0 &&
              (await reader.ReadRootJournalAsync(tenant, "missing", 0, 1)).Rows.Count == 0 &&
              (await reader.ReadRootJournalAsync(tenant, second.Reference, 0, 1)).Rows.Single().Entries == """["second:waiting"]""",
            "tenant and exact reference scope the journal");
        var inclusive = await reader.ReadRootJournalAsync(tenant, first.Reference, firstPosition, 1);
        var after = await reader.ReadRootJournalAsync(tenant, first.Reference, firstPosition + 1, 1);
        Check(inclusive.Rows.SequenceEqual(own.Rows) && after.Rows.Count == 0 && after.AsOf >= own.AsOf,
            "the physical lower bound can continue within one commit and reports a database timestamp on empty pages");

        await CheckReaderOpen(create);

        async Task<long> Start(string reference, string journal)
        {
            var creator = (await ingress.ReadPendingCreatorAsync(tenant, reference))!;
            var lease = TimeSpan.FromSeconds(30);
            var taken = (await store.TakeAsync(creator.Target.Partition, "journal-fixture", lease))!;
            var fence = new PartitionFence(creator.Target.Partition, taken.Epoch, taken.Position);
            var committed = await store.CommitFirstRootAsync(tenant, reference, creator, fence, lease, "waiting", 1, "workflow",
                new Dictionary<string, string>(), journal);
            Check(committed.Landed, "the first checkpoint and journal land together");
            return fence.Position + 1;
        }
    }

    private static async Task CheckReaderOpen(Func<KernelDb> create)
    {
        var reader = new PostgresRootAdmission(create, TimeSpan.FromMinutes(1));
        foreach (var (change, versions) in new (Action<StoreVersionEntity>, string)[]
        {
            (row => row.StoreSchema = 2, "schema=2, message protocol=1, journal codec=1"),
            (row => row.MessageProtocol = 0, "schema=1, message protocol=0, journal codec=1"),
            (row => row.JournalCodec = 2, "schema=1, message protocol=1, journal codec=2"),
        })
        {
            await using var db = create();
            var marker = await db.StoreVersions.SingleAsync();
            change(marker);
            await db.SaveChangesAsync();
            await VersionTakeChecks.Refuses(() => reader.OpenJournalCursorAsync(default), versions, "public reader startup");
            marker.StoreSchema = StoreVersionEntity.StoreSchemaVersion;
            marker.MessageProtocol = StoreVersionEntity.MessageProtocolVersion;
            marker.JournalCodec = StoreVersionEntity.JournalCodecVersion;
            await db.SaveChangesAsync();
        }

        await reader.OpenJournalCursorAsync(default);
        Check(true, "a public reader opens its cursor with the supported format");
        await using var missing = create();
        await missing.StoreVersions.ExecuteDeleteAsync();
        await VersionTakeChecks.Refuses(() => reader.OpenJournalCursorAsync(default), "version marker is missing", "public reader startup");
    }

    private static void Check(bool valid, string description)
    {
        if (!valid) throw new InvalidOperationException(description);
        Console.WriteLine($"ok {description}");
    }
}
