using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;

namespace Kernel.EntityFramework;

public sealed partial class EfStore
{
    internal static Task InstallFirstRootCheckpointsAsync(KernelDb db, CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlRawAsync(RenderPostgresFirstRootGuards(db), cancellationToken);

    internal static string RenderPostgresFirstRootGuards(KernelDb db) => RenderRootGuards(db, PostgresFirstRootGuards);

    private const string RootCheckpointWriteAllowed = """
        NEW."FoundationWriteRevision" = OLD."Revision" + 1
                AND NEW."Revision" = NEW."FoundationWriteRevision"
                AND NEW."FoundationReference" = OLD."FoundationReference" AND NEW."FoundationTenant" = OLD."FoundationTenant"
                AND NEW."Id" = OLD."Id" AND NEW."Partition" = OLD."Partition" AND NOT OLD."Retired" /*root-park-generation*/
                AND ((OLD."Status" = 0 AND NOT NEW."Retired") OR /*root-park-continues*/ OR /*root-park-recover*/ OR /*root-parked-terminate*/ OR
                    (/*root-retirement*/ AND NEW."Retired" AND NEW."State" = 'null' AND NEW."FoundationHeader" IS NULL /*root-retirement-wire*/
                        AND ((OLD."Status" <> 0 AND NEW."Status" = OLD."Status"
                            AND NEW."FoundationTerminalRevision" = OLD."FoundationTerminalRevision"
                            AND NEW."FoundationTerminalAt" = OLD."FoundationTerminalAt")
                        OR /*root-parked-retirement*/
                        OR (/*root-atomic-retirement*/ AND OLD."Status" = 0 AND NEW."Status" = /*cancelled*/
                            AND OLD."FoundationTerminalRevision" IS NULL AND OLD."FoundationTerminalAt" IS NULL
                            AND NEW."FoundationTerminalRevision" = NEW."Revision" AND NEW."FoundationTerminalAt" IS NOT NULL))))
        """;

    private const string RootJournalWriteRefused = """
        EXISTS (SELECT 1 FROM "Instances" i WHERE i."Id" = NEW."Instance" AND i."FoundationReference" IS NOT NULL
                AND (i."Partition" <> NEW."Partition" OR NEW."Revision" <> i."Revision" + 1 OR i."Status" <> 0 OR i."Retired")
                AND NOT (/*root-park-journal*/ OR (/*root-retirement*/ AND i."Retired" AND i."Partition" = NEW."Partition" AND NEW."Revision" = -1
                    AND NEW."Entries" = '["delete"]' AND NOT EXISTS (
                        SELECT 1 FROM "Journal" j WHERE j."Instance" = i."Id" AND j."Partition" = i."Partition" AND j."Revision" = -1))))
        """;

    private const string PostgresFirstRootGuards = """
        ALTER TABLE "Instances" ADD COLUMN IF NOT EXISTS "FoundationReference" text COLLATE "C" REFERENCES "FoundationRootBirths" ("Reference"),
                              ADD COLUMN IF NOT EXISTS "FoundationTenant" text COLLATE "C",
                              ADD COLUMN IF NOT EXISTS "FoundationHeader" text,
                              ADD COLUMN IF NOT EXISTS "FoundationWriteRevision" bigint,
                              ADD COLUMN IF NOT EXISTS "FoundationTerminalRevision" bigint,
                              ADD COLUMN IF NOT EXISTS "FoundationTerminalAt" bigint;
        CREATE OR REPLACE FUNCTION kernel_require_root_checkpoint_writer() RETURNS trigger LANGUAGE plpgsql AS $fn$
        BEGIN
            IF TG_OP = 'UPDATE' AND /*checkpoint-write-allowed*/
            THEN RETURN NEW; END IF;
            RAISE EXCEPTION 'Qualified actor checkpoint requires a qualified update' USING ERRCODE = '23514';
        END
        $fn$;
        CREATE OR REPLACE TRIGGER kernel_require_root_checkpoint_writer BEFORE UPDATE OR DELETE ON "Instances"
        FOR EACH ROW WHEN (OLD."FoundationReference" IS NOT NULL) EXECUTE FUNCTION kernel_require_root_checkpoint_writer();
        CREATE OR REPLACE FUNCTION kernel_require_root_journal_revision() RETURNS trigger LANGUAGE plpgsql AS $fn$
        BEGIN
            IF /*journal-write-refused*/
            THEN RAISE EXCEPTION 'Qualified actor journal requires the next running checkpoint revision' USING ERRCODE = '23514'; END IF;
            /*root-park-recovered-journal*/
            RETURN NEW;
        END
        $fn$;
        CREATE OR REPLACE TRIGGER kernel_require_root_journal_revision BEFORE INSERT ON "Journal"
        FOR EACH ROW EXECUTE FUNCTION kernel_require_root_journal_revision();
        """;

    private static string RenderRootGuards(KernelDb db, string template) => template
        .Replace("/*checkpoint-write-allowed*/", RootCheckpointWriteAllowed, StringComparison.Ordinal)
        .Replace("/*journal-write-refused*/", RootJournalWriteRefused, StringComparison.Ordinal)
        .Replace("/*root-park-continues*/", db.UsesRootParkArrivals ? """
            (OLD."Status" = 2 AND NEW."Status" = OLD."Status" AND NOT NEW."Retired" /*parked-clearing-continues*/
                AND OLD."FoundationParkAt" IS NOT NULL AND OLD."FoundationParkRevision" > 0
                AND NEW."FoundationParkAt" = OLD."FoundationParkAt" AND NEW."FoundationParkRevision" = OLD."FoundationParkRevision"
                AND NEW."State" = OLD."State" AND NEW."StateVersion" = OLD."StateVersion" AND NEW."FoundationHeader" = OLD."FoundationHeader"
                AND NEW."Fault" IS NOT DISTINCT FROM OLD."Fault" AND NEW."Suspended" = OLD."Suspended"
                AND NEW."History" IS NOT DISTINCT FROM OLD."History")
            """ : db.UsesRootParkedOperators ? """
            (OLD."Status" = 2 AND NEW."Status" = 2 AND NOT NEW."Retired" AND OLD."FoundationParkAt" IS NOT NULL /*parked-clearing-pinned*/
                AND NEW."FoundationParkAt" = OLD."FoundationParkAt" AND NEW."FoundationTerminalRevision" IS NULL
                AND NEW."State" = OLD."State" AND NEW."StateVersion" = OLD."StateVersion" AND NEW."FoundationHeader" = OLD."FoundationHeader"
                AND NEW."Fault" IS NOT DISTINCT FROM OLD."Fault" AND NEW."Suspended" = OLD."Suspended"
                AND NEW."History" IS NOT DISTINCT FROM OLD."History"
                AND EXISTS (SELECT 1 FROM "Journal" j WHERE j."Partition" = NEW."Partition" AND j."Instance" = NEW."Id"
                    AND j."Revision" = NEW."Revision" AND j."Entries" LIKE '{{"Outcome":"refused",%'))
            """ : "false", StringComparison.Ordinal)
        // S5a: a composed set with Park and operators journals an operator refusal on a parked root at its next revision.
        .Replace("/*root-park-journal*/", db.UsesRootParkArrivals ? """
            (i."Status" = 2 AND NOT i."Retired" AND NEW."Partition" = i."Partition" AND NEW."Revision" = i."Revision" + 1
                AND i."FoundationParkAt" IS NOT NULL AND i."FoundationParkRevision" > 0
                AND (NEW."Entries" = '["parked"]' /*root-park-recover-journal*/))
            """ : db.UsesRootParkedOperators ? """
            (i."Status" = 2 AND NOT i."Retired" AND NEW."Partition" = i."Partition" AND NEW."Revision" = i."Revision" + 1
                AND i."FoundationParkAt" IS NOT NULL AND (NEW."Entries" LIKE '{{"Outcome":"refused",%' /*root-parked-terminated-journal*/))
            """ : "false", StringComparison.Ordinal)
        // Only a recover leaves Faulted for Running. It clears the Park mark and moves the release generation by one.
        // A recover outcome at the new revision binds the write: "recovered" only with that transition, "refused"
        // only with the row unchanged apart from its revision.
        .Replace("/*root-park-generation*/", db.UsesRootParkRecovery ? """
            AND (NEW."FoundationParkGeneration" = OLD."FoundationParkGeneration" OR (OLD."Status" = 2 AND NEW."Status" = 0
                AND NEW."FoundationParkGeneration" = OLD."FoundationParkGeneration" + 1))
            AND NOT EXISTS (SELECT 1 FROM "Journal" j WHERE j."Partition" = NEW."Partition" AND j."Instance" = NEW."Id"
                AND j."Revision" = NEW."Revision" AND /*root-outcome-entry*/
                AND CASE WHEN j."Entries" LIKE '{{"Outcome":"recovered",%' THEN NOT (OLD."Status" = 2 AND NEW."Status" = 0)
                    ELSE j."Entries" LIKE '{{"Outcome":"refused",%' AND ROW(NEW."Status", NEW."Retired", NEW."Suspended", NEW."State",
                        NEW."StateVersion", NEW."FoundationHeader", NEW."Fault", NEW."History", NEW."Jobs", NEW."Children", NEW."Parent",
                        NEW."FoundationParkAt", NEW."FoundationParkRevision", NEW."FoundationParkGeneration",
                        NEW."FoundationTerminalRevision", NEW."FoundationTerminalAt", NEW."Model", NEW."Definition",
                        NEW."NextJobGeneration", NEW."CustomHandles" /*new-wire*/)
                    IS DISTINCT FROM ROW(OLD."Status", OLD."Retired", OLD."Suspended", OLD."State",
                        OLD."StateVersion", OLD."FoundationHeader", OLD."Fault", OLD."History", OLD."Jobs", OLD."Children", OLD."Parent",
                        OLD."FoundationParkAt", OLD."FoundationParkRevision", OLD."FoundationParkGeneration",
                        OLD."FoundationTerminalRevision", OLD."FoundationTerminalAt", OLD."Model", OLD."Definition",
                        OLD."NextJobGeneration", OLD."CustomHandles" /*old-wire*/) END)
            """.Replace("/*root-outcome-entry*/", RootOutcomeEntry, StringComparison.Ordinal)
            // The state wire column exists only on stable-body profiles.
            .Replace("/*new-wire*/", db.UsesRootStableBody ? """, NEW."FoundationStateWire" """ : "", StringComparison.Ordinal)
            .Replace("/*old-wire*/", db.UsesRootStableBody ? """, OLD."FoundationStateWire" """ : "", StringComparison.Ordinal)
            : db.UsesRootParkedOperators ? ParkedRefusalRow(db) : "", StringComparison.Ordinal)
        .Replace("/*root-park-recover*/", db.UsesRootParkRecovery ? """
            (OLD."Status" = 2 AND NEW."Status" = 0 AND NOT NEW."Retired" AND NOT OLD."Suspended" AND NOT NEW."Suspended"
                AND OLD."FoundationParkAt" IS NOT NULL AND OLD."FoundationParkRevision" > 0
                AND NEW."FoundationParkAt" IS NULL AND NEW."FoundationParkRevision" IS NULL AND NEW."Fault" IS NULL
                AND NEW."FoundationParkGeneration" = OLD."FoundationParkGeneration" + 1
                AND NEW."State" = OLD."State" AND NEW."StateVersion" = OLD."StateVersion" AND NEW."FoundationHeader" = OLD."FoundationHeader"
                AND NEW."History" IS NOT DISTINCT FROM OLD."History"
                AND EXISTS (SELECT 1 FROM "Journal" j WHERE j."Partition" = NEW."Partition" AND j."Instance" = NEW."Id"
                    AND j."Revision" = NEW."Revision" AND /*root-outcome-entry*/ AND j."Entries" LIKE '{{"Outcome":"recovered",%'))
            """.Replace("/*root-outcome-entry*/", RootOutcomeEntry, StringComparison.Ordinal) : "false", StringComparison.Ordinal)
        // A recovered entry is journaled only on the parked root it resolves.
        .Replace("/*root-park-recovered-journal*/", db.UsesRootParkRecovery ? """
            IF NEW."Entries" LIKE '{{"Outcome":"recovered",%' AND EXISTS (SELECT 1 FROM "Instances" i WHERE i."Id" = NEW."Instance"
                AND i."FoundationReference" IS NOT NULL AND NOT (i."Status" = 2 AND NOT i."Retired" AND NOT i."Suspended"
                    AND i."Partition" = NEW."Partition" AND NEW."Revision" = i."Revision" + 1
                    AND i."FoundationParkAt" IS NOT NULL AND i."FoundationParkRevision" > 0))
            THEN RAISE EXCEPTION 'A recovered journal entry requires the parked checkpoint it resolves' USING ERRCODE = '23514'; END IF;
            """ : "", StringComparison.Ordinal)
        .Replace("/*root-park-recover-journal*/", db.UsesRootParkRecovery
            // The raw command is formatted, so the literal brace is doubled.
            ? """OR NEW."Entries" LIKE '{{"Outcome":"recovered",%' OR NEW."Entries" LIKE '{{"Outcome":"refused",%'"""
            : db.UsesRootParkedOperators ? """OR NEW."Entries" LIKE '{{"Outcome":"refused",%' /*root-parked-terminated-journal*/""" : "", StringComparison.Ordinal)
        // S5b: a Terminate that names the Park ends a parked root in one commit, journalled "terminated" at its revision.
        .Replace("/*root-parked-terminated-journal*/", db.UsesRootParkedOperators ? ParkedTerminatedJournal(db) : "", StringComparison.Ordinal)
        .Replace("/*root-parked-terminate*/", db.UsesRootParkedOperators ? ParkedTerminateCheckpoint(db) : "false", StringComparison.Ordinal)
        // S5b-3a: a Terminate-then-delete retires a parked root, Cancelled with its terminal mark and its Park cleared.
        // S5b-3b: a Delete retires it still Faulted, under its terminal mark.
        .Replace("/*root-parked-retirement*/", db.UsesRootParkedOperators && db.UsesRootRetirement ? ParkedRetirementCheckpoint(db) : "false",
            StringComparison.Ordinal)
        // S5b-2: a refusal leaves an open clearing as it is, and no keep or other write moves a root whose Park is being cleared.
        .Replace("/*parked-clearing-continues*/", db.UsesRootParkedOperators ? ParkedClearingPinned + ParkedClearingKeeps : "", StringComparison.Ordinal)
        .Replace("/*parked-clearing-pinned*/", db.UsesRootParkedOperators ? ParkedClearingPinned : "", StringComparison.Ordinal)
        .Replace("/*root-retirement*/", db.UsesRootRetirement ? "true" : "false", StringComparison.Ordinal)
        .Replace("/*root-atomic-retirement*/", db.UsesRootTerminateThenDelete ? "true" : "false", StringComparison.Ordinal)
        .Replace("/*root-retirement-wire*/", db.UsesRootRetirement ? """AND NEW."FoundationStateWire" IS NULL""" : "", StringComparison.Ordinal)
        .Replace("/*cancelled*/", FormattableString.Invariant($"{(int)InstanceStatus.Cancelled}"), StringComparison.Ordinal);

    // These templates use the same conditions as the PostgreSQL functions. SQLite attaches one trigger per operation.
    // The explicit IS TRUE on the update matches PL/pgSQL's IF, which also refuses an indeterminate predicate.
    private static IEnumerable<SqliteDefinition> SqliteFirstRootGuards(KernelDb db)
    {
        // SQLite can implicitly delete a row on a key conflict. Protect the target without relying on recursive triggers.
        const string qualifiedTarget = """
            EXISTS (SELECT 1 FROM "Instances" i WHERE i."Id" = NEW."Id" AND i."FoundationReference" IS NOT NULL)
            """;
        yield return new("trigger", "kernel_require_root_checkpoint_insert", "Instances", $$"""
            CREATE TRIGGER kernel_require_root_checkpoint_insert BEFORE INSERT ON "Instances"
            WHEN {{qualifiedTarget}}
            BEGIN SELECT RAISE(ABORT, 'Qualified actor checkpoint requires a qualified update'); END
            """);
        yield return new("trigger", "kernel_require_root_checkpoint_writer", "Instances", RenderSqlite($$"""
            CREATE TRIGGER kernel_require_root_checkpoint_writer BEFORE UPDATE ON "Instances"
            WHEN (OLD."FoundationReference" IS NOT NULL AND NOT (({{RootCheckpointWriteAllowed}}) IS TRUE))
                OR (NEW."Id" <> OLD."Id" AND {{qualifiedTarget}})
            BEGIN SELECT RAISE(ABORT, 'Qualified actor checkpoint requires a qualified update'); END
            """));
        yield return new("trigger", "kernel_require_root_checkpoint_delete", "Instances", """
            CREATE TRIGGER kernel_require_root_checkpoint_delete BEFORE DELETE ON "Instances"
            WHEN OLD."FoundationReference" IS NOT NULL
            BEGIN SELECT RAISE(ABORT, 'Qualified actor checkpoint requires a qualified update'); END
            """);
        yield return new("trigger", "kernel_require_root_journal_revision", "Journal", RenderSqlite($$"""
            CREATE TRIGGER kernel_require_root_journal_revision BEFORE INSERT ON "Journal"
            WHEN ({{RootJournalWriteRefused}})
            BEGIN SELECT RAISE(ABORT, 'Qualified actor journal requires the next running checkpoint revision'); END
            """));

        string RenderSqlite(string template) => PostgresOutcomePrefix.Replace(
            RenderRootGuards(db, template).Replace("ROW(", "(", StringComparison.Ordinal),
            static match => $"{match.Groups["column"].Value} GLOB '{{{match.Groups["prefix"].Value}*'");
    }

    // Only literal outcome prefixes appear in the shared predicates. GLOB keeps their PostgreSQL case sensitivity.
    private static readonly Regex PostgresOutcomePrefix = new(
        """(?<column>(?:[A-Za-z_][A-Za-z0-9_]*\.)?"Entries") LIKE '\{\{(?<prefix>[^'%]*)%'""", RegexOptions.CultureInvariant);
}
