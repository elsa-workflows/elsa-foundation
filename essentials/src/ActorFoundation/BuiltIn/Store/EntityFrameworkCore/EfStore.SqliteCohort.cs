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

    private static FormattableString SqliteCohortCandidates(int[] partitions, long[] epochs, long[] afterDue,
        long[] afterSeq, long[] through, int limit) => FormattableStringFactory.Create(
        $"""
        WITH fences AS MATERIALIZED (
            SELECT f.value ->> 'Partition' AS "Partition", f.value ->> 'Epoch' AS "Epoch",
                f.value ->> 'AfterDue' AS "AfterDue", f.value ->> 'AfterSeq' AS "AfterSeq", f.value ->> 'Through' AS "Through"
            FROM json_each({"{0}"}) f
        ), bounds AS MATERIALIZED (
            SELECT f."Partition", f."Epoch", f."AfterDue", f."AfterSeq",
                CASE WHEN f."Through" >= 0 THEN f."Through" ELSE coalesce((
                    SELECT m."Seq" FROM "Messages" m WHERE m."Partition" = f."Partition" ORDER BY m."Seq" DESC LIMIT 1
                ), 0) END AS "Through"
            FROM fences f
        ), candidates AS MATERIALIZED (
            SELECT b."Partition", b."Epoch", row.value ->> 'target' AS "Target", row.value ->> 'due' AS "Due",
                row.value ->> 'seq' AS "Seq", row.value ->> 'seen' AS "Seen"
            FROM bounds b CROSS JOIN json_each((
                SELECT json_group_array(json_object('target', page."Target", 'due', page."Due", 'seq', page."Seq", 'seen', page."Seen"))
                FROM (SELECT "Target", "Due", "Seq", "Seen" FROM "Messages"
                    WHERE "Partition" = b."Partition" AND ("Due", "Seq") > (b."AfterDue", b."AfterSeq") AND "Seq" <= b."Through"
                        AND "Due" <= {SqliteCommitDialect.MillisecondClock} AND {SqliteCohortWaitingWork}
                    ORDER BY "Due", "Seq" LIMIT {"{1}"}) page
            )) row
        ), tail AS (
            SELECT "Partition", "Due", "Seq", row_number() OVER (PARTITION BY "Partition" ORDER BY "Due" DESC, "Seq" DESC) AS "Ordinal"
            FROM candidates
        ), eligible AS (
            {CohortEligibleRoots}
        )
        SELECT b."Partition", b."Through", t."Due", t."Seq", e."Actor", e."Revision", e."Tenant", e."Reference", e."Header"
        FROM bounds b LEFT JOIN tail t ON t."Partition" = b."Partition" AND t."Ordinal" = 1
        LEFT JOIN eligible e ON e."Partition" = b."Partition"
        /* sqlite-cohort-waiting */
        """, Json.Write(partitions.Select((partition, index) => new
        {
            Partition = partition, Epoch = epochs[index], AfterDue = afterDue[index], AfterSeq = afterSeq[index], Through = through[index]
        }).ToArray()), limit);
}
