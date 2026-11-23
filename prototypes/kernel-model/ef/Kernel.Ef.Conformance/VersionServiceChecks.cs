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
            await db.SaveChangesAsync();
        }

        (string Operation, Func<Task> Call)[] services =
        [
            ("projector position", () => store.ProjectedAsync(0).AsTask()),
            ("partition journal", () => store.ReadJournalAsync(0, 0, 8).AsTask()),
            ("actor journal", () => store.ReadJournalOfAsync(0, actor, 0).AsTask()),
            ("write projection", () => store.ProjectAsync(0, 0, 1, [journal]).AsTask()),
            ("read views", () => store.ReadViewsAsync([actor]).AsTask()),
            ("sweep", () => store.SweepAsync().AsTask()),
            ("requested projection", () => new Projector(store, 0).CatchUpAsync())
        ];
        foreach (var unsupported in VersionTakeChecks.IncompatibleFormats())
        {
            await VersionTakeChecks.SetFormatAsync(create, unsupported);
            foreach (var (operation, call) in services)
                await VersionTakeChecks.Refuses(call, $"Stored {unsupported}.", operation);
            await CheckUnchangedAsync(unsupported);
        }

        var current = StoreFormat.Current;
        var higherCodec = current with { JournalCodec = current.JournalCodec + 1 };
        await VersionTakeChecks.SetFormatAsync(create, higherCodec);
        var reports = new List<StoreFormatException>();
        var background = new NodeProjection(store, reportFormatFailure: reports.Add);
        background.Nudge([0, 1]);
        await background.Idle.WaitAsync(TimeSpan.FromSeconds(10));
        Check(reports is [var reported] && ReferenceEquals(reported, background.FormatFailure) &&
            reported.Message.Contains($"Stored {higherCodec}.", StringComparison.Ordinal),
            "scheduled projection reports the stored format to its host");
        background.Nudge([0, 1]);
        Check(background.Idle.IsCompletedSuccessfully && background.Rounds == 1, "scheduled projection stops on an incompatible format");
        await CheckUnchangedAsync(higherCodec);
        await VersionTakeChecks.SetFormatAsync(create, current);

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

        async Task CheckUnchangedAsync(StoreFormat unsupported)
        {
            await using var db = create();
            var unchangedJournal = await db.Journal.AsNoTracking().SingleAsync();
            Check((await db.Projections.SingleAsync(row => row.Partition == 0)).Position == 0 &&
                !await db.Views.AnyAsync() &&
                new JournalRow(unchangedJournal.Partition, unchangedJournal.Position, unchangedJournal.Instance,
                    unchangedJournal.Revision, unchangedJournal.Entries) == journal &&
                await db.Messages.AnyAsync(row => row.Id == "retained" && row.Done && row.KeepUntil == 1) &&
                await db.Events.AnyAsync(row => row.Id == "expired" && row.Key == "updates" && row.KeepUntil == 1) &&
                (await db.StoreVersions.SingleAsync()).Format == unsupported,
                $"refused services preserve the journal, cursor, views, retained data and marker for {unsupported}");
        }

    }

    private static void Check(bool valid, string message)
    {
        if (!valid) throw new InvalidOperationException(message);
        Console.WriteLine($"ok {message}");
    }
}
