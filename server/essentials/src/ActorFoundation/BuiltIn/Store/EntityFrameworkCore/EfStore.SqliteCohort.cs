using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;

namespace Kernel.EntityFramework;

public sealed partial class EfStore
{
    private static string SqliteCohortWaitingWork =>
        CohortWaitingWork.Replace("LIKE 'operator/%'", "GLOB 'operator/*'", StringComparison.Ordinal);

    private const string SqliteCohortDecision = """
        EXISTS (SELECT 1 FROM json_each({4}) decision
            WHERE (decision.value ->> 'Actor') = i."Id" AND (decision.value ->> 'Revision') = i."Revision")
        """;
    private static string SqliteCohortDecisions(string[] actors, long[] revisions) =>
        Json.Write(actors.Zip(revisions, (actor, revision) => new { Actor = actor, Revision = revision }).ToArray());

    private static FormattableString SqliteCohortCandidates(int[] partitions, long[] epochs, long[] afterSeq,
        long[] through, int limit) => FormattableStringFactory.Create(
        $"""
        WITH fences AS MATERIALIZED (
            SELECT f.value ->> 'Partition' AS "Partition", f.value ->> 'Epoch' AS "Epoch",
                f.value ->> 'AfterSeq' AS "AfterSeq", f.value ->> 'Through' AS "Through"
            FROM json_each({"{0}"}) f
        ), bounds AS MATERIALIZED (
            SELECT f."Partition", f."Epoch", f."AfterSeq",
                CASE WHEN f."Through" >= 0 THEN f."Through" ELSE coalesce((
                    SELECT m."Seq" FROM "Messages" m WHERE m."Partition" = f."Partition" ORDER BY m."Seq" DESC LIMIT 1
                ), 0) END AS "Through"
            FROM fences f
        ), candidates AS MATERIALIZED (
            SELECT b."Partition", row.value ->> 'target' AS "Target", row.value ->> 'seq' AS "Seq"
            FROM bounds b CROSS JOIN json_each((
                SELECT json_group_array(json_object('target', page."Target", 'seq', page."Seq"))
                FROM (SELECT "Target", "Seq" FROM "Messages" INDEXED BY "IX_FoundationRootInbox_CohortSequence"
                    WHERE "Partition" = b."Partition" AND "Seq" > b."AfterSeq" AND "Seq" <= b."Through"
                        AND "Seen" < b."Epoch" AND "Due" <= {SqliteCommitDialect.MillisecondClock} AND {SqliteCohortWaitingWork}
                    ORDER BY "Seq" LIMIT {"{1}"}) page
            )) row
        ), tail AS (
            SELECT "Partition", max("Seq") AS "Seq" FROM candidates GROUP BY "Partition"
        ), eligible AS (
            {CohortEligibleRoots}
        )
        SELECT b."Partition", b."Through", t."Seq", e."Actor", e."Revision", e."Tenant", e."Reference", e."Header"
        FROM bounds b LEFT JOIN tail t ON t."Partition" = b."Partition"
        LEFT JOIN eligible e ON e."Partition" = b."Partition"
        /* sqlite-cohort-waiting */
        """, Json.Write(partitions.Select((partition, index) => new
        {
            Partition = partition, Epoch = epochs[index], AfterSeq = afterSeq[index], Through = through[index]
        }).ToArray()), limit);
}
