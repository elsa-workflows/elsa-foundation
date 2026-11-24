using Microsoft.EntityFrameworkCore;

namespace Kernel.EntityFramework;

public sealed partial class EfStore
{
    // S5a (#4229): a composed Park set refuses an operator command on a parked root with a journalled reason. The command
    // settles at the next revision and nothing else of the root, its Park or its inbox changes. S5b replaces the refusal.
    private async Task<PreparedRoot?> PrepareRootParkedRefusalAsync(WorkerRoot root, TurnCommit turn, CancellationToken token)
    {
        var command = turn.FoundationRootOperator!;
        if (turn.Row is not { Status: InstanceStatus.Faulted, Suspended: false, FoundationDispatchHeader: { Terminal: null } header } row ||
            turn.Expected is not { } expected || turn.Done is not [var key] || key != command.InboxKey || !root.Owns(key) ||
            !PostgresRootAdmission.IsOperatorOccurrence(key) || command.Target.Id != root.Actor || command.TargetCreation != 1 ||
            command.ReplyTo is not null || command.Kind is not (Kinds.Suspend or Kinds.Resume or Kinds.Terminate or Kinds.Delete) ||
            turn.Journal != Json.Write(new { Outcome = "refused", Operation = command.Id, Reason = "parked" }) ||
            turn != new TurnCommit(root.Actor, row, expected, turn.Consumed, turn.Done, turn.Sent, turn.Subscribed, turn.Unsubscribed,
                turn.Journal, Held: turn.Held)
                { FoundationRootOperator = command } || turn.Consumed.Count + turn.Sent.Count + turn.Subscribed.Count +
                turn.Unsubscribed.Count != 0 || turn.Held is { Count: > 0 })
            throw RootShape();

        var current = await ReadFirstRootAsync(root.Tenant, root.Reference, token);
        if (current is not { Status: InstanceStatus.Faulted, Suspended: false, TerminalRevision: null } || current.Revision != expected) return null;
        // The owner's row may carry the fault text the Park opened with; the refusal writes neither it nor anything but the revision.
        if (row != WorkerRow(root, current) with { Revision = checked(expected + 1), Fault = row.Fault, FoundationDispatchHeader = header } ||
            header.Tenant != root.Tenant || header.Reference != root.Reference)
            throw RootShape();
        return new(new TurnCommit(root.Actor, null, null, [], [], [], [], [], turn.Journal), new RootParkedRefusal(root, expected, command));
    }

    private sealed record LockedParkedRoot(long At, string Fault, byte[] StateDigest, byte[] HeaderDigest, int StateVersion);

    private sealed record RootParkedRefusal(WorkerRoot Root, long Expected, Message Command) : QualifiedRootCheckpoint(checked(Expected + 1))
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
                WHERE i."Id" = {Root.Actor} AND i."Partition" = {Root.Partition} AND NOT i."Retired" AND NOT i."Suspended"
                    AND i."Status" = {(int)InstanceStatus.Faulted} AND i."Revision" = {Expected} AND i."FoundationParkAt" IS NOT NULL
                    AND i."Fault" IS NOT NULL AND i."FoundationTerminalRevision" IS NULL
                    AND i."FoundationTenant" = {Root.Tenant} AND i."FoundationReference" = {Root.Reference}
                FOR UPDATE OF i
                """).ToListAsync(token);
            if (locked is not [var prior]) return false;
            // Without Park arrivals the parked root withholds its ordinary inputs, so the settled prefix is its operator rows.
            if (!await SettleRootInputsAsync(db, Root.Reference, new(Root.Actor, 1), [Command.InboxKey], [Command], null,
                    suspended: !db.UsesRootParkArrivals, token)) return false;
            if (await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE "Instances" SET "Revision" = {JournalRevision}, "FoundationWriteRevision" = {JournalRevision}
                WHERE "Id" = {Root.Actor} AND "Partition" = {Root.Partition} AND "Revision" = {Expected}
                """, token) != 1) return false;
            return await db.Database.SqlQuery<bool>($"""
                SELECT EXISTS (SELECT 1 FROM "Instances" WHERE "Id" = {Root.Actor} AND "Partition" = {Root.Partition}
                        AND "Revision" = {JournalRevision} AND "FoundationWriteRevision" = {JournalRevision}
                        AND "Status" = {(int)InstanceStatus.Faulted} AND NOT "Suspended" AND "Fault" = {prior.Fault}
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
