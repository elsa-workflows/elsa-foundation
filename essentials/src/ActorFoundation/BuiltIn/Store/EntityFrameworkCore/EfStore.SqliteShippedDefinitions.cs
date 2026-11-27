using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Kernel.EntityFramework;

public sealed partial class EfStore
{
    internal static void RefuseSqliteShippedRuntime(KernelDb db)
    {
        if (db.UsesSqliteShippedInstall && !db.UsesSqliteRootRuntime)
            throw new NotSupportedException("The staged SQLite context is available for install verification and ingress only.");
    }

    private static Task RequireSqliteOperationalFormatAsync(KernelDb db, CancellationToken token) =>
        db.UsesSqliteRootRuntime ? RequireSqliteWorkerFormatAsync(db, token)
        : db.Database.IsSqlite() ? RequireStoreVersionsAsync(db, token) : Task.CompletedTask;

    private static Task RequireSqliteWorkerFormatAsync(KernelDb db, CancellationToken token) =>
        RequireSqliteRootAdmissionAsync(db, db.SqliteRuntimeDefinitions!, token);

    private sealed record SqliteDefinition(string Type, string Name, string Table, string Sql);

    // The installer owns one immediate transaction from the empty-store check to the definition and marker checks.
    // Reinstallation only verifies. A missing or edited definition requires a deliberate recreate or migration.
    internal static async Task InstallSqliteShippedAsync(KernelDb db, int partitions, CancellationToken token)
    {
        if (!db.UsesSqliteShippedInstall) throw new NotSupportedException("This install requires the SQLite shipped format.");
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(partitions);
        await db.Database.OpenConnectionAsync(token);
        try
        {
            var connection = (SqliteConnection)db.Database.GetDbConnection();
            token.ThrowIfCancellationRequested();
            await using var transaction = connection.BeginTransaction(deferred: false);
            await using var enlistment = await db.Database.UseTransactionAsync(transaction, token);

            await RequireSqliteInstallConnectionAsync(db, token);
            var installed = await ReadSqliteDefinitionsAsync(db, token);
            var expected = SqliteBaseDefinitions(db).Concat(SqliteRootDefinitions(db)).ToArray();
            if (installed.Count == 0)
            {
                foreach (var definition in expected)
                {
                    await using var command = connection.CreateCommand();
                    command.Transaction = transaction;
                    command.CommandText = definition.Sql;
                    await command.ExecuteNonQueryAsync(token);
                }
                await SeedSqliteShippedAsync(db, partitions, token);
                installed = await ReadSqliteDefinitionsAsync(db, token);
            }
            await RequireInstalledStoreVersionsAsync(db, FormatLock.None, token);

            RequireSqliteDefinitions(installed, expected);
            if (!await db.Database.SqlQuery<bool>($"""
                SELECT
                    (SELECT count(*) FROM "Partitions") = {partitions}
                    AND (SELECT min("Id") FROM "Partitions") = 0
                    AND (SELECT max("Id") FROM "Partitions") = {partitions - 1}
                    AND EXISTS (SELECT 1 FROM "FoundationRootIssuer"
                        WHERE "Singleton" = 1 AND typeof("Key") = 'blob' AND length("Key") = 32 AND "Partitions" = {partitions}) AS "Value"
                """).SingleAsync(token))
                throw new StoreFormatException("The SQLite root issuer and partition count differ from this install. Recreate or migrate the database.");

            await transaction.CommitAsync(token);
        }
        finally { await db.Database.CloseConnectionAsync(); }
    }

    private sealed record SqliteOperationalStamp(bool SafeConnection, long SchemaVersion, int? StoreSchema,
        int? MessageProtocol, int? JournalCodec, string? Definitions);

    private static readonly string SqliteOperationalStampSql = FormattableString.Invariant($"""
        SELECT ({SqliteConnectionPolicy}) AS "SafeConnection", schema.schema_version AS "SchemaVersion",
            version."StoreSchema", version."MessageProtocol", version."JournalCodec",
            definitions."StoreSchema" || '/' || definitions."MessageProtocol" || '/' || definitions."JournalCodec" AS "Definitions"
        FROM pragma_schema_version AS schema
        LEFT JOIN main."{StoreVersionEntity.Table}" AS version ON version."Id" = {StoreVersionEntity.MarkerId}
        LEFT JOIN main."{StoreVersionEntity.Table}" AS definitions ON definitions."Id" = {StoreDefinitionsRevision.MarkerId}
        """);

    internal static async Task RequireSqliteRootAdmissionAsync(KernelDb db, SqliteAdmissionDefinitions definitions, CancellationToken token)
    {
        if (!db.UsesSqliteShippedInstall)
            throw new NotSupportedException("Root admission requires the SQLite shipped context.");

        // Keep the grouped check and any full catalog reread on the same connection, including outside a transaction.
        await db.Database.OpenConnectionAsync(token);
        try
        {
            SqliteOperationalStamp stamp;
            try
            {
                stamp = await db.Database.SqlQueryRaw<SqliteOperationalStamp>(SqliteOperationalStampSql).SingleAsync(token);
            }
            catch (SqliteException error) when (error.SqliteErrorCode == 1 &&
                error.Message.Contains("no such table: main." + StoreVersionEntity.Table, StringComparison.Ordinal))
            {
                // Preserve the connection-policy refusal even on an invalid database without a marker table.
                await RequireSqliteInstallConnectionAsync(db, token);
                throw new StoreFormatException("The kernel store version marker table is missing.", error);
            }

            if (!stamp.SafeConnection) throw new StoreFormatException(SqliteConnectionRefusal);
            RequireSnapshotFormat(db, stamp.StoreSchema, stamp.MessageProtocol, stamp.JournalCodec, stamp.Definitions);
            await definitions.RequireAsync(db, stamp.SchemaVersion, token);
        }
        finally { await db.Database.CloseConnectionAsync(); }
    }

    internal static async Task RequireSqliteRootAdmissionDefinitionsAsync(KernelDb db, CancellationToken token)
    {
        var installed = await ReadSqliteDefinitionsAsync(db, token);
        RequireSqliteDefinitions(installed, SqliteBaseDefinitions(db).Concat(SqliteRootDefinitions(db)));
    }

    private static void RequireSqliteDefinitions(IReadOnlyList<SqliteDefinition> installed, IEnumerable<SqliteDefinition> expected)
    {
        var expectedByName = expected.ToDictionary(definition => definition.Name, StringComparer.Ordinal);
        if (installed.Count != expectedByName.Count ||
            installed.Any(definition => !expectedByName.TryGetValue(definition.Name, out var match) || definition != match))
            throw new StoreFormatException("The installed SQLite store definitions differ from this build. Recreate or migrate the database.");
    }

    private const string SqliteConnectionPolicy = """
        (SELECT foreign_keys FROM pragma_foreign_keys) = 1
            AND (SELECT ignore_check_constraints FROM pragma_ignore_check_constraints) = 0
            AND (SELECT journal_mode FROM pragma_journal_mode) IN ('delete', 'truncate', 'persist', 'wal')
            AND NOT EXISTS (SELECT 1 FROM temp.sqlite_schema)
            AND NOT EXISTS (SELECT 1 FROM pragma_database_list WHERE name NOT IN ('main', 'temp'))
        """;

    private const string SqliteConnectionRefusal =
        "The SQLite shipped store requires a durable journal, foreign keys and CHECK constraints, and a dedicated database without temporary objects or attachments.";

    private static async Task RequireSqliteInstallConnectionAsync(KernelDb db, CancellationToken token)
    {
        if (!await db.Database.SqlQueryRaw<bool>("SELECT " + SqliteConnectionPolicy + """ AS "Value" """).SingleAsync(token))
            throw new StoreFormatException(SqliteConnectionRefusal);
    }

    private static Task<List<SqliteDefinition>> ReadSqliteDefinitionsAsync(KernelDb db, CancellationToken token) =>
        db.Database.SqlQueryRaw<SqliteDefinition>("""
            SELECT type AS "Type", name AS "Name", tbl_name AS "Table", sql AS "Sql"
            FROM main.sqlite_schema WHERE sql IS NOT NULL AND name NOT GLOB 'sqlite_*'
            """).ToListAsync(token);

    private static async Task SeedSqliteShippedAsync(KernelDb db, int partitions, CancellationToken token)
    {
        var format = db.RequiredFormat;
        var definitions = StoreDefinitionsRevision.Seed(db)!;
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "KernelStoreVersions" ("Id", "StoreSchema", "MessageProtocol", "JournalCodec")
            VALUES ({StoreVersionEntity.MarkerId}, {format.StoreSchema}, {format.MessageProtocol}, {format.JournalCodec}),
                   ({definitions.Id}, {definitions.StoreSchema}, {definitions.MessageProtocol}, {definitions.JournalCodec})
            """, token);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            WITH RECURSIVE scopes(id) AS (SELECT 0 UNION ALL SELECT id+1 FROM scopes WHERE id+1 < {partitions})
            INSERT INTO "Partitions" ("Id", "Epoch", "Position", "Died") SELECT id, 0, 0, 0 FROM scopes
            """, token);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "FoundationRootIssuer" ("Singleton", "Key", "Partitions")
            VALUES (1, {RandomNumberGenerator.GetBytes(32)}, {partitions})
            """, token);
        if (RootScheduleOwnerMode.Applies(db)) await RootScheduleOwnerMode.SeedSqliteAsync(db, token);
    }

    private static IEnumerable<SqliteDefinition> SqliteBaseDefinitions(KernelDb db)
    {
        var model = db.GetService<IDesignTimeModel>().Model;
        var operations = db.GetService<IMigrationsModelDiffer>().GetDifferences(null, model.GetRelationalModel());
        var generator = db.GetService<IMigrationsSqlGenerator>();
        foreach (var operation in operations)
        {
            switch (operation)
            {
                case CreateTableOperation table:
                    AddSqliteRootColumns(table, db);
                    // The qualified checkpoint's Id is its only row identity, including during SQLite conflict handling.
                    var options = table.Name == "Instances" ? " STRICT, WITHOUT ROWID" : " STRICT";
                    yield return new("table", table.Name, table.Name, Render(table) + options);
                    break;
                case CreateIndexOperation index:
                    // The deletion index also bounds each shipped actor's journal continuation.
                    if (index.Table == "Journal" && index.Columns.SequenceEqual(["Partition", "Instance"], StringComparer.Ordinal))
                    {
                        index.Name = "IX_Journal_Partition_Instance_Position";
                        index.Columns = ["Partition", "Instance", "Position"];
                    }
                    yield return new("index", index.Name, index.Table, Render(index));
                    break;
                case InsertDataOperation { Table: StoreVersionEntity.Table or RootScheduleOwnerMode.Table }:
                    break; // Seeding stays in this install transaction with the issuer and partitions.
                default:
                    throw new InvalidOperationException($"Unsupported SQLite schema operation {operation.GetType().Name}.");
            }
        }

        string Render(MigrationOperation definition) => generator.Generate([definition], model).Single()
            .CommandText.TrimEnd().TrimEnd(';').ReplaceLineEndings("\n");
    }

    private static void AddSqliteRootColumns(CreateTableOperation table, KernelDb db)
    {
        var parkArrivals = db.UsesRootParkArrivals;
        var parkRecovery = db.UsesRootParkRecovery;
        if (table.Name == "Instances")
        {
            Add("FoundationReference", "TEXT");
            Add("FoundationTenant", "TEXT");
            Add("FoundationHeader", "TEXT");
            Add("FoundationWriteRevision", "INTEGER");
            Add("FoundationTerminalRevision", "INTEGER");
            Add("FoundationTerminalAt", "INTEGER");
            Add("FoundationStateWire", "BLOB");
            Add("FoundationParkAt", "INTEGER");
            Add("FoundationParkRevision", "INTEGER");
            Add("FoundationParkClearing", "TEXT");
            Add("FoundationParkClearingKind", "TEXT");
            Add("FoundationParkCleared", "INTEGER");

            table.ForeignKeys.Add(new AddForeignKeyOperation
            {
                Name = "FK_Instances_FoundationRootBirths_FoundationReference",
                Table = table.Name,
                Columns = ["FoundationReference"],
                PrincipalTable = "FoundationRootBirths",
                PrincipalColumns = ["Reference"]
            });
            Check("CK_Instances_FoundationParkRevision", """ "FoundationParkRevision" > 0 """);
            Check("CK_Instances_FoundationBoolean", """ "Retired" IN (0, 1) AND "Suspended" IN (0, 1) """);
            if (parkRecovery)
            {
                table.Columns.Add(new AddColumnOperation
                {
                    Name = "FoundationParkGeneration", Table = table.Name, ColumnType = "INTEGER",
                    IsNullable = false, DefaultValueSql = "0", ClrType = typeof(long)
                });
                Check("CK_Instances_FoundationParkGeneration", """ "FoundationParkGeneration" >= 0 """);
            }
            if (RecordsConsumedRootJob(db))
            {
                Add("FoundationParkJobResult", "TEXT");
                Add("FoundationParkLiveJob", "TEXT");
            }
            Check("CK_FoundationParkClearing", $"""
                ("FoundationParkClearing" IS NULL) = ("FoundationParkCleared" IS NULL)
                AND ("FoundationParkClearing" IS NULL) = ("FoundationParkClearingKind" IS NULL)
                AND ("FoundationParkClearing" IS NULL OR "Status" = 2 AND "FoundationParkAt" IS NOT NULL AND "FoundationParkCleared" > 0
                    AND "FoundationParkClearingKind" IN ('{Kinds.Terminate}', '{Kinds.Delete}'))
                """);
        }
        if (table.Name == "Messages")
        {
            Add("FoundationKeptBy", "INTEGER");
            if (parkRecovery)
            {
                Add("FoundationReleasedBy", "INTEGER");
                Check("CK_Messages_FoundationReleasedBy", $"""
                    "FoundationReleasedBy" IS NULL OR ("FoundationReleasedBy" > 0 AND {ParkInputPredicate(db, "\"InboxScope\"")})
                    """);
            }
            Check("CK_Messages_FoundationKeptBy", parkArrivals
                ? """ "FoundationKeptBy" IS NULL OR ("FoundationKeptBy" IN (0, 1) AND "Done" = 1) """
                : """ "FoundationKeptBy" IS NULL OR ("FoundationKeptBy" = 0 AND "Done" = 1) """);
            Check("CK_Messages_Done", """ "Done" IN (0, 1) """);
        }

        if (table.Name == "Partitions") Check("CK_Partitions_Died", """ "Died" IN (0, 1) """);

        void Check(string name, string sql) => table.CheckConstraints.Add(new AddCheckConstraintOperation
        {
            Name = name,
            Table = table.Name,
            Sql = sql
        });

        void Add(string name, string type) => table.Columns.Add(new AddColumnOperation
        {
            Name = name,
            Table = table.Name,
            ColumnType = type,
            IsNullable = true
        });
    }

    private static IEnumerable<SqliteDefinition> SqliteRootDefinitions(KernelDb db)
    {
        yield return Table("FoundationRootIssuer", """
            "Singleton" INTEGER NOT NULL PRIMARY KEY CHECK ("Singleton" = 1),
            "Key" BLOB NOT NULL,
            "Partitions" INTEGER NOT NULL CHECK ("Partitions" > 0)
            """);
        if (db.UsesRootRetentionPolicy)
            yield return Table(RetentionPolicyTable, RetentionPolicyColumns(sqlite: true));
        const string births = """
            "Reference" TEXT NOT NULL PRIMARY KEY,
            "Tenant" TEXT NOT NULL CHECK (length("Tenant") BETWEEN 1 AND 64 AND instr("Tenant", char(0)) = 0
                AND "Tenant" NOT GLOB '*[^a-z0-9-]*'),
            "Actor" TEXT NOT NULL,
            "Partition" INTEGER NOT NULL REFERENCES "Partitions" ("Id"),
            "Creation" INTEGER NOT NULL,
            "Creator" TEXT NOT NULL,
            "Fingerprint" TEXT NOT NULL,
            UNIQUE ("Actor", "Creation")
            """;
        yield return Table("FoundationRootBirths", db.UsesRootInlinePromotion
            ? RootInlineBirthRevision.Columns(births, sqlite: true) : births);
        if (db.UsesRootKeyedStarts)
        {
            yield return Table(RootKeyedAdmission.BindingTable, RootKeyedAdmission.BindingColumns(sqlite: true));
            yield return Table(RootKeyedAdmission.ReceiptTable, RootKeyedAdmission.ReceiptColumns(sqlite: true));
        }
        if (db.UsesRootInlinePromotion)
            yield return new("trigger", RootInlineBirthRevision.Guard, "FoundationRootBirths", RootInlineBirthRevision.SqliteGuard);
        if (db.UsesRootScheduleInventory)
        {
            yield return Table(RootScheduleInventory.Table, RootScheduleInventory.Columns(sqlite: true));
            yield return Index(RootScheduleInventory.DefinitionIndex, RootScheduleInventory.Table, RootScheduleInventory.IndexedColumns);
        }
        if (db.UsesRootScheduleControls)
            yield return Table(RootScheduleControls.Table, RootScheduleControls.Columns(sqlite: true));
        if (db.UsesRootScheduleLineage)
        {
            yield return Table(RootScheduleLineage.Table, RootScheduleLineage.Columns(sqlite: true));
            yield return Index(RootScheduleLineage.CohortIndex, RootScheduleLineage.Table, RootScheduleLineage.IndexedColumns);
        }
        if (db.UsesRootPreparedStarts)
        {
            yield return Table(RootPreparedStarts.Table, RootPreparedStarts.Columns(sqlite: true, inlinePromotion: db.UsesRootInlinePromotion));
            yield return Index(RootPreparedStarts.DeadlineIndex, RootPreparedStarts.Table, RootPreparedStarts.DeadlineColumns);
        }
        if (db.UsesRootEventStarts)
        {
            yield return Table(RootEventStarts.Slots, RootEventStarts.SlotColumns(sqlite: true));
            yield return Table(RootEventStarts.Registrations, RootEventStarts.RegistrationColumns(sqlite: true));
            yield return Index(RootEventStarts.MatchIndex, RootEventStarts.Registrations, RootEventStarts.MatchColumns);
            yield return Table(RootEventStarts.Targets, RootEventStarts.TargetColumns(sqlite: true, db.UsesRootEventStartReceipts, db.UsesRootEventStartScan));
            if (db.UsesRootEventStartReceipts)
                yield return Index(RootEventStarts.ReceiptIndex, RootEventStarts.Targets, RootEventStarts.ReceiptColumns);
            yield return Index(RootEventStarts.PendingIndex, RootEventStarts.Targets, RootEventStarts.PendingColumns);
            yield return Index(RootEventStarts.SlotPendingIndex, RootEventStarts.Targets, RootEventStarts.SlotPendingColumns);
            if (db.UsesRootEventStartScan)
                yield return Index(RootEventStarts.ScanIndex, RootEventStarts.Targets, RootEventStarts.ScanColumns);
        }
        if (db.UsesRootPublicEventStarts)
        {
            yield return Table(RootPublicEventStarts.Registrations, RootPublicEventStarts.RegistrationColumns(sqlite: true));
            yield return Index(RootPublicEventStarts.CatchUpIndex, RootPublicEventStarts.Registrations, RootPublicEventStarts.CatchUpColumns);
            yield return Table(RootPublicEventStarts.Targets, RootPublicEventStarts.TargetColumns(sqlite: true));
            yield return Table(RootPublicEventPublications.Table, RootPublicEventPublications.Columns(sqlite: true));
            yield return Table(RootPublicEventLiveWaits.Table, RootPublicEventLiveWaits.Columns(sqlite: true));
            yield return Index(RootPublicEventLiveWaits.CatchUpIndex, RootPublicEventLiveWaits.Table, RootPublicEventLiveWaits.CatchUpColumns);
            yield return Table(RootPublicEventLiveWaits.Receipts, RootPublicEventLiveWaits.ReceiptColumns(sqlite: true));
            yield return Table(RootPublicEventPublications.Horizons, RootPublicEventPublications.HorizonColumns(sqlite: true));
            yield return Index(RootPublicEventPublications.RetainedIndex, RootPublicEventPublications.Table, RootPublicEventPublications.RetainedColumns);
            yield return Index(RootPublicEventPublications.CleanupIndex, RootPublicEventPublications.Table, RootPublicEventPublications.CleanupColumns);
        }
        yield return Table("FoundationRootReceipts", """
            "Tenant" TEXT NOT NULL,
            "Operation" TEXT NOT NULL,
            "Reference" TEXT NOT NULL REFERENCES "FoundationRootBirths" ("Reference") DEFERRABLE INITIALLY DEFERRED,
            "Until" INTEGER NOT NULL,
            PRIMARY KEY ("Tenant", "Operation")
            """);
        yield return Table("FoundationDirectReceipts", """
            "Reference" TEXT NOT NULL REFERENCES "FoundationRootBirths" ("Reference"),
            "Operation" TEXT NOT NULL,
            "Until" INTEGER NOT NULL,
            PRIMARY KEY ("Reference", "Operation")
            """);
        if (db.UsesRootChildren)
            yield return Table("FoundationRootChildCascades", """
                "Reference" TEXT NOT NULL PRIMARY KEY REFERENCES "FoundationRootBirths" ("Reference"),
                "Revision" INTEGER NOT NULL CHECK ("Revision" > 0),
                "Cursor" INTEGER NOT NULL CHECK ("Cursor" > 0),
                "Children" TEXT NOT NULL CHECK (json_valid("Children") AND "Cursor" < json_array_length("Children"))
                """);
        yield return Table("FoundationOperatorReceipts", """
            "Tenant" TEXT NOT NULL,
            "Operation" TEXT NOT NULL,
            "Reference" TEXT NOT NULL REFERENCES "FoundationRootBirths" ("Reference"),
            "Until" INTEGER NOT NULL,
            PRIMARY KEY ("Tenant", "Operation")
            """);
        yield return Table("FoundationActorList", """
            "Tenant" TEXT NOT NULL,
            "Reference" TEXT NOT NULL,
            "Status" INTEGER NOT NULL,
            "EndedAt" INTEGER,
            "Header" TEXT NOT NULL,
            "Type" TEXT NOT NULL,
            "Labels" TEXT NOT NULL,
            "TerminalRevision" INTEGER,
            "ParkAt" INTEGER,
            "ParkFault" TEXT,
            "ParkRevision" INTEGER,
            "ParkResolution" TEXT,
            PRIMARY KEY ("Tenant", "Reference")
            """);
        yield return Index("IX_FoundationRootReceipts_Until", "FoundationRootReceipts", """("Until")""");
        yield return Index("IX_FoundationDirectReceipts_Until", "FoundationDirectReceipts", """("Until")""");
        yield return Index("IX_FoundationOperatorReceipts_Until", "FoundationOperatorReceipts", """("Until")""");
        yield return Index("IX_FoundationRootInbox_Pending", "Messages", """
            ("Partition", "Target", "Seq", "Id")
            WHERE "InboxScope" = -4 AND "EventMatch" IS NULL AND NOT "Done"
            """);
        yield return Index("IX_FoundationRootInbox_Inspectable", "Messages", db.UsesRootParkArrivals ? """
            ("Partition", "Target", "Seq", "Id")
            WHERE "InboxScope" = -4 AND "EventMatch" IS NULL AND (NOT "Done" OR "FoundationKeptBy" IN (0, 1))
            """ : """
            ("Partition", "Target", "Seq", "Id")
            WHERE "InboxScope" = -4 AND "EventMatch" IS NULL AND (NOT "Done" OR "FoundationKeptBy" = 0)
            """);
        yield return Index("IX_FoundationRootInbox_Kept", "Messages", """
            ("Partition", "Target", "Seq", "Id", "InboxScope") WHERE "FoundationKeptBy" IS NOT NULL
            """);
        if (RecordsConsumedRootJob(db))
        {
            yield return Index("IX_FoundationRootInbox_RecordedJobResult", "Messages", RecordedJobResultIndex);
            yield return Index("IX_FoundationRootInbox_LiveJob", "Messages", LiveRootJobIndex);
            foreach (var guard in SqliteParkLiveJobGuards()) yield return guard;
        }
        if (db.UsesRootParkRecovery)
        {
            if (db.UsesRootPrivateParkInputs)
            {
                yield return Index("IX_FoundationRootInbox_ParkTimers", "Messages", ParkTimerIndex);
                yield return Index("IX_FoundationRootInbox_TerminalSelf", "Messages", TerminalRootSelfIndex(db));
            }
            yield return Index("IX_FoundationRootInbox_Released", "Messages", $"""
                ("Partition", "Target", "FoundationReleasedBy", "Seq")
                WHERE {ParkInputPredicate(db, "\"InboxScope\"")} AND "EventMatch" IS NULL AND NOT "Done" AND "FoundationReleasedBy" IS NOT NULL
                """);
            yield return Index("IX_FoundationRootInbox_CohortGroups", "Messages", $"""
                ("Partition", "Seen", "Due", "Seq", "Target") WHERE {SqliteCohortWaitingWork}
                """);
            yield return Index("IX_FoundationRootInbox_CohortSequence", "Messages", $"""
                ("Partition", "Seq", "Target", "Due", "Seen") WHERE {SqliteCohortWaitingWork}
                """);
        }
        yield return Index("IX_FoundationRootOperators_Pending", "Messages", """
            ("Partition", "Target", "Seq", "Due")
            WHERE "InboxScope" = -4 AND "Id" GLOB 'operator/*' AND NOT "Done"
            """);
        yield return Index("IX_FoundationActorList_Parked", "FoundationActorList", """
            ("Tenant", "Reference") WHERE "ParkAt" IS NOT NULL
            """);
        yield return new("trigger", "kernel_physical_retirement", "Instances", PhysicalRetirement.SqliteRootGuard(db));
        yield return new("trigger", "kernel_job_generation", "Instances", "CREATE TRIGGER " + SqliteGenerationClause);
        foreach (var guard in SqliteFirstRootGuards(db)) yield return guard;
        foreach (var projector in SqliteRootProjectors()) yield return projector;
        foreach (var history in SqliteRootWorkflowHistoryDefinitions(db)) yield return history;
        if (db.UsesRootSequencedJournal)
            foreach (var segment in SqliteSequencedJournalDefinitions()) yield return segment;
        if (db.UsesRootPlayback)
            foreach (var playback in SqlitePlaybackDefinitions(db.UsesRootPlaybackDescendants)) yield return playback;
        if (db.UsesRootTerminalOutput) yield return SqliteRootTerminalOutputDefinition();
        foreach (var outcome in SqliteRootOutcomes(db)) yield return outcome;
    }

    private static SqliteDefinition Table(string name, string columns) =>
        new("table", name, name, $"""CREATE TABLE "{name}" ({columns}) STRICT""");

    private static SqliteDefinition Index(string name, string table, string columns) =>
        new("index", name, table, $"""CREATE INDEX "{name}" ON "{table}" {columns}""");
}

internal sealed class SqliteAdmissionDefinitions
{
    // A validation lives for one file lease interval. SQLite changes this schema version with DDL.
    private long _verifiedVersion = long.MinValue;

    internal async Task RequireAsync(KernelDb db, long version, CancellationToken token)
    {
        if (version == Volatile.Read(ref _verifiedVersion)) return;
        await EfStore.RequireSqliteRootAdmissionDefinitionsAsync(db, token);
        Volatile.Write(ref _verifiedVersion, version);
    }
}
