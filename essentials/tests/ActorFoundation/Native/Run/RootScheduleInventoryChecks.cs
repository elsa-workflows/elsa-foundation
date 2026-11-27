using System.Data.Common;
using Kernel.Durable;
using Kernel.EntityFramework;
using Kernel.WorkflowActor;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Native.Run;

internal static class RootScheduleInventoryChecks
{
    internal static async Task<int> RunAsync(bool postgres)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(1));
        var token = timeout.Token;
        var directory = postgres ? null : Directory.CreateTempSubdirectory("root-schedule-inventory-");
        try
        {
            var options = new DbContextOptionsBuilder<KernelDb>();
            var barrier = new PostgresClaimBarrier();
            if (postgres)
                options.UseNpgsql(Environment.GetEnvironmentVariable("KERNEL_PREVIEW_POSTGRES")
                    ?? throw new InvalidOperationException("Use a dedicated empty PostgreSQL qualification database."))
                    .AddInterceptors(barrier);
            else
                options.UseSqlite(new SqliteConnectionStringBuilder
                {
                    DataSource = Path.Combine(directory!.FullName, "schedules.db"), ForeignKeys = true, Pooling = false, DefaultTimeout = 1
                }.ConnectionString);

            var format = StoreFormat.RootScheduleInventoryQualification;
            KernelDb Open() => postgres ? KernelDb.ForPostgresRootScheduleInventoryQualification(options.Options)
                : KernelDb.ForSqliteRootShipped(options.Options, format);
            await using var db = Open();
            var pg = new PostgresRootAdmission(Open, TimeSpan.Zero, scheduleActorType: ScheduleActor.ActorType);
            var sqlite = new SqliteRootAdmission(Open, TimeSpan.Zero, new SqliteAdmissionDefinitions(), scheduleActorType: ScheduleActor.ActorType);
            if (postgres)
            {
                Require(await PostgresFixtureDatabase.IsEmptyAsync(db, token), "the PostgreSQL qualification database is empty");
                await new EfStore(Open, postgres: true).CreateAsync(2);
                await pg.InstallAsync(token);
            }
            else await EfStore.InstallSqliteShippedAsync(db, 2, token);

            const string typeData = """{"Schedule":{"Kind":"interval","Value":"PT1M"},"StartKey":"unused","Revision":1}""";
            var timer = new ScheduleRootKey("definition", "timer-a");
            try
            {
                if (postgres) await new PostgresRootAdmission(Open, TimeSpan.Zero).AdmitScheduleAsync("tenant-a", timer, typeData, token);
                else await new SqliteRootAdmission(Open, TimeSpan.Zero, new SqliteAdmissionDefinitions()).AdmitScheduleAsync("tenant-a", timer, typeData, token);
                throw new InvalidOperationException("An ordinary ingress admitted a schedule creator.");
            }
            catch (NotSupportedException) { Console.WriteLine("ok ordinary ingress cannot reserve a schedule key"); }
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var tasks = Enumerable.Range(0, 6).Select(_ => Task.Run(async () =>
            {
                await start.Task;
                return await AdmitAsync("tenant-a", timer);
            }, token)).ToArray();
            start.SetResult();
            var contenders = await Task.WhenAll(tasks);
            var reference = contenders.Single(item => item.Outcome == Contract.AdmissionOutcome.Accepted).Actor?.Id
                ?? throw new InvalidOperationException("The winning admission supplied no actor reference.");
            Require(contenders.Count(item => item.Outcome == Contract.AdmissionOutcome.Duplicate && item.Actor?.Id == reference) == 5,
                "six contending admissions yield one exact schedule actor");
            if (postgres)
                Require(barrier.ConfirmedOverlap, "two PostgreSQL transactions saw a missing key before attempting the same insert");

            var again = postgres
                ? await new PostgresRootAdmission(Open, TimeSpan.Zero, scheduleActorType: ScheduleActor.ActorType).AdmitScheduleAsync("tenant-a", timer, typeData, token)
                : await new SqliteRootAdmission(Open, TimeSpan.Zero, new SqliteAdmissionDefinitions(), scheduleActorType: ScheduleActor.ActorType)
                    .AdmitScheduleAsync("tenant-a", timer, typeData, token);
            Require(again.Outcome == Contract.AdmissionOutcome.Duplicate && again.Actor?.Id == reference,
                "a fresh admission service recovers the creator before any actor checkpoint");
            Require(!await db.Instances.AnyAsync(token) && await db.Messages.CountAsync(row => row.InboxScope == 0 && !row.Done, token) == 1,
                "the inventory lists a queued creator and requires no actor checkpoint");
            try
            {
                if (postgres)
                    await new PostgresRootAdmission(Open, TimeSpan.Zero, scheduleActorType: "ordinary").AdmitScheduleAsync("tenant-a", timer, typeData, token);
                else await new SqliteRootAdmission(Open, TimeSpan.Zero, new SqliteAdmissionDefinitions(), scheduleActorType: "ordinary")
                    .AdmitScheduleAsync("tenant-a", timer, typeData, token);
                throw new InvalidOperationException("An incompatible host binding claimed the reserved key.");
            }
            catch (StoreFormatException) { Console.WriteLine("ok a different host-bound actor type cannot claim the existing schedule"); }

            var second = await AdmitAsync("tenant-a", new("definition", "timer-b"));
            var unrelated = await AdmitAsync("tenant-b", timer);
            _ = await AdmitAsync("tenant-a", new("another-definition", "timer-c"));
            Require(unrelated.Actor?.Id != reference, "another tenant owns its independent schedule");
            var page1 = await ReadAsync("tenant-a", "definition", null, 1);
            var page2 = await ReadAsync("tenant-a", "definition", page1.Next, 1);
            Require(page1 is { Roots: [{ Key: "timer-a" } first], Next: "timer-a" } && first.Reference == reference &&
                page2 is { Roots: [{ Key: "timer-b" } last], Next: null } && last.Reference == second.Actor?.Id,
                "restart inventory seeks within the exact tenant and definition without reading the checkpoint table");
            Require((await ReadAsync("missing", "definition", null, 1)).Roots.Count == 0,
                "a foreign tenant cannot see either schedule actor");
            if (!postgres)
            {
                await db.Database.OpenConnectionAsync(token);
                await using var writer = ((SqliteConnection)db.Database.GetDbConnection()).BeginTransaction(deferred: false);
                Require((await ReadAsync("tenant-a", "definition", null, 1)).Roots is [{ Key: "timer-a" }],
                    "the inventory reads a WAL snapshot while another connection holds the writer");
                await writer.RollbackAsync(token);
            }

            var ordinarySpec = new StartSpec("ordinary", "{}", History: History.Minimal);
            var ordinary = postgres
                ? (await pg.AdmitAsync("tenant-a", "ordinary", ordinarySpec, cancellationToken: token)).Reference
                : (await sqlite.StartAsync("tenant-a", "ordinary", ordinarySpec, null, null, token)).Actor?.Id;
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE "FoundationScheduleRoots" SET "Reference" = {ordinary}
                WHERE "Tenant" = 'tenant-a' AND "Key" = 'timer-a'
                """, token);
            try
            {
                await ReadAsync("tenant-a", "definition", null, 1);
                throw new InvalidOperationException("The inventory accepted an ordinary creator as its schedule root.");
            }
            catch (StoreFormatException) { Console.WriteLine("ok even an exact-tenant ordinary creator cannot satisfy schedule inventory"); }
            if (postgres)
            {
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE "FoundationScheduleRoots" SET "Reference" = {reference}
                    WHERE "Tenant" = 'tenant-a' AND "Key" = 'timer-a'
                    """, token);
                await db.Database.ExecuteSqlRawAsync("""
                    DROP INDEX "IX_FoundationScheduleRoots_Definition";
                    CREATE INDEX "IX_FoundationScheduleRoots_Definition"
                        ON "FoundationScheduleRoots" ("Tenant", "Definition", "Key" text_pattern_ops);
                    """, token);
                try
                {
                    await ReadAsync("tenant-a", "definition", null, 1);
                    throw new InvalidOperationException("An inventory read accepted an incompatible ordered index.");
                }
                catch (StoreFormatException) { Console.WriteLine("ok PostgreSQL refuses the wrong physical definition index"); }
                var pendingBefore = await db.Messages.CountAsync(token);
                try
                {
                    await AdmitAsync("tenant-a", new("definition", "drifted-index"));
                    throw new InvalidOperationException("A creator was admitted under an incompatible definition index.");
                }
                catch (StoreFormatException) { Console.WriteLine("ok PostgreSQL also refuses to admit a creator with that index"); }
                Require(await db.Messages.CountAsync(token) == pendingBefore, "schema refusal leaves the inbox unchanged");
            }

            await using var shipped = postgres ? KernelDb.ForPostgresRootShipped(options.Options) : KernelDb.ForSqliteRootShipped(options.Options);
            try
            {
                if (postgres) await EfStore.RequireStoreVersionsAsync(shipped, token);
                else await EfStore.InstallSqliteShippedAsync(shipped, 2, token);
                throw new InvalidOperationException("The shipped marker admitted the private schedule inventory.");
            }
            catch (StoreFormatException) { Console.WriteLine("ok shipped format refuses the private schedule inventory"); }
            return 0;

            Task<Contract.Admission> AdmitAsync(string tenant, ScheduleRootKey key) => postgres
                ? pg.AdmitScheduleAsync(tenant, key, typeData, token) : sqlite.AdmitScheduleAsync(tenant, key, typeData, token);
            Task<RootScheduleInventory.Page> ReadAsync(string tenant, string definition, string? after, int limit) => postgres
                ? pg.ReadScheduleRootsAsync(tenant, definition, after, limit, token)
                : sqlite.ReadScheduleRootsAsync(tenant, definition, after, limit, token);
        }
        finally
        {
            directory?.Delete(recursive: true);
        }
    }

    private static void Require(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException(description);
        Console.WriteLine("ok " + description);
    }

    private sealed class PostgresClaimBarrier : DbCommandInterceptor
    {
        private readonly TaskCompletionSource _contenders = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _reached;
        internal bool ConfirmedOverlap => Volatile.Read(ref _reached) >= 2;

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("INSERT INTO \"FoundationScheduleRoots\"", StringComparison.Ordinal))
            {
                if (Interlocked.Increment(ref _reached) >= 2) _contenders.TrySetResult();
                await _contenders.Task.WaitAsync(cancellationToken);
            }
            return result;
        }
    }
}
