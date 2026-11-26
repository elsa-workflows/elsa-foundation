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
                ("Instance", "Partition", "Revision") WHERE {RootOutcomeEntry}
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

        var resolved = SqliteParkedOutcomeCheckpoint("");
        yield return new("trigger", "kernel_resolve_root_pending_outcome", "Instances", $$"""
            CREATE TRIGGER kernel_resolve_root_pending_outcome AFTER UPDATE OF "Revision" ON "Instances"
            WHEN NEW."FoundationReference" IS NOT NULL AND NEW."Revision" IS DISTINCT FROM OLD."Revision"
            BEGIN
                DELETE FROM "FoundationRootPendingOutcomes"
                WHERE "Instance" = NEW."Id" AND "Partition" = NEW."Partition" AND "Revision" = NEW."Revision"
                    AND ({{resolved}}) IS TRUE;
            END
            """);

        // The first turn journals before its root is bound, just as an ordinary turn can journal before a row exists.
        // Once a row becomes qualified, check those outcomes against the new checkpoint as PostgreSQL does at commit.
        const string unpaired = """
            SELECT RAISE(ABORT, 'An operator outcome entry requires the checkpoint write at its revision')
            WHERE EXISTS (SELECT 1 FROM "Journal" j WHERE j."Instance" = NEW."Id" AND j./*outcome-entry*/
                AND NOT ((j."Partition" = NEW."Partition" AND j."Revision" = NEW."Revision" AND /*outcome-checkpoint*/) IS TRUE));
            """;
        var checkFirst = unpaired.Replace("/*outcome-entry*/", RenderSqliteOutcomePrefixes(RootOutcomeEntry), StringComparison.Ordinal)
            .Replace("/*outcome-checkpoint*/", SqliteParkedOutcomeCheckpoint("j."), StringComparison.Ordinal);
        yield return new("trigger", "kernel_require_first_root_outcome_bind", "Instances", $$"""
            CREATE TRIGGER kernel_require_first_root_outcome_bind AFTER UPDATE OF "FoundationReference" ON "Instances"
            WHEN OLD."FoundationReference" IS NULL AND NEW."FoundationReference" IS NOT NULL
            BEGIN {{checkFirst}} END
            """);
        yield return new("trigger", "kernel_require_first_root_outcome_insert", "Instances", $$"""
            CREATE TRIGGER kernel_require_first_root_outcome_insert AFTER INSERT ON "Instances"
            WHEN NEW."FoundationReference" IS NOT NULL
            BEGIN {{checkFirst}} END
            """);
    }

    // The shared predicate refers to the checkpoint as i and the journal entry as NEW.
    private static string SqliteParkedOutcomeCheckpoint(string journalAlias) =>
        RenderSqliteOutcomePrefixes(RootParkedOutcomeCheckpoint)
            .Replace("NEW.", journalAlias, StringComparison.Ordinal)
            .Replace("i.", "NEW.", StringComparison.Ordinal);
}
