using System.Text.RegularExpressions;
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

    private sealed record InstalledBody(string Name, string Body);

    private sealed record InstalledShapes(bool KeptBy, bool Inspectable);

    [GeneratedRegex("""CREATE OR REPLACE FUNCTION (?<name>\w+)\([^)]*\)[^$]*\$fn\$(?<body>.*?)\$fn\$""", RegexOptions.Singleline)]
    private static partial Regex InstalledFunction();

    // An install adds a column CHECK or a partial index only when its name is free, so a database that a narrower set
    // installed first keeps that set's definition. After installing, each capability-driven one must equal a twin built
    // from this set's own source on an empty temporary copy of its table. The server renders both, so the comparison does
    // not depend on how a PostgreSQL version prints an expression. Trigger functions are rewritten by every install, and
    // their stored source must be the body this set writes. Preview databases are recreated, not migrated, so a mismatch
    // refuses the install. Install only: the per-operation pins check where relations live, and a twin needs DDL.
    internal static async Task RequireRootDefinitionsAsync(KernelDb db, CancellationToken token)
    {
        // Raw commands are formatted before they run, so a literal brace in the source is doubled there.
        var expected = new[] { FirstRootCheckpointSql(db), ActorListProjection.Of(db).InstallSql, db.UsesRootParkInspection ? ParkReadModelSql(db) : "" }
            .SelectMany(sql => InstalledFunction().Matches(sql.Replace("{{", "{", StringComparison.Ordinal).Replace("}}", "}", StringComparison.Ordinal)))
            .ToDictionary(match => match.Groups["name"].Value, match => match.Groups["body"].Value, StringComparer.Ordinal);
        var names = expected.Keys.ToArray();
        var stored = await db.Database.SqlQuery<InstalledBody>($"""
            SELECT p.proname::text AS "Name", p.prosrc AS "Body" FROM pg_catalog.pg_proc p
            WHERE p.pronamespace = (SELECT relnamespace FROM pg_catalog.pg_class WHERE oid = to_regclass('"Instances"'))
                AND p.proname::text = ANY({names})
            """).ToListAsync(token);
        var functions = names.Where(name => stored.Count(row => row.Name == name) != 1 ||
            stored.Single(row => row.Name == name).Body != expected[name]).ToList();

        if (db.UsesRootParkOpening)
        {
            var twin = """
                CREATE TEMP TABLE kernel_root_definition_twin (LIKE "Messages") ON COMMIT DROP;
                ALTER TABLE pg_temp.kernel_root_definition_twin ADD
                """ + KeptByCheck + ";" + (db.UsesRootParkInspection ? """

                CREATE INDEX "IX_FoundationRootInbox_Inspectable" ON pg_temp.kernel_root_definition_twin
                """ + InspectableIndex + ";" : "");
            await db.Database.ExecuteSqlRawAsync(twin.Replace("/*kept-reasons*/", KeptReasons(db), StringComparison.Ordinal), token);
            var shapes = await db.Database.SqlQueryRaw<InstalledShapes>("""
                WITH relation(installed, oid) AS (
                    VALUES (true, to_regclass('"Messages"')), (false, to_regclass('pg_temp.kernel_root_definition_twin'))
                ), shape AS (
                    SELECT r.installed, (
                        SELECT array_agg(pg_catalog.pg_get_constraintdef(c.oid) ORDER BY pg_catalog.pg_get_constraintdef(c.oid))
                        FROM pg_catalog.pg_constraint c
                        JOIN pg_catalog.pg_attribute a ON a.attrelid = c.conrelid AND a.attname = 'FoundationKeptBy' AND a.attnum = ANY (c.conkey)
                        WHERE c.conrelid = r.oid AND c.contype = 'c') AS kept, (
                        SELECT array_agg(substring(pg_catalog.pg_get_indexdef(i.indexrelid) FROM ' USING .*$'))
                        FROM pg_catalog.pg_index i JOIN pg_catalog.pg_class x ON x.oid = i.indexrelid
                        WHERE i.indrelid = r.oid AND x.relname = 'IX_FoundationRootInbox_Inspectable') AS inspectable
                    FROM relation r
                )
                SELECT i.kept IS NOT DISTINCT FROM t.kept AS "KeptBy", i.inspectable IS NOT DISTINCT FROM t.inspectable AS "Inspectable"
                FROM shape i JOIN shape t ON i.installed AND NOT t.installed
                """).SingleAsync(token);
            await db.Database.ExecuteSqlRawAsync("DROP TABLE pg_temp.kernel_root_definition_twin", token);
            if (!shapes.KeptBy) functions.Add("the FoundationKeptBy CHECK");
            if (!shapes.Inspectable) functions.Add("IX_FoundationRootInbox_Inspectable");
        }
        if (functions.Count != 0)
            throw new StoreFormatException("The installed root definitions differ from this store profile: " + string.Join(", ", functions) +
                ". Recreate the preview database.");
    }
}
