namespace Kernel.EntityFramework;

public sealed partial class EfStore
{
    private static IEnumerable<SqliteDefinition> SqliteRootProjectors()
    {
        // SQLite runs the two projections in one trigger so a Park update sees the actor-list row.
        // JSON values stay encoded until the read path compares them, preserving escaped NUL and backslash.
        yield return new("trigger", "kernel_project_root_actor_list", "Instances", """
            CREATE TRIGGER kernel_project_root_actor_list AFTER UPDATE ON "Instances"
            WHEN NEW."FoundationReference" IS NOT NULL
            BEGIN
                DELETE FROM "FoundationActorList"
                WHERE NEW."Retired" AND "Tenant" = NEW."FoundationTenant" AND "Reference" = NEW."FoundationReference";

                INSERT INTO "FoundationActorList" ("Tenant", "Reference", "Status", "EndedAt", "TerminalRevision", "Header", "Type", "Labels")
                SELECT NEW."FoundationTenant", NEW."FoundationReference", NEW."Status", NEW."FoundationTerminalAt",
                    NEW."FoundationTerminalRevision", NEW."FoundationHeader",
                    NEW."FoundationHeader" -> '$.Model', NEW."FoundationHeader" -> '$.Labels'
                WHERE NOT NEW."Retired" AND (
                    OLD."FoundationReference" IS DISTINCT FROM NEW."FoundationReference"
                    OR OLD."FoundationHeader" IS DISTINCT FROM NEW."FoundationHeader"
                    OR OLD."Status" IS DISTINCT FROM NEW."Status"
                    OR OLD."FoundationTerminalAt" IS DISTINCT FROM NEW."FoundationTerminalAt"
                    OR OLD."FoundationTerminalRevision" IS DISTINCT FROM NEW."FoundationTerminalRevision")
                ON CONFLICT ("Tenant", "Reference") DO UPDATE SET "Status" = excluded."Status", "EndedAt" = excluded."EndedAt",
                    "TerminalRevision" = excluded."TerminalRevision", "Header" = excluded."Header",
                    "Type" = excluded."Type", "Labels" = excluded."Labels";

                SELECT RAISE(ABORT, 'Park requires the projected actor header')
                WHERE NOT NEW."Retired" AND OLD."FoundationParkAt" IS DISTINCT FROM NEW."FoundationParkAt"
                    AND NOT EXISTS (SELECT 1 FROM "FoundationActorList"
                        WHERE "Tenant" = NEW."FoundationTenant" AND "Reference" = NEW."FoundationReference");

                UPDATE "FoundationActorList"
                SET "ParkAt" = NEW."FoundationParkAt", "ParkRevision" = NEW."FoundationParkRevision",
                    "ParkFault" = CASE WHEN NEW."FoundationParkAt" IS NOT NULL THEN NEW."Fault" END
                WHERE NOT NEW."Retired" AND OLD."FoundationParkAt" IS DISTINCT FROM NEW."FoundationParkAt"
                    AND "Tenant" = NEW."FoundationTenant" AND "Reference" = NEW."FoundationReference";
            END
            """);

        yield return new("trigger", "kernel_require_root_actor_list", "Instances", """
            CREATE TRIGGER kernel_require_root_actor_list BEFORE UPDATE ON "Instances"
            WHEN NEW."FoundationReference" IS NOT NULL AND NOT EXISTS (SELECT 1 FROM main.sqlite_schema
                WHERE type = 'trigger' AND name = 'kernel_project_root_actor_list' AND tbl_name = 'Instances')
            BEGIN SELECT RAISE(ABORT, 'Required actor projection is absent'); END
            """);
    }
}
