using Microsoft.EntityFrameworkCore;

namespace Kernel.EntityFramework;

public sealed partial class EfStore
{
    // Protocol 23 pages a recover whose kept set exceeds one page. The open drain lives on the root row: the cursor after
    // the last restored row and the totals the Complete journals. It is open only while the root is Running; any write
    // that ends or parks the root closes it, and the rows after the cursor stay kept.
    internal static Task InstallRootParkDrainAsync(KernelDb db, CancellationToken token) =>
        db.Database.ExecuteSqlRawAsync("""
            ALTER TABLE "Instances" ADD COLUMN IF NOT EXISTS "FoundationParkDrainSeq" bigint,
                ADD COLUMN IF NOT EXISTS "FoundationParkDrainId" text COLLATE "C",
                ADD COLUMN IF NOT EXISTS "FoundationParkDrainRestored" bigint,
                ADD COLUMN IF NOT EXISTS "FoundationParkDrainCleared" bigint;
            ALTER TABLE "Instances" DROP CONSTRAINT IF EXISTS "CK_FoundationParkDrain";
            ALTER TABLE "Instances" ADD CONSTRAINT "CK_FoundationParkDrain" CHECK (("FoundationParkDrainSeq" IS NULL
                AND "FoundationParkDrainId" IS NULL AND "FoundationParkDrainRestored" IS NULL AND "FoundationParkDrainCleared" IS NULL)
                OR ("FoundationParkDrainId" IS NOT NULL AND "FoundationParkDrainRestored" > 0 AND "FoundationParkDrainCleared" >= 0 AND "Status" = 0));
            CREATE OR REPLACE FUNCTION kernel_close_root_park_drain() RETURNS trigger LANGUAGE plpgsql AS $fn$
            BEGIN
                NEW."FoundationParkDrainSeq" := NULL; NEW."FoundationParkDrainId" := NULL;
                NEW."FoundationParkDrainRestored" := NULL; NEW."FoundationParkDrainCleared" := NULL;
                RETURN NEW;
            END
            $fn$;
            CREATE OR REPLACE TRIGGER kernel_close_root_park_drain BEFORE UPDATE ON "Instances" FOR EACH ROW
                WHEN (OLD."FoundationParkDrainSeq" IS NOT NULL AND NEW."Status" <> 0) EXECUTE FUNCTION kernel_close_root_park_drain();
            """, token);

    private sealed record DrainRow(long Generation, long? Seq, string? Id, long? Restored, long? Cleared);

    private static async Task<FoundationParkDrainState?> ReadDrainAsync(KernelDb db, string actor, int partition, CancellationToken token)
    {
        if (!db.UsesRootParkDrain) return null;
        var row = await db.Database.SqlQuery<DrainRow>($"""
            SELECT "FoundationParkGeneration" AS "Generation", "FoundationParkDrainSeq" AS "Seq", "FoundationParkDrainId" AS "Id",
                "FoundationParkDrainRestored" AS "Restored", "FoundationParkDrainCleared" AS "Cleared"
            FROM "Instances" WHERE "Id" = {actor} AND "Partition" = {partition} AND NOT "Retired"
            """).SingleOrDefaultAsync(token);
        return row is { Seq: { } seq, Id: { } id, Restored: { } restored, Cleared: { } cleared } ? new(row.Generation, seq, id, restored, cleared) : null;
    }

    // Released-row eligibility: while a drain is open the root receives only operator rows (the drain control among them)
    // and the rows its resolution released. Every other row waits until Complete, whatever its Seq.
    private const string RootDrainFilter = """
        AND (i."FoundationParkDrainSeq" IS NULL OR m."Id" LIKE 'operator/%' OR m."FoundationReleasedBy" = i."FoundationParkGeneration")
        """;
    // An operator row passes earlier rows that cannot be delivered yet, so the drain control never waits on them.
    private const string RootDrainPrior = """
        AND (target."FoundationParkDrainSeq" IS NULL OR prior."Id" LIKE 'operator/%' OR prior."FoundationReleasedBy" = target."FoundationParkGeneration")
        """;

    // The checkpoint writer: the drain columns move only at Open (Faulted to Running with a "recovering" entry), at a Page
    // (the cursor strictly advances and Restored grows, the checkpoint and journal untouched), at Complete (closed with a
    // "recovered" entry) and when the root leaves Running. The raw command is formatted, so the literal brace is doubled.
    private const string RootDrainColumns = """
        "FoundationParkDrainSeq", "FoundationParkDrainId", "FoundationParkDrainRestored", "FoundationParkDrainCleared"
        """;
    private static readonly string RootDrainCheckpoint = """
        AND (ROW(NEW./*c*/) IS NOT DISTINCT FROM ROW(OLD./*c*/)
            OR OLD."FoundationParkDrainSeq" IS NULL AND OLD."Status" = 2 AND NEW."Status" = 0 AND NEW."FoundationParkDrainSeq" IS NOT NULL
            OR OLD."FoundationParkDrainSeq" IS NOT NULL AND NEW."Status" <> 0 AND NEW."FoundationParkDrainSeq" IS NULL
            OR OLD."FoundationParkDrainSeq" IS NOT NULL AND OLD."Status" = 0 AND NEW."Status" = 0
                AND ROW(NEW."State", NEW."StateVersion", NEW."FoundationHeader", NEW."Fault", NEW."History", NEW."Suspended")
                    IS NOT DISTINCT FROM ROW(OLD."State", OLD."StateVersion", OLD."FoundationHeader", OLD."Fault", OLD."History", OLD."Suspended")
                AND CASE WHEN NEW."FoundationParkDrainSeq" IS NULL THEN EXISTS (SELECT 1 FROM "Journal" j WHERE j."Partition" = NEW."Partition"
                        AND j."Instance" = NEW."Id" AND j."Revision" = NEW."Revision" AND j."Entries" LIKE '{{"Outcome":"recovered",%')
                    ELSE (NEW."FoundationParkDrainSeq", NEW."FoundationParkDrainId") > (OLD."FoundationParkDrainSeq", OLD."FoundationParkDrainId")
                        AND NEW."FoundationParkDrainRestored" > OLD."FoundationParkDrainRestored"
                        AND NEW."FoundationParkDrainCleared" = OLD."FoundationParkDrainCleared"
                        AND NOT EXISTS (SELECT 1 FROM "Journal" j WHERE j."Partition" = NEW."Partition" AND j."Instance" = NEW."Id"
                            AND j."Revision" = NEW."Revision") END)
        """.Replace("NEW./*c*/", "NEW." + RootDrainColumns.Trim().Replace(", ", ", NEW.", StringComparison.Ordinal), StringComparison.Ordinal)
        .Replace("OLD./*c*/", "OLD." + RootDrainColumns.Trim().Replace(", ", ", OLD.", StringComparison.Ordinal), StringComparison.Ordinal);

    // The trigger fragments each placeholder takes under protocol 23, and the empty text it takes before.
    private static string DrainFragment(KernelDb db, string placeholder) => !db.UsesRootParkDrain ? "" : placeholder switch
    {
        "/*drain-checkpoint*/" => RootDrainCheckpoint,
        // A recovered entry closes a drain or resolves a page-sized Park with no drain; a recovering entry opens one.
        "/*drain-recovered*/" => """
            AND NEW."FoundationParkDrainSeq" IS NULL OR OLD."Status" = 0 AND NEW."Status" = 0
                AND OLD."FoundationParkDrainSeq" IS NOT NULL AND NEW."FoundationParkDrainSeq" IS NULL
            """,
        "/*drain-recovering*/" => """
            WHEN j."Entries" LIKE '{{"Outcome":"recovering",%' THEN NOT (OLD."Status" = 2 AND NEW."Status" = 0 AND NEW."FoundationParkDrainSeq" IS NOT NULL)
            """,
        "/*drain-open-entry*/" => """
            AND NEW."FoundationParkDrainSeq" IS NULL OR j."Entries" LIKE '{{"Outcome":"recovering",%' AND NEW."FoundationParkDrainSeq" IS NOT NULL
            """,
        "/*new-drain*/" => ", NEW." + RootDrainColumns.Trim().Replace(", ", ", NEW.", StringComparison.Ordinal),
        "/*old-drain*/" => ", OLD." + RootDrainColumns.Trim().Replace(", ", ", OLD.", StringComparison.Ordinal),
        // The journal: an Open entry on the parked root, a Complete entry on a Running root with its drain open.
        "/*drain-open-journal*/" => """OR NEW."Entries" LIKE '{{"Outcome":"recovering",%'""",
        "/*drain-complete-journal*/" => """
            OR i."Status" = 0 AND NOT i."Retired" AND i."Partition" = NEW."Partition" AND NEW."Revision" = i."Revision" + 1
                AND i."FoundationParkDrainSeq" IS NOT NULL
            """,
        // At commit: a recovered entry leaves no drain open, and a recovering entry leaves one open on the Running root.
        "/*drain-closed*/" => """AND i."FoundationParkDrainSeq" IS NULL""",
        "/*drain-opened*/" => """
            AND (NEW."Entries" NOT LIKE '{{"Outcome":"recovering",%' OR (i."Status" = 0 AND NOT i."Retired"
                AND i."FoundationParkAt" IS NULL AND i."FoundationParkRevision" IS NULL AND i."FoundationParkDrainSeq" IS NOT NULL))
            """,
        _ => throw new ArgumentOutOfRangeException(nameof(placeholder))
    };

    private static string WithDrainFragments(KernelDb db, string sql, params string[] placeholders) =>
        placeholders.Aggregate(sql, (text, placeholder) => text.Replace(placeholder, DrainFragment(db, placeholder), StringComparison.Ordinal));
}
