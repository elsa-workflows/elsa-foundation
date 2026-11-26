using Microsoft.EntityFrameworkCore;

namespace Kernel.EntityFramework;

public sealed partial class EfStore
{
    // The reasons a kept Park input may carry: Consumed always, Arrived with Park arrivals.
    internal static string KeptReasons(KernelDb db) => db.UsesRootParkArrivals
        ? """ "FoundationKeptBy" IN (0, 1) """
        : """ "FoundationKeptBy" = 0 """;

    internal const string KeptByCheck = """ CHECK ("FoundationKeptBy" IS NULL OR (/*kept-reasons*/ AND "Done"))""";

    private const string InspectableIndex = """
        ("Partition", "Target" COLLATE "C", "Seq", "Id" COLLATE "C")
                        WHERE "InboxScope" = -4 AND "EventMatch" IS NULL AND (NOT "Done" OR /*kept-reasons*/)
        """;

    private sealed record InstalledShapes(bool KeptBy, bool Inspectable);

    // An install adds a column CHECK or a partial index only when its name is free, so a database that a narrower set
    // installed first keeps that set's definition. After installing, each capability-driven one must equal a twin built
    // from this set's own source on an empty copy of its table. The server renders both, so the comparison does not depend
    // on how a PostgreSQL version prints an expression. The twin is an ordinary table under a unique name in the store
    // schema, where the installer already creates relations, and a savepoint rolls it back, so the install needs no
    // TEMPORARY privilege. Functions need no check: every install reapplies them with CREATE OR REPLACE, and the store's
    // trigger presence checks cover their attachment. Preview databases are recreated, not migrated, so a mismatch
    // refuses the install. Install only: the per-operation pins check where relations live, and a twin needs DDL.
    internal static async Task RequireRootDefinitionsAsync(KernelDb db, CancellationToken token)
    {
        if (!db.UsesRootParkOpening) return;
        var transaction = db.Database.CurrentTransaction
            ?? throw new InvalidOperationException("The root definition check runs inside the install transaction.");
        // The preview install schema check has placed the store relations in the active schema, where these are created.
        var unique = Guid.NewGuid().ToString("N");
        var table = "kernel_definition_twin_" + unique;
        var index = "kernel_definition_index_" + unique;
        const string savepoint = "kernel_root_definition_twin";
        await transaction.CreateSavepointAsync(savepoint, token);
        var twin = $"""
            CREATE TABLE "{table}" (LIKE "Messages");
            ALTER TABLE "{table}" ADD {KeptByCheck};
            """ + (db.UsesRootParkInspection ? $"""

            CREATE INDEX "{index}" ON "{table}" {InspectableIndex};
            """ : "");
        await db.Database.ExecuteSqlRawAsync(twin.Replace("/*kept-reasons*/", KeptReasons(db), StringComparison.Ordinal), token);
        var shapes = await db.Database.SqlQueryRaw<InstalledShapes>($"""
            WITH relation(installed, oid, inspectable) AS (
                VALUES (true, to_regclass('"Messages"'), 'IX_FoundationRootInbox_Inspectable'), (false, to_regclass('"{table}"'), '{index}')
            ), shape AS (
                SELECT r.installed, (
                    SELECT array_agg(pg_catalog.pg_get_constraintdef(c.oid) ORDER BY pg_catalog.pg_get_constraintdef(c.oid))
                    FROM pg_catalog.pg_constraint c
                    JOIN pg_catalog.pg_attribute a ON a.attrelid = c.conrelid AND a.attname = 'FoundationKeptBy' AND a.attnum = ANY (c.conkey)
                    WHERE c.conrelid = r.oid AND c.contype = 'c') AS kept, (
                    -- The definition is compared from USING on, so the index and table names drop out.
                    SELECT array_agg(format('unique=%s valid=%s%s', i.indisunique, i.indisvalid,
                        substring(pg_catalog.pg_get_indexdef(i.indexrelid) FROM ' USING .*$')))
                    FROM pg_catalog.pg_index i JOIN pg_catalog.pg_class x ON x.oid = i.indexrelid
                    WHERE i.indrelid = r.oid AND x.relname = r.inspectable) AS inspectable
                FROM relation r
            )
            SELECT i.kept IS NOT DISTINCT FROM t.kept AS "KeptBy", i.inspectable IS NOT DISTINCT FROM t.inspectable AS "Inspectable"
            FROM shape i JOIN shape t ON i.installed AND NOT t.installed
            """).SingleAsync(token);
        await transaction.RollbackToSavepointAsync(savepoint, token);
        var differences = new List<string>();
        if (!shapes.KeptBy) differences.Add("the FoundationKeptBy CHECK");
        if (!shapes.Inspectable) differences.Add("IX_FoundationRootInbox_Inspectable");
        if (differences.Count != 0)
            throw new StoreFormatException("The installed root definitions differ from this store profile: " + string.Join(", ", differences) +
                ". Recreate the preview database.");
    }
}
