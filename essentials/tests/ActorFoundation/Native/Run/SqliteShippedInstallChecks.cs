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
    private const int ForeignKeyConstraint = 787;
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

            const string invalidTenant = "tenant\0invalid";
            await RefusedAsync<SqliteException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "FoundationRootBirths" ("Reference", "Tenant", "Actor", "Partition", "Creation", "Creator", "Fingerprint")
                VALUES ('tenant-test', {invalidTenant}, 'tenant-test', 0, 1, 'creator', 'fingerprint')
                """, token), "SQLite refuses an embedded NUL in a tenant name", CheckConstraint);
            await CheckGuardsAsync(db, token);
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
                row => row.SetProperty(x => x.MessageProtocol, marker.MessageProtocol - 1), token);
            await RefusedAsync<StoreFormatException>(() => EfStore.InstallSqliteShippedAsync(db, 2, token),
                "SQLite refuses the previous install-only definitions revision");
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

    private static async Task CheckGuardsAsync(KernelDb db, CancellationToken token)
    {
        await SeedAsync("direct", InstanceStatus.Running, qualified: false);
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
            if (id == "parked")
            {
                await db.Database.ExecuteSqlRawAsync("""
                    INSERT INTO "Subscriptions" ("Key", "Instance", "Wait", "Partition") VALUES ('key', 'parked', 'wait', 0)
                    """, token);
                await RefusedAsync<SqliteException>(() => RetireAsync(id),
                    "a marked parked retirement refuses a checkpoint with an outstanding subscription");
                await db.Database.ExecuteSqlRawAsync("""DELETE FROM "Subscriptions" WHERE "Instance" = 'parked'""", token);
            }
            await RetireAsync(id);
            await RefusedAsync<SqliteException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE "Instances" SET "Retired" = 0 WHERE "Id" = {id}
                """, token), "the retirement trigger refuses a reversal");
        }
        Require(await db.Instances.CountAsync(instance => instance.Retired, token) == 2,
            "the physical guard accepts qualified running and parked atomic retirements");

        await SeedAsync("terminal", InstanceStatus.Finished, jobs: "[]");
        await RefusedAsync<SqliteException>(() => db.Database.ExecuteSqlRawAsync("""
            UPDATE "Instances" SET "Retired" = 1, "Revision" = 2, "FoundationWriteRevision" = 2,
                "State" = 'null', "FoundationHeader" = NULL, "Jobs" = NULL WHERE "Id" = 'terminal'
            """, token), "a marked terminal retirement refuses outstanding jobs on the previous SQLite checkpoint");

        await SeedAsync("checkpoint", InstanceStatus.Running);
        Require(await db.Database.SqlQueryRaw<bool>("""
            SELECT wr AS "Value" FROM pragma_table_list WHERE schema = 'main' AND name = 'Instances'
            """).SingleAsync(token), "the SQLite checkpoint table uses only its declared primary key for row identity");
        await RefusedAsync<SqliteException>(() => db.Database.ExecuteSqlRawAsync("""
            UPDATE "Instances" SET "State" = '[]' WHERE "Id" = 'checkpoint'
            """, token), "the checkpoint guard refuses an update without a qualified revision");
        await RefusedAsync<SqliteException>(() => db.Database.ExecuteSqlRawAsync("""
            DELETE FROM "Instances" WHERE "Id" = 'checkpoint'
            """, token), "the checkpoint guard refuses physical deletion of a qualified root");
        await db.Database.OpenConnectionAsync(token);
        try
        {
            await db.Database.ExecuteSqlRawAsync("PRAGMA recursive_triggers = OFF", token);
            await RefusedAsync<SqliteException>(() => db.Database.ExecuteSqlRawAsync("""
                INSERT OR REPLACE INTO "Instances" SELECT * FROM "Instances" WHERE "Id" = 'checkpoint'
                """, token), "an insert cannot replace a qualified checkpoint");
            await RefusedAsync<SqliteException>(() => db.Database.ExecuteSqlRawAsync("""
                UPDATE OR REPLACE "Instances" SET "Id" = 'checkpoint' WHERE "Id" = 'direct'
                """, token), "a key move cannot replace a qualified checkpoint");
        }
        finally { await db.Database.CloseConnectionAsync(); }
        await RefusedAsync<SqliteException>(() => JournalAsync("checkpoint", revision: 4, "[]"),
            "the journal guard requires the next running checkpoint revision");
        await JournalAsync("checkpoint", revision: 2, "[]");
        Require(await db.Database.ExecuteSqlRawAsync("""
            UPDATE "Instances" SET "Revision" = 2, "FoundationWriteRevision" = 2, "State" = '[]' WHERE "Id" = 'checkpoint'
            """, token) == 1, "the checkpoint and journal guards accept a qualified running revision");

        await SeedAsync("refusal", InstanceStatus.Faulted);
        await RefusedAsync<SqliteException>(() => JournalAsync("refusal", revision: 2, """{"Outcome":"REFUSED","Reason":"parked"}"""),
            "the journal guard compares outcome prefixes using ordinal case");
        const string refused = """{"Outcome":"refused","Reason":"parked"}""";
        await using (var write = await db.Database.BeginTransactionAsync(token))
        {
            await JournalAsync("refusal", revision: 2, refused);
            await RefusedAsync<SqliteException>(() => db.Database.ExecuteSqlRawAsync("""
                UPDATE "Instances" SET "Revision" = 2, "FoundationWriteRevision" = 2, "Children" = '[]' WHERE "Id" = 'refusal'
                """, token), "a parked refusal preserves a null checkpoint field beside its new revision");
            Require(await db.Database.ExecuteSqlRawAsync("""
                UPDATE "Instances" SET "Revision" = 2, "FoundationWriteRevision" = 2 WHERE "Id" = 'refusal'
                """, token) == 1, "the checkpoint guard accepts a journalled refusal on the unchanged parked root");
            await write.CommitAsync(token);
        }

        await SeedAsync("first-bind", InstanceStatus.Running, qualified: false);
        await using (var write = await db.Database.BeginTransactionAsync(token))
        {
            await JournalAsync("first-bind", revision: 2, refused);
            await RefusedAsync<SqliteException>(() => BindAsync("first-bind", InstanceStatus.Running),
                "binding a fresh root refuses an outcome for the wrong checkpoint revision");
            await write.RollbackAsync(token);
        }
        await using (var write = await db.Database.BeginTransactionAsync(token))
        {
            await db.Database.ExecuteSqlRawAsync("""DELETE FROM "Instances" WHERE "Id" = 'first-bind'""", token);
            await JournalAsync("first-bind", revision: 2, refused);
            await RefusedAsync<SqliteException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "Instances" ("Id", "Partition", "Model", "Definition", "State", "Status", "Revision",
                    "StateVersion", "Suspended", "Retired", "History", "NextJobGeneration",
                    "FoundationReference", "FoundationTenant", "FoundationHeader")
                SELECT 'first-bind', "Partition", "Model", "Definition", "State", "Status", "Revision",
                    "StateVersion", "Suspended", "Retired", "History", "NextJobGeneration",
                    'first-bind', 'tenant', {ProjectionHeader}
                FROM "Instances" WHERE "Id" = 'direct'
                """, token), "inserting a qualified root refuses a previously journalled outcome at the wrong revision");
            await write.RollbackAsync(token);
        }
        await using (var write = await db.Database.BeginTransactionAsync(token))
        {
            await JournalAsync("first-bind", revision: 1, refused);
            await BindAsync("first-bind", InstanceStatus.Running);
            await write.CommitAsync(token);
        }
        Require(await db.Journal.AnyAsync(entry => entry.Instance == "first-bind" && entry.Revision == 1, token)
            && await ReadProjectionAsync("first-bind") is { Status: InstanceStatus.Running, Header: ProjectionHeader },
            "binding a fresh root commits its matching journal outcome and actor projection");

        await SeedAsync("projected", InstanceStatus.Running);
        var initial = await ReadProjectionAsync("projected");
        Require(initial is { Status: InstanceStatus.Running, EndedAt: null, TerminalRevision: null, ParkAt: null, ParkFault: null, ParkRevision: null }
            && initial.Header == ProjectionHeader
            && initial.Type == "\"model\\u0000suffix\\\\escape\""
            && initial.Labels == """[{"Key":"label\u0000key","Value":"value\\escape"}]""",
            "the actor list projects public header fields with their encoded strings intact");
        await db.Database.ExecuteSqlRawAsync("""
            UPDATE "Instances" SET "Status" = 2, "Revision" = 2, "FoundationWriteRevision" = 2,
                "FoundationParkAt" = 200, "FoundationParkRevision" = 2, "Fault" = 'opened'
            WHERE "Id" = 'projected'
            """, token);
        Require(await ReadProjectionAsync("projected") is { Status: InstanceStatus.Faulted, ParkAt: 200, ParkFault: "opened", ParkRevision: 2 },
            "the actor list projects Park metadata alongside the changed checkpoint status");
        Require(!await db.Database.SqlQueryRaw<bool>("""
            SELECT EXISTS (SELECT 1 FROM "FoundationActorList" WHERE "Reference" IN ('atomic', 'parked')) AS "Value"
            """).SingleAsync(token), "retirement removes the running and parked actor projections");

        await SeedAsync("unpaired", InstanceStatus.Faulted);
        await SeedAsync("other", InstanceStatus.Running);
        await using (var write = await db.Database.BeginTransactionAsync(token))
        {
            await JournalAsync("unpaired", revision: 2, refused);
            await db.Database.ExecuteSqlRawAsync("""
                UPDATE "Instances" SET "Revision" = 2, "FoundationWriteRevision" = 2 WHERE "Id" = 'other'
                """, token);
            await RefusedAsync<SqliteException>(() => write.CommitAsync(token),
                "an outcome fails commit when only another actor reached its revision", ForeignKeyConstraint);
            await write.RollbackAsync(token);
        }
        Require(!await db.Journal.AnyAsync(entry => entry.Instance == "unpaired", token)
            && await db.Instances.Where(row => row.Id == "other").Select(row => row.Revision).SingleAsync(token) == 1,
            "rolling back an unpaired outcome also rolls back the checkpoint of the other actor");

        const string clearing = """{"Outcome":"clearing","Operation":"operator/test","Cleared":1}""";
        const string terminated = """{"Outcome":"terminated","Operation":"operator/test","Cleared":1}""";
        const string discarded = """{"Outcome":"protected","Disposition":"discarded","Operation":"operator/test","Cleared":1}""";
        await using (var write = await db.Database.BeginTransactionAsync(token))
        {
            await JournalAsync("other", revision: 2, clearing);
            await db.Database.ExecuteSqlRawAsync("""
                UPDATE "Instances" SET "Revision" = 2, "FoundationWriteRevision" = 2 WHERE "Id" = 'other'
                """, token);
            await RefusedAsync<SqliteException>(() => write.CommitAsync(token),
                "a clearing outcome requires its checkpoint to open the clearing", ForeignKeyConstraint);
            await write.RollbackAsync(token);
        }
        await using (var write = await db.Database.BeginTransactionAsync(token))
        {
            await JournalAsync("projected", revision: 3, clearing);
            await db.Database.ExecuteSqlRawAsync("""
                UPDATE "Instances" SET "Revision" = 3, "FoundationWriteRevision" = 3,
                    "FoundationParkClearing" = 'operator/test', "FoundationParkCleared" = 1
                WHERE "Id" = 'projected'
                """, token);
            await write.CommitAsync(token);
        }
        Require(await ReadProjectionAsync("projected") is { ParkAt: 200, ParkFault: "opened", ParkRevision: 2 },
            "opening a clearing commits its outcome and preserves the projected Park mark");

        await using (var write = await db.Database.BeginTransactionAsync(token))
        {
            await JournalAsync("projected", revision: 4, terminated);
            await JournalAsync("projected", revision: 4, discarded);
            Require(await EndParkAsync("projected", 4, InstanceStatus.Cancelled) == 1, "the terminated outcome admits the matching checkpoint update");
            await RefusedAsync<SqliteException>(() => write.CommitAsync(token),
                "the paired revision cannot commit an additional protected outcome with the wrong terminal disposition", ForeignKeyConstraint);
            await write.RollbackAsync(token);
        }
        Require(!await db.Journal.AnyAsync(entry => entry.Instance == "projected" && entry.Revision == 4, token)
            && await ReadProjectionAsync("projected") is { Status: InstanceStatus.Faulted, ParkAt: 200, TerminalRevision: null },
            "a failed outcome commit preserves the parked checkpoint, projection, and earlier journal");

        await using (var write = await db.Database.BeginTransactionAsync(token))
        {
            await JournalAsync("projected", revision: 4, discarded);
            Require(await EndParkAsync("projected", 4, InstanceStatus.Faulted) == 1, "the protected discard admits its Faulted terminal checkpoint");
            await write.CommitAsync(token);
        }
        Require(await ReadProjectionAsync("projected") is { Status: InstanceStatus.Faulted, EndedAt: 300, TerminalRevision: 4,
            ParkAt: null, ParkRevision: null, ParkFault: null },
            "the protected discard projects the terminal mark and clears the Park metadata");
        await SeedAsync("terminated", InstanceStatus.Faulted);
        await using (var write = await db.Database.BeginTransactionAsync(token))
        {
            await JournalAsync("terminated", revision: 2, terminated);
            Require(await EndParkAsync("terminated", 2, InstanceStatus.Cancelled) == 1,
                "a standalone terminated outcome admits its Cancelled checkpoint");
            await write.CommitAsync(token);
        }
        Require(await ReadProjectionAsync("terminated") is { Status: InstanceStatus.Cancelled, EndedAt: 300, TerminalRevision: 2, ParkAt: null },
            "the terminated outcome commits the Cancelled projection and terminal mark");
        Require(!await db.Database.SqlQueryRaw<bool>("""
            SELECT EXISTS (SELECT 1 FROM "FoundationRootPendingOutcomes") AS "Value"
            """).SingleAsync(token), "successful outcome commits leave no pending rows");

        await using (var write = await db.Database.BeginTransactionAsync(token))
        {
            await db.Database.ExecuteSqlRawAsync("DROP TRIGGER kernel_project_root_actor_list", token);
            await RefusedAsync<SqliteException>(() => db.Database.ExecuteSqlRawAsync("""
                UPDATE "Instances" SET "Revision" = 2, "FoundationWriteRevision" = 2 WHERE "Id" = 'other'
                """, token), "a qualified checkpoint requires the actor projection trigger");
            await write.RollbackAsync(token);
        }

        Task<ActorProjection> ReadProjectionAsync(string reference) => db.Database.SqlQuery<ActorProjection>($"""
            SELECT "Status", "EndedAt", "TerminalRevision", "Header", "Type", "Labels", "ParkAt", "ParkFault", "ParkRevision"
            FROM "FoundationActorList" WHERE "Tenant" = 'tenant' AND "Reference" = {reference}
            """).SingleAsync(token);

        Task<int> EndParkAsync(string id, long revision, InstanceStatus status) => db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE "Instances" SET "Status" = {(int)status}, "Revision" = {revision}, "FoundationWriteRevision" = {revision},
                "FoundationTerminalRevision" = {revision}, "FoundationTerminalAt" = 300, "FoundationParkAt" = NULL,
                "FoundationParkRevision" = NULL, "FoundationParkClearing" = NULL, "FoundationParkCleared" = NULL
            WHERE "Id" = {id}
            """, token);

        Task<int> RetireAsync(string id) => db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE "Instances" SET "Retired" = 1, "Status" = {(int)InstanceStatus.Cancelled}, "Revision" = 2,
                "FoundationWriteRevision" = 2, "FoundationTerminalRevision" = 2, "FoundationTerminalAt" = 100,
                "FoundationHeader" = NULL, "FoundationParkAt" = NULL, "FoundationParkRevision" = NULL
            WHERE "Id" = {id}
            """, token);

        Task<int> JournalAsync(string id, long revision, string entries) =>
            db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "Journal" ("Partition", "Position", "Instance", "Revision", "Entries")
                VALUES (0, (SELECT coalesce(max("Position"), 0) + 1 FROM "Journal"), {id}, {revision}, {entries})
                """, token);

        async Task SeedAsync(string id, InstanceStatus status, bool qualified = true, string? jobs = null)
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "FoundationRootBirths" ("Reference", "Tenant", "Actor", "Partition", "Creation", "Creator", "Fingerprint")
                VALUES ({id}, 'tenant', {id}, 0, 1, {id}, 'fingerprint')
                """, token);
            db.Instances.Add(new InstanceEntity
            {
                Id = id, Partition = 0, Model = "model", Definition = "definition", State = "null",
                Status = status, Revision = 1, NextJobGeneration = 1, Jobs = jobs,
                Fault = status == InstanceStatus.Faulted ? "seeded fault" : null
            });
            await db.SaveChangesAsync(token);
            if (qualified) await BindAsync(id, status);
        }

        async Task BindAsync(string id, InstanceStatus status)
        {
            var parkedAt = status == InstanceStatus.Faulted ? (long?)100 : null;
            var parkRevision = parkedAt.HasValue ? (long?)1 : null;
            var terminalRevision = status == InstanceStatus.Finished ? (long?)1 : null;
            var terminalAt = terminalRevision.HasValue ? (long?)100 : null;
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE "Instances" SET "FoundationReference" = {id}, "FoundationTenant" = 'tenant', "FoundationHeader" = {ProjectionHeader},
                    "FoundationParkAt" = {parkedAt}, "FoundationParkRevision" = {parkRevision},
                    "FoundationTerminalRevision" = {terminalRevision}, "FoundationTerminalAt" = {terminalAt} WHERE "Id" = {id}
                """, token);
        }
    }

    private const string ProjectionHeader = """{"Model":"model\u0000suffix\\escape","Definition":"definition","TypeData":"","Labels":[{"Key":"label\u0000key","Value":"value\\escape"}],"EscapedState":false}""";

    private sealed record ActorProjection(InstanceStatus Status, long? EndedAt, long? TerminalRevision, string Header,
        string Type, string Labels, long? ParkAt, string? ParkFault, long? ParkRevision);

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
