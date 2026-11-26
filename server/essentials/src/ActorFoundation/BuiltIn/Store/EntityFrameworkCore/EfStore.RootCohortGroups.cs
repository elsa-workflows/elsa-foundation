using System.Runtime.CompilerServices;

namespace Kernel.EntityFramework;

public sealed partial class EfStore
{
    private static string CohortGroupBounds(string source, string waiting, string clock) => $"""
        bounds AS MATERIALIZED (
            SELECT f.*, CASE WHEN f."Through" >= 0 THEN f."Through" ELSE coalesce((
                SELECT m."Seq" FROM "Messages" m WHERE m."Partition" = f."Partition" ORDER BY m."Seq" DESC LIMIT 1
            ), 0) END AS "Ceiling",
            CASE WHEN f."Through" >= 0 THEN f."ReadyThrough" ELSE {clock} END AS "ReadyAt"
            FROM fences f
        ), by_seen AS MATERIALIZED (
            SELECT b.*, (SELECT "Seen" FROM {source}
                WHERE "Partition" = b."Partition" AND "Seen" >= b."Seen" AND "Seen" < b."Epoch" AND {waiting}
                ORDER BY "Seen" LIMIT 1) AS "SeenGroup"
            FROM bounds b
        ), by_due AS MATERIALIZED (
            SELECT s.*, (SELECT "Due" FROM {source}
                WHERE "Partition" = s."Partition" AND "Seen" = s."SeenGroup" AND "Due" <= s."ReadyAt"
                    AND "Due" >= CASE WHEN s."SeenGroup" = s."Seen" THEN s."Due" ELSE 0 END AND {waiting}
                ORDER BY "Due" LIMIT 1) AS "DueGroup"
            FROM by_seen s
        )
        """;

    private static string CohortSequenceRange(string source, string waiting) => $"""
        SELECT "Target", "Seq" FROM {source}
        WHERE "Partition" = d."Partition" AND "Seen" = d."SeenGroup" AND "Due" = d."DueGroup"
            AND "Seq" > CASE WHEN d."Seen" = d."SeenGroup" AND d."Due" = d."DueGroup" THEN d."Seq" ELSE 0 END
            AND "Seq" <= d."Ceiling" AND {waiting}
        ORDER BY "Seq"
        """;

    private static string CohortPage(string limit) => $"""
        page AS (
            SELECT p.*, row_number() OVER (PARTITION BY "Partition" ORDER BY "Seq") AS "Ordinal" FROM picked p
        ), candidates AS MATERIALIZED (
            SELECT "Partition", "Target" FROM page WHERE "Ordinal" <= {limit}
        ), tail AS (
            SELECT "Partition", max(CASE WHEN "Ordinal" <= {limit} THEN "Seq" END) AS "Seq", count(*) > {limit} AS "More"
            FROM page GROUP BY "Partition"
        ), eligible AS (
            {CohortEligibleRoots}
        )
        SELECT d."Partition", d."Ceiling" AS "Through", d."ReadyAt" AS "ReadyThrough", d."SeenGroup", d."DueGroup",
            t."Seq", coalesce(t."More", false) AS "More", e."Actor", e."Revision", e."Tenant", e."Reference", e."Header"
        FROM by_due d LEFT JOIN tail t ON t."Partition" = d."Partition"
        LEFT JOIN eligible e ON e."Partition" = d."Partition"
        """;

    private static FormattableString PostgresCohortCandidates(List<CohortFence> windows, int limit) => FormattableStringFactory.Create(
        $"""
        WITH fences AS MATERIALIZED (
            SELECT * FROM unnest({"{0}"}::integer[], {"{1}"}::bigint[], {"{2}"}::bigint[], {"{3}"}::bigint[],
                {"{4}"}::bigint[], {"{5}"}::bigint[], {"{6}"}::bigint[])
                AS f("Partition", "Epoch", "Through", "ReadyThrough", "Seen", "Due", "Seq")
        ), {CohortGroupBounds("\"Messages\"", CohortWaitingWork, "floor(extract(epoch from statement_timestamp()) * 1000)::bigint")},
        picked AS MATERIALIZED (
            SELECT d."Partition", p."Target", p."Seq" FROM by_due d CROSS JOIN LATERAL (
                {CohortSequenceRange("\"Messages\"", CohortWaitingWork)} LIMIT ({"{7}"} + 1)
            ) p
        ), {CohortPage("{7}")}
        /* root-cohort-waiting */
        """, windows.Select(fence => fence.Partition).ToArray(), windows.Select(fence => fence.Epoch).ToArray(),
        windows.Select(fence => fence.Through).ToArray(), windows.Select(fence => fence.ReadyThrough).ToArray(),
        windows.Select(fence => fence.Seen).ToArray(), windows.Select(fence => fence.Due).ToArray(), windows.Select(fence => fence.Seq).ToArray(), limit);
}
