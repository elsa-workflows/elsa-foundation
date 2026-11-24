using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Kernel.EntityFramework;

public sealed partial class EfStore
{
    // Keep the marker lock and the pinned PostgreSQL namespace for the whole operation.
    private static async ValueTask<IDbContextTransaction?> BeginRootRetirementOperationAsync(KernelDb db, CancellationToken token)
    {
        if (!db.UsesRootRetirement) return null;
        var transaction = await db.Database.BeginTransactionAsync(token);
        try
        {
            await PostgresRootAdmission.PinSelectedOperatorSchemaAsync(db, token);
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
            if (!db.UsesRootRetirement || Position <= 0 || Command.Operator?.QualifiedTerminal is not { } mark)
                return false;

            var occurrence = InboxKey.FoundationOccurrenceScope;
            var rows = await db.Database.SqlQuery<long>($"""
                SELECT i."Revision" AS "Value" FROM "Instances" i
                JOIN "FoundationRootBirths" b ON b."Actor" = i."Id" AND b."Partition" = i."Partition"
                    AND b."Tenant" = i."FoundationTenant" AND b."Reference" = i."FoundationReference"
                WHERE b."Tenant" = {Root.Tenant} AND b."Reference" = {Root.Reference} AND b."Creation" = {Current.Actor.Creation}
                    AND i."Id" = {Root.Actor} AND i."Partition" = {Root.Partition} AND i."Revision" = {Current.Revision}
                    AND i."Status" = {(int)Current.Status} AND NOT i."Retired"
                    AND i."FoundationTerminalRevision" = {mark.Revision} AND i."FoundationTerminalAt" = {mark.At}
                    AND i."Jobs" IS NULL AND i."Children" IS NULL AND i."Parent" IS NULL AND i."CustomHandles" IS NULL
                    AND NOT EXISTS (SELECT 1 FROM "Subscriptions" s WHERE s."Instance" = i."Id")
                    AND NOT EXISTS (SELECT 1 FROM "Messages" m WHERE m."Target" = i."Id" AND m."Partition" = i."Partition"
                        AND NOT m."Done" AND (m."InboxScope" <> {occurrence} OR m."KeepUntil" IS NULL OR m."EventMatch" IS NOT NULL))
                FOR UPDATE OF i
                """).ToListAsync(token);
            if (rows.Count != 1 ||
                !await SettleRootInputsAsync(db, Root.Reference, Current.Actor, [Command.InboxKey], [Command], null, false, token))
                return false;

            var revision = checked(Current.Revision + 1);
            if (await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE "Instances" SET "State" = 'null', "FoundationHeader" = NULL, "FoundationStateWire" = NULL,
                    "Model" = '', "Definition" = '', "Fault" = NULL, "StateVersion" = 1, "History" = {(int)History.Minimal},
                    "NextJobGeneration" = NULL, "Suspended" = false, "Retired" = true,
                    "Revision" = {revision}, "FoundationWriteRevision" = {revision}
                WHERE "Id" = {Root.Actor} AND "Partition" = {Root.Partition} AND "Revision" = {Current.Revision}
                """, token) != 1)
                return false;

            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE "Messages" SET "Done" = true, "Payload" = NULL, "Start" = NULL, "ReplyTo" = NULL
                WHERE "InboxScope" = {occurrence} AND "Target" = {Root.Actor} AND "Partition" = {Root.Partition} AND NOT "Done";
                DELETE FROM "Journal" WHERE "Instance" = {Root.Actor} AND "Partition" = {Root.Partition} AND "Revision" > 0;
                """, token);
            return await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "Journal" ("Instance", "Partition", "Position", "Revision", "Entries")
                SELECT {Root.Actor}, {Root.Partition}, {Position}, -1, '["delete"]'
                WHERE NOT EXISTS (SELECT 1 FROM "FoundationActorList" WHERE "Tenant" = {Root.Tenant} AND "Reference" = {Root.Reference})
                    AND NOT EXISTS (SELECT 1 FROM "Journal" WHERE "Instance" = {Root.Actor} AND "Partition" = {Root.Partition})
                    AND NOT EXISTS (SELECT 1 FROM "Messages" WHERE "Target" = {Root.Actor} AND "Partition" = {Root.Partition}
                        AND (NOT "Done" OR "Payload" IS NOT NULL OR "Start" IS NOT NULL OR "ReplyTo" IS NOT NULL))
                """, token) == 1;
        }
    }

    private async Task<PreparedRoot?> PrepareRootRetirementAsync(WorkerRoot root, TurnCommit turn, CancellationToken token)
    {
        var command = turn.FoundationRootOperator;
        if (turn.Row is not { Retired: true } marker || command is not { Kind: Kinds.Delete, Operator.QualifiedTerminal: { } stamp } ||
            command.Target.Id != root.Actor || command.TargetCreation != 1 || !root.Owns(command.InboxKey) ||
            !turn.Unsubscribe || turn.Journal != """["delete"]""" || turn.Done is not [var key] || key != command.InboxKey ||
            turn.Consumed.Count != 0 || turn.Sent.Count != 0 || turn.Subscribed.Count != 0 || turn.Unsubscribed.Count != 0 ||
            turn.Held is { Count: > 0 } || turn != new TurnCommit(turn.Instance, marker, turn.Expected, turn.Consumed, turn.Done,
                turn.Sent, turn.Subscribed, turn.Unsubscribed, turn.Journal, true, turn.Held, true) { FoundationRootOperator = command })
            throw RootShape();

        await using var db = create();
        if (!db.UsesRootRetirement) throw RootShape();
        var current = await ReadFirstRootAsync(root.Tenant, root.Reference, token);
        if (current is not { Status: not InstanceStatus.Running } || current.Revision != turn.Expected ||
            current.TerminalRevision != stamp.Revision || current.TerminalAt != stamp.At)
            return null;
        var previous = WorkerRow(root, current);
        if (marker != CreationProtocol.Retire(previous))
            throw RootShape();

        return new(new TurnCommit(root.Actor, null, null, [], [], [], [], [], turn.Journal), new RootRetirement(root, current, command));
    }
}
