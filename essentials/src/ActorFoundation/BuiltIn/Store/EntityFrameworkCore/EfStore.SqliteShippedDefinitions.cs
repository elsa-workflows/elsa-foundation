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
        if (db.UsesSqliteShippedInstall)
            throw new NotSupportedException("The SQLite shipped format is available for install verification only.");
    }

    private static Task RequireSqliteOperationalFormatAsync(KernelDb db, CancellationToken token) =>
        db.Database.IsSqlite() ? RequireStoreVersionsAsync(db, token) : Task.CompletedTask;

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
                    await db.Database.ExecuteSqlRawAsync(definition.Sql, token);
                await SeedSqliteShippedAsync(db, partitions, token);
                installed = await ReadSqliteDefinitionsAsync(db, token);
            }
            await RequireInstalledStoreVersionsAsync(db, FormatLock.None, token);

            var expectedByName = expected.ToDictionary(definition => definition.Name, StringComparer.Ordinal);
            if (installed.Count != expectedByName.Count ||
                installed.Any(definition => !expectedByName.TryGetValue(definition.Name, out var match) || definition != match))
                throw new StoreFormatException("The installed SQLite store definitions differ from this build. Recreate or migrate the database.");
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

    private static async Task RequireSqliteInstallConnectionAsync(KernelDb db, CancellationToken token)
    {
        if (!await db.Database.SqlQueryRaw<bool>("""
            SELECT (SELECT foreign_keys FROM pragma_foreign_keys) = 1
                AND (SELECT ignore_check_constraints FROM pragma_ignore_check_constraints) = 0
                AND (SELECT journal_mode FROM pragma_journal_mode) IN ('delete', 'truncate', 'persist', 'wal')
                AND NOT EXISTS (SELECT 1 FROM temp.sqlite_schema)
                AND NOT EXISTS (SELECT 1 FROM pragma_database_list WHERE name NOT IN ('main', 'temp'))
                AS "Value"
            """).SingleAsync(token))
            throw new StoreFormatException("The SQLite installer requires a durable journal, foreign keys and CHECK constraints, and a dedicated database without temporary objects or attachments.");
    }

    private static Task<List<SqliteDefinition>> ReadSqliteDefinitionsAsync(KernelDb db, CancellationToken token) =>
        db.Database.SqlQueryRaw<SqliteDefinition>("""
            SELECT type AS "Type", name AS "Name", tbl_name AS "Table", sql AS "Sql"
            FROM main.sqlite_schema WHERE sql IS NOT NULL AND name NOT GLOB 'sqlite_*'
            """).ToListAsync(token);

    private static async Task SeedSqliteShippedAsync(KernelDb db, int partitions, CancellationToken token)
    {
        var format = StoreFormat.RootShipped;
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
                    AddSqliteRootColumns(table);
                    yield return new("table", table.Name, table.Name, Render(table) + " STRICT");
                    break;
                case CreateIndexOperation index:
                    yield return new("index", index.Name, index.Table, Render(index));
                    break;
                case InsertDataOperation { Table: StoreVersionEntity.Table }:
                    break; // Seeding stays in this install transaction with the issuer and partitions.
                default:
                    throw new InvalidOperationException($"Unsupported SQLite schema operation {operation.GetType().Name}.");
            }
        }

        string Render(MigrationOperation definition) => generator.Generate([definition], model).Single()
            .CommandText.TrimEnd().TrimEnd(';').ReplaceLineEndings("\n");
    }

    private static void AddSqliteRootColumns(CreateTableOperation table)
    {
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
            Check("CK_FoundationParkClearing", """
                ("FoundationParkClearing" IS NULL) = ("FoundationParkCleared" IS NULL)
                AND ("FoundationParkClearing" IS NULL OR "Status" = 2 AND "FoundationParkAt" IS NOT NULL AND "FoundationParkCleared" > 0)
                """);
        }
        if (table.Name == "Messages")
        {
            Add("FoundationKeptBy", "INTEGER");
            Check("CK_Messages_FoundationKeptBy", """ "FoundationKeptBy" IS NULL OR ("FoundationKeptBy" = 0 AND "Done" = 1) """);
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
        yield return Table("FoundationRootBirths", """
            "Reference" TEXT NOT NULL PRIMARY KEY,
            "Tenant" TEXT NOT NULL CHECK (length("Tenant") BETWEEN 1 AND 64 AND "Tenant" NOT GLOB '*[^a-z0-9-]*'),
            "Actor" TEXT NOT NULL,
            "Partition" INTEGER NOT NULL REFERENCES "Partitions" ("Id"),
            "Creation" INTEGER NOT NULL,
            "Creator" TEXT NOT NULL,
            "Fingerprint" TEXT NOT NULL,
            UNIQUE ("Actor", "Creation")
            """);
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
            PRIMARY KEY ("Tenant", "Reference")
            """);
        yield return Index("IX_FoundationRootReceipts_Until", "FoundationRootReceipts", """("Until")""");
        yield return Index("IX_FoundationDirectReceipts_Until", "FoundationDirectReceipts", """("Until")""");
        yield return Index("IX_FoundationOperatorReceipts_Until", "FoundationOperatorReceipts", """("Until")""");
        yield return Index("IX_FoundationRootInbox_Pending", "Messages", """
            ("Partition", "Target", "Seq", "Id")
            WHERE "InboxScope" = -4 AND "EventMatch" IS NULL AND NOT "Done"
            """);
        yield return Index("IX_FoundationRootInbox_Inspectable", "Messages", """
            ("Partition", "Target", "Seq", "Id")
            WHERE "InboxScope" = -4 AND "EventMatch" IS NULL AND (NOT "Done" OR "FoundationKeptBy" = 0)
            """);
        yield return Index("IX_FoundationRootOperators_Pending", "Messages", """
            ("Partition", "Target", "Seq", "Due")
            WHERE "InboxScope" = -4 AND "Id" GLOB 'operator/*' AND NOT "Done"
            """);
        yield return Index("IX_FoundationActorList_Parked", "FoundationActorList", """
            ("Tenant", "Reference") WHERE "ParkAt" IS NOT NULL
            """);
        yield return new("trigger", "kernel_physical_retirement", "Instances", PhysicalRetirement.SqliteRootGuard(db));
        yield return new("trigger", "kernel_job_generation", "Instances", "CREATE TRIGGER " + SqliteGenerationClause);
    }

    private static SqliteDefinition Table(string name, string columns) =>
        new("table", name, name, $"""CREATE TABLE "{name}" ({columns}) STRICT""");

    private static SqliteDefinition Index(string name, string table, string columns) =>
        new("index", name, table, $"""CREATE INDEX "{name}" ON "{table}" {columns}""");
}
