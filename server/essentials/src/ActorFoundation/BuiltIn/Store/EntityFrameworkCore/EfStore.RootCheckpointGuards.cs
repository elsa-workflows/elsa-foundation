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
                AND NEW."Id" = OLD."Id" AND NEW."Partition" = OLD."Partition" AND NOT OLD."Retired" /*root-park-generation*/ /*pinned-result-write*/
                AND (/*park-clearing-stop-write*/ ((OLD."Status" = 0 AND NOT NEW."Retired") OR /*root-park-continues*/ OR /*root-park-recover*/ OR /*root-parked-terminate*/ OR
                    (/*root-retirement*/ AND NEW."Retired" AND NEW."State" = 'null' AND NEW."FoundationHeader" IS NULL /*root-retirement-wire*/ /*root-retirement-footprint*/
                        AND ((OLD."Status" <> 0 AND NEW."Status" = OLD."Status"
                            AND NEW."FoundationTerminalRevision" = OLD."FoundationTerminalRevision"
                            AND NEW."FoundationTerminalAt" = OLD."FoundationTerminalAt")
                        OR /*root-parked-retirement*/
                        OR (/*root-atomic-retirement*/ AND OLD."Status" = 0 AND NEW."Status" = /*cancelled*/
                            AND OLD."FoundationTerminalRevision" IS NULL AND OLD."FoundationTerminalAt" IS NULL
                            AND NEW."FoundationTerminalRevision" = NEW."Revision" AND NEW."FoundationTerminalAt" IS NOT NULL)))) /*park-clearing-stop-end*/)
        """;

    private const string RootJournalWriteRefused = """
        EXISTS (SELECT 1 FROM "Instances" i WHERE i."Id" = NEW."Instance" AND i."FoundationReference" IS NOT NULL
                AND (i."Partition" <> NEW."Partition" OR NEW."Revision" <> i."Revision" + 1 OR i."Status" <> 0 OR i."Retired")
                AND NOT (/*root-park-journal*/ OR (/*root-retirement*/ AND i."Retired" AND i."Partition" = NEW."Partition" AND NEW."Revision" = -1
                    AND NEW."Entries" = '["delete"]' AND NOT EXISTS (
                        SELECT 1 FROM "Journal" j WHERE j."Instance" = i."Id" AND j."Partition" = i."Partition" AND j."Revision" = -1))))
        /*park-clearing-stop-journal*/ /*invalid-recovered-journal*/
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
            /*lock-live-job-before-park*/
            IF TG_OP = 'UPDATE' AND /*checkpoint-write-allowed*/
            THEN RETURN NEW; END IF;
            RAISE EXCEPTION 'Qualified actor checkpoint requires a qualified update'
                USING ERRCODE = '23514', CONSTRAINT = 'kernel_require_root_checkpoint_writer';
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
        .Replace("    /*lock-live-job-before-park*/\n", RecordsConsumedRootJob(db) ? $"""
            IF TG_OP = 'UPDATE' AND OLD."Status" = 0 AND NEW."Status" = 2 AND NEW."FoundationParkLiveJob" IS NOT NULL THEN
                PERFORM 1 FROM "Messages" m WHERE m."Partition" = NEW."Partition" AND m."Target" = NEW."Id"
                    AND m."JobGeneration" = NEW."NextJobGeneration" - 1 AND {IsLiveRootJob("m.")}
                ORDER BY m."Seq", m."Id" LIMIT 3 FOR UPDATE OF m;
            END IF;
            """ + "\n" : "", StringComparison.Ordinal)
        .Replace("/*checkpoint-write-allowed*/", RootCheckpointWriteAllowed, StringComparison.Ordinal)
        .Replace("/*journal-write-refused*/", RootJournalWriteRefused, StringComparison.Ordinal)
        .Replace("/*root-park-continues*/", db.UsesRootParkArrivals ? """
            (OLD."Status" = 2 AND NEW."Status" = OLD."Status" AND NOT NEW."Retired" /*parked-clearing-continues*/
                AND OLD."FoundationParkAt" IS NOT NULL AND OLD."FoundationParkRevision" > 0
                AND NEW."FoundationParkAt" = OLD."FoundationParkAt" AND NEW."FoundationParkRevision" = OLD."FoundationParkRevision"
                AND NEW."State" = OLD."State" AND NEW."StateVersion" = OLD."StateVersion" AND NEW."FoundationHeader" = OLD."FoundationHeader"
                AND NEW."Fault" IS NOT DISTINCT FROM OLD."Fault" AND NEW."Suspended" = OLD."Suspended"
                AND NEW."History" IS NOT DISTINCT FROM OLD."History"/*park-arrival-footprint*/)
            """.Replace("/*park-arrival-footprint*/", db.UsesRootParkedOperators &&
                (db.UsesSqliteShippedInstall || db.UsesRootComposedParkRecovery) ? " " + ParkedClearingRow(db) : "", StringComparison.Ordinal)
            : db.UsesRootParkedOperators ? """
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
                        NEW."NextJobGeneration", NEW."CustomHandles" /*new-wire*/ /*new-clearing*/)
                    IS DISTINCT FROM ROW(OLD."Status", OLD."Retired", OLD."Suspended", OLD."State",
                        OLD."StateVersion", OLD."FoundationHeader", OLD."Fault", OLD."History", OLD."Jobs", OLD."Children", OLD."Parent",
                        OLD."FoundationParkAt", OLD."FoundationParkRevision", OLD."FoundationParkGeneration",
                        OLD."FoundationTerminalRevision", OLD."FoundationTerminalAt", OLD."Model", OLD."Definition",
                        OLD."NextJobGeneration", OLD."CustomHandles" /*old-wire*/ /*old-clearing*/) END)
            """.Replace("/*root-outcome-entry*/", RootOutcomeEntry, StringComparison.Ordinal)
            // The state wire column exists only on stable-body profiles.
            .Replace("/*new-wire*/", db.UsesRootStableBody ? """, NEW."FoundationStateWire" """ : "", StringComparison.Ordinal)
            .Replace("/*old-wire*/", db.UsesRootStableBody ? """, OLD."FoundationStateWire" """ : "", StringComparison.Ordinal)
            .Replace(" /*new-clearing*/", db.UsesRootComposedParkRecovery
                ? """, NEW."FoundationParkClearing", NEW."FoundationParkClearingKind", NEW."FoundationParkCleared" """ : "", StringComparison.Ordinal)
            .Replace(" /*old-clearing*/", db.UsesRootComposedParkRecovery
                ? """, OLD."FoundationParkClearing", OLD."FoundationParkClearingKind", OLD."FoundationParkCleared" """ : "", StringComparison.Ordinal)
            : db.UsesRootParkedOperators ? ParkedRefusalRow(db) : "", StringComparison.Ordinal)
        .Replace(" /*pinned-result-write*/", RecordsConsumedRootJob(db) ? $"""
            AND (NEW."FoundationParkJobResult" IS NOT DISTINCT FROM OLD."FoundationParkJobResult"
                OR (OLD."Status" = 0 AND NEW."Status" = 2 AND NOT NEW."Retired" AND OLD."FoundationParkJobResult" IS NULL
                    AND NEW."FoundationParkAt" IS NOT NULL AND NEW."FoundationParkRevision" = NEW."Revision"
                    AND NEW."Jobs" IS NOT NULL AND NEW."Jobs" = OLD."Jobs" AND NEW."NextJobGeneration" = OLD."NextJobGeneration"
                    AND {ConsumedParkJobExists("NEW")})
                OR (OLD."Status" = 2 AND NEW."Status" = 0 AND NEW."FoundationParkJobResult" IS NULL))
            AND (NEW."FoundationParkLiveJob" IS NOT DISTINCT FROM OLD."FoundationParkLiveJob"
                OR (OLD."Status" = 0 AND NEW."Status" = 2 AND NOT NEW."Retired" AND OLD."FoundationParkLiveJob" IS NULL
                    AND NEW."FoundationParkAt" IS NOT NULL AND NEW."FoundationParkRevision" = NEW."Revision"
                    AND NEW."Jobs" IS NOT NULL AND NEW."Jobs" = OLD."Jobs" AND NEW."NextJobGeneration" = OLD."NextJobGeneration"
                    AND NEW."FoundationParkJobResult" IS NULL AND {LiveRootJobFootprint("NEW", journaling: true)})
                OR (OLD."Status" = 2 AND NEW."Status" = 0 AND NEW."FoundationParkLiveJob" IS NULL))
            """ : "", StringComparison.Ordinal)
        .Replace("/*root-park-recover*/", db.UsesRootParkRecovery ? """
            (OLD."Status" = 2 AND NEW."Status" = 0 AND NOT NEW."Retired" AND NOT OLD."Suspended" AND NOT NEW."Suspended"
                AND OLD."FoundationParkAt" IS NOT NULL AND OLD."FoundationParkRevision" > 0 /*park-recover-footprint*/
                /*recovered-jobs*/ AND NEW."NextJobGeneration" IS NOT DISTINCT FROM OLD."NextJobGeneration"
                AND NEW."FoundationParkAt" IS NULL AND NEW."FoundationParkRevision" IS NULL AND NEW."Fault" IS NULL
                AND NEW."FoundationParkGeneration" = OLD."FoundationParkGeneration" + 1
                AND NEW."State" = OLD."State" AND NEW."StateVersion" = OLD."StateVersion" AND NEW."FoundationHeader" = OLD."FoundationHeader"
                AND NEW."History" IS NOT DISTINCT FROM OLD."History"/*sqlite-recover-footprint*/
                AND EXISTS (SELECT 1 FROM "Journal" j WHERE j."Partition" = NEW."Partition" AND j."Instance" = NEW."Id"
                    AND j."Revision" = NEW."Revision" AND /*root-outcome-entry*/ AND j."Entries" LIKE '{{"Outcome":"recovered",%'))
            """.Replace("/*root-outcome-entry*/", RootOutcomeEntry, StringComparison.Ordinal)
            .Replace("/*park-recover-footprint*/", RootRecoverFootprint(db, "OLD", journaling: false), StringComparison.Ordinal)
            .Replace("/*recovered-jobs*/", RecordsConsumedRootJob(db) ? "" : """AND NEW."Jobs" IS NULL""", StringComparison.Ordinal)
            .Replace("/*sqlite-recover-footprint*/", db.UsesSqliteRootRuntime ? """

                AND NEW."FoundationStateWire" IS NOT DISTINCT FROM OLD."FoundationStateWire"
                AND NEW."FoundationTerminalRevision" IS NULL AND NEW."FoundationTerminalAt" IS NULL
                AND NEW."FoundationParkClearing" IS NULL AND NEW."FoundationParkClearingKind" IS NULL AND NEW."FoundationParkCleared" IS NULL
                AND ROW(NEW."Children", NEW."Parent", NEW."Model", NEW."Definition", NEW."CustomHandles")
                    IS NOT DISTINCT FROM ROW(OLD."Children", OLD."Parent", OLD."Model", OLD."Definition", OLD."CustomHandles")
                """ : "", StringComparison.Ordinal)
            : "false", StringComparison.Ordinal)
        // A recovered entry is journaled only on the parked root it resolves.
        .Replace("/*root-park-recovered-journal*/", db.UsesRootParkRecovery ? """
            IF NEW."Entries" LIKE '{{"Outcome":"recovered",%' AND EXISTS (SELECT 1 FROM "Instances" i WHERE i."Id" = NEW."Instance"
                AND i."FoundationReference" IS NOT NULL AND NOT (i."Status" = 2 AND NOT i."Retired" AND NOT i."Suspended"
                    AND i."Partition" = NEW."Partition" AND NEW."Revision" = i."Revision" + 1
                    AND i."FoundationParkAt" IS NOT NULL AND i."FoundationParkRevision" > 0 /*park-recover-footprint*/))
            THEN RAISE EXCEPTION 'A recovered journal entry requires the parked checkpoint it resolves' USING ERRCODE = '23514'; END IF;
            """.Replace("/*park-recover-footprint*/", RootRecoverFootprint(db, "i", journaling: true), StringComparison.Ordinal) : "", StringComparison.Ordinal)
        .Replace(" /*invalid-recovered-journal*/", db.UsesSqliteRootRuntime && db.UsesRootParkRecovery ? """
            OR (NEW."Entries" LIKE '{{"Outcome":"recovered",%' AND EXISTS (SELECT 1 FROM "Instances" i WHERE i."Id" = NEW."Instance"
                AND i."FoundationReference" IS NOT NULL AND NOT ((i."Status" = 2 AND NOT i."Retired" AND NOT i."Suspended"
                    AND i."Partition" = NEW."Partition" AND NEW."Revision" = i."Revision" + 1
                    AND i."FoundationParkAt" IS NOT NULL AND i."FoundationParkRevision" > 0 /*park-recover-footprint*/) IS TRUE)))
            """.Replace("/*park-recover-footprint*/", RootRecoverFootprint(db, "i", journaling: true), StringComparison.Ordinal) : "",
            StringComparison.Ordinal)
        .Replace("/*root-park-recover-journal*/", db.UsesRootParkRecovery
            // The raw command is formatted, so the literal brace is doubled.
            ? """OR NEW."Entries" LIKE '{{"Outcome":"recovered",%' OR NEW."Entries" LIKE '{{"Outcome":"refused",%'""" +
                (db.UsesRootComposedParkRecovery ? " /*root-parked-terminated-journal*/" : "")
            : db.UsesRootParkedOperators ? """OR NEW."Entries" LIKE '{{"Outcome":"refused",%' /*root-parked-terminated-journal*/""" : "", StringComparison.Ordinal)
        // S5b: a Terminate that names the Park ends a parked root in one commit, journalled "terminated" at its revision.
        .Replace("/*root-parked-terminated-journal*/", db.UsesRootParkedOperators ? ParkedTerminatedJournal(db) : "", StringComparison.Ordinal)
        .Replace("/*root-parked-terminate*/", db.UsesRootParkedOperators ? ParkedTerminateCheckpoint(db) : "false", StringComparison.Ordinal)
        .Replace("/*park-clearing-stop-write*/", db.UsesRootParkedOperators && db.UsesRootRetirement ? ParkClearingStopWrite(db) : "", StringComparison.Ordinal)
        .Replace("/*park-clearing-stop-end*/", db.UsesRootParkedOperators && db.UsesRootRetirement ? "END" : "", StringComparison.Ordinal)
        // A stopped outcome requires an already-qualified checkpoint with an open clearing when it is journalled.
        .Replace("/*park-clearing-stop-journal*/", db.UsesRootParkedOperators && db.UsesRootRetirement ? """
            OR (NEW."Entries" LIKE '{{"Outcome":"clearing-stopped",%' AND NOT EXISTS (
                SELECT 1 FROM "Instances" i WHERE i."Id" = NEW."Instance" AND i."Partition" = NEW."Partition"
                    AND i."FoundationReference" IS NOT NULL AND i."Revision" + 1 = NEW."Revision"
                    AND i."FoundationParkClearing" IS NOT NULL))
            """ : "", StringComparison.Ordinal)
        // S5b-3a: a Terminate-then-delete retires a parked root, Cancelled with its terminal mark and its Park cleared.
        // S5b-3b: a Delete retires it still Faulted, under its terminal mark.
        .Replace("/*root-parked-retirement*/", db.UsesRootParkedOperators && db.UsesRootRetirement ? ParkedRetirementCheckpoint(db) : "false",
            StringComparison.Ordinal)
        // S5b-2: a refusal leaves an open clearing as it is, and no keep or other write moves a root whose Park is being cleared.
        .Replace("/*parked-clearing-continues*/", db.UsesRootParkedOperators ? ParkedClearingUnchanged(db) + ParkedClearingKeeps : "", StringComparison.Ordinal)
        .Replace("/*parked-clearing-pinned*/", db.UsesRootParkedOperators ? ParkedClearingUnchanged(db) : "", StringComparison.Ordinal)
        .Replace("/*root-retirement*/", db.UsesRootRetirement ? "true" : "false", StringComparison.Ordinal)
        .Replace("/*root-atomic-retirement*/", db.UsesRootTerminateThenDelete ? "true" : "false", StringComparison.Ordinal)
        .Replace("/*root-retirement-wire*/", db.UsesRootRetirement ? """AND NEW."FoundationStateWire" IS NULL""" : "", StringComparison.Ordinal)
        .Replace("/*root-retirement-footprint*/", db.UsesRootRetirement ? RootRetirementFootprint : "", StringComparison.Ordinal)
        .Replace("/*cancelled*/", FormattableString.Invariant($"{(int)InstanceStatus.Cancelled}"), StringComparison.Ordinal);

    private static string RootRecoverFootprint(KernelDb db, string actor, bool journaling)
    {
        if (!db.UsesRootComposedParkRecovery) return "";
        if (!RecordsConsumedRootJob(db)) return $"""AND {actor}."FoundationParkClearing" IS NULL AND {actor}."Jobs" IS NULL""";

        // The Park binds the retained result by its storage key. Journal before removing it, then write the
        // running checkpoint only after the key and its pin have been cleared in the same transaction.
        var retained = journaling ? ConsumedParkJobExists(actor) : $"""

            NEW."Jobs" IS NULL AND NEW."FoundationParkJobResult" IS NULL AND NEW."FoundationParkLiveJob" IS NULL AND NOT EXISTS (
                SELECT 1 FROM "Messages" m WHERE m."Id" = OLD."FoundationParkJobResult" AND m."InboxScope" = {RootTimedJobScope})
            AND NOT EXISTS (SELECT 1 FROM "Messages" m
                WHERE m."Partition" = OLD."Partition" AND m."Target" = OLD."Id" AND m."JobGeneration" = OLD."NextJobGeneration" - 1
                    AND {IsRecordedJobResult("m.")})
            """;
        var jobless = journaling ? "" : """
            AND NEW."Jobs" IS NULL AND NEW."FoundationParkJobResult" IS NULL AND NEW."FoundationParkLiveJob" IS NULL
            """;
        var preserved = journaling ? "" : """
            AND NEW."Jobs" = OLD."Jobs" AND NEW."FoundationParkJobResult" IS NULL AND NEW."FoundationParkLiveJob" IS NULL
            """;
        return $"""
            AND {actor}."FoundationParkClearing" IS NULL
            AND (({actor}."Jobs" IS NULL AND {actor}."FoundationParkJobResult" IS NULL AND {actor}."FoundationParkLiveJob" IS NULL {jobless})
                OR ({actor}."Jobs" IS NOT NULL AND {actor}."FoundationParkJobResult" IS NOT NULL
                    AND {actor}."FoundationParkLiveJob" IS NULL AND {retained})
                OR ({actor}."Jobs" IS NOT NULL AND {actor}."FoundationParkJobResult" IS NULL
                    AND {actor}."FoundationParkLiveJob" IS NOT NULL {preserved}
                    AND {LiveRootJobFootprint(actor, journaling)}))
            """;
    }

    private static string IsRecordedJobResult(string prefix = "") =>
        $"""{prefix}"InboxScope" = {RootTimedJobScope} AND {prefix}"Kind" = '{Kinds.JobFinished}'""";

    private static readonly string RecordedJobResultIndex = $"""
        ("Partition", "Target", "JobGeneration") WHERE {IsRecordedJobResult()}
        """;

    private static string ConsumedParkJobExists(string actor) => $"""
        EXISTS (SELECT 1 FROM "Messages" m
            WHERE m."Id" = {actor}."FoundationParkJobResult" AND {IsRecordedJobResult("m.")}
                AND m."Target" = {actor}."Id" AND m."Partition" = {actor}."Partition"
                AND m."EventMatch" IS NULL AND m."Done"
                AND m."FoundationKeptBy" = {(short)ParkKeepReason.Consumed} AND m."JobGeneration" = {actor}."NextJobGeneration" - 1
                AND m."Due" = 0 AND m."Start" IS NULL AND m."Payload" IS NOT NULL AND m."Sender" = {actor}."Id"
                AND m."ReplyTo" IS NULL AND m."KeepUntil" IS NULL)
        """;

    // Match the retirement lock's preconditions against the previous checkpoint and its persisted inputs.
    private static readonly string RootRetirementFootprint = """
        /*retire-jobs*/ AND OLD."Jobs" IS NULL
        /*retire-children*/ AND OLD."Children" IS NULL
        /*retire-parent*/ AND OLD."Parent" IS NULL
        /*retire-handles*/ AND OLD."CustomHandles" IS NULL
        /*retire-subscriptions*/ AND NOT EXISTS (SELECT 1 FROM "Subscriptions" s WHERE s."Instance" = NEW."Id")
        /*retire-pending*/ AND NOT EXISTS (SELECT 1 FROM "Messages" m WHERE m."Target" = NEW."Id" AND m."Partition" = NEW."Partition"
            AND /*pending-residue*/)
        """.Replace("/*pending-residue*/", PendingResidue, StringComparison.Ordinal);

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

        string RenderSqlite(string template) => RenderSqliteOutcomePrefixes(
            RenderRootGuards(db, template).Replace("ROW(", "(", StringComparison.Ordinal));
    }

    private static string RenderSqliteOutcomePrefixes(string sql) => PostgresOutcomePrefix.Replace(sql,
        static match => $"{match.Groups["column"].Value}{match.Groups["negation"].Value} GLOB '{{{match.Groups["prefix"].Value}*'");

    // Only literal outcome prefixes appear in the shared predicates. GLOB keeps their PostgreSQL case sensitivity.
    private static readonly Regex PostgresOutcomePrefix = new(
        """(?<column>(?:[A-Za-z_][A-Za-z0-9_]*\.)?"Entries")(?<negation> NOT)? LIKE '\{\{(?<prefix>[^'%]*)%'""", RegexOptions.CultureInvariant);
}
