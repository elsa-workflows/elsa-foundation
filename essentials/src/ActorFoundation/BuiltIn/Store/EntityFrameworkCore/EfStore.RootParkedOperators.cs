using Microsoft.EntityFrameworkCore;

namespace Kernel.EntityFramework;

public sealed partial class EfStore
{
    // S5a (#4229): a composed Park set refuses an operator command on a parked root with a journalled reason. The command
    // settles at the next revision and nothing else of the root, its Park or its inbox changes. S5b: a Terminate that names
    // the Park is refused only for a stale Park ("selection") or one larger than a page ("page"); otherwise it ends the root.
    private async Task<PreparedRoot?> PrepareRootParkedRefusalAsync(WorkerRoot root, TurnCommit turn, CancellationToken token)
    {
        var command = turn.FoundationRootOperator!;
        string[] reasons = command.Operator?.RootParkMark is null ? ["parked"]
            : command is { Kind: Kinds.Terminate, Operator.RootParkFacts: not null } ? ["selection", "outstanding", "page"] : [];
        // The refusal keeps the Suspended flag: a Park on a suspended root keeps it, as the in-memory reference does.
        if (turn.Row is not { Status: InstanceStatus.Faulted, Retired: false, FoundationDispatchHeader: { Terminal: null } header } row ||
            turn.Expected is not { } expected || turn.Done is not [var key] || key != command.InboxKey || !root.Owns(key) ||
            !PostgresRootAdmission.IsOperatorOccurrence(key) || command.Target.Id != root.Actor || command.TargetCreation != 1 ||
            (command.ReplyTo is not null) != (command.Operator?.RootHeldReply == true) ||
            command.Kind is not (Kinds.Suspend or Kinds.Resume or Kinds.Terminate or Kinds.Delete) ||
            !reasons.Any(reason => turn.Journal == Json.Write(new { Outcome = "refused", Operation = command.Id, Reason = reason })) ||
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
        return new(new TurnCommit(root.Actor, null, null, [], [], [], [], [], turn.Journal),
            new RootParkedRefusal(root, expected, WithoutParkFacts(command)!, row.Suspended));
    }

    // The polled envelope carries the Park facts read with it; the stored row does not.
    private static Message? WithoutParkFacts(Message? message) => message?.Operator is { RootParkFacts: not null } command
        ? message with { Operator = command with { RootParkFacts = null } } : message;

    // S5b: a Terminate that names the parked root's Park, and whose kept rows fit one page, ends the root in one fenced commit.
    private async Task<PreparedRoot?> PrepareRootParkedTerminateAsync(WorkerRoot root, TurnCommit turn, CancellationToken token)
    {
        var command = turn.FoundationRootOperator!;
        if (turn.Row is not { Status: InstanceStatus.Cancelled, Retired: false, Suspended: false, FoundationDispatchHeader: { Terminal: null } header } row ||
            turn.Expected is not { } expected || turn.Done is not [var key] || key != command.InboxKey || !root.Owns(key) ||
            !PostgresRootAdmission.IsOperatorOccurrence(key) || command.Target.Id != root.Actor || command.TargetCreation != 1 ||
            command is not { Kind: Kinds.Terminate, Operator: { RootParkMark: { } mark, RootParkWindow: >= 0, RootParkFacts: { } facts, RootThenDelete: false } } ||
            (command.ReplyTo is not null) != command.Operator.RootHeldReply || facts.Mark != mark || facts.Outstanding || !facts.FitsPage ||
            turn.Journal != Json.Write(new { Outcome = "terminated", Operation = command.Id, Cleared = facts.Arrived + facts.Consumed }) ||
            turn != new TurnCommit(root.Actor, row, expected, turn.Consumed, turn.Done, turn.Sent, turn.Subscribed, turn.Unsubscribed,
                turn.Journal, Unsubscribe: true, Held: turn.Held) { FoundationRootOperator = command } ||
            turn.Consumed.Count + turn.Sent.Count + turn.Subscribed.Count + turn.Unsubscribed.Count != 0 || turn.Held is { Count: > 0 })
            throw RootShape();
        var current = await ReadFirstRootAsync(root.Tenant, root.Reference, token);
        if (current is not { Status: InstanceStatus.Faulted, TerminalRevision: null } || current.Revision != expected) return null;
        if (row != WorkerRow(root, current) with
            {
                Status = InstanceStatus.Cancelled, Suspended = false, Revision = checked(expected + 1), Fault = row.Fault, FoundationDispatchHeader = header
            } || header.Tenant != root.Tenant || header.Reference != root.Reference || header.Header.TypeData != current.TypeData ||
            header.Header.Labels.Count != current.Labels.Count ||
            current.Labels.Any(pair => !header.Header.Labels.TryGetValue(pair.Key, out var value) || pair.Value != value))
            throw RootShape();
        return new(new TurnCommit(root.Actor, null, null, [], [], [], [], [], turn.Journal),
            new RootParkedTerminate(root, expected, WithoutParkFacts(command)!, mark, facts.KeptDigest, current.Suspended));
    }

    // The checkpoint writer's branch for that commit: a parked root becomes Cancelled and unsuspended with its Park mark
    // cleared and its terminal mark written, its fault kept, every kept row already cleared, no jobs, children, custom
    // handles or subscriptions, and the rest of the row unchanged, beside a "terminated" entry at its revision. The raw command is formatted, so the literal brace is doubled.
    private static string ParkedTerminateCheckpoint(KernelDb db)
    {
        const string columns = """
            ROW(_."State", _."StateVersion", _."FoundationHeader", _."Fault", _."History", _."Jobs", _."Children", _."Parent",
                _."Model", _."Definition", _."NextJobGeneration", _."CustomHandles" /*wire*/)
            """;
        string Row(string side) => columns.Replace("_.", side + ".", StringComparison.Ordinal)
            .Replace("/*wire*/", db.UsesRootStableBody ? $""", {side}."FoundationStateWire" """ : "", StringComparison.Ordinal);
        return $$$"""
            (OLD."Status" = 2 AND NEW."Status" = 3 AND NOT NEW."Retired" AND NOT NEW."Suspended"
                AND OLD."FoundationParkAt" IS NOT NULL AND OLD."FoundationParkRevision" > 0 AND OLD."FoundationTerminalRevision" IS NULL
                AND NEW."FoundationParkAt" IS NULL AND NEW."FoundationParkRevision" IS NULL
                AND NEW."FoundationTerminalRevision" = NEW."Revision" AND NEW."FoundationTerminalAt" IS NOT NULL
                AND NEW."Jobs" IS NULL AND NEW."Children" IS NULL AND NEW."CustomHandles" IS NULL AND OLD."Fault" IS NOT NULL
                AND NOT EXISTS (SELECT 1 FROM "Subscriptions" s WHERE s."Instance" = NEW."Id")
                AND {{{Row("NEW")}}} IS NOT DISTINCT FROM {{{Row("OLD")}}}
                AND NOT EXISTS (SELECT 1 FROM "Messages" k WHERE k."Target" = NEW."Id" AND k."Partition" = NEW."Partition"
                    AND k."InboxScope" = -4 AND k."FoundationKeptBy" IS NOT NULL)
                AND EXISTS (SELECT 1 FROM "Journal" j WHERE j."Partition" = NEW."Partition" AND j."Instance" = NEW."Id"
                    AND j."Revision" = NEW."Revision" AND j."Entries" LIKE '{{"Outcome":"terminated",%'))
            """;
    }

    // The journal trigger admits a "terminated" entry on the parked root at its next revision.
    private const string ParkedTerminatedJournal = """OR NEW."Entries" LIKE '{{"Outcome":"terminated",%'""";

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

    // A refused or terminated entry commits only with the checkpoint write at its revision, as format 22's outcome constraint
    // requires; a lone entry would wedge the root. A terminated entry also needs the ended root with its Park mark cleared. Format 22 installs its own constraint, which also covers recovered entries.
    internal static Task InstallRootParkedOutcomeAsync(KernelDb db, CancellationToken token) =>
        db.Database.ExecuteSqlRawAsync("""
            CREATE INDEX IF NOT EXISTS "IX_FoundationRootJournal_Outcome" ON "Journal" ("Partition", "Instance", "Revision")
                WHERE {RootOutcomeEntry};
            CREATE OR REPLACE FUNCTION kernel_require_root_outcome_checkpoint() RETURNS trigger LANGUAGE plpgsql AS $fn$
            BEGIN
                IF EXISTS (SELECT 1 FROM "Instances" i WHERE i."Id" = NEW."Instance" AND i."FoundationReference" IS NOT NULL
                    AND NOT (i."Partition" = NEW."Partition" AND i."Revision" = NEW."Revision"
                        AND (NEW."Entries" NOT LIKE '{{"Outcome":"terminated",%' OR (i."Status" = 3 AND NOT i."Retired"
                            AND i."FoundationParkAt" IS NULL AND i."FoundationTerminalRevision" = NEW."Revision"))))
                THEN RAISE EXCEPTION 'An operator outcome entry requires the checkpoint write at its revision' USING ERRCODE = '23514'; END IF;
                RETURN NULL;
            END
            $fn$;
            DROP TRIGGER IF EXISTS kernel_require_root_outcome_checkpoint ON "Journal";
            CREATE CONSTRAINT TRIGGER kernel_require_root_outcome_checkpoint AFTER INSERT ON "Journal"
                DEFERRABLE INITIALLY DEFERRED FOR EACH ROW WHEN (NEW.{RootOutcomeEntry})
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
    // One fenced commit: the kept rows are cleared as format 22 clears its fault input (settled, no kept reason, no content,
    // the ordinary window from now), then the root ends Cancelled and unsuspended with its terminal mark and without its
    // Park mark. The Park and the kept set must be the ones the Terminate was decided on.
    private sealed record RootParkedTerminate(WorkerRoot Root, long Expected, Message Command, FoundationParkMark Mark, string KeptDigest,
        bool Suspended) : QualifiedRootCheckpoint(checked(Expected + 1))
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
                    AND i."Status" = {(int)InstanceStatus.Faulted} AND i."Revision" = {Expected} AND i."FoundationParkAt" = {Mark.At}
                    AND i."FoundationParkRevision" = {Mark.Revision} AND i."Fault" IS NOT NULL AND i."FoundationTerminalRevision" IS NULL
                    AND i."Jobs" IS NULL AND i."Children" IS NULL AND i."CustomHandles" IS NULL
                    AND i."FoundationTenant" = {Root.Tenant} AND i."FoundationReference" = {Root.Reference}
                FOR UPDATE OF i
                """).ToListAsync(token);
            if (locked is not [var prior]) return false;
            var kept = await ReadKeptAsync(db, Root.Actor, Root.Partition, true, token);
            if (KeptSetDigest(kept) != KeptDigest ||
                !await SettleRootInputsAsync(db, Root.Reference, new(Root.Actor, 1), [Command.InboxKey], [Command], null,
                    suspended: !db.UsesRootParkArrivals, token)) return false;
            var window = Command.Operator!.RootParkWindow!.Value;
            var cleared = kept.Select(row => row.Id).ToArray();
            if (await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE "Messages" SET "FoundationKeptBy" = NULL, "Payload" = NULL, "Start" = NULL, "ReplyTo" = NULL,
                    "KeepUntil" = floor(extract(epoch from transaction_timestamp()) * 1000)::bigint + {window}
                WHERE "Id" = ANY({cleared}) AND "Target" = {Root.Actor} AND "Partition" = {Root.Partition}
                    AND "InboxScope" = -4 AND "EventMatch" IS NULL AND "Done" AND "FoundationKeptBy" IS NOT NULL
                """, token) != cleared.Length) return false;
            if (await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE "Instances" SET "Revision" = {JournalRevision}, "FoundationWriteRevision" = {JournalRevision},
                    "Status" = {(int)InstanceStatus.Cancelled}, "Suspended" = false, "FoundationParkAt" = NULL, "FoundationParkRevision" = NULL,
                    "FoundationTerminalRevision" = {JournalRevision},
                    "FoundationTerminalAt" = floor(extract(epoch from clock_timestamp()) * 1000)::bigint
                WHERE "Id" = {Root.Actor} AND "Partition" = {Root.Partition} AND "Revision" = {Expected}
                    AND NOT EXISTS (SELECT 1 FROM "Subscriptions" WHERE "Instance" = {Root.Actor})
                """, token) != 1) return false;
            return await db.Database.SqlQuery<bool>($"""
                SELECT EXISTS (SELECT 1 FROM "Instances" WHERE "Id" = {Root.Actor} AND "Partition" = {Root.Partition}
                        AND "Revision" = {JournalRevision} AND "FoundationWriteRevision" = {JournalRevision}
                        AND "Status" = {(int)InstanceStatus.Cancelled} AND NOT "Suspended" AND "Fault" = {prior.Fault}
                        AND "FoundationParkAt" IS NULL AND "FoundationParkRevision" IS NULL
                        AND "FoundationTerminalRevision" = {JournalRevision} AND "StateVersion" = {prior.StateVersion}
                        AND sha256(convert_to("State", 'UTF8')) = {prior.StateDigest}
                        AND sha256(convert_to("FoundationHeader", 'UTF8')) = {prior.HeaderDigest})
                    AND (SELECT count(*) FROM "Messages" WHERE "Id" = ANY({cleared}) AND "Target" = {Root.Actor}
                        AND "Partition" = {Root.Partition} AND "InboxScope" = -4 AND "Done" AND "FoundationKeptBy" IS NULL
                        AND "Payload" IS NULL AND "Start" IS NULL AND "ReplyTo" IS NULL
                        AND "KeepUntil" = floor(extract(epoch from transaction_timestamp()) * 1000)::bigint + {window}) = {cleared.Length}
                    AND (NOT {db.UsesRootParkInspection} OR EXISTS (SELECT 1 FROM "FoundationActorList"
                        WHERE "Tenant" = {Root.Tenant} AND "Reference" = {Root.Reference} AND "Status" = {(int)InstanceStatus.Cancelled}
                            AND "ParkAt" IS NULL AND "ParkFault" IS NULL AND "EndedAt" IS NOT NULL))
                AS "Value"
                """).SingleAsync(token);
        }
    }
}
