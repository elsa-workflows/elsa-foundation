using System.Runtime.CompilerServices;
using Kernel.Durable;
using Microsoft.EntityFrameworkCore;

namespace Kernel.EntityFramework;

public sealed partial class EfStore
{
    private static PreparedRoot PrepareRootRefusal(WorkerRoot root, TurnCommit turn, Message refusal)
    {
        var key = refusal.InboxKey;
        var disposition = new TurnCommit(root.Actor, null, null, turn.Consumed, turn.Done, turn.Sent, turn.Subscribed,
            turn.Unsubscribed, null, Held: turn.Held)
            { FoundationRootRefusedInput = refusal };
        var consumes = turn.Consumed is [var consumed] && consumed == key && turn.Done.Count == 0;
        var marksDone = turn.Done is [var done] && done == key && turn.Consumed.Count == 0;
        var matches = key.Scope switch
        {
            0 => consumes && refusal.FoundationRootBinding == (root.Actor, root.Tenant, root.Reference),
            InboxKey.FoundationOccurrenceScope => (consumes || marksDone) && refusal is { Operator: null, Start: null },
            _ => false
        };
        // A no-turn disposition may only settle the one witnessed physical envelope.
        if (turn != disposition || !matches || key.Match is not null || !root.Owns(key) ||
            refusal.Target.Id != root.Actor || refusal.Target.Partition != root.Partition || refusal.TargetCreation != 1 ||
            turn.Sent.Count != 0 || turn.Subscribed.Count != 0 || turn.Unsubscribed.Count != 0 || turn.Held is { Count: > 0 })
            throw RootShape();
        return new(null, new RefusedRootInput(root, refusal));
    }

    private sealed record RefusedRootInput(WorkerRoot Root, Message Refused) : QualifiedRootCheckpoint(0)
    {
        internal uint? PostgresMarker { get; init; }

        internal override async Task<bool> BindCheckpointAsync(KernelDb db, CancellationToken token)
        {
            var sqlite = db.UsesSqliteRootRuntime;
            if (!db.UsesRootScheduleInventory || !sqlite && (PostgresMarker is null || !db.Database.IsNpgsql()))
                return false;
            var key = Refused.InboxKey;
            var creator = key.Scope == 0;
            var ready = Bind("""SELECT m.* FROM "Messages" m""", sqlite ? "" : "FOR UPDATE OF m");
            var row = await db.Messages.FromSqlInterpolated(ready).AsNoTracking().SingleOrDefaultAsync(token);
            if (row is null) return false;
            var envelope = creator
                ? (sqlite ? DecodeSqliteCreator(row, db.UsesRootChildren) : PostgresRootAdmission.DecodeCreator(row, db.UsesRootChildren))
                    with { FoundationRootBinding = (Root.Actor, Root.Tenant, Root.Reference) }
                : DecodeRootInput(db, row, Root.Reference, issue: null);
            if (envelope != Refused) return false;

            var settle = row.KeepUntil is null
                ? """DELETE FROM "Messages" AS m"""
                : """UPDATE "Messages" AS m SET "Done" = true, "Payload" = NULL, "Start" = NULL, "ReplyTo" = NULL""";
            return await db.Database.ExecuteSqlInterpolatedAsync(Bind(settle, ""), token) == 1;

            FormattableString Bind(string statement, string suffix)
            {
                var clock = sqlite ? SqliteCommitDialect.MillisecondClock : RootClockMilliseconds;
                var messageSource = sqlite ? "" : """
                    AND EXISTS (SELECT 1 FROM pg_catalog.pg_class marker
                        JOIN pg_catalog.pg_class inbox ON inbox.oid = m.tableoid AND inbox.relnamespace = marker.relnamespace
                        JOIN pg_catalog.pg_class checkpoints ON checkpoints.oid = pg_catalog.to_regclass('"Instances"')
                            AND checkpoints.relnamespace = marker.relnamespace
                        WHERE marker.oid::bigint = {7} AND marker.oid = pg_catalog.to_regclass('"KernelStoreVersions"'))
                    """;
                var birthSource = sqlite ? "" : """
                    AND EXISTS (SELECT 1 FROM pg_catalog.pg_class marker
                        JOIN pg_catalog.pg_class births ON births.oid = b.tableoid AND births.relnamespace = marker.relnamespace
                        WHERE marker.oid::bigint = {7} AND marker.oid = pg_catalog.to_regclass('"KernelStoreVersions"'))
                    """;
                var beforeFirstCheckpoint = !creator ? "" : """
                    AND NOT EXISTS (SELECT 1 FROM "Instances" i WHERE i."Id" = m."Target" AND i."Partition" = m."Partition")
                    """;
                var sql = $$"""
                    {{statement}}
                    WHERE m."Id" = {0} AND m."InboxScope" = {1} AND m."Target" = {2} AND m."Partition" = {3}
                        AND NOT m."Done" AND m."EventMatch" IS NULL AND m."Due" <= {{clock}} {{messageSource}}
                        AND EXISTS (SELECT 1 FROM "FoundationRootBirths" b WHERE b."Actor" = m."Target"
                            AND b."Partition" = m."Partition" AND b."Tenant" = {4} AND b."Reference" = {5}
                            AND b."Creation" = 1 AND b."Fingerprint" = {6} AND (m."InboxScope" <> 0 OR b."Creator" = m."Id") {{birthSource}})
                        {{beforeFirstCheckpoint}}
                    {{suffix}}
                    """;
                object[] args = [key.Id, key.Scope, Root.Actor, Root.Partition, Root.Tenant, Root.Reference, Root.Fingerprint];
                return FormattableStringFactory.Create(sql, sqlite ? args : [.. args, (long)PostgresMarker!.Value]);
            }
        }
    }
}
