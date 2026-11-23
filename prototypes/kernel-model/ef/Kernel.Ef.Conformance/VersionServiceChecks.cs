using Kernel.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace Kernel.Ef.Conformance;

internal static class VersionServiceChecks
{
    internal static async Task Run(Func<KernelDb> create, IKernelStore store)
    {
        const string actor = "0/versioned-actor";
        var journal = new JournalRow(0, 1, actor, 1, "[]");
        await using (var db = create())
        {
            var partition = await db.Partitions.SingleAsync(row => row.Id == 0);
            partition.Position = 1;
            db.Journal.Add(new JournalEntity
            {
                Partition = journal.Partition, Position = journal.Position, Instance = journal.Instance,
                Revision = journal.Revision, Entries = journal.Entries
            });
            db.Messages.Add(new MessageEntity { Id = "retained", Target = actor, Kind = "input", Done = true, KeepUntil = 1 });
            db.Events.Add(new EventEntity { Id = "expired", Key = "updates", KeepUntil = 1 });
            (await db.StoreVersions.SingleAsync()).JournalCodec = 2;
            await db.SaveChangesAsync();
        }

        foreach (var (operation, call) in new (string, Func<Task>)[]
        {
            ("projector position", () => store.ProjectedAsync(0).AsTask()),
            ("partition journal", () => store.ReadJournalAsync(0, 0, 8).AsTask()),
            ("actor journal", () => store.ReadJournalOfAsync(0, actor, 0).AsTask()),
            ("write projection", () => store.ProjectAsync(0, 0, 1, [journal]).AsTask()),
            ("read views", () => store.ReadViewsAsync([actor]).AsTask()),
            ("sweep", () => store.SweepAsync().AsTask())
        })
            await VersionTakeChecks.Refuses(call, "journal codec=2", operation);
        await using (var db = create())
        {
            Check((await db.Projections.SingleAsync(row => row.Partition == 0)).Position == 0 &&
                !await db.Views.AnyAsync() && await db.Messages.AnyAsync() && await db.Events.AnyAsync(),
                "a refused service performs no projection or retention changes");
            (await db.StoreVersions.SingleAsync()).JournalCodec = StoreVersionEntity.JournalCodecVersion;
            await db.SaveChangesAsync();
        }

        Check(await store.ProjectedAsync(0) == new Projection(0, 1) &&
            (await store.ReadJournalAsync(0, 0, 8)).SequenceEqual([journal]) &&
            (await store.ReadJournalOfAsync(0, actor, 0)).SequenceEqual([journal]), "supported services read the committed journal");
        Check(await store.ProjectAsync(0, 0, 1, [journal]) &&
            await store.ProjectedAsync(0) == new Projection(1, 1) &&
            (await store.ReadViewsAsync([actor])).Single().Revision == 1, "a supported projector advances its view");
        await store.SweepAsync();
        await using var verified = create();
        Check(!await verified.Messages.AnyAsync() && !await verified.Events.AnyAsync(),
            "a supported sweeper removes expired retained data");
    }

    private static void Check(bool valid, string message)
    {
        if (!valid) throw new InvalidOperationException(message);
        Console.WriteLine($"ok {message}");
    }
}
