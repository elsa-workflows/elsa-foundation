using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Kernel.EntityFramework;

public sealed partial class EfStore
{
    // Keep the marker lock and the pinned PostgreSQL namespace for the whole operation.
    internal static async ValueTask<IDbContextTransaction?> BeginPinnedRootOperationAsync(KernelDb db, CancellationToken token)
    {
        if (!db.RequiresPinnedRootNamespace) return null;
        var transaction = await db.Database.BeginTransactionAsync(token);
        try
        {
            await PostgresRootAdmission.PinRootNamespaceAsync(db, token);
            return transaction;
        }
        catch
        {
            await transaction.DisposeAsync();
            throw;
        }
    }

    private sealed record RootRetirement(WorkerRoot Root, RootImage Current, Message Command, long Position = 0)
        : QualifiedRootCheckpoint(-1)
    {
        internal override async Task<bool> BindCheckpointAsync(KernelDb db, CancellationToken token)
        {
            var running = Current.Status == InstanceStatus.Running;
            if (!db.UsesRootRetirement || Position <= 0 || running && !db.UsesRootTerminateThenDelete ||
                Command.Operator?.RetentionDelete == true && !db.UsesRootRetention)
                return false;

            var occurrence = InboxKey.FoundationOccurrenceScope;
            var rows = await db.Database.SqlQuery<long>($"""
                SELECT i."Revision" AS "Value" FROM "Instances" i
                JOIN "FoundationRootBirths" b ON b."Actor" = i."Id" AND b."Partition" = i."Partition"
                    AND b."Tenant" = i."FoundationTenant" AND b."Reference" = i."FoundationReference"
                WHERE b."Tenant" = {Root.Tenant} AND b."Reference" = {Root.Reference} AND b."Creation" = {Current.Actor.Creation}
                    AND i."Id" = {Root.Actor} AND i."Partition" = {Root.Partition} AND i."Revision" = {Current.Revision}
                    AND i."Status" = {(int)Current.Status} AND NOT i."Retired"
                    AND i."Suspended" = {Current.Suspended}
                    AND i."FoundationTerminalRevision" IS NOT DISTINCT FROM {Current.TerminalRevision}
                    AND i."FoundationTerminalAt" IS NOT DISTINCT FROM {Current.TerminalAt}
                    AND i."Jobs" IS NULL AND i."Children" IS NULL AND i."Parent" IS NULL AND i."CustomHandles" IS NULL
                    AND NOT EXISTS (SELECT 1 FROM "Subscriptions" s WHERE s."Instance" = i."Id")
                    AND NOT EXISTS (SELECT 1 FROM "Messages" m WHERE m."Target" = i."Id" AND m."Partition" = i."Partition"
                        AND NOT m."Done" AND (m."InboxScope" <> {occurrence} OR m."KeepUntil" IS NULL OR m."EventMatch" IS NOT NULL))
                FOR UPDATE OF i
                """).ToListAsync(token);
            if (rows.Count != 1 ||
                !await SettleRootInputsAsync(db, Root.Reference, Current.Actor, [Command.InboxKey], [Command], null, running && Current.Suspended, token))
                return false;

            var revision = checked(Current.Revision + 1);
            var status = running ? InstanceStatus.Cancelled : Current.Status;
            var terminalRevision = running ? revision : Current.TerminalRevision;
            var updated = await db.Database.SqlQuery<long>($"""
                WITH terminal AS MATERIALIZED (
                    SELECT CASE WHEN {running} THEN floor(extract(epoch from clock_timestamp()) * 1000)::bigint
                        ELSE {Current.TerminalAt} END AS "At")
                UPDATE "Instances" i SET "State" = 'null', "FoundationHeader" = NULL, "FoundationStateWire" = NULL,
                    "Model" = '', "Definition" = '', "Fault" = NULL, "StateVersion" = 1, "History" = {(int)History.Minimal},
                    "NextJobGeneration" = NULL, "Suspended" = false, "Retired" = true,
                    "Revision" = {revision}, "FoundationWriteRevision" = {revision},
                    "Status" = {(int)status},
                    "FoundationTerminalRevision" = CASE WHEN {running} THEN {revision} ELSE "FoundationTerminalRevision" END,
                    "FoundationTerminalAt" = terminal."At"
                FROM terminal
                WHERE i."Id" = {Root.Actor} AND i."Partition" = {Root.Partition} AND i."Revision" = {Current.Revision}
                RETURNING terminal."At" AS "Value"
                """).ToListAsync(token);
            if (updated is not [var terminalAt]) return false;

            if (db.UsesRootOperatorReplies)
            {
                // Admission holds a shared lock on this root. The retirement lock fixes the set of remaining callers.
                var waiting = await db.Messages.FromSqlInterpolated($"""
                    SELECT * FROM "Messages" WHERE "InboxScope" = {occurrence} AND "Target" = {Root.Actor}
                        AND "Partition" = {Root.Partition} AND NOT "Done" AND "ReplyTo" IS NOT NULL
                        AND "Id" LIKE 'operator/%' AND "Payload" IS NULL
                    ORDER BY "Seq" FOR UPDATE
                    """).AsNoTracking().ToListAsync(token);
                foreach (var row in waiting)
                {
                    var message = PostgresRootAdmission.DecodeOperator(db, row, Root.Reference);
                    if (message.ReplyTo is { } route)
                        (db.RetiredOperatorReplies ??= []).Add(new(row.Id, new(route, Root.Tenant)));
                }
            }

            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE "Messages" SET "Done" = true, "Payload" = NULL, "Start" = NULL, "ReplyTo" = NULL
                WHERE "InboxScope" = {occurrence} AND "Target" = {Root.Actor} AND "Partition" = {Root.Partition} AND NOT "Done";
                DELETE FROM "Journal" WHERE "Instance" = {Root.Actor} AND "Partition" = {Root.Partition} AND "Revision" > 0;
                """, token);
            return await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "Journal" ("Instance", "Partition", "Position", "Revision", "Entries")
                SELECT {Root.Actor}, {Root.Partition}, {Position}, -1, '["delete"]'
                WHERE EXISTS (SELECT 1 FROM "Instances" WHERE "Id" = {Root.Actor} AND "Partition" = {Root.Partition}
                    AND "FoundationTenant" = {Root.Tenant} AND "FoundationReference" = {Root.Reference}
                    AND "Revision" = {revision} AND "FoundationWriteRevision" = {revision}
                    AND "Retired" AND "Status" = {(int)status} AND "State" = 'null'
                    AND "FoundationHeader" IS NULL AND "FoundationStateWire" IS NULL
                    AND "FoundationTerminalRevision" = {terminalRevision} AND "FoundationTerminalAt" = {terminalAt})
                    AND NOT EXISTS (SELECT 1 FROM "FoundationActorList" WHERE "Tenant" = {Root.Tenant} AND "Reference" = {Root.Reference})
                    AND NOT EXISTS (SELECT 1 FROM "Journal" WHERE "Instance" = {Root.Actor} AND "Partition" = {Root.Partition})
                    AND NOT EXISTS (SELECT 1 FROM "Messages" WHERE "Target" = {Root.Actor} AND "Partition" = {Root.Partition}
                        AND (NOT "Done" OR "Payload" IS NOT NULL OR "Start" IS NOT NULL OR "ReplyTo" IS NOT NULL))
                """, token) == 1;
        }
    }

    private async Task<PreparedRoot?> PrepareRootRetirementAsync(WorkerRoot root, TurnCommit turn, CancellationToken token)
    {
        var command = turn.FoundationRootOperator;
        var termination = command is { Kind: Kinds.Terminate, Operator.RootThenDelete: true };
        var stamp = command?.Operator?.QualifiedTerminal;
        if (turn.Row is not { Retired: true } marker || command is null ||
            (termination ? command.Operator!.DeleteTarget != root.Reference || stamp is not null : command.Kind != Kinds.Delete || stamp is null) ||
            command.Target.Id != root.Actor || command.TargetCreation != 1 || !root.Owns(command.InboxKey) ||
            !turn.Unsubscribe || turn.Journal != """["delete"]""" || turn.Done is not [var key] || key != command.InboxKey ||
            turn.Consumed.Count != 0 || turn.Sent.Count != 0 || turn.Subscribed.Count != 0 || turn.Unsubscribed.Count != 0 ||
            turn.Held is { Count: > 0 } || turn != new TurnCommit(turn.Instance, marker, turn.Expected, turn.Consumed, turn.Done,
                turn.Sent, turn.Subscribed, turn.Unsubscribed, turn.Journal, true, turn.Held, true) { FoundationRootOperator = command })
            throw RootShape();

        await using var db = create();
        if (!db.UsesRootRetirement || termination && !db.UsesRootTerminateThenDelete) throw RootShape();
        var current = await ReadFirstRootAsync(root.Tenant, root.Reference, token);
        if (current is null || current.Revision != turn.Expected ||
            (current.Status == InstanceStatus.Running ? !termination || current.TerminalRevision is not null || current.TerminalAt is not null :
                current.TerminalRevision is null || current.TerminalAt is null ||
                stamp is not null && (current.TerminalRevision != stamp.Revision || current.TerminalAt != stamp.At)))
            return null;
        var previous = WorkerRow(root, current);
        var ended = previous.Status == InstanceStatus.Running ? previous with { Status = InstanceStatus.Cancelled } : previous;
        if (marker != CreationProtocol.Retire(ended))
            throw RootShape();

        return new(new TurnCommit(root.Actor, null, null, [], [], [], [], [], turn.Journal), new RootRetirement(root, current, command));
    }
}
