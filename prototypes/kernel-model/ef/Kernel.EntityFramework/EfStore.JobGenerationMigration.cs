using Microsoft.EntityFrameworkCore;
using MySql.Data.MySqlClient;

namespace Kernel.EntityFramework;

public sealed partial class EfStore
{
    /// <summary>Run during the coordinated stop. Preserves the ordinary, immediate-continuation, or timer profile.
    /// Existing checkpoints and messages retain null/zero generation.</summary>
    public async Task<bool> UpgradeJobGenerationFormatAsync(CancellationToken cancellationToken = default)
    {
        // Select the migration from the persisted protocol. AdvanceStoreFormatAsync rechecks its
        // exact source and target under the marker lock before any DDL or marker publication.
        await using var db = create();
        var stored = await ReadStoreFormatAsync(db, FormatLock.None, cancellationToken);
        var continuation = db.Database.IsNpgsql() && stored.MessageProtocol is 3 or 4;
        var expected = new StoreFormat(2, continuation ? stored.MessageProtocol : 1, 1);
        var next = new StoreFormat(3, continuation ? stored.MessageProtocol : 2, 1);
        var advanced = await AdvanceStoreFormatAsync(expected, next, async (transaction, token) =>
        {
            await LockRootSchemaAsync(transaction, token);
            await AddGenerationColumns(transaction, token);
            if (continuation) await InstallRootSelfAsync(transaction, token);
            await BackfillRootActorListAsync(transaction, token);
        }, cancellationToken);
        if (!advanced && db.Database.IsNpgsql())
        {
            // An earlier coordinator can have advanced the format before this projection was required.
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            var current = await ReadStoreFormatAsync(db, FormatLock.Shared, cancellationToken);
            if (current != next)
                throw new StoreFormatException($"Cannot prepare the root actor projection for {next}. The store records {current}.");
            await LockRootSchemaAsync(db, cancellationToken);
            await BackfillRootActorListAsync(db, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        return advanced;
    }

    // Root admission takes this lock after the format marker and before any root-table DDL.
    private static Task LockRootSchemaAsync(KernelDb db, CancellationToken token) => db.Database.IsNpgsql()
        ? db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(48602345, 1)", token)
        : Task.CompletedTask;

    private static async Task BackfillRootActorListAsync(KernelDb db, CancellationToken token)
    {
        if (!db.Database.IsNpgsql()) return;
        var missing = await db.Database.SqlQueryRaw<bool>("""
            SELECT to_regclass('"FoundationRootBirths"') IS NOT NULL
                AND to_regclass('"FoundationActorList"') IS NULL AS "Value"
            """).SingleAsync(token);
        if (missing)
        {
            await RequirePreviewInstallSchemaAsync(db, token);
            await InstallRootActorListAsync(db, token);
        }
    }

    private async Task AddGenerationColumns(KernelDb db, CancellationToken token)
    {
        if (Engine is MySqlEngine)
        {
            // MySQL DDL commits implicitly. Keep the marker and live-node check locked on the
            // coordinator connection. These additive columns are inert until the marker advances.
            await using var ddl = create();
            if (ReferenceEquals(db.Database.GetDbConnection(), ddl.Database.GetDbConnection()))
                throw new StoreFormatException("MySQL format migration needs a separate connection for its additive DDL.");
            await AddMySqlColumn("Instances", "NextJobGeneration", "BIGINT NULL");
            await AddMySqlColumn("Messages", "JobGeneration", "BIGINT NOT NULL DEFAULT 0");
            await InstallJobGenerationGuard(ddl, token);
            return;

            async Task AddMySqlColumn(string table, string column, string definition)
            {
                try { await ddl.Database.ExecuteSqlRawAsync($"ALTER TABLE `{table}` ADD COLUMN `{column}` {definition}", token); }
                catch (MySqlException ex) when (ex.Number == 1060)
                {
                    // A previous attempt may have prepared the column before its marker rolled back.
                    var nullable = table == "Instances" ? "YES" : "NO";
                    var expectedDefault = table == "Instances" ? null : "0";
                    var existing = await ddl.Database.SqlQuery<ColumnShape>($"""
                        SELECT IS_NULLABLE AS IsNullable, COLUMN_DEFAULT AS DefaultValue, COLUMN_TYPE AS ColumnType,
                               EXTRA AS Extra, GENERATION_EXPRESSION AS GenerationExpression
                        FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE()
                        AND TABLE_NAME = {table} AND COLUMN_NAME = {column}
                        """).SingleAsync(token);
                    if (existing.IsNullable != nullable || existing.DefaultValue != expectedDefault || existing.ColumnType != "bigint" ||
                        existing.Extra.Length != 0 || existing.GenerationExpression.Length != 0)
                        throw new StoreFormatException($"The pre-existing {table}.{column} column has an incompatible shape.");
                }
            }
        }

        await db.Database.ExecuteSqlRawAsync(Q("""ALTER TABLE "Instances" ADD "NextJobGeneration" BIGINT NULL"""), token);
        await db.Database.ExecuteSqlRawAsync(Q("""ALTER TABLE "Messages" ADD "JobGeneration" BIGINT NOT NULL DEFAULT 0"""), token);
        await InstallJobGenerationGuard(db, token);
    }

    private async Task InstallJobGenerationGuard(KernelDb db, CancellationToken token)
    {
        // The legacy physical address cannot be recycled. Its live checkpoint is one creation.
        // A retired marker may release the counter. Future multi-creation writers need a creation predicate.
        var sql = Engine switch
        {
            SqlServerEngine => """
                CREATE OR ALTER TRIGGER kernel_job_generation ON Instances AFTER UPDATE AS
                BEGIN
                    SET NOCOUNT ON;
                    IF EXISTS (SELECT 1 FROM inserted n JOIN deleted o ON n.Id = o.Id
                      WHERE n.Retired = 0 AND o.NextJobGeneration IS NOT NULL
                      AND (n.NextJobGeneration IS NULL OR n.NextJobGeneration < o.NextJobGeneration))
                        THROW 51003, 'Job generation counter cannot rewind.', 1;
                END
                """,
            MySqlEngine => "CREATE TRIGGER IF NOT EXISTS kernel_job_generation BEFORE UPDATE ON Instances FOR EACH ROW\n" + MySqlGenerationBody,
            _ when postgres => """
                CREATE OR REPLACE FUNCTION kernel_job_generation_guard() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF NOT NEW."Retired" AND OLD."NextJobGeneration" IS NOT NULL AND
                       (NEW."NextJobGeneration" IS NULL OR NEW."NextJobGeneration" < OLD."NextJobGeneration") THEN
                        RAISE EXCEPTION 'Job generation counter cannot rewind.' USING ERRCODE = 'KJG01';
                    END IF;
                    RETURN NEW;
                END $$;
                CREATE OR REPLACE TRIGGER kernel_job_generation BEFORE UPDATE ON "Instances"
                    FOR EACH ROW EXECUTE FUNCTION kernel_job_generation_guard();
                """,
            _ => "CREATE TRIGGER IF NOT EXISTS " + SqliteGenerationClause
        };
        await db.Database.ExecuteSqlRawAsync(sql, token);
        // Both dialects keep an existing trigger with the same name. Verify the installed definition
        // before admitting the store or publishing its format, including after interrupted MySQL DDL.
        if (Engine is MySqlEngine)
        {
            var body = await db.Database.SqlQueryRaw<string>("""
                SELECT ACTION_STATEMENT AS Value FROM information_schema.TRIGGERS
                WHERE BINARY TRIGGER_SCHEMA = BINARY DATABASE() AND TRIGGER_NAME = 'kernel_job_generation'
                  AND BINARY EVENT_OBJECT_SCHEMA = BINARY DATABASE()
                  AND (BINARY EVENT_OBJECT_TABLE = BINARY 'Instances' OR
                       (@@lower_case_table_names <> 0 AND LOWER(EVENT_OBJECT_TABLE) = 'instances'))
                  AND EVENT_MANIPULATION = 'UPDATE' AND ACTION_TIMING = 'BEFORE' AND ACTION_ORIENTATION = 'ROW'
                """).SingleOrDefaultAsync(token);
            RequireDefinition(body, MySqlGenerationBody);
        }
        else if (db.Database.IsSqlite())
        {
            var definition = await db.Database.SqlQueryRaw<string>("""
                SELECT sql AS Value FROM sqlite_schema WHERE type = 'trigger'
                  AND name = 'kernel_job_generation' COLLATE NOCASE AND tbl_name = 'Instances' COLLATE NOCASE
                """).SingleOrDefaultAsync(token);
            RequireDefinition(definition, "CREATE TRIGGER " + SqliteGenerationClause);
        }
    }

    private static void RequireDefinition(string? actual, string expected)
    {
        if (actual?.ReplaceLineEndings("\n") != expected.ReplaceLineEndings("\n"))
            throw new StoreFormatException("The pre-existing kernel_job_generation trigger has an incompatible definition.");
    }

    private const string MySqlGenerationBody = """
        BEGIN
            IF NEW.Retired = 0 AND OLD.NextJobGeneration IS NOT NULL AND
               (NEW.NextJobGeneration IS NULL OR NEW.NextJobGeneration < OLD.NextJobGeneration) THEN
                SIGNAL SQLSTATE '45000' SET MYSQL_ERRNO = 51003, MESSAGE_TEXT = 'Job generation counter cannot rewind.';
            END IF;
        END
        """;

    private const string SqliteGenerationClause = """
        kernel_job_generation BEFORE UPDATE ON Instances
        WHEN NEW.Retired = 0 AND OLD.NextJobGeneration IS NOT NULL AND
             (NEW.NextJobGeneration IS NULL OR NEW.NextJobGeneration < OLD.NextJobGeneration)
        BEGIN SELECT RAISE(ABORT, 'Job generation counter cannot rewind.'); END
        """;

    private sealed class ColumnShape
    {
        public string IsNullable { get; set; } = "";
        public string? DefaultValue { get; set; }
        public string ColumnType { get; set; } = "";
        public string Extra { get; set; } = "";
        public string GenerationExpression { get; set; } = "";
    }
}
