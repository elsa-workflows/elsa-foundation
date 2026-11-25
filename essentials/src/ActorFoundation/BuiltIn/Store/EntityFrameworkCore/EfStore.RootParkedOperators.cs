using Microsoft.EntityFrameworkCore;

namespace Kernel.EntityFramework;

public sealed partial class EfStore
{
    // S5a (#4229): a composed Park set refuses an operator command on a parked root with a journalled reason. The command
    // settles at the next revision and nothing else of the root, its Park or its inbox changes. S5b replaces the refusal.
    private async Task<PreparedRoot?> PrepareRootParkedRefusalAsync(WorkerRoot root, TurnCommit turn, CancellationToken token)
    {
        var command = turn.FoundationRootOperator!;
        // The refusal keeps the Suspended flag: a Park on a suspended root keeps it, as the in-memory reference does.
        if (turn.Row is not { Status: InstanceStatus.Faulted, Retired: false, FoundationDispatchHeader: { Terminal: null } header } row ||
            turn.Expected is not { } expected || turn.Done is not [var key] || key != command.InboxKey || !root.Owns(key) ||
            !PostgresRootAdmission.IsOperatorOccurrence(key) || command.Target.Id != root.Actor || command.TargetCreation != 1 ||
            (command.ReplyTo is not null) != (command.Operator?.RootHeldReply == true) ||
            command.Kind is not (Kinds.Suspend or Kinds.Resume or Kinds.Terminate or Kinds.Delete) ||
            turn.Journal != Json.Write(new { Outcome = "refused", Operation = command.Id, Reason = "parked" }) ||
            turn != new TurnCommit(root.Actor, row, expected, turn.Consumed, turn.Done, turn.Sent, turn.Subscribed, turn.Unsubscribed,
                turn.Journal, Held: turn.Held)
                { FoundationRootOperator = command } || turn.Consumed.Count + turn.Sent.Count + turn.Subscribed.Count +
                turn.Unsubscribed.Count != 0 || turn.Held is { Count: > 0 })
            throw RootShape();

        var current = await ReadFirstRootAsync(root.Tenant, root.Reference, token);
        if (current is not { Status: InstanceStatus.Faulted, TerminalRevision: null } || current.Revision != expected) return null;
        // The owner's row may carry the fault text the Park opened with; the refusal writes neither it nor anything but the revision.
        if (row != WorkerRow(root, current) with { Revision = checked(expected + 1), Fault = row.Fault, FoundationDispatchHeader = header } ||
            header.Tenant != root.Tenant || header.Reference != root.Reference)
            throw RootShape();
        return new(new TurnCommit(root.Actor, null, null, [], [], [], [], [], turn.Journal), new RootParkedRefusal(root, expected, command, row.Suspended));
    }

    // A composed set's refused entry pins the whole row but its revision, as format 22's refused-row rule does, at any status.
    // Each column of the set's Instances table is compared; the Park revision and the state wire exist only with their capability.
    private static string ParkedRefusalRow(KernelDb db)
    {
        const string columns = """
            ROW(_."Status", _."Retired", _."Suspended", _."State", _."StateVersion", _."FoundationHeader", _."Fault", _."History",
                _."Jobs", _."Children", _."Parent", _."FoundationParkAt" /*park-revision*/, _."FoundationTerminalRevision",
                _."FoundationTerminalAt", _."Model", _."Definition", _."NextJobGeneration", _."CustomHandles" /*wire*/)
            """;
        string Row(string side) => columns.Replace("_.", side + ".", StringComparison.Ordinal)
            .Replace("/*park-revision*/", db.RecordsRootParkRevision ? $""", {side}."FoundationParkRevision" """ : "", StringComparison.Ordinal)
            .Replace("/*wire*/", db.UsesRootStableBody ? $""", {side}."FoundationStateWire" """ : "", StringComparison.Ordinal);
        // The raw command is formatted, so the literal brace is doubled.
        return $$$"""
            AND NOT EXISTS (SELECT 1 FROM "Journal" j WHERE j."Partition" = NEW."Partition" AND j."Instance" = NEW."Id"
                AND j."Revision" = NEW."Revision" AND {{{RootOutcomeEntry}}} AND j."Entries" LIKE '{{"Outcome":"refused",%'
                AND {{{Row("NEW")}}} IS DISTINCT FROM {{{Row("OLD")}}})
            """;
    }

    // A refused entry commits only with the checkpoint write at its revision, as format 22's outcome constraint requires;
    // a lone entry would wedge the root. Format 22 installs its own constraint, which also covers recovered entries.
    internal static Task InstallRootParkedOutcomeAsync(KernelDb db, CancellationToken token) =>
        db.Database.ExecuteSqlRawAsync("""
            CREATE INDEX IF NOT EXISTS "IX_FoundationRootJournal_Outcome" ON "Journal" ("Partition", "Instance", "Revision")
                WHERE {RootOutcomeEntry};
            CREATE OR REPLACE FUNCTION kernel_require_root_outcome_checkpoint() RETURNS trigger LANGUAGE plpgsql AS $fn$
            BEGIN
                IF EXISTS (SELECT 1 FROM "Instances" i WHERE i."Id" = NEW."Instance" AND i."FoundationReference" IS NOT NULL
                    AND NOT (i."Partition" = NEW."Partition" AND i."Revision" = NEW."Revision"))
                THEN RAISE EXCEPTION 'An operator refusal entry requires the checkpoint write at its revision' USING ERRCODE = '23514'; END IF;
                RETURN NULL;
            END
            $fn$;
            DROP TRIGGER IF EXISTS kernel_require_root_outcome_checkpoint ON "Journal";
            CREATE CONSTRAINT TRIGGER kernel_require_root_outcome_checkpoint AFTER INSERT ON "Journal"
                DEFERRABLE INITIALLY DEFERRED FOR EACH ROW WHEN (NEW."Entries" LIKE '{{"Outcome":"refused",%')
                EXECUTE FUNCTION kernel_require_root_outcome_checkpoint();
            """.Replace("{RootOutcomeEntry}", RootOutcomeEntry, StringComparison.Ordinal), token);

    private sealed record LockedParkedRoot(long At, string Fault, byte[] StateDigest, byte[] HeaderDigest, int StateVersion);

    private sealed record RootParkedRefusal(WorkerRoot Root, long Expected, Message Command, bool Suspended)
        : QualifiedRootCheckpoint(checked(Expected + 1))
    {
        internal override async Task<bool> BindCheckpointAsync(KernelDb db, CancellationToken token)
        {
            if (!db.UsesRootParkedOperators) return false;
            var locked = await db.Database.SqlQuery<LockedParkedRoot>($"""
                SELECT i."FoundationParkAt" AS "At", i."Fault", sha256(convert_to(i."State", 'UTF8')) AS "StateDigest",
                    sha256(convert_to(i."FoundationHeader", 'UTF8')) AS "HeaderDigest", i."StateVersion"
                FROM "Instances" i JOIN "FoundationRootBirths" b
                    ON b."Actor" = i."Id" AND b."Partition" = i."Partition" AND b."Creation" = 1
                    AND b."Tenant" = i."FoundationTenant" AND b."Reference" = i."FoundationReference"
                WHERE i."Id" = {Root.Actor} AND i."Partition" = {Root.Partition} AND NOT i."Retired" AND i."Suspended" = {Suspended}
                    AND i."Status" = {(int)InstanceStatus.Faulted} AND i."Revision" = {Expected} AND i."FoundationParkAt" IS NOT NULL
                    AND i."Fault" IS NOT NULL AND i."FoundationTerminalRevision" IS NULL
                    AND i."FoundationTenant" = {Root.Tenant} AND i."FoundationReference" = {Root.Reference}
                FOR UPDATE OF i
                """).ToListAsync(token);
            if (locked is not [var prior]) return false;
            // Without Park arrivals the parked root withholds its ordinary inputs, so the settled prefix is its operator rows.
            // The envelope comparison binds a held command's exact reply address.
            if (!await SettleRootInputsAsync(db, Root.Reference, new(Root.Actor, 1), [Command.InboxKey], [Command], null,
                    suspended: !db.UsesRootParkArrivals, token)) return false;
            if (await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE "Instances" SET "Revision" = {JournalRevision}, "FoundationWriteRevision" = {JournalRevision}
                WHERE "Id" = {Root.Actor} AND "Partition" = {Root.Partition} AND "Revision" = {Expected}
                """, token) != 1) return false;
            return await db.Database.SqlQuery<bool>($"""
                SELECT EXISTS (SELECT 1 FROM "Instances" WHERE "Id" = {Root.Actor} AND "Partition" = {Root.Partition}
                        AND "Revision" = {JournalRevision} AND "FoundationWriteRevision" = {JournalRevision}
                        AND "Status" = {(int)InstanceStatus.Faulted} AND "Suspended" = {Suspended} AND "Fault" = {prior.Fault}
                        AND "FoundationParkAt" = {prior.At} AND "FoundationTerminalRevision" IS NULL AND "StateVersion" = {prior.StateVersion}
                        AND sha256(convert_to("State", 'UTF8')) = {prior.StateDigest}
                        AND sha256(convert_to("FoundationHeader", 'UTF8')) = {prior.HeaderDigest})
                    AND (NOT {db.UsesRootParkInspection} OR EXISTS (SELECT 1 FROM "FoundationActorList"
                        WHERE "Tenant" = {Root.Tenant} AND "Reference" = {Root.Reference} AND "Status" = {(int)InstanceStatus.Faulted}
                            AND "ParkAt" = {prior.At} AND "ParkFault" = {prior.Fault}))
                AS "Value"
                """).SingleAsync(token);
        }
    }
}
