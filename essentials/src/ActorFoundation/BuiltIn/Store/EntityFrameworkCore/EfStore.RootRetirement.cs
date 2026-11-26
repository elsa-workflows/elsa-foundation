using System.Runtime.CompilerServices;
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

    // S5b-3a: a Terminate-then-delete of a parked root clears the Park's last kept rows, ends it Cancelled and retires it in
    // one commit: the Park mark, the open clearing (if any) and the kept set must be the ones the command was decided on.
    // S5b-3b: a Delete that names the Park retires it the same way, still Faulted; both write the terminal mark the
    // retirement marker's residue check reads.
    private sealed record ParkedRetirement(FoundationParkMark Mark, string KeptDigest, string? Clearing, long Cleared, int Count);

    private sealed record RootRetirement(WorkerRoot Root, RootImage Current, Message Command, long Position = 0, ParkedRetirement? Park = null)
        : QualifiedRootCheckpoint(-1)
    {
        internal override async Task<bool> BindCheckpointAsync(KernelDb db, CancellationToken token)
        {
            var running = Current.Status == InstanceStatus.Running;
            var ending = running || Park is not null;
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
            var thenDelete = Command.Operator?.RootThenDelete == true;
            if (rows.Count != 1 || Park is { } parked && (!db.UsesRootParkedOperators || thenDelete && !db.UsesRootTerminateThenDelete ||
                    !await ClearParkedAsync(parked))) return false;
            // A parked root without Park arrivals, or one being cleared, receives only its operator rows.
            if (!await SettleRootInputsAsync(db, Root.Reference, Current.Actor, [Command.InboxKey], [Command], null,
                    Park is null ? running && Current.Suspended : !db.UsesRootParkArrivals || Park.Clearing is not null, token))
                return false;

            var revision = checked(Current.Revision + 1);
            var status = running || Park is not null && thenDelete ? InstanceStatus.Cancelled : Current.Status;
            var terminalRevision = ending ? revision : Current.TerminalRevision;
            FormattableString retire = $"""
                WITH terminal AS MATERIALIZED (
                    SELECT CASE WHEN {ending} THEN floor(extract(epoch from clock_timestamp()) * 1000)::bigint
                        ELSE {Current.TerminalAt} END AS "At")
                UPDATE "Instances" i SET "State" = 'null', "FoundationHeader" = NULL, "FoundationStateWire" = NULL,
                    "Model" = '', "Definition" = '', "Fault" = NULL, "StateVersion" = 1, "History" = {(int)History.Minimal},
                    "NextJobGeneration" = NULL, "Suspended" = false, "Retired" = true,
                    "Revision" = {revision}, "FoundationWriteRevision" = {revision},
                    "Status" = {(int)status}, /*park*/
                    "FoundationTerminalRevision" = CASE WHEN {ending} THEN {revision} ELSE "FoundationTerminalRevision" END,
                    "FoundationTerminalAt" = terminal."At"
                FROM terminal
                WHERE i."Id" = {Root.Actor} AND i."Partition" = {Root.Partition} AND i."Revision" = {Current.Revision}
                RETURNING terminal."At" AS "Value"
                """;
            var updated = await db.Database.SqlQuery<long>(FormattableStringFactory.Create(retire.Format.Replace("/*park*/", Park is null ? "" : """
                "FoundationParkAt" = NULL, "FoundationParkRevision" = NULL, "FoundationParkClearing" = NULL, "FoundationParkCleared" = NULL,
                """, StringComparison.Ordinal), retire.GetArguments())).ToListAsync(token);
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
                        (db.RetiredReplies ??= []).Add(new(row.Id, new(route, Root.Tenant)));
                }
            }
            if (db.UsesRootReplyProtocol)
            {
                // #5756: a held direct input still pending here is answered once, as an operator command is: the in-memory
                // reference refuses an input that reaches a retired row (RetiredCreation), which its caller reads as "missing".
                // Direct admission also share-locks the root, so this lock fixes these callers too.
                var held = await db.Messages.FromSqlInterpolated($"""
                    SELECT * FROM "Messages" WHERE "InboxScope" = {occurrence} AND "Target" = {Root.Actor}
                        AND "Partition" = {Root.Partition} AND NOT "Done" AND "ReplyTo" IS NOT NULL
                        AND "Id" NOT LIKE 'operator/%'
                    ORDER BY "Seq" FOR UPDATE
                    """).AsNoTracking().ToListAsync(token);
                foreach (var row in held)
                {
                    var message = PostgresRootAdmission.DecodeDirect(row, Root.Reference, replies: true);
                    if (message.ReplyTo is { } route)
                        (db.RetiredReplies ??= []).Add(new(row.Id, new(route, Root.Tenant)));
                }
            }

            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE "Messages" SET "Done" = true, "Payload" = NULL, "Start" = NULL, "ReplyTo" = NULL
                WHERE "InboxScope" = {occurrence} AND "Target" = {Root.Actor} AND "Partition" = {Root.Partition} AND NOT "Done";
                DELETE FROM "Journal" WHERE "Instance" = {Root.Actor} AND "Partition" = {Root.Partition} AND "Revision" > 0;
                """, token);
            // A composed set may carry workflow history. The checkpoint update above holds the lock that a history install
            // needs, so the table either exists now or appears only after this commit, when no history row can name the root.
            var history = db.SupportsRootWorkflowHistory && await db.Database.SqlQueryRaw<bool>("""
                SELECT to_regclass('"FoundationWorkflowHistory"') IS NOT NULL AS "Value"
                """).SingleAsync(token);
            if (history)
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    DELETE FROM "FoundationWorkflowHistory" WHERE "Tenant" = {Root.Tenant} AND "Reference" = {Root.Reference}
                    """, token);
            FormattableString marker = $"""
                INSERT INTO "Journal" ("Instance", "Partition", "Position", "Revision", "Entries")
                SELECT {Root.Actor}, {Root.Partition}, {Position}, -1, '["delete"]'
                WHERE EXISTS (SELECT 1 FROM "Instances" WHERE "Id" = {Root.Actor} AND "Partition" = {Root.Partition}
                    AND "FoundationTenant" = {Root.Tenant} AND "FoundationReference" = {Root.Reference}
                    AND "Revision" = {revision} AND "FoundationWriteRevision" = {revision}
                    AND "Retired" AND "Status" = {(int)status} AND "State" = 'null'
                    AND "FoundationHeader" IS NULL AND "FoundationStateWire" IS NULL
                    AND "FoundationTerminalRevision" = {terminalRevision} AND "FoundationTerminalAt" = {terminalAt})
                    AND NOT EXISTS (SELECT 1 FROM "FoundationActorList" WHERE "Tenant" = {Root.Tenant} AND "Reference" = {Root.Reference})/*history-residue*/
                    AND NOT EXISTS (SELECT 1 FROM "Journal" WHERE "Instance" = {Root.Actor} AND "Partition" = {Root.Partition})
                    AND NOT EXISTS (SELECT 1 FROM "Messages" WHERE "Target" = {Root.Actor} AND "Partition" = {Root.Partition}
                        AND (NOT "Done" OR "Payload" IS NOT NULL OR "Start" IS NOT NULL OR "ReplyTo" IS NOT NULL))
                """;
            // The residue check that guards the deletion marker covers the history row too.
            if (history)
                marker = FormattableStringFactory.Create(marker.Format.Replace("/*history-residue*/", $$"""
                    AND NOT EXISTS (SELECT 1 FROM "FoundationWorkflowHistory" WHERE "Tenant" = {{{marker.ArgumentCount}}} AND "Reference" = {{{marker.ArgumentCount + 1}}})
                    """, StringComparison.Ordinal), [.. marker.GetArguments(), Root.Tenant, Root.Reference]);
            return await db.Database.ExecuteSqlInterpolatedAsync(marker, token) == 1;

            // The Park, its clearing and its kept set as decided; the last kept rows are cleared as format 22 clears them.
            async Task<bool> ClearParkedAsync(ParkedRetirement parked)
            {
                if (!await db.Database.SqlQuery<bool>($"""
                    SELECT EXISTS (SELECT 1 FROM "Instances" WHERE "Id" = {Root.Actor} AND "Partition" = {Root.Partition}
                        AND "Revision" = {Current.Revision} AND "Status" = {(int)InstanceStatus.Faulted} AND "Fault" IS NOT NULL
                        AND "FoundationParkAt" = {parked.Mark.At} AND "FoundationParkRevision" = {parked.Mark.Revision}
                        AND "FoundationParkClearing" IS NOT DISTINCT FROM {parked.Clearing}::text
                        AND coalesce("FoundationParkCleared", 0) = {parked.Cleared}) AS "Value"
                    """).SingleAsync(token)) return false;
                var kept = await ReadKeptAsync(db, Root.Actor, Root.Partition, true, token,
                    parked.Clearing is null ? int.MaxValue : InMemoryStore.ParkPageLimit + 1);
                if (KeptSetDigest(kept) != parked.KeptDigest || kept.Count != parked.Count || PageLength(kept) != kept.Count) return false;
                var ids = kept.Select(row => row.Id).ToArray();
                return await db.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE "Messages" SET "FoundationKeptBy" = NULL, "Payload" = NULL, "Start" = NULL, "ReplyTo" = NULL,
                        "KeepUntil" = floor(extract(epoch from transaction_timestamp()) * 1000)::bigint + {Command.Operator!.RootParkWindow!.Value}
                    WHERE "Id" = ANY({ids}) AND "Target" = {Root.Actor} AND "Partition" = {Root.Partition}
                        AND "InboxScope" = -4 AND "EventMatch" IS NULL AND "Done" AND "FoundationKeptBy" IS NOT NULL
                    """, token) == ids.Length;
            }
        }
    }

    private async Task<PreparedRoot?> PrepareRootRetirementAsync(WorkerRoot root, TurnCommit turn, CancellationToken token)
    {
        var command = turn.FoundationRootOperator;
        var termination = command is { Kind: Kinds.Terminate, Operator.RootThenDelete: true };
        var stamp = command?.Operator?.QualifiedTerminal;
        // S5b-3b: a Delete names the root's terminal mark, or the Park of a parked root.
        if (turn.Row is not { Retired: true } marker || command is null ||
            (termination ? command.Operator!.DeleteTarget != root.Reference || stamp is not null
                : command.Kind != Kinds.Delete || (stamp is null) == (command.Operator?.RootParkMark is null)) ||
            command.Target.Id != root.Actor || command.TargetCreation != 1 || !root.Owns(command.InboxKey) ||
            !turn.Unsubscribe || turn.Journal != """["delete"]""" || turn.Done is not [var key] || key != command.InboxKey ||
            turn.Consumed.Count != 0 || turn.Sent.Count != 0 || turn.Subscribed.Count != 0 || turn.Unsubscribed.Count != 0 ||
            turn.Held is { Count: > 0 } || turn != new TurnCommit(turn.Instance, marker, turn.Expected, turn.Consumed, turn.Done,
                turn.Sent, turn.Subscribed, turn.Unsubscribed, turn.Journal, true, turn.Held, true) { FoundationRootOperator = command })
            throw RootShape();

        await using var db = create();
        if (!db.UsesRootRetirement || termination && !db.UsesRootTerminateThenDelete) throw RootShape();
        var current = await ReadFirstRootAsync(root.Tenant, root.Reference, token);
        if (command.Operator!.RootParkFacts is { } facts)
        {
            // A Terminate-then-delete, or a Delete, that names the parked root's Park, whose kept rows fit this commit.
            if (stamp is not null || !db.UsesRootParkedOperators || command.Operator is not { RootParkMark: { } mark, RootParkWindow: >= 0 } ||
                facts.Mark != mark || facts.Outstanding || facts.Residue && !command.Operator.RootParkClear || !facts.FitsPage || facts.Clearing != (command.Operator.RootParkClear ? command.Id : null) ||
                command.Operator.RootParkClear && command.ReplyTo is not null)
                throw RootShape();
            if (current is not { Status: InstanceStatus.Faulted, TerminalRevision: null, TerminalAt: null } || current.Revision != turn.Expected)
                return null;
            if (marker != CreationProtocol.Retire(WorkerRow(root, current) with { Status = termination ? InstanceStatus.Cancelled : current.Status }))
                throw RootShape();
            return new(new TurnCommit(root.Actor, null, null, [], [], [], [], [], turn.Journal), new RootRetirement(root, current,
                WithoutParkFacts(command)!, Park: new(mark, facts.KeptDigest, facts.Clearing, facts.Cleared, facts.Arrived + facts.Consumed)));
        }
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
