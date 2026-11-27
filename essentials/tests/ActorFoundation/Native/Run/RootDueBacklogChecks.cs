using Kernel.Durable;
using Kernel.EntityFramework;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Kernel.Native.Run;

internal static class RootDueBacklogChecks
{
    internal static async Task<int> RunAsync(bool postgres, bool inBatch)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(1));
        var token = timeout.Token;
        var directory = Directory.CreateTempSubdirectory("root-due-backlog-");
        try
        {
            var builder = new DbContextOptionsBuilder<KernelDb>();
            SqliteAdmissionDefinitions? definitions = null;
            if (postgres)
                builder.UseNpgsql(Environment.GetEnvironmentVariable("KERNEL_PREVIEW_POSTGRES")
                    ?? throw new InvalidOperationException("Use an empty dedicated PostgreSQL database."));
            else
            {
                builder.UseSqlite(new SqliteConnectionStringBuilder
                {
                    DataSource = Path.Combine(directory.FullName, "backlog.db"), ForeignKeys = true, Pooling = false
                }.ToString());
                definitions = new();
            }

            KernelDb Open() => KernelDb.ForRootDueBacklogQualification(builder.Options, definitions);
            await using var db = Open();
            var store = new EfStore(Open, postgres, inBatch) { CommitSql = CommitSql.Owned };
            if (postgres)
            {
                Check(await PostgresFixtureDatabase.IsEmptyAsync(db, token), "the PostgreSQL fixture is empty");
                await store.CreateAsync(2);
                await new PostgresRootAdmission(Open, TimeSpan.Zero).InstallAsync(token);
            }
            else
                await EfStore.InstallSqliteShippedAsync(db, 2, token);

            await using var shipped = postgres ? KernelDb.ForPostgresRootShipped(builder.Options) : KernelDb.ForSqliteRootShipped(builder.Options);
            try
            {
                if (postgres) await EfStore.RequireStoreVersionsAsync(shipped, token);
                else await EfStore.InstallSqliteShippedAsync(shipped, 2, token);
                throw new InvalidOperationException("The shipped marker admitted the qualification store.");
            }
            catch (StoreFormatException) { Console.WriteLine("ok shipped format refuses the qualification store"); }

            await store.EnableRootJobsAsync(token);
            if (postgres) store.BindWorkerNode();
            else store.BindSqliteWorkerNode();
            IDueBacklogStore backlog = store;
            Check(backlog.MeasuresDueBacklog, "the owned root exposes due-backlog measurements");
            db.Messages.AddRange(
                Row(1, 0), Row(2, 0), Row(3, 0, due: long.MaxValue), Row(4, 0, done: true), Row(5, 1));
            await db.SaveChangesAsync(token);
            Check(await backlog.CountDueBacklogAsync(0, 1, token) == 1 &&
                await backlog.CountDueBacklogAsync(0, 10, token) == 2 &&
                await backlog.CountDueBacklogAsync(1, 10, token) == 1,
                "the capped count checks the partition, clock and pending status");

            var lease = TimeSpan.FromSeconds(30);
            var owned = await store.TakeAsync([0, 1], "pressure-sampler", lease, token);
            Check(owned.Taken.Count == 2, "the worker takes both fences");
            var sampler = new BacklogSampling(backlog, new(1, 1, TimeSpan.FromSeconds(1), TimeSpan.Zero));
            await sampler.ReadAsync(owned.Taken, owned.Now, token);
            var estimates = sampler.ForCommit(owned.Taken);
            Check(estimates is not null && estimates[0] == 2 && estimates[1] == 1,
                "the owner samples both partitions for its actual epoch");
            var batch = CommitBatch.Owned(owned.Taken, lease, false, [], [], []) with { DueBacklog = estimates };
            Check((await store.CommitAsync(batch, token)).Landed &&
                await backlog.ReadPublishedDueBacklogAsync(0, token) == 2 &&
                await backlog.ReadPublishedDueBacklogAsync(1, token) == 1,
                "one pressure-only fenced commit publishes both estimates");

            var staleFences = new[] { owned.Taken[0], owned.Taken[1] with { Position = owned.Taken[1].Position + 1 } };
            var stale = CommitBatch.Owned(staleFences, lease, false, [], [], []) with { DueBacklog = new Dictionary<int, int> { [0] = 9 } };
            Check(!(await store.CommitAsync(stale, token)).Landed &&
                await backlog.ReadPublishedDueBacklogAsync(0, token) == 2,
                "a stale sibling fence rolls back the other partition's write");

            foreach (var row in db.Messages.Local.Where(row => row.Partition == 0 && row.Due == 0)) row.Done = true;
            await db.SaveChangesAsync(token);
            await store.ReleaseAsync(owned.Taken, token);
            var successor = await store.TakeAsync([0, 1], "next-sampler", lease, token);
            Check(successor.Taken.Count == 2 && successor.Taken[0].Epoch > owned.Taken[0].Epoch,
                "the successor owns a new epoch");
            await sampler.ReadAsync(successor.Taken, successor.Now, token);
            var reduced = sampler.ForCommit(successor.Taken);
            Check(reduced is { Count: 1 } && reduced[0] == 0 &&
                sampler.PressureFences(successor.Taken, successor.Now).SequenceEqual([successor.Taken[0]]),
                "the new epoch detects relief against the inherited estimate");
            var relief = CommitBatch.Owned(successor.Taken, lease, false, [], [], []) with { DueBacklog = reduced };
            Check((await store.CommitAsync(relief, token)).Landed &&
                await backlog.ReadPublishedDueBacklogAsync(0, token) == 0 &&
                await backlog.ReadPublishedDueBacklogAsync(1, token) == 1,
                "partial relief leaves the other estimate intact");

            db.Messages.Local.Single(row => row.Partition == 1).Done = true;
            await db.SaveChangesAsync(token);
            var one = successor.Taken[1];
            var single = new CommitBatch(one.Partition, one.Epoch, one.Position, lease, false, [], [], [])
                { DueBacklog = new Dictionary<int, int> { [one.Partition] = await backlog.CountDueBacklogAsync(one.Partition, 10, token) } };
            Check((await store.CommitAsync(single, token)).Landed && await backlog.ReadPublishedDueBacklogAsync(1, token) == 0,
                "the single-row fence also publishes relief");
            return 0;
        }
        finally { directory.Delete(recursive: true); }
    }

    private static MessageEntity Row(int sequence, int partition, long due = 0, bool done = false) => new()
    {
        Id = "depth-" + sequence, Seq = sequence, Partition = partition, Target = partition + "/depth", Kind = "depth",
        Due = due, Done = done
    };

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException(description);
        Console.WriteLine("ok " + description);
    }
}
