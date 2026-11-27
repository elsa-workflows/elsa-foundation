using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;

namespace Kernel.EntityFramework;

public sealed partial class EfStore
{
    private sealed record SqliteWorkflowHistoryRow(string Reference, InstanceStatus Status, string Header, long UpdatedAt);

    private static IEnumerable<SqliteDefinition> SqliteRootWorkflowHistoryDefinitions(KernelDb db)
    {
        var firstRevision = db.UsesRootInlinePromotion ? """b."FirstRevision" """ : "1 ";
        yield return Table("FoundationWorkflowHistory", """
            "Tenant" TEXT NOT NULL,
            "Reference" TEXT NOT NULL,
            "Revision" INTEGER NOT NULL CHECK ("Revision" > 0),
            "UpdatedAt" INTEGER NOT NULL CHECK ("UpdatedAt" > 0),
            PRIMARY KEY ("Tenant", "Reference"),
            FOREIGN KEY ("Tenant", "Reference") REFERENCES "FoundationActorList" ("Tenant", "Reference") ON DELETE CASCADE DEFERRABLE INITIALLY DEFERRED
            """);
        yield return Index("IX_FoundationWorkflowHistory_Chronology", "FoundationWorkflowHistory", """("Tenant", "UpdatedAt" DESC, "Reference")""");
        yield return new("trigger", "kernel_project_root_workflow_history", "Instances", $"""
            CREATE TRIGGER kernel_project_root_workflow_history AFTER UPDATE OF "FoundationReference", "Revision" ON "Instances"
            WHEN NEW."FoundationReference" IS NOT NULL AND NOT NEW."Retired" AND
                (OLD."FoundationReference" IS DISTINCT FROM NEW."FoundationReference" OR OLD."Revision" IS DISTINCT FROM NEW."Revision")
            BEGIN
                INSERT INTO "FoundationWorkflowHistory" ("Tenant", "Reference", "Revision", "UpdatedAt")
                SELECT b."Tenant", b."Reference", NEW."Revision", CASE WHEN NEW."FoundationTerminalRevision" = NEW."Revision"
                    THEN NEW."FoundationTerminalAt" ELSE {SqliteCommitDialect.MillisecondClock} END
                FROM "FoundationRootBirths" b
                WHERE b."Tenant" = NEW."FoundationTenant" AND b."Reference" = NEW."FoundationReference"
                    AND b."Actor" = NEW."Id" AND b."Partition" = NEW."Partition" AND b."Creation" = 1
                    AND (OLD."FoundationReference" IS NULL AND NEW."Revision" = {firstRevision}OR
                        OLD."FoundationReference" = NEW."FoundationReference" AND NEW."Revision" = OLD."Revision" + 1
                        AND NEW."FoundationWriteRevision" = NEW."Revision")
                ON CONFLICT ("Tenant", "Reference") DO UPDATE SET "Revision" = excluded."Revision", "UpdatedAt" = excluded."UpdatedAt"
                WHERE "FoundationWorkflowHistory"."Revision" < excluded."Revision";
                SELECT RAISE(ABORT, 'Workflow history requires the next qualified root checkpoint') WHERE changes() <> 1;
            END
            """);
    }

    private static Task<RootWorkflowHistoryPage> ReadSqliteRootWorkflowHistoryAsync(KernelDb db, string tenant, string type, string? definition,
        int? status, long? from, long? to, long? afterTime, string? afterRef, int limit, CancellationToken token) =>
        ReadSqliteRootSnapshotAsync(db, async at =>
        {
            var encodedType = DurableKeyJson.Write(type);
            var encodedDefinition = definition is null ? null : DurableKeyJson.Write(new KeyValuePair<string, string>("definition", definition));
            var earliest = from ?? long.MinValue;
            var latest = to ?? long.MaxValue;
            List<SqliteWorkflowHistoryRow> rows;
            if (afterTime is { } time && time <= latest)
            {
                // A timestamp tie is a separate seek. An OR over time and reference would revisit earlier ties on every page.
                rows = time >= earliest && afterRef is not null && limit > 0
                    ? await ReadWindowAsync(time, time, limit, afterRef)
                    : [];
                if (rows.Count < limit && time > earliest)
                    rows.AddRange(await ReadWindowAsync(earliest, time - 1, limit - rows.Count));
            }
            else
                rows = await ReadWindowAsync(earliest, latest, limit);

            var items = rows.Select(row => new RootWorkflowHistoryItem(row.Reference, row.Status,
                DurableKeyJson.Read<RootHeader>(row.Header).Labels.FirstOrDefault(label => label.Key == "definition").Value, row.UpdatedAt)).ToArray();
            return new RootWorkflowHistoryPage(items, at);

            Task<List<SqliteWorkflowHistoryRow>> ReadWindowAsync(long lower, long upper, int take, string? reference = null)
            {
                var range = reference is not null
                    ? """h."UpdatedAt" = {4} AND h."Reference" > {5}"""
                    : """h."UpdatedAt" BETWEEN {4} AND {5}""";
                var sql = """
                    /* sqlite-root-workflow-history */
                    SELECT h."Reference", l."Status", l."Header", h."UpdatedAt"
                    FROM "FoundationWorkflowHistory" h JOIN "FoundationActorList" l USING ("Tenant", "Reference")
                    WHERE h."Tenant" = {0} AND l."Type" COLLATE BINARY = {1}
                        AND ({2} IS NULL OR EXISTS (SELECT 1 FROM json_each(l."Labels") label
                            WHERE label.value COLLATE BINARY = {2}))
                        AND ({3} IS NULL OR l."Status" = {3}) AND /* chronology */
                    ORDER BY h."UpdatedAt" DESC, h."Reference" LIMIT {6}
                    """.Replace("/* chronology */", range, StringComparison.Ordinal);
                var query = FormattableStringFactory.Create(sql, tenant, encodedType, encodedDefinition, status, lower, reference ?? (object)upper, take);
                return db.Database.SqlQuery<SqliteWorkflowHistoryRow>(query).ToListAsync(token);
            }
        }, token);
}
