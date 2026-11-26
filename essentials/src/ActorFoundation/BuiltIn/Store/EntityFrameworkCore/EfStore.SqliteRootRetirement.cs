using Microsoft.EntityFrameworkCore;

namespace Kernel.EntityFramework;

public sealed partial class EfStore
{
    // An earlier turn can register a job after compound admission. The actor's refusal consumes only the command;
    // the exact running checkpoint and its ticket/deadline remain under their original revision.
    private sealed record SqliteLiveCompoundRefusal(WorkerRoot Root, RootImage Current, Message Command, bool Withheld)
        : QualifiedRootCheckpoint(0)
    {
        internal override async Task<bool> BindCheckpointAsync(KernelDb db, CancellationToken token)
        {
            if (!db.UsesSqliteRootRuntime || !db.UsesRootTerminateThenDelete ||
                !await db.Database.SqlQuery<bool>($"""
                    SELECT EXISTS (SELECT 1 FROM "Instances" WHERE "Id" = {Root.Actor} AND "Partition" = {Root.Partition}
                        AND "FoundationTenant" = {Root.Tenant} AND "FoundationReference" = {Root.Reference}
                        AND "Status" = {(int)InstanceStatus.Running} AND NOT "Retired" AND "Revision" = {Current.Revision}
                        AND "FoundationParkAt" IS NULL
                        AND "Jobs" = {Current.Jobs} AND "NextJobGeneration" IS NOT DISTINCT FROM {Current.NextJobGeneration}
                        AND "Suspended" = {Current.Suspended}) AS "Value"
                    """).SingleAsync(token)) return false;
            return await SettleSqliteRootInputsAsync(db, Root.Reference, Current.Actor, [Command.InboxKey], [Command],
                Current.Suspended || Withheld, token);
        }
    }

    private async Task<SqliteLiveCompoundRefusal?> PrepareSqliteLiveCompoundRefusalAsync(WorkerRoot root, InboxKey key, CancellationToken token)
    {
        var current = await ReadFirstRootAsync(root.Tenant, root.Reference, token);
        if (current is not { Status: InstanceStatus.Running, Jobs: not null }) return null;
        await using var db = create();
        await RequireSqliteOperationalFormatAsync(db, token);
        var row = await db.Messages.AsNoTracking().SingleOrDefaultAsync(message => message.Partition == root.Partition &&
            message.Target == root.Actor && message.InboxScope == InboxKey.FoundationOccurrenceScope && message.Id == key.Id && !message.Done,
            token);
        if (row is null) return null;
        var command = DecodeRootInput(db, row, root.Reference, issue: null);
        return command is { Kind: Kinds.Terminate, Operator.RootThenDelete: true }
            ? new(root, current, command, AuthoredSqliteWithholding(root, current)) : null;
    }

    private static async Task<bool> BindSqliteRootRetirementAsync(KernelDb db, RootRetirement retire, CancellationToken token)
    {
        var root = retire.Root;
        var current = retire.Current;
        var running = current.Status == InstanceStatus.Running;
        var scope = InboxKey.FoundationOccurrenceScope;
        // The owned immediate transaction holds the position and prevents a concurrent admission during retirement.
        if (!await db.Database.SqlQuery<bool>($"""
            SELECT EXISTS (SELECT 1 FROM "Instances" i
                JOIN "FoundationRootBirths" b ON b."Actor" = i."Id" AND b."Partition" = i."Partition"
                    AND b."Tenant" = i."FoundationTenant" AND b."Reference" = i."FoundationReference"
                WHERE b."Tenant" = {root.Tenant} AND b."Reference" = {root.Reference} AND b."Creation" = {current.Actor.Creation}
                    AND i."Id" = {root.Actor} AND i."Partition" = {root.Partition} AND i."Revision" = {current.Revision}
                    AND i."Status" = {(int)current.Status} AND NOT i."Retired" AND i."Suspended" = {current.Suspended}
                    AND i."FoundationTerminalRevision" IS NOT DISTINCT FROM {current.TerminalRevision}
                    AND i."FoundationTerminalAt" IS NOT DISTINCT FROM {current.TerminalAt}
                    AND i."FoundationParkAt" IS NULL
                    AND i."Jobs" IS NULL AND i."Children" IS NULL AND i."Parent" IS NULL AND i."CustomHandles" IS NULL
                    AND NOT EXISTS (SELECT 1 FROM "Subscriptions" s WHERE s."Instance" = i."Id")
                    AND NOT EXISTS (SELECT 1 FROM "Messages" m WHERE m."Target" = i."Id" AND m."Partition" = i."Partition"
                        AND NOT m."Done" AND (m."InboxScope" <> {scope} OR m."KeepUntil" IS NULL OR m."EventMatch" IS NOT NULL)))
            AS "Value"
            """).SingleAsync(token)) return false;

        if (!await SettleSqliteRootInputsAsync(db, root.Reference, current.Actor, [retire.Command.InboxKey],
            [retire.Command], running && (current.Suspended || retire.WithheldInputs), token)) return false;

        var revision = checked(current.Revision + 1);
        var status = running ? InstanceStatus.Cancelled : current.Status;
        var terminalRevision = running ? revision : current.TerminalRevision;
        var terminalAt = running ? await db.Database.SqlQueryRaw<long>(
            "SELECT " + SqliteCommitDialect.MillisecondClock + """ AS "Value" """).SingleAsync(token) : current.TerminalAt;
        if (await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE "Instances" SET "State" = 'null', "FoundationHeader" = NULL, "FoundationStateWire" = NULL,
                "Model" = '', "Definition" = '', "Fault" = NULL, "StateVersion" = 1, "History" = {(int)History.Minimal},
                "NextJobGeneration" = NULL, "Suspended" = false, "Retired" = true, "Revision" = {revision},
                "FoundationWriteRevision" = {revision}, "Status" = {(int)status},
                "FoundationTerminalRevision" = {terminalRevision}, "FoundationTerminalAt" = {terminalAt}
            WHERE "Id" = {root.Actor} AND "Partition" = {root.Partition} AND "Revision" = {current.Revision}
            """, token) != 1) return false;

        var waiting = await db.Messages.AsNoTracking().Where(message => message.InboxScope == scope && message.Target == root.Actor &&
            message.Partition == root.Partition && !message.Done && message.ReplyTo != null).OrderBy(message => message.Seq).ToListAsync(token);
        foreach (var row in waiting)
        {
            var message = PostgresRootAdmission.IsOperatorOccurrence(new(row.Id, row.InboxScope))
                ? PostgresRootAdmission.DecodeOperator(db, row, root.Reference)
                : PostgresRootAdmission.DecodeDirect(row, root.Reference, replies: true);
            if (message.ReplyTo is { } route)
                (db.RetiredReplies ??= []).Add(new(row.Id, new(route, root.Tenant)));
        }

        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE "Messages" SET "Done" = 1, "Payload" = NULL, "Start" = NULL, "ReplyTo" = NULL
            WHERE "InboxScope" = {scope} AND "Target" = {root.Actor} AND "Partition" = {root.Partition} AND NOT "Done";
            DELETE FROM "Journal" WHERE "Instance" = {root.Actor} AND "Partition" = {root.Partition} AND "Revision" > 0;
            """, token);

        // The current stage may precede installation of the workflow-history read model.
        if (await db.Database.SqlQueryRaw<bool>("""
            SELECT EXISTS (SELECT 1 FROM main.sqlite_schema WHERE type = 'table' AND name = 'FoundationWorkflowHistory') AS "Value"
            """).SingleAsync(token))
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                DELETE FROM "FoundationWorkflowHistory" WHERE "Tenant" = {root.Tenant} AND "Reference" = {root.Reference}
                """, token);
            if (await db.Database.SqlQuery<bool>($"""
                SELECT EXISTS (SELECT 1 FROM "FoundationWorkflowHistory"
                    WHERE "Tenant" = {root.Tenant} AND "Reference" = {root.Reference}) AS "Value"
                """).SingleAsync(token)) return false;
        }

        return await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "Journal" ("Instance", "Partition", "Position", "Revision", "Entries")
            SELECT {root.Actor}, {root.Partition}, {retire.Position}, -1, '["delete"]'
            WHERE EXISTS (SELECT 1 FROM "Instances" WHERE "Id" = {root.Actor} AND "Partition" = {root.Partition}
                AND "FoundationTenant" = {root.Tenant} AND "FoundationReference" = {root.Reference}
                AND "Revision" = {revision} AND "FoundationWriteRevision" = {revision}
                AND "Retired" AND "Status" = {(int)status} AND "State" = 'null'
                AND "FoundationHeader" IS NULL AND "FoundationStateWire" IS NULL
                AND "FoundationTerminalRevision" = {terminalRevision} AND "FoundationTerminalAt" = {terminalAt})
                AND NOT EXISTS (SELECT 1 FROM "FoundationActorList" WHERE "Tenant" = {root.Tenant} AND "Reference" = {root.Reference})
                AND NOT EXISTS (SELECT 1 FROM "Journal" WHERE "Instance" = {root.Actor} AND "Partition" = {root.Partition})
                AND NOT EXISTS (SELECT 1 FROM "Messages" WHERE "Target" = {root.Actor} AND "Partition" = {root.Partition}
                    AND (NOT "Done" OR "Payload" IS NOT NULL OR "Start" IS NOT NULL OR "ReplyTo" IS NOT NULL))
            """, token) == 1;
    }
}
