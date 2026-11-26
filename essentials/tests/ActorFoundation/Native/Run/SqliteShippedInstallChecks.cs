using Kernel.Durable;
using Kernel.EntityFramework;
using Kernel.Runtime;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Kernel.Native.Run;

internal static class SqliteShippedInstallChecks
{
    private const int TriggerConstraint = 1811;

    internal static async Task<int> RunAsync()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var token = deadline.Token;
        var folder = Directory.CreateTempSubdirectory("sqlite-shipped-install");
        var path = Path.Combine(folder.FullName, "kernel.db");
        KernelDb New(bool foreignKeys = true) => KernelDb.ForSqliteRootShipped(
            new DbContextOptionsBuilder<KernelDb>().UseSqlite(new SqliteConnectionStringBuilder
            { DataSource = path, ForeignKeys = foreignKeys, Pooling = false }.ToString()).Options);

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
            var store = new EfStore(() => New(), postgres: false);
            await RefusedAsync<NotSupportedException>(() => store.CreateAsync(2), "the generic installer refuses the staged shipped format");
            await RefusedAsync<NotSupportedException>(() => store.TakeAsync(0, "owner", TimeSpan.FromSeconds(10), token).AsTask(),
                "a node cannot take ownership of the install-only store");
            await RefusedAsync<NotSupportedException>(() => store.PollAsync(0, 1, TimeSpan.Zero, 1, token).AsTask(),
                "the runtime cannot poll the install-only store");
            await RefusedAsync<NotSupportedException>(() => store.AddAsync(new("ordinary", InstanceRef.In(0, "root"), "advance"), token).AsTask(),
                "ordinary admission cannot bypass the install-only guard");

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

    private static async Task RefusedAsync<T>(Func<Task> action, string witness) where T : Exception
    {
        try { await action(); }
        catch (T error) when (error is not SqliteException sqlite || sqlite.SqliteExtendedErrorCode == TriggerConstraint)
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
