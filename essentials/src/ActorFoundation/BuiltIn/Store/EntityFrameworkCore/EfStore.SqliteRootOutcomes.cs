namespace Kernel.EntityFramework;

public sealed partial class EfStore
{
    private static IEnumerable<SqliteDefinition> SqliteRootOutcomes()
    {
        // An outcome creates a deferred foreign-key violation. The matching checkpoint resolves it before commit.
        // A successful commit leaves both tables empty. The batch admits one checkpoint mutation per actor.
        yield return Table("FoundationRootOutcomeCommit", """
            "Marker" INTEGER PRIMARY KEY CHECK ("Marker" = 0)
            """);
        yield return Table("FoundationRootPendingOutcomes", """
            "Instance" TEXT NOT NULL,
            "Partition" INTEGER NOT NULL,
            "Revision" INTEGER NOT NULL,
            "Position" INTEGER NOT NULL,
            "Entries" TEXT NOT NULL,
            "CommitMarker" INTEGER NOT NULL DEFAULT 1 CHECK ("CommitMarker" = 1)
                REFERENCES "FoundationRootOutcomeCommit" ("Marker") DEFERRABLE INITIALLY DEFERRED,
            PRIMARY KEY ("Instance", "Partition", "Revision", "Position")
            """);
        yield return Index("IX_FoundationRootJournal_Outcome", "Journal",
            RenderSqliteOutcomePrefixes($"""
                ("Partition", "Instance", "Revision") WHERE {RootOutcomeEntry}
                """));
        yield return new("trigger", "kernel_record_root_pending_outcome", "Journal",
            RenderSqliteOutcomePrefixes($$"""
                CREATE TRIGGER kernel_record_root_pending_outcome AFTER INSERT ON "Journal"
                WHEN NEW.{{RootOutcomeEntry}} AND EXISTS (SELECT 1 FROM "Instances" i
                    WHERE i."Id" = NEW."Instance" AND i."FoundationReference" IS NOT NULL)
                BEGIN
                    INSERT INTO "FoundationRootPendingOutcomes" ("Instance", "Partition", "Revision", "Position", "Entries")
                    VALUES (NEW."Instance", NEW."Partition", NEW."Revision", NEW."Position", NEW."Entries");
                END
                """));

        // The parked-outcome predicate refers to the checkpoint as i and the journal entry as NEW.
        var resolved = RenderSqliteOutcomePrefixes(RootParkedOutcomeCheckpoint)
            .Replace("NEW.", "", StringComparison.Ordinal)
            .Replace("i.", "NEW.", StringComparison.Ordinal);
        yield return new("trigger", "kernel_resolve_root_pending_outcome", "Instances", $$"""
            CREATE TRIGGER kernel_resolve_root_pending_outcome AFTER UPDATE OF "Revision" ON "Instances"
            WHEN NEW."FoundationReference" IS NOT NULL AND NEW."Revision" IS DISTINCT FROM OLD."Revision"
            BEGIN
                DELETE FROM "FoundationRootPendingOutcomes"
                WHERE "Instance" = NEW."Id" AND "Partition" = NEW."Partition" AND "Revision" = NEW."Revision"
                    AND ({{resolved}}) IS TRUE;
            END
            """);
    }
}
