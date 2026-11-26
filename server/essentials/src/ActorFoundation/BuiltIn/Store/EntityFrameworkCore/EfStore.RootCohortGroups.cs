using System.Runtime.CompilerServices;

namespace Kernel.EntityFramework;

public sealed partial class EfStore
{
    private const int CohortSpareReadAhead = 32;

    private static string CohortGroupBounds(string source, string waiting, string clock) => $"""
        bounds AS MATERIALIZED (
            SELECT f.*, CASE WHEN f."Through" >= 0 THEN f."Through" ELSE coalesce((
                SELECT m."Seq" FROM "Messages" m WHERE m."Partition" = f."Partition" ORDER BY m."Seq" DESC LIMIT 1
            ), 0) END AS "Ceiling",
            CASE WHEN f."Through" >= 0 THEN f."ReadyThrough" ELSE {clock} END AS "ReadyAt", {clock} AS "ScanReadyAt"
            FROM fences f
        ), by_seen AS MATERIALIZED (
            SELECT b.*, (SELECT "Seen" FROM {source}
                WHERE "Partition" = b."Partition" AND "Seen" >= b."Seen" AND "Seen" < b."Epoch" AND {waiting}
                ORDER BY "Seen" LIMIT 1) AS "SeenGroup"
            FROM bounds b
        ), by_due AS MATERIALIZED (
            SELECT s.*, (SELECT "Due" FROM {source}
                WHERE "Partition" = s."Partition" AND "Seen" = s."SeenGroup" AND "Due" <= s."ReadyAt"
                    AND "Due" >= CASE WHEN s."SeenGroup" = s."Seen" THEN s."Due" ELSE CAST('-9223372036854775808' AS bigint) END
                    AND {waiting}
                ORDER BY "Due" LIMIT 1) AS "DueGroup"
            FROM by_seen s
        )
        """;

    private static string CohortSequenceRange(string source, string waiting) => $"""
        SELECT "Target", "Seq" FROM {source}
        WHERE "Partition" = d."Partition" AND d."SeenGroup" IS NOT NULL AND d."DueGroup" IS NOT NULL
            AND "Seen" = d."SeenGroup" AND "Due" = d."DueGroup"
            AND "Seq" > CASE WHEN d."Seen" = d."SeenGroup" AND d."Due" = d."DueGroup" THEN d."Seq" ELSE 0 END
            AND "Seq" <= d."Ceiling" AND {waiting}
        ORDER BY "Seq"
        """;

    private static string CohortPage(string limit) => $"""
        page AS (
            SELECT p.*, row_number() OVER (PARTITION BY "Partition" ORDER BY "Seq") AS "Ordinal" FROM picked p
        ), tail AS (
            SELECT "Partition", max(CASE WHEN "Ordinal" <= {limit} THEN "Seq" END) AS "LastSeq",
                count(*) > {limit} AS "More", CAST(count(*) AS integer) AS "Count"
            FROM page GROUP BY "Partition"
        ), spare AS MATERIALIZED (
            SELECT d.*, t."LastSeq", coalesce(t."More", false) AS "More",
                CASE WHEN t."More" THEN 0 ELSE {limit} - coalesce(t."Count", 0) END AS "Spare"
            FROM by_due d LEFT JOIN tail t ON t."Partition" = d."Partition"
        )
        """;

    // Fill spare slots with a bounded, sequence-ordered scan. A backdated admission can add a group after the
    // ceiling, but it cannot stop this cursor from passing the earlier pending controls in sequence order.
    private static string CohortSpareRange(string source, string waiting, bool partial = false) => $"""
        SELECT "Target", "Seq", "Due", "Seen", ({waiting}) AS "Waiting" FROM {source}
        WHERE "Partition" = s."Partition" AND s."DueGroup" IS NOT NULL AND s."Spare" > 0
            AND "Seq" > s."ScanSeq" AND "Seq" <= s."Ceiling" {(partial ? "AND " + waiting : "")}
        ORDER BY "Seq"
        """;

    private static string CohortOutput(string limit) => $"""
        scan_page AS MATERIALIZED (
            SELECT p.*, row_number() OVER (PARTITION BY "Partition" ORDER BY "Seq") AS "Ordinal"
            FROM scanned p WHERE "Waiting" AND "Seen" < "Epoch" AND "Due" <= "ScanReadyAt"
        ), scan_tail AS (
            SELECT "Partition", max("Seq") AS "ScanTail" FROM scanned GROUP BY "Partition"
        ), accepted_tail AS (
            SELECT "Partition", "Seq" FROM scan_page WHERE "Ordinal" = "Spare"
        ), candidates AS MATERIALIZED (
            SELECT "Partition", "Target" FROM page WHERE "Ordinal" <= {limit}
            UNION
            SELECT "Partition", "Target" FROM scan_page WHERE "Ordinal" <= "Spare"
        ), eligible AS (
            {CohortEligibleRoots}
        )
        SELECT s."Partition", s."Ceiling" AS "Through", s."ReadyAt" AS "ReadyThrough", s."SeenGroup", s."DueGroup",
            s."LastSeq" AS "Seq", s."More", s."Spare", coalesce(a."Seq", t."ScanTail") AS "ScanTail",
            e."Actor", e."Revision", e."Tenant", e."Reference", e."Header"
        FROM spare s LEFT JOIN scan_tail t ON t."Partition" = s."Partition"
        LEFT JOIN accepted_tail a ON a."Partition" = s."Partition"
        LEFT JOIN eligible e ON e."Partition" = s."Partition"
        """;

    private static FormattableString PostgresCohortCandidates(List<CohortFence> windows, int limit) => FormattableStringFactory.Create(
        $"""
        WITH fences AS MATERIALIZED (
            SELECT * FROM unnest({"{0}"}::integer[], {"{1}"}::bigint[], {"{2}"}::bigint[], {"{3}"}::bigint[],
                {"{4}"}::bigint[], {"{5}"}::bigint[], {"{6}"}::bigint[], {"{7}"}::bigint[])
                AS f("Partition", "Epoch", "Through", "ReadyThrough", "Seen", "Due", "Seq", "ScanSeq")
        ), {CohortGroupBounds("\"Messages\"", CohortWaitingWork, "floor(extract(epoch from statement_timestamp()) * 1000)::bigint")},
        picked AS MATERIALIZED (
            SELECT d."Partition", p."Target", p."Seq" FROM by_due d CROSS JOIN LATERAL (
                {CohortSequenceRange("\"Messages\"", CohortWaitingWork)} LIMIT ({"{8}"} + 1)
            ) p
        ), {CohortPage("{8}")}, scanned AS MATERIALIZED (
            SELECT s."Partition", s."Spare", s."Epoch", s."ScanReadyAt", p."Target", p."Seq", p."Due", p."Seen", p."Waiting"
            FROM spare s CROSS JOIN LATERAL (
                {CohortSpareRange("\"Messages\"", CohortWaitingWork)} LIMIT ({"{8}"} + {CohortSpareReadAhead})
            ) p
        ), {CohortOutput("{8}")}
        /* root-cohort-waiting */
        """, windows.Select(fence => fence.Partition).ToArray(), windows.Select(fence => fence.Epoch).ToArray(),
        windows.Select(fence => fence.Through).ToArray(), windows.Select(fence => fence.ReadyThrough).ToArray(),
        windows.Select(fence => fence.Seen).ToArray(), windows.Select(fence => fence.Due).ToArray(), windows.Select(fence => fence.Seq).ToArray(),
        windows.Select(fence => fence.ScanSeq).ToArray(), limit);
}
