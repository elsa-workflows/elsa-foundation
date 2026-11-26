using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;

namespace Kernel.EntityFramework;

public sealed partial class EfStore
{
    private static async Task<List<SqliteKeptRow>> ReadSqliteRecoveryRowsAsync(KernelDb db, string actor, int partition,
        long? released, CancellationToken token)
    {
        var scope = released is null ? $"""
                AND m."Done" AND m."FoundationKeptBy" = {(short)ParkKeepReason.Arrived}
                """ : $"""
                AND NOT m."Done" AND m."FoundationKeptBy" IS NULL AND m."InboxScope" = {InboxKey.FoundationOccurrenceScope}
                AND m."FoundationReleasedBy" IS NOT NULL AND m."FoundationReleasedBy" = {"{2}"}
                """;
        var discarded = released is null ? $"""
            UNION ALL
            SELECT m."Id", m."InboxScope", m."Seq", m."FoundationKeptBy", 0, 0,
                m."Kind", NULL, NULL, m."Due", NULL, m."KeepUntil", m."JobGeneration", m."Sender"
            FROM "Messages" m WHERE m."Target" = {"{0}"} AND m."Partition" = {"{1}"} AND m."EventMatch" IS NULL
                AND m."Done" AND m."FoundationKeptBy" = {(short)ParkKeepReason.Consumed}
            """ : "";
        // Quote the arrivals before opening their envelopes. Clearing a consumed input needs only its metadata.
        var sql = $"""
            WITH arrivals AS MATERIALIZED (
                SELECT m."Id", m."InboxScope", m."Seq",
                    (octet_length(m."Id") + octet_length(m."Kind") + coalesce(octet_length(m."Start"), 0)
                        + coalesce(octet_length(m."Payload"), 0) + coalesce(octet_length(m."ReplyTo"), 0)) AS "Bytes"
                FROM "Messages" m WHERE m."Target" = {"{0}"} AND m."Partition" = {"{1}"} AND m."EventMatch" IS NULL {scope}
                ORDER BY m."Seq", m."Id", m."InboxScope" LIMIT {InMemoryStore.ParkPageLimit + 1}
            ), quote AS (
                SELECT arrivals.*, row_number() OVER step AS "Ordinal", sum("Bytes") OVER step AS "QuoteBytes"
                FROM arrivals WINDOW step AS (ORDER BY "Seq", "Id", "InboxScope" ROWS UNBOUNDED PRECEDING)
            )
            SELECT q."Id" AS "Id", q."InboxScope" AS "InboxScope", q."Seq" AS "Seq", {(short)ParkKeepReason.Arrived} AS "KeptBy", q."Bytes", 0 AS "TotalRows",
                m."Kind", m."Start" AS "Header", m."Payload", m."Due", m."ReplyTo", m."KeepUntil", m."JobGeneration", m."Sender"
            FROM quote q LEFT JOIN "Messages" m ON m."Id" = q."Id" AND m."InboxScope" = q."InboxScope"
                AND (q."Ordinal" = 1 OR (q."Ordinal" <= {InMemoryStore.ParkPageLimit} AND q."QuoteBytes" <= {ParkRecoveryCredit.LimitBytes}))
            {discarded}
            ORDER BY "Seq", "Id", "InboxScope"
            /* bounded-park-recovery-page */
            """;
        var rows = db.Database.SqlQuery<SqliteKeptEnvelope>(released is long generation
            ? FormattableStringFactory.Create(sql, actor, partition, generation)
            : FormattableStringFactory.Create(sql, actor, partition));
        var kept = new List<SqliteKeptRow>();
        var charged = 0;
        long bytes = 0;
        await foreach (var row in rows.AsAsyncEnumerable().WithCancellation(token))
        {
            var digest = Hash(row);
            kept.Add(new(row.Id, row.InboxScope, new(row.Id, row.KeptBy, digest, Hash(row with { KeepUntil = null }), row.Bytes)));
            if (row.KeptBy != (short)ParkKeepReason.Arrived) continue;
            bytes += row.Bytes;
            if (++charged > InMemoryStore.ParkPageLimit || charged > 1 && bytes > ParkRecoveryCredit.LimitBytes) break;
        }
        return kept;

        static string Hash(SqliteKeptEnvelope row) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(DurableKeyJson.Write(row))));
    }

    private sealed record SqliteRecoverCheckpoint(long? At, long? Revision, long Generation, string? Clearing, long? NextJobGeneration);

    private static async Task<bool> BindSqliteRootRecoverAsync(KernelDb db, RootParkRecover turn, CancellationToken token)
    {
        var (root, command) = (turn.Root, turn.Command);
        var facts = command.Operator!.RootParkFacts!;
        var prior = await db.Database.SqlQuery<SqliteRecoverCheckpoint>($"""
            SELECT i."FoundationParkAt" AS "At", i."FoundationParkRevision" AS "Revision",
                i."FoundationParkGeneration" AS "Generation", i."FoundationParkClearing" AS "Clearing", i."NextJobGeneration"
            FROM "Instances" i JOIN "FoundationRootBirths" b
                ON b."Actor" = i."Id" AND b."Partition" = i."Partition" AND b."Creation" = 1
                AND b."Tenant" = i."FoundationTenant" AND b."Reference" = i."FoundationReference"
            WHERE i."Id" = {root.Actor} AND i."Partition" = {root.Partition} AND i."Revision" = {turn.Expected}
                AND i."Status" = {(int)turn.Prior} AND NOT i."Suspended" AND NOT i."Retired"
                AND i."FoundationTenant" = {root.Tenant} AND i."FoundationReference" = {root.Reference} AND b."Fingerprint" = {root.Fingerprint}
                AND (NOT {turn.Recovered} OR i."Jobs" IS NOT DISTINCT FROM {facts.ConsumedJobRegistry}
                    AND i."FoundationParkClearing" IS NULL)
            """).SingleOrDefaultAsync(token);
        if (prior is null || (prior.At is null) != (turn.Prior == InstanceStatus.Running)) return false;
        var kept = turn.Recovered ? await ReadSqliteRecoveryRowsAsync(db, root.Actor, root.Partition, null, token) : [];
        var arrived = kept.Where(row => row.Facts.KeptBy == (short)ParkKeepReason.Arrived).ToArray();
        var consumedTimedJobs = turn.Recovered
            ? await ReadConsumedRootJobsAsync(db, root.Actor, root.Partition, facts.ConsumedJobRegistry, token) : [];
        if (consumedTimedJobs is null || !consumedTimedJobs.ToHashSet(StringComparer.Ordinal)
            .SetEquals(kept.Where(row => row.Scope == RootTimedJobScope).Select(row => row.Id))) return false;
        if (turn.Recovered && (facts.Mark != new FoundationParkMark(prior.Revision ?? 0, prior.At ?? 0) ||
            PageLength(arrived.Select(row => row.Facts)) != arrived.Length || arrived.Any(row => row.Scope != InboxKey.FoundationOccurrenceScope) ||
            KeptSetDigest(kept.Select(row => row.Facts)) != facts.KeptDigest)) return false;
        var envelope = command with { Operator = command.Operator with { RootParkFacts = null } };
        if (!await SettleSqliteRootInputsAsync(db, root.Reference, new(root.Actor, 1), [command.InboxKey], [envelope],
                turn.WithheldInputs || prior.Clearing is not null, token, parked: turn.Prior == InstanceStatus.Faulted))
            return false;

        var revision = turn.JournalRevision;
        var generation = turn.Recovered ? checked(prior.Generation + 1) : prior.Generation;
        if (await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE "Instances" SET "Revision" = {revision}, "FoundationWriteRevision" = {revision},
                "Status" = CASE WHEN {turn.Recovered} THEN {(int)InstanceStatus.Running} ELSE "Status" END,
                "Fault" = CASE WHEN {turn.Recovered} THEN NULL ELSE "Fault" END,
                "Jobs" = CASE WHEN {turn.Recovered} THEN NULL ELSE "Jobs" END,
                "FoundationParkAt" = CASE WHEN {turn.Recovered} THEN NULL ELSE "FoundationParkAt" END,
                "FoundationParkRevision" = CASE WHEN {turn.Recovered} THEN NULL ELSE "FoundationParkRevision" END,
                "FoundationParkGeneration" = {generation}
            WHERE "Id" = {root.Actor} AND "Partition" = {root.Partition} AND "Revision" = {turn.Expected}
            """, token) != 1) return false;
        if (!turn.Recovered) return true;

        var until = checked(await ReadSqliteParkClockAsync(db, token) + command.Operator.RootParkWindow!.Value);
        if (await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE "Messages" SET "Done" = 0, "FoundationKeptBy" = NULL, "Seen" = 0, "Trial" = 0, "Attempts" = 0,
                "FoundationReleasedBy" = {generation}, "KeepUntil" = {until}
            WHERE "Target" = {root.Actor} AND "Partition" = {root.Partition} AND "EventMatch" IS NULL AND "Done"
                AND "InboxScope" = {InboxKey.FoundationOccurrenceScope} AND "FoundationKeptBy" = {(short)ParkKeepReason.Arrived}
            """, token) != arrived.Length ||
            !await ClearSqliteParkRowsAsync(db, root, kept.Where(row => row.Facts.KeptBy == (short)ParkKeepReason.Consumed &&
                row.Scope != RootTimedJobScope).ToArray(), until, token))
            return false;
        if (consumedTimedJobs.Length != 0 && await db.Database.ExecuteSqlInterpolatedAsync($"""
            DELETE FROM "Messages" WHERE "Target" = {root.Actor} AND "Partition" = {root.Partition}
                AND "InboxScope" = {RootTimedJobScope} AND "EventMatch" IS NULL AND "Done"
                AND "Id" IN (SELECT value FROM json_each({Json.Write(consumedTimedJobs)}))
                AND "FoundationKeptBy" = {(short)ParkKeepReason.Consumed}
            """, token) != consumedTimedJobs.Length) return false;
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE "Messages" SET "Seen" = 0 WHERE "Target" = {root.Actor} AND "Partition" = {root.Partition}
                AND "InboxScope" = {InboxKey.FoundationOccurrenceScope} AND "EventMatch" IS NULL AND NOT "Done" AND "Seen" <> 0
            """, token);
        var restored = await ReadSqliteRecoveryRowsAsync(db, root.Actor, root.Partition, generation, token);
        return restored.Select(row => row.Facts.Envelope).SequenceEqual(arrived.Select(row => row.Facts.Envelope)) &&
            await db.Database.SqlQuery<bool>($"""
                SELECT EXISTS (SELECT 1 FROM "Instances" WHERE "Id" = {root.Actor} AND "Partition" = {root.Partition}
                    AND "Revision" = {revision} AND "Jobs" IS NULL AND "NextJobGeneration" IS NOT DISTINCT FROM {prior.NextJobGeneration})
                AND EXISTS (SELECT 1 FROM "FoundationActorList" WHERE "Tenant" = {root.Tenant} AND "Reference" = {root.Reference}
                    AND "Status" = {(int)InstanceStatus.Running} AND "ParkAt" IS NULL AND "ParkRevision" IS NULL AND "ParkFault" IS NULL
                    AND "ParkResolution" IS NULL AND "EndedAt" IS NULL AND "TerminalRevision" IS NULL) AS "Value"
                """).SingleAsync(token);
    }
}
