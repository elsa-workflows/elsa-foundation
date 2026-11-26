using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;

namespace Kernel.EntityFramework;

public sealed partial class EfStore
{
    private static string SqliteCohortWaitingWork =>
        CohortWaitingWork.Replace("LIKE 'operator/%'", "GLOB 'operator/*'", StringComparison.Ordinal);

    private const string SqliteCohortDecision = """
        EXISTS (SELECT 1 FROM json_each({4}) actor JOIN json_each({5}) rev ON rev.key = actor.key
            WHERE actor.value = i."Id" AND rev.value = i."Revision")
        """;

    private static FormattableString SqliteCohortCandidates(int[] partitions, long[] epochs, long[] afterDue,
        long[] afterSeq, long[] through, int limit) => FormattableStringFactory.Create(
        $"""
        WITH fences AS MATERIALIZED (
            SELECT p.value AS "Partition", e.value AS "Epoch", d.value AS "AfterDue", s.value AS "AfterSeq", t.value AS "Through"
            FROM json_each({"{0}"}) p JOIN json_each({"{1}"}) e ON e.key = p.key
            JOIN json_each({"{2}"}) d ON d.key = p.key JOIN json_each({"{3}"}) s ON s.key = p.key
            JOIN json_each({"{4}"}) t ON t.key = p.key
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
                    ORDER BY "Due", "Seq" LIMIT {"{5}"}) page
            )) row
        ), tail AS (
            SELECT "Partition", "Due", "Seq", row_number() OVER (PARTITION BY "Partition" ORDER BY "Due" DESC, "Seq" DESC) AS "Ordinal"
            FROM candidates
        ), eligible AS (
            SELECT i."Partition", i."Id" AS "Actor", i."Revision", i."FoundationTenant" AS "Tenant", i."FoundationReference" AS "Reference",
                i."FoundationHeader" AS "Header"
            FROM (SELECT DISTINCT "Partition", "Target" FROM candidates WHERE "Seen" < "Epoch") w
            JOIN "Instances" i ON i."Id" = w."Target" AND i."Partition" = w."Partition"
            WHERE i."Status" = {(int)InstanceStatus.Running} AND NOT i."Retired"
                AND i."FoundationReference" IS NOT NULL AND i."FoundationParkGeneration" > 0
                AND EXISTS (SELECT 1 FROM "Messages" c
                    WHERE c."Partition" = i."Partition" AND c."Target" = i."Id" AND c."EventMatch" IS NULL AND NOT c."Done"
                        AND c."InboxScope" = {InboxKey.FoundationOccurrenceScope} AND c."FoundationReleasedBy" = i."FoundationParkGeneration")
        )
        SELECT b."Partition", b."Through", t."Due", t."Seq", e."Actor", e."Revision", e."Tenant", e."Reference", e."Header"
        FROM bounds b LEFT JOIN tail t ON t."Partition" = b."Partition" AND t."Ordinal" = 1
        LEFT JOIN eligible e ON e."Partition" = b."Partition"
        /* sqlite-cohort-waiting */
        """, Json.Write(partitions), Json.Write(epochs), Json.Write(afterDue), Json.Write(afterSeq), Json.Write(through), limit);
}
