using Microsoft.EntityFrameworkCore;

namespace Kernel.EntityFramework;

public sealed partial class EfStore
{
    private sealed record SqliteWorkflowHistoryRow(string Reference, InstanceStatus Status, string Header, long UpdatedAt);

    private static IEnumerable<SqliteDefinition> SqliteRootWorkflowHistoryDefinitions()
    {
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
                    AND (OLD."FoundationReference" IS NULL AND NEW."Revision" = 1 OR
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
            var latest = Math.Min(to ?? long.MaxValue, afterTime ?? long.MaxValue);
            var rows = await db.Database.SqlQuery<SqliteWorkflowHistoryRow>($"""
                /* sqlite-root-workflow-history */
                SELECT h."Reference", l."Status", l."Header", h."UpdatedAt"
                FROM "FoundationWorkflowHistory" h JOIN "FoundationActorList" l USING ("Tenant", "Reference")
                WHERE h."Tenant" = {tenant} AND l."Type" COLLATE BINARY = {encodedType}
                    AND ({encodedDefinition} IS NULL OR EXISTS (SELECT 1 FROM json_each(l."Labels") label
                        WHERE label.value COLLATE BINARY = {encodedDefinition}))
                    AND ({status} IS NULL OR l."Status" = {status})
                    AND h."UpdatedAt" BETWEEN {earliest} AND {latest}
                    AND ({afterTime} IS NULL OR h."UpdatedAt" < {afterTime} OR h."UpdatedAt" = {afterTime} AND h."Reference" > {afterRef})
                ORDER BY h."UpdatedAt" DESC, h."Reference" LIMIT {limit}
                """).ToListAsync(token);
            var items = rows.Select(row => new RootWorkflowHistoryItem(row.Reference, row.Status,
                DurableKeyJson.Read<RootHeader>(row.Header).Labels.FirstOrDefault(label => label.Key == "definition").Value, row.UpdatedAt)).ToArray();
            return new RootWorkflowHistoryPage(items, at);
        }, token);
}
