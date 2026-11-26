using Microsoft.EntityFrameworkCore;

namespace Kernel.EntityFramework;

public sealed partial class EfStore
{
    private async Task<PreparedRoot?> PrepareSqliteCreatorParkAsync(WorkerRoot root, TurnCommit turn, PartitionFence fence,
        CancellationToken token)
    {
        var key = new InboxKey("root:" + root.Actor);
        if (turn.Row is not { Fault: { } fault } row || turn.FoundationHeader is not { } header || turn.Journal is null ||
            turn.FoundationKept is not { Count: 1 } kept || !kept.TryGetValue(key, out var reason) || reason != ParkKeepReason.Consumed ||
            turn.Done is not [var consumed] || consumed != key || turn.Consumed.Count != 0 || turn.Sent.Count != 0 ||
            turn.Subscribed.Count != 0 || turn.Unsubscribed.Count != 0 || turn.Held is { Count: > 0 } ||
            turn != new TurnCommit(root.Actor, row, null, turn.Consumed, turn.Done, turn.Sent, turn.Subscribed, turn.Unsubscribed,
                turn.Journal, Unsubscribe: true, Held: turn.Held) { OpensPark = true, FoundationHeader = header, FoundationKept = kept })
            throw RootShape();

        var creator = await ReadSqlitePendingCreatorAsync(root.Tenant, root.Reference, token);
        if (creator is null) return null;
        var start = creator.Start!;
        var initial = new InstanceRow(root.Actor, start.Model, start.Definition, InstanceStatus.Faulted, "null", 1, null,
            Fault: fault, History: start.History, Creation: 1, StartIntent: root.Fingerprint) { FoundationNullState = true };
        if (row != initial || header.TypeData != start.Definition || header.Labels.Count != 0) throw RootShape();
        return PrepareFirstRoot(root.Tenant, root.Reference, creator, fence, null, row.StateVersion, header.TypeData,
            header.Labels, turn.Journal, status: row.Status, creatorFault: fault);
    }

    private static async Task<bool> BindSqliteParkOpeningAsync(KernelDb db, RootParkOpening park, CancellationToken token)
    {
        if (!park.Opening || park.Reason != ParkKeepReason.Consumed) return false;
        var root = park.Root;
        var recorded = park.Inputs is [{ FoundationJobResult: true }];
        var scope = recorded ? RootTimedJobScope : InboxKey.FoundationOccurrenceScope;
        var ids = park.Inputs.Select(input => recorded ? StoredJobId(input.InboxKey) : input.InboxKey.Id).ToArray();
        var at = await db.Database.SqlQueryRaw<long>($"SELECT {SqliteCommitDialect.MillisecondClock} AS \"Value\"").SingleAsync(token);
        var pending = await db.Messages.AsNoTracking().Where(row => row.Target == root.Actor && row.Partition == root.Partition &&
            row.InboxScope == scope && row.EventMatch == null && !row.Done && row.Due <= at && ids.Contains(row.Id))
            .OrderBy(row => row.Seq).ToArrayAsync(token);
        var inputs = recorded ? pending.Select(DecodeRootTimedRow) : pending.Select(row => DecodeRootInput(db, row, root.Reference, issue: null));
        if (!inputs.SequenceEqual(park.Inputs)) return false;

        var revision = park.JournalRevision;
        if (await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE "Instances" SET "Status" = {(int)InstanceStatus.Faulted}, "Fault" = {park.EncodedFault},
                "Revision" = {revision}, "FoundationWriteRevision" = {revision}, "FoundationParkAt" = {at}, "FoundationParkRevision" = {revision}
            WHERE "Id" = {root.Actor} AND "Partition" = {root.Partition} AND "Revision" = {park.Expected}
                AND "Status" = {(int)InstanceStatus.Running} AND NOT "Retired" AND "Suspended" = {park.Suspended}
                AND "FoundationParkAt" IS NULL AND "FoundationParkRevision" IS NULL AND "FoundationParkClearing" IS NULL
                AND "FoundationTerminalAt" IS NULL AND "FoundationTerminalRevision" IS NULL
                AND "Jobs" IS NOT DISTINCT FROM {park.Jobs} AND "NextJobGeneration" IS NOT DISTINCT FROM {park.NextJobGeneration}
                AND "FoundationTenant" = {root.Tenant} AND "FoundationReference" = {root.Reference}
                AND EXISTS (SELECT 1 FROM "FoundationRootBirths" WHERE "Actor" = {root.Actor} AND "Partition" = {root.Partition}
                    AND "Tenant" = {root.Tenant} AND "Reference" = {root.Reference} AND "Creation" = 1 AND "Fingerprint" = {root.Fingerprint})
            """, token) != 1 || !await KeepSqliteParkInputsAsync(db, root.Actor, root.Partition, scope, ids, token))
            return false;

        if (park.Returned.Length != 0 && await db.Database.ExecuteSqlInterpolatedAsync(WithSqliteOperatorPredicate($"""
            UPDATE "Messages" SET "Seen" = 0
            WHERE "Target" = {root.Actor} AND "Partition" = {root.Partition} AND "InboxScope" = {InboxKey.FoundationOccurrenceScope}
                AND "EventMatch" IS NULL AND NOT "Done" AND (/*supported-operator*/)
                AND "Id" IN (SELECT value FROM json_each({Json.Write(park.Returned)}))
            """), token) != park.Returned.Length) return false;

        await db.Database.ExecuteSqlInterpolatedAsync($"""
            DELETE FROM "Subscriptions" WHERE "Instance" = {root.Actor} AND "Partition" = {root.Partition}
            """, token);
        return await VerifySqliteParkProjectionAsync(db, root.Tenant, root.Reference, revision, park.EncodedFault, token);
    }

    // Ordinary commands admitted before the Park are still delivered. Settle their refusal with
    // the exact command and a new checkpoint revision, leaving the Park and its retained inputs intact.
    private static async Task<bool> BindSqliteParkedRefusalAsync(KernelDb db, RootParkedRefusal refusal, CancellationToken token)
    {
        if (refusal.Retries || refusal.Command.Operator?.RootParkMark is not null) return false;
        var root = refusal.Root;
        if (!await SettleSqliteRootInputsAsync(db, root.Reference, new(root.Actor, 1), [refusal.Command.InboxKey],
                [refusal.Command], suspended: true, token)) return false;
        return await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE "Instances" SET "Revision" = {refusal.JournalRevision}, "FoundationWriteRevision" = {refusal.JournalRevision}
            WHERE "Id" = {root.Actor} AND "Partition" = {root.Partition} AND "Revision" = {refusal.Expected}
                AND "Status" = {(int)InstanceStatus.Faulted} AND NOT "Retired" AND "Suspended" = {refusal.Suspended}
                AND "FoundationParkAt" > 0 AND "FoundationParkRevision" > 0 AND "Fault" IS NOT NULL
                AND "FoundationParkClearing" IS NULL AND "FoundationTerminalAt" IS NULL AND "FoundationTerminalRevision" IS NULL
                AND "FoundationTenant" = {root.Tenant} AND "FoundationReference" = {root.Reference}
            """, token) == 1;
    }

    private static async Task<bool> KeepSqliteParkInputsAsync(KernelDb db, string actor, int partition, long scope,
        string[] ids, CancellationToken token) => await db.Database.ExecuteSqlInterpolatedAsync($"""
        UPDATE "Messages" SET "Done" = 1, "Trial" = 0, "FoundationKeptBy" = {(short)ParkKeepReason.Consumed}
        WHERE "Target" = {actor} AND "Partition" = {partition} AND "InboxScope" = {scope} AND "EventMatch" IS NULL
            AND "Id" IN (SELECT value FROM json_each({Json.Write(ids)})) AND NOT "Done" AND "FoundationKeptBy" IS NULL
        """, token) == ids.Length;

    private static Task<bool> VerifySqliteParkProjectionAsync(KernelDb db, string tenant, string reference, long revision,
        string fault, CancellationToken token) => db.Database.SqlQuery<bool>($"""
        SELECT EXISTS (SELECT 1 FROM "FoundationActorList" WHERE "Tenant" = {tenant} AND "Reference" = {reference}
            AND "Status" = {(int)InstanceStatus.Faulted} AND "ParkRevision" = {revision} AND "ParkFault" = {fault}
            AND "ParkAt" > 0 AND "EndedAt" IS NULL AND "TerminalRevision" IS NULL) AS "Value"
        """).SingleAsync(token);
}
