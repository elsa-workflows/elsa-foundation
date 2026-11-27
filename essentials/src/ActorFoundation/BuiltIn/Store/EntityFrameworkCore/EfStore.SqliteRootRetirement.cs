using Microsoft.EntityFrameworkCore;

namespace Kernel.EntityFramework;

public sealed partial class EfStore
{
    private static async Task<bool> BindSqliteRootRetirementAsync(KernelDb db, RootRetirement retire, CancellationToken token)
    {
        var root = retire.Root;
        var current = retire.Current;
        var running = current.Status == InstanceStatus.Running;
        var park = retire.Park;
        var ending = running || park is not null;
        var scope = InboxKey.FoundationOccurrenceScope;
        var clearsPrivate = park is null && db.UsesRootPrivateParkInputs;
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
                    AND i."FoundationParkAt" IS NOT DISTINCT FROM {park?.Mark.At}
                    AND i."Jobs" IS NULL AND i."Children" IS NULL AND i."CustomHandles" IS NULL
                    AND (i."Parent" IS NULL OR {db.UsesRootChildren && !ending} AND i."FoundationTerminalRevision" = i."Revision")
                    AND NOT EXISTS (SELECT 1 FROM "Subscriptions" s WHERE s."Instance" = i."Id")
                    AND ({clearsPrivate} OR NOT EXISTS (SELECT 1 FROM "Messages" m WHERE m."Target" = i."Id" AND m."Partition" = i."Partition"
                        AND NOT m."Done" AND (m."InboxScope" <> {scope} OR m."KeepUntil" IS NULL OR m."EventMatch" IS NOT NULL))))
            AS "Value"
            """).SingleAsync(token)) return false;

        if (park is null)
        {
            if (!await SettleSqliteRootInputsAsync(db, root.Reference, current.Actor, [retire.Command.InboxKey],
                    [retire.Command], running && (current.Suspended || retire.WithheldInputs), token)) return false;
        }
        else
        {
            var page = await BindSqliteParkPageAsync(db, root, current.Revision, current.Suspended, park.Mark, park.Clearing,
                park.Cleared, park.KeptDigest, park.Count, ends: true, retire.Command, token);
            if (page is null || !await ClearSqliteParkPageAsync(db, root, page, retire.Command.Operator!.RootParkWindow!.Value, token)) return false;
        }

        if (retire.RetentionEligible == false) return true;

        if (clearsPrivate && (!await ClearTerminalRootSelfAsync(db, current.Actor, root.Partition, current.Revision, retire.SelfIssuer, includeImmediate: true, token) ||
            await HasRootRetirementInboxResidueAsync(db, root, token)))
            return false;

        var revision = checked(current.Revision + 1);
        var status = running || park is not null && retire.Command.Operator?.RootThenDelete == true ? InstanceStatus.Cancelled : current.Status;
        var terminalRevision = ending ? revision : current.TerminalRevision;
        var terminalAt = ending ? await ReadSqliteParkClockAsync(db, token) : current.TerminalAt;
        if (await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE "Instances" SET "State" = 'null', "FoundationHeader" = NULL, "FoundationStateWire" = NULL,
                "Model" = '', "Definition" = '', "Fault" = NULL, "StateVersion" = 1, "History" = {(int)History.Minimal},
                "NextJobGeneration" = NULL, "Parent" = NULL, "Suspended" = false, "Retired" = true, "Revision" = {revision},
                "FoundationWriteRevision" = {revision}, "Status" = {(int)status},
                "FoundationParkAt" = NULL, "FoundationParkRevision" = NULL, "FoundationParkClearing" = NULL, "FoundationParkClearingKind" = NULL, "FoundationParkCleared" = NULL,
                "FoundationTerminalRevision" = {terminalRevision}, "FoundationTerminalAt" = {terminalAt}
            WHERE "Id" = {root.Actor} AND "Partition" = {root.Partition} AND "Revision" = {current.Revision}
            """, token) != 1) return false;

        var waiting = await db.Messages.AsNoTracking().Where(message => message.InboxScope == scope && message.Target == root.Actor &&
            message.Partition == root.Partition && !message.Done && message.ReplyTo != null).OrderBy(message => message.Seq).ToListAsync(token);
        foreach (var row in waiting)
        {
            // Retirement ends every pending route, including a Park-selected command admitted before an earlier command settled.
            var message = PostgresRootAdmission.IsOperatorOccurrence(new(row.Id, row.InboxScope))
                ? PostgresRootAdmission.DecodeOperator(row, root.Reference, terminations: true, retirements: true, terminateThenDelete: true,
                    retentionDeletes: true, operatorReplies: true, parkedTerminate: true)
                : PostgresRootAdmission.DecodeDirect(row, root.Reference, replies: true, liveEventWaits: db.UsesRootLiveEventWaits, scheduleControls: db.UsesRootScheduleControls,
                    authoredEvents: db.UsesRootAuthoredEvents, keyedStarts: db.UsesRootKeyedStarts);
            if (message.ReplyTo is { } route)
                (db.RetiredReplies ??= []).Add(new(row.Id, new(route, root.Tenant)));
        }

        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE "Messages" SET "Done" = 1, "Payload" = NULL, "Start" = NULL, "ReplyTo" = NULL
            WHERE "InboxScope" = {scope} AND "Target" = {root.Actor} AND "Partition" = {root.Partition} AND NOT "Done";
            DELETE FROM "Journal" WHERE "Instance" = {root.Actor} AND "Partition" = {root.Partition} AND "Revision" > 0;
            """, token);

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
                AND NOT EXISTS (SELECT 1 FROM "FoundationWorkflowHistory" WHERE "Tenant" = {root.Tenant} AND "Reference" = {root.Reference})
                AND NOT EXISTS (SELECT 1 FROM "Journal" WHERE "Instance" = {root.Actor} AND "Partition" = {root.Partition})
                AND NOT EXISTS (SELECT 1 FROM "Messages" WHERE "Target" = {root.Actor} AND "Partition" = {root.Partition}
                    AND (NOT "Done" OR "Payload" IS NOT NULL OR "Start" IS NOT NULL OR "ReplyTo" IS NOT NULL))
            """, token) == 1 &&
            (!db.UsesRootScheduleLineage || await RootScheduleLineage.MarkRetiredAsync(db, root.Tenant, root.Reference,
                root.Actor, root.Partition, retire.Position, token));
    }
}
