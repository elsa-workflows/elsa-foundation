using Microsoft.EntityFrameworkCore;

namespace Kernel.EntityFramework;

public sealed partial class EfStore
{
    private static async Task<bool> BindSqliteParkOpeningAsync(KernelDb db, RootParkOpening park, CancellationToken token)
    {
        if (park.Opening ? park.Reason != ParkKeepReason.Consumed && !park.RetriesInitialTurn : !db.UsesRootParkArrivals) return false;
        var root = park.Root;
        var recorded = park.Inputs is [{ FoundationJobResult: true }];
        var at = await db.Database.SqlQueryRaw<long>($"SELECT {SqliteCommitDialect.MillisecondClock} AS \"Value\"").SingleAsync(token);

        var revision = park.JournalRevision;
        var prior = park.Opening ? null : await ReadSqliteParkMarkAsync(db, root.Actor, root.Partition, token);
        if (!park.Opening && prior is not { At: > 0, Revision: > 0, Clearing: null }) return false;
        var parkAt = prior?.At ?? at;
        var openRevision = prior?.Revision ?? revision;
        var priorStatus = park.Opening ? InstanceStatus.Running : InstanceStatus.Faulted;
        var pinsResult = recorded && RecordsConsumedRootJob(db);
        var live = park.Opening && !recorded && park.Jobs is not null
            ? await ReadLiveRootJobAsync(db, root.Actor, root.Partition, park.Model, park.Jobs, token) : null;
        if (live is not null) await ProtectLiveJobAsync(db, root.Actor, root.Partition, live, token);
        var write = WithParkJobResult(db, $"""
            UPDATE "Instances" SET "Status" = {(int)InstanceStatus.Faulted}, "Fault" = {park.EncodedFault},
                "Revision" = {revision}, "FoundationWriteRevision" = {revision}, "FoundationParkAt" = {parkAt}, "FoundationParkRevision" = {openRevision} /*park-job-result*/ /*park-live-job*/
            WHERE "Id" = {root.Actor} AND "Partition" = {root.Partition} AND "Revision" = {park.Expected}
                AND "Status" = {(int)priorStatus} AND NOT "Retired" AND "Suspended" = {park.Suspended}
                AND "FoundationParkAt" IS NOT DISTINCT FROM {prior?.At} AND "FoundationParkRevision" IS NOT DISTINCT FROM {prior?.Revision}
                AND "FoundationParkClearing" IS NULL AND ({park.Opening} OR "Fault" = {park.EncodedFault})
                AND "FoundationTerminalAt" IS NULL AND "FoundationTerminalRevision" IS NULL
                AND "Jobs" IS NOT DISTINCT FROM {park.Jobs} AND "NextJobGeneration" IS NOT DISTINCT FROM {park.NextJobGeneration}
                AND "FoundationTenant" = {root.Tenant} AND "FoundationReference" = {root.Reference}
                AND EXISTS (SELECT 1 FROM "FoundationRootBirths" WHERE "Actor" = {root.Actor} AND "Partition" = {root.Partition}
                    AND "Tenant" = {root.Tenant} AND "Reference" = {root.Reference} AND "Creation" = 1 AND "Fingerprint" = {root.Fingerprint})
            """, pinsResult ? ParkStorageId(park.Inputs[0]) : null);
        write = WithLiveParkJob(db, write, live?.Proof);
        if (!pinsResult && await db.Database.ExecuteSqlInterpolatedAsync(write, token) != 1) return false;
        foreach (var group in park.Inputs.GroupBy(ParkStorageScope))
        {
            var ids = group.Select(ParkStorageId).ToArray();
            var pending = await db.Messages.AsNoTracking().Where(row => row.Target == root.Actor && row.Partition == root.Partition &&
                row.InboxScope == group.Key && row.EventMatch == null && !row.Done && row.Due <= at && ids.Contains(row.Id))
                .OrderBy(row => row.Seq).ToArrayAsync(token);
            var decoded = pending.Select(row => DecodeRootInput(db, row, root.Reference, park.SelfIssuer, timed: recorded));
            if (!ParkInputsMatch(decoded, group)) return false;
            if (pinsResult) await ReleaseProtectedTimedJobsAsync(db, root.Actor, ids, token);
            if (!await KeepSqliteParkInputsAsync(db, root.Actor, root.Partition, group.Key, ids, park.Reason, token)) return false;
        }
        if (pinsResult && await db.Database.ExecuteSqlInterpolatedAsync(write, token) != 1) return false;

        if (park.Returned.Length != 0 && await db.Database.ExecuteSqlInterpolatedAsync(WithSqliteOperatorPredicate(db, $"""
            UPDATE "Messages" SET "Seen" = 0
            WHERE "Target" = {root.Actor} AND "Partition" = {root.Partition} AND "InboxScope" = {InboxKey.FoundationOccurrenceScope}
                AND "EventMatch" IS NULL AND NOT "Done" AND (/*supported-operator*/)
                AND "Id" IN (SELECT value FROM json_each({Json.Write(park.Returned)}))
            """), token) != park.Returned.Length) return false;

        if (!park.Opening && RereadsParkFacts(db))
            await db.Database.ExecuteSqlInterpolatedAsync(WithSqliteOperatorPredicate(db, $"""
                UPDATE "Messages" SET "Seen" = 0, "Trial" = 0
                WHERE "Target" = {root.Actor} AND "Partition" = {root.Partition} AND "InboxScope" = {InboxKey.FoundationOccurrenceScope}
                    AND "EventMatch" IS NULL AND NOT "Done" AND (/*supported-operator*/) AND "Start" LIKE {ParkSelectedOperator}
                """), token);
        if (db.UsesRootChildren)
            await db.Database.ExecuteSqlRawAsync($"""
                UPDATE "Messages" AS m SET "Seen" = 0, "Trial" = 0
                WHERE m."Target" = {"{0}"} AND m."Partition" = {"{1}"} AND m."InboxScope" = {InboxKey.FoundationOccurrenceScope}
                    AND m."EventMatch" IS NULL AND NOT m."Done" AND {RootChildNoticePredicate("m")}
                """, [root.Actor, root.Partition], token);
        if (park.Reason == ParkKeepReason.Consumed)
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                DELETE FROM "Subscriptions" WHERE "Instance" = {root.Actor} AND "Partition" = {root.Partition}
                """, token);
        return await VerifyRootParkProjectionAsync(db, root.Tenant, root.Reference, openRevision, park.EncodedFault, token) &&
            (park.ChildEffects is null || await park.ChildEffects.BindAsync(db, root.Tenant, token));
    }

    private static async Task<bool> KeepSqliteParkInputsAsync(KernelDb db, string actor, int partition, long scope,
        string[] ids, ParkKeepReason reason, CancellationToken token) => await db.Database.ExecuteSqlInterpolatedAsync($"""
        UPDATE "Messages" SET "Done" = 1, "Trial" = 0, "FoundationKeptBy" = {(short)reason}
        WHERE "Target" = {actor} AND "Partition" = {partition} AND "InboxScope" = {scope} AND "EventMatch" IS NULL
            AND "Id" IN (SELECT value FROM json_each({Json.Write(ids)})) AND NOT "Done" AND "FoundationKeptBy" IS NULL
        """, token) == ids.Length;

}
