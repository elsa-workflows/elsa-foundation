using Kernel.Durable;
using Kernel.EntityFramework;
using Kernel.Foundation.Postgres;
using Kernel.Runtime;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Kernel.Native.Run;

internal static class SqliteShippedInstallChecks
{
    private const int CheckConstraint = 275;
    private const int TriggerConstraint = 1811;
    private const int DataTypeConstraint = 3091;

    internal static async Task<int> RunAsync()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var token = deadline.Token;
        var folder = Directory.CreateTempSubdirectory("sqlite-shipped-install");
        var path = Path.Combine(folder.FullName, "kernel.db");
        DbContextOptions<KernelDb> Options(bool foreignKeys = true) =>
            new DbContextOptionsBuilder<KernelDb>().UseSqlite(new SqliteConnectionStringBuilder
            { DataSource = path, ForeignKeys = foreignKeys, Pooling = false }.ToString()).Options;
        KernelDb New(bool foreignKeys = true) => KernelDb.ForSqliteRootShipped(Options(foreignKeys));

        try
        {
            await using var db = New();
            await EfStore.InstallSqliteShippedAsync(db, 2, token);
            var initialKey = await IssuerKeyAsync();
            await EfStore.InstallSqliteShippedAsync(db, 2, token);
            var reinstalledKey = await IssuerKeyAsync();
            Require(initialKey.SequenceEqual(reinstalledKey) && await db.Partitions.CountAsync(token) == 2,
                "a clean SQLite shipped install and its reinstall keep one issuer and the requested partitions");

            await CheckRetirementAndGenerationAsync(db, token);
            var staged = new EfStore(() => New(), postgres: false);
            var ordinary = new EfStore(() => new KernelDb(Options()), postgres: false);
            (string Name, Func<EfStore, Task> Run)[] operations =
            [
                ("generic install", store => store.CreateAsync(2)),
                ("take", store => store.TakeAsync(0, "owner", TimeSpan.FromSeconds(10), token).AsTask()),
                ("heartbeat", store => store.HeartbeatAsync("owner", TimeSpan.FromSeconds(10), token).AsTask()),
                ("fence", store => store.FenceAsync([new(0, 0, 0, 1)], token).AsTask()),
                ("release", store => store.ReleaseAsync([new(0, 0, 0)], token).AsTask()),
                ("single poll", store => store.PollAsync(0, 1, TimeSpan.Zero, 1, token).AsTask()),
                ("set poll", store => store.PollAsync([new(0, 1, 0)], TimeSpan.Zero, 1, token).AsTask()),
                ("ordinary send", store => store.AddAsync(new("ordinary", InstanceRef.In(0, "root"), "advance"), token).AsTask()),
                ("event publish", store => store.PublishAsync(new("topic", "event", null, 10), token).AsTask()),
                ("subscription", store => store.SubscribeAsync(new("topic", "root", "wait"), token).AsTask())
            ];
            foreach (var (name, run) in operations)
            {
                await RefusedAsync<NotSupportedException>(() => run(staged), $"the SQLite install profile refuses {name}");
                await RefusedAsync<StoreFormatException>(() => run(ordinary), $"the ordinary SQLite profile refuses {name} on the shipped file");
            }
            await using (var wrongProvider = PostgresPreviewHosting.CreateShippedContext(Options()))
                await RefusedAsync<NotSupportedException>(() => Task.FromResult(wrongProvider.Model),
                    "the PostgreSQL shipped context refuses SQLite at model build");

            await RefusedAsync<SqliteException>(() => db.Database.ExecuteSqlRawAsync("""
                UPDATE "KernelStoreVersions" SET "MessageProtocol" = 'wrong-revision'
                """, token), "SQLite requires integer version fields", DataTypeConstraint);
            await RefusedAsync<SqliteException>(() => db.Database.ExecuteSqlRawAsync("""
                UPDATE "FoundationRootIssuer" SET "Key" = 'abcdefghijklmnopqrstuvwxyzabcdef'
                """, token), "SQLite requires a BLOB issuer key", DataTypeConstraint);
            var marker = StoreDefinitionsRevision.Seed(db)!;
            await db.StoreVersions.Where(row => row.Id == marker.Id).ExecuteUpdateAsync(
                row => row.SetProperty(x => x.MessageProtocol, marker.MessageProtocol + 1), token);
            await RefusedAsync<StoreFormatException>(() => EfStore.InstallSqliteShippedAsync(db, 2, token),
                "SQLite refuses an unsupported definitions revision");
            await db.StoreVersions.Where(row => row.Id == marker.Id).ExecuteUpdateAsync(
                row => row.SetProperty(x => x.StoreSchema, marker.StoreSchema + 1)
                    .SetProperty(x => x.MessageProtocol, marker.MessageProtocol), token);
            await RefusedAsync<StoreFormatException>(() => EfStore.InstallSqliteShippedAsync(db, 2, token),
                "SQLite refuses a definitions marker for a different engine");
            await db.StoreVersions.Where(row => row.Id == marker.Id).ExecuteDeleteAsync(token);
            await RefusedAsync<StoreFormatException>(() => EfStore.InstallSqliteShippedAsync(db, 2, token),
                "SQLite refuses an absent definitions marker");
            db.StoreVersions.Add(marker);
            await db.SaveChangesAsync(token);

            await using (var uncheckedForeignKeys = New(foreignKeys: false))
                await RefusedAsync<StoreFormatException>(() => EfStore.InstallSqliteShippedAsync(uncheckedForeignKeys, 2, token),
                    "SQLite refuses a connection with foreign keys disabled");

            await db.Database.OpenConnectionAsync(token);
            await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode = OFF", token);
            await RefusedAsync<StoreFormatException>(() => EfStore.InstallSqliteShippedAsync(db, 2, token),
                "SQLite refuses installation without a durable journal");
            await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode = DELETE", token);
            await db.Database.ExecuteSqlRawAsync("PRAGMA ignore_check_constraints = ON", token);
            await RefusedAsync<StoreFormatException>(() => EfStore.InstallSqliteShippedAsync(db, 2, token),
                "SQLite refuses installation with CHECK constraints disabled");
            await db.Database.ExecuteSqlRawAsync("PRAGMA ignore_check_constraints = OFF", token);
            await db.Database.ExecuteSqlRawAsync("""CREATE TEMP TABLE "Instances" ("Id" TEXT)""", token);
            await RefusedAsync<StoreFormatException>(() => EfStore.InstallSqliteShippedAsync(db, 2, token),
                "SQLite refuses temporary objects that can shadow the main schema");
            await db.Database.ExecuteSqlRawAsync("""DROP TABLE temp."Instances" """, token);
            await db.Database.ExecuteSqlRawAsync("""ATTACH DATABASE ':memory:' AS unexpected""", token);
            await RefusedAsync<StoreFormatException>(() => EfStore.InstallSqliteShippedAsync(db, 2, token),
                "SQLite refuses an attached database");
            await db.Database.ExecuteSqlRawAsync("DETACH DATABASE unexpected", token);
            await db.Database.ExecuteSqlRawAsync("DROP TRIGGER kernel_physical_retirement", token);
            await db.Database.ExecuteSqlRawAsync("""
                CREATE TRIGGER kernel_physical_retirement BEFORE UPDATE ON Instances BEGIN SELECT 1; END
                """, token);
            await RefusedAsync<StoreFormatException>(() => EfStore.InstallSqliteShippedAsync(db, 2, token),
                "SQLite refuses a trigger whose name is intact and whose body changed");
            return 0;

            Task<byte[]> IssuerKeyAsync() => db.Database.SqlQueryRaw<byte[]>("""
                SELECT "Key" AS "Value" FROM "FoundationRootIssuer" WHERE "Singleton" = 1
                """).SingleAsync(token);
        }
        finally { folder.Delete(recursive: true); }
    }

    private static async Task CheckRetirementAndGenerationAsync(KernelDb db, CancellationToken token)
    {
        await SeedAsync("direct", InstanceStatus.Running);
        await SeedAsync("atomic", InstanceStatus.Running);
        await SeedAsync("parked", InstanceStatus.Faulted);
        await RefusedAsync<SqliteException>(() => db.Database.ExecuteSqlRawAsync("""
            UPDATE "Instances" SET "Retired" = 1 WHERE "Id" = 'direct'
            """, token), "a direct retirement refuses a running instance");
        await RefusedAsync<SqliteException>(() => db.Database.ExecuteSqlRawAsync("""
            UPDATE "Instances" SET "NextJobGeneration" = 0 WHERE "Id" = 'direct'
            """, token), "the job generation trigger refuses a rewind");

        await RefusedAsync<SqliteException>(() => db.Database.ExecuteSqlRawAsync("""
            UPDATE "Instances" SET "Retired" = 2 WHERE "Id" = 'direct'
            """, token), "SQLite keeps the retirement flag in the Boolean domain", CheckConstraint);
        await RefusedAsync<SqliteException>(() => db.Database.ExecuteSqlRawAsync("""
            UPDATE "Instances" SET "NextJobGeneration" = 'wrong-type' WHERE "Id" = 'direct'
            """, token), "SQLite keeps job generations in the integer domain", DataTypeConstraint);
        await RefusedAsync<SqliteException>(() => db.Database.ExecuteSqlRawAsync("""
            UPDATE "Instances" SET "FoundationParkClearing" = 'operation' WHERE "Id" = 'direct'
            """, token), "the Park clearing columns require the paired count and a parked root", CheckConstraint);

        foreach (var id in new[] { "atomic", "parked" })
        {
            await RefusedAsync<SqliteException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE "Instances" SET "Retired" = 1, "Status" = {(int)InstanceStatus.Cancelled}, "Revision" = 2,
                    "FoundationWriteRevision" = 2 WHERE "Id" = {id}
                """, token), "an atomic retirement requires its terminal mark");
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE "Instances" SET "Retired" = 1, "Status" = {(int)InstanceStatus.Cancelled}, "Revision" = 2,
                    "FoundationWriteRevision" = 2, "FoundationTerminalRevision" = 2, "FoundationTerminalAt" = 100
                WHERE "Id" = {id}
                """, token);
            await RefusedAsync<SqliteException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE "Instances" SET "Retired" = 0 WHERE "Id" = {id}
                """, token), "the retirement trigger refuses a reversal");
        }
        Require(await db.Instances.CountAsync(instance => instance.Retired, token) == 2,
            "the physical guard accepts qualified running and parked atomic retirements");

        async Task SeedAsync(string id, InstanceStatus status)
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "FoundationRootBirths" ("Reference", "Tenant", "Actor", "Partition", "Creation", "Creator", "Fingerprint")
                VALUES ({id}, 'tenant', {id}, 0, 1, {id}, 'fingerprint')
                """, token);
            db.Instances.Add(new InstanceEntity
            {
                Id = id, Partition = 0, Model = "model", Definition = "definition", State = "null",
                Status = status, Revision = 1, NextJobGeneration = 1
            });
            await db.SaveChangesAsync(token);
            var parkedAt = status == InstanceStatus.Faulted ? (long?)100 : null;
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE "Instances" SET "FoundationReference" = {id}, "FoundationParkAt" = {parkedAt} WHERE "Id" = {id}
                """, token);
        }
    }

    private static async Task RefusedAsync<T>(Func<Task> action, string witness, int sqliteCode = TriggerConstraint) where T : Exception
    {
        try { await action(); }
        catch (T error) when (error is not SqliteException sqlite || sqlite.SqliteExtendedErrorCode == sqliteCode)
        {
            Require(true, witness);
            return;
        }
        throw new InvalidOperationException(witness);
    }

    private static void Require(bool ok, string witness)
    {
        if (!ok) throw new InvalidOperationException(witness);
        Console.WriteLine($"ok {witness}");
    }
}
