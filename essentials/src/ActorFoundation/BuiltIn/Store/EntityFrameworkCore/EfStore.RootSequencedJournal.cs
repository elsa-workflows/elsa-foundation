using System.Runtime.CompilerServices;
using System.Text.Json;
using Kernel.Foundation.Contracts;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Kernel.EntityFramework;

public sealed partial class EfStore
{
    private const int SequencedJournalCommitBytes = 256 * 1024;
    private const int SequencedJournalPageBytes = 1024 * 1024;
    private const long MaximumJournalSequence = 9_007_199_254_740_991;
    private const int MaximumSequencedJournalPageSize = 500;
    private const string PostgresJournalClock = "floor(extract(epoch from clock_timestamp()) * 1000)::bigint";

    // Store one range per physical commit. The original entries remain in Journal, and retirement
    // removes their ranges through the foreign key in the same transaction.
    private static string SequencedJournalColumns(string integer, string actor) => $"""
        "Partition" integer NOT NULL, "Position" {integer} NOT NULL,
        "Actor" {actor} NOT NULL, "Revision" {integer} NOT NULL CHECK ("Revision" > 0),
        "FirstSequence" {integer} NOT NULL CHECK ("FirstSequence" > 0),
        "LastSequence" {integer} NOT NULL CHECK ("LastSequence" BETWEEN "FirstSequence" AND {MaximumJournalSequence}
            AND "LastSequence" - "FirstSequence" < 2147483647),
        "EntryBytes" {integer} NOT NULL CHECK ("EntryBytes" > 0),
        "RecordedAt" {integer} NOT NULL,
        PRIMARY KEY ("Partition", "Actor", "LastSequence"),
        UNIQUE ("Partition", "Position"),
        FOREIGN KEY ("Partition", "Position") REFERENCES "Journal" ("Partition", "Position") ON DELETE CASCADE
        """;

    private const string SequencedJournalName = "FoundationSequencedJournal";
    private const string SequencedJournalTrigger = "kernel_project_root_sequenced_journal";
    private const string SequencedJournalGuard = "kernel_require_root_sequenced_journal";

    private static string SequencedJournalInsert(string count, string clock) => $"""
        INSERT INTO "{SequencedJournalName}" (
            "Partition", "Position", "Actor", "Revision", "FirstSequence", "LastSequence", "EntryBytes", "RecordedAt")
        SELECT NEW."Partition", NEW."Position", NEW."Instance", NEW."Revision",
            coalesce(prior."LastSequence", 0) + 1,
            coalesce(prior."LastSequence", 0) + ({count}),
            octet_length(NEW."Entries"), {clock}
        FROM (SELECT 1) sentinel LEFT JOIN (SELECT "LastSequence", "Revision" FROM "{SequencedJournalName}"
            WHERE "Partition" = NEW."Partition" AND "Actor" = NEW."Instance"
            ORDER BY "LastSequence" DESC LIMIT 1) prior ON true
        WHERE prior."Revision" IS NULL OR prior."Revision" < NEW."Revision";
        """;

    private const string QualifiedSequencedJournal = """
        NEW."Revision" > 0 AND EXISTS (SELECT 1 FROM "FoundationRootBirths" b
            WHERE b."Actor" = NEW."Instance" AND b."Partition" = NEW."Partition" AND b."Creation" = 1)
        """;

    private static readonly string PostgresSequencedJournal = $"""
        CREATE TABLE "{SequencedJournalName}" ({SequencedJournalColumns("bigint", """text COLLATE "C" """)});
        CREATE FUNCTION {SequencedJournalTrigger}() RETURNS trigger LANGUAGE plpgsql AS $fn$
        BEGIN
            IF {QualifiedSequencedJournal} THEN
                {SequencedJournalInsert("""
                    CASE WHEN json_typeof(NEW."Entries"::json) = 'array'
                        THEN greatest(json_array_length(NEW."Entries"::json), 1) ELSE 1 END
                    """, PostgresJournalClock)}
                IF NOT FOUND THEN
                    RAISE EXCEPTION 'Sequenced journal requires increasing revisions' USING ERRCODE = '23514';
                END IF;
            END IF;
            RETURN NULL;
        END
        $fn$;
        CREATE TRIGGER {SequencedJournalTrigger} AFTER INSERT ON "Journal"
        FOR EACH ROW EXECUTE FUNCTION {SequencedJournalTrigger}();
        CREATE FUNCTION {SequencedJournalGuard}() RETURNS trigger LANGUAGE plpgsql AS $fn$
        BEGIN
            IF {QualifiedSequencedJournal} AND NOT EXISTS (
                SELECT 1 FROM "{SequencedJournalName}" s
                WHERE s."Partition" = NEW."Partition" AND s."Position" = NEW."Position"
                    AND s."Actor" = NEW."Instance" AND s."Revision" = NEW."Revision")
            THEN RAISE EXCEPTION 'A qualified journal row requires its sequence range'
                USING ERRCODE = '23514', CONSTRAINT = '{SequencedJournalGuard}'; END IF;
            RETURN NULL;
        END
        $fn$;
        -- PostgreSQL runs equally timed triggers in name order. Assert the preceding projector's
        -- exact row in the same fenced write even if the projector is disabled after startup.
        CREATE TRIGGER {SequencedJournalGuard} AFTER INSERT ON "Journal"
        FOR EACH ROW EXECUTE FUNCTION {SequencedJournalGuard}();
        """;

    private static IEnumerable<SqliteDefinition> SqliteSequencedJournalDefinitions()
    {
        yield return Table(SequencedJournalName, SequencedJournalColumns("INTEGER", "TEXT COLLATE BINARY"));
        yield return new("trigger", SequencedJournalTrigger, "Journal", $"""
            CREATE TRIGGER {SequencedJournalTrigger} AFTER INSERT ON "Journal"
            WHEN {QualifiedSequencedJournal}
            BEGIN
                {SequencedJournalInsert("""
                    CASE WHEN json_type(NEW."Entries") = 'array'
                        THEN max(json_array_length(NEW."Entries"), 1) ELSE 1 END
                    """, SqliteCommitDialect.MillisecondClock)}
                SELECT RAISE(ABORT, 'Sequenced journal requires increasing revisions') WHERE changes() <> 1;
            END
            """);
    }

    internal static string PostgresSequencedJournalReady(string physicalMarker) => $"""
        EXISTS (SELECT 1 FROM pg_catalog.pg_class selected
            WHERE selected.oid = {physicalMarker} AND selected.relkind = 'r'
                AND (SELECT count(*) FROM (VALUES ('"Journal"'), ('"FoundationSequencedJournal"'),
                    ('"Instances"'), ('"FoundationRootBirths"')) named(relation)
                    JOIN pg_catalog.pg_class source ON source.oid = pg_catalog.to_regclass(named.relation)
                        AND source.relnamespace = selected.relnamespace AND source.relkind = 'r') = 4
                AND (SELECT count(*) FROM pg_catalog.pg_trigger stamp
                    JOIN pg_catalog.pg_proc implementation ON implementation.oid = stamp.tgfoid
                    WHERE stamp.tgrelid = pg_catalog.to_regclass('"Journal"')
                        AND stamp.tgname IN ('{SequencedJournalTrigger}', '{SequencedJournalGuard}')
                        AND stamp.tgenabled IN ('O', 'A') AND stamp.tgtype = 5 AND NOT stamp.tgisinternal
                        AND implementation.proname = stamp.tgname AND implementation.pronamespace = selected.relnamespace) = 2)
        """;

    internal static async Task RequirePostgresSequencedJournalAsync(KernelDb db, CancellationToken token)
    {
        var check = FormattableStringFactory.Create($$"""
            SELECT {{PostgresSequencedJournalReady("v.tableoid")}} AS "Value"
            FROM "KernelStoreVersions" v WHERE v."Id" = {0}
            """, StoreVersionEntity.MarkerId);
        if (!await db.Database.SqlQuery<bool>(check).SingleOrDefaultAsync(token))
            throw new StoreFormatException("The installed sequenced journal projection is incomplete.");
    }

    internal bool SupportsSequencedJournal()
    {
        using var db = create();
        return db.UsesRootSequencedJournal;
    }

    private sealed record SequencedJournalRow(long AsOf, bool SourceReady, long? RootRevision, int? History, long? FirstRevision, long? ExpectedFirstRevision,
        long? RetainedFromSequence, long? LatestSequence, long? LastRecordedAt, long? FirstSequence, long? LastSequence,
        long? Revision, long? RecordedAt, long? EntryBytes, string? Entries,
        int? StoreSchema, int? MessageProtocol, int? JournalCodec, string? DefinitionStamp);

    internal async Task<ActorSequencedJournalPage?> ReadRootSequencedJournalAsync(string tenant, string reference, long after,
        long? upper, int limit, CancellationToken token)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(after);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(after, MaximumJournalSequence);
        if (upper is { } bound)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(bound, after, nameof(upper));
            ArgumentOutOfRangeException.ThrowIfGreaterThan(bound, MaximumJournalSequence, nameof(upper));
        }
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(limit, MaximumSequencedJournalPageSize);
        await using var db = create();
        if (Engine is not null || !db.UsesRootSequencedJournal)
            throw new NotSupportedException("This store does not carry the sequenced journal projection.");
        var sqlite = db.UsesSqliteRootRuntime;
        if (!sqlite && !postgres) throw new NotSupportedException("The sequenced journal requires a physical foundation store.");

        var expected = db.RequiredFormat;
        // Seek by the last sequence of each row. At most one row can straddle the requested start.
        // Quote bounded raw rows from scalar metadata before joining Journal. An oversized commit occupies
        // its sequence interval without pulling its contents into the reader.
        FormattableString query = $"""
            /* root-sequenced-journal */
            WITH marker AS (
                SELECT "StoreSchema", "MessageProtocol", "JournalCodec" /* marker-identity */
                FROM "KernelStoreVersions" WHERE "Id" = {StoreVersionEntity.MarkerId}
            ), root AS (
                SELECT b."Actor", b."Partition", i."Revision", i."History", /*first-revision*/ AS "ExpectedFirstRevision"
                FROM "FoundationRootBirths" b JOIN "Instances" i
                    ON i."Id" = b."Actor" AND i."Partition" = b."Partition"
                    AND i."FoundationReference" = b."Reference" AND i."FoundationTenant" = b."Tenant"
                WHERE b."Tenant" = {tenant} AND b."Reference" = {reference} AND b."Creation" = 1 AND NOT i."Retired"
                    AND EXISTS (SELECT 1 FROM marker WHERE "StoreSchema" = {expected.StoreSchema}
                        AND "MessageProtocol" = {expected.MessageProtocol} AND "JournalCodec" = {expected.JournalCodec})
            ), head AS MATERIALIZED (
                SELECT root.*,
                    (SELECT s."FirstSequence" FROM "FoundationSequencedJournal" s
                        WHERE s."Partition" = root."Partition" AND s."Actor" = root."Actor"
                        ORDER BY s."LastSequence" LIMIT 1) AS "RetainedFromSequence",
                    (SELECT s."Revision" FROM "FoundationSequencedJournal" s
                        WHERE s."Partition" = root."Partition" AND s."Actor" = root."Actor"
                        ORDER BY s."LastSequence" LIMIT 1) AS "FirstRevision",
                    coalesce((SELECT s."LastSequence" FROM "FoundationSequencedJournal" s
                        WHERE s."Partition" = root."Partition" AND s."Actor" = root."Actor"
                        ORDER BY s."LastSequence" DESC LIMIT 1), 0) AS "LatestSequence"
                FROM root
            ), end_bound AS MATERIALIZED (
                SELECT h.*,
                    (SELECT s."LastSequence" FROM "FoundationSequencedJournal" s
                        WHERE s."Partition" = h."Partition" AND s."Actor" = h."Actor"
                            AND s."LastSequence" >= CASE
                                WHEN coalesce({upper}, h."LatestSequence") < {Math.Min(MaximumJournalSequence, after + limit)}
                                THEN coalesce({upper}, h."LatestSequence") ELSE {Math.Min(MaximumJournalSequence, after + limit)} END
                        ORDER BY s."LastSequence" LIMIT 1) AS "LastPageRow"
                FROM head h WHERE coalesce({upper}, h."LatestSequence") > {after}
            ), slice AS MATERIALIZED (
                SELECT s.* FROM "FoundationSequencedJournal" s
                WHERE s."Partition" = (SELECT "Partition" FROM end_bound)
                    AND s."Actor" = (SELECT "Actor" FROM end_bound)
                    AND s."LastSequence" > {after} AND s."LastSequence" <= (SELECT "LastPageRow" FROM end_bound)
                ORDER BY s."LastSequence" LIMIT {limit}
            ), quoted AS (
                SELECT slice.*,
                    sum(CASE WHEN "EntryBytes" <= {SequencedJournalCommitBytes} THEN "EntryBytes" ELSE 0 END)
                        OVER (ORDER BY "LastSequence" ROWS UNBOUNDED PRECEDING) AS "PageBytes"
                FROM slice
            ), page AS (
                SELECT q.*, CASE WHEN q."EntryBytes" <= {SequencedJournalCommitBytes} THEN
                    (SELECT j."Entries" FROM "Journal" j WHERE j."Partition" = q."Partition" AND j."Position" = q."Position"
                        AND j."Instance" = q."Actor" AND j."Revision" = q."Revision") END AS "Entries"
                FROM quoted q
                WHERE q."PageBytes" <= {SequencedJournalPageBytes}
            )
            SELECT /* journal-clock */ AS "AsOf", /* journal-ready */ AS "SourceReady",
                h."Revision" AS "RootRevision", h."History", h."FirstRevision", h."ExpectedFirstRevision",
                h."RetainedFromSequence", h."LatestSequence",
                (SELECT s."RecordedAt" FROM "FoundationSequencedJournal" s
                    WHERE s."Partition" = h."Partition" AND s."Actor" = h."Actor"
                        AND s."LastSequence" >= coalesce({upper}, h."LatestSequence")
                        AND coalesce({upper}, h."LatestSequence") > 0
                    ORDER BY s."LastSequence" LIMIT 1) AS "LastRecordedAt",
                p."FirstSequence", p."LastSequence", p."Revision", p."RecordedAt", p."EntryBytes", p."Entries",
                marker."StoreSchema", marker."MessageProtocol", marker."JournalCodec", /*definitions-stamp*/ AS "DefinitionStamp"
            FROM (SELECT 1) sentinel LEFT JOIN head h ON true LEFT JOIN marker ON true LEFT JOIN page p ON true
            ORDER BY p."LastSequence"
            """;
        var sql = query.Format
            .Replace("/*first-revision*/", db.UsesRootInlinePromotion ? """b."FirstRevision" """ : "CAST(1 AS bigint)", StringComparison.Ordinal)
            .Replace("/* journal-clock */", sqlite ? SqliteCommitDialect.MillisecondClock :
                "floor(extract(epoch from statement_timestamp()) * 1000)::bigint", StringComparison.Ordinal)
            .Replace("/* marker-identity */", sqlite ? "" : """, tableoid AS "PhysicalMarker" """, StringComparison.Ordinal)
            .Replace("/* journal-ready */", sqlite ? "true" : PostgresSequencedJournalReady("""marker."PhysicalMarker" """), StringComparison.Ordinal);
        if (sqlite)
        {
            // The admission policy, install stamp, actor head and page are pinned to one read snapshot.
            var sqliteQuery = FormattableStringFactory.Create(sql.Replace("/*definitions-stamp*/", "NULL", StringComparison.Ordinal),
                query.GetArguments());
            return await ReadSqliteRootSnapshotAsync(db, async at =>
                BuildSequencedJournalPage(await db.Database.SqlQuery<SequencedJournalRow>(sqliteQuery).ToListAsync(token),
                    after, upper, limit, at), token);
        }

        List<SequencedJournalRow> rows;
        try
        {
            rows = await db.Database.SqlQuery<SequencedJournalRow>(StoreDefinitionsRevision.AddPostgresStamp(
                FormattableStringFactory.Create(sql, query.GetArguments()))).ToListAsync(token);
        }
        catch (PostgresException error) when (error.SqlState is PostgresErrorCodes.UndefinedTable or PostgresErrorCodes.UndefinedColumn)
        {
            throw new StoreFormatException("The sequenced journal has an unreadable schema.", error);
        }
        var format = rows[0];
        RequireSnapshotFormat(db, format.StoreSchema, format.MessageProtocol, format.JournalCodec, format.DefinitionStamp);
        if (!format.SourceReady) throw new StoreFormatException("The installed sequenced journal projection is incomplete.");
        return BuildSequencedJournalPage(rows, after, upper, limit, format.AsOf);
    }

    private static ActorSequencedJournalPage? BuildSequencedJournalPage(List<SequencedJournalRow> rows,
        long after, long? requestedUpper, int limit, long asOf)
    {
        var head = rows[0];
        if (head.RootRevision is not { } rootRevision) return null;
        var upper = requestedUpper ?? head.LatestSequence!.Value;
        if (upper > head.LatestSequence || after > upper)
            throw new ArgumentException("The requested journal range is no longer retained.", nameof(requestedUpper));
        var through = after;
        var items = new List<SequencedJournalEntry>(limit);
        foreach (var row in rows)
        {
            if (row.FirstSequence is not { } first || row.LastSequence is not { } last) break;
            if (first > through + 1 || items.Count > 0 && first != through + 1 || row.Revision > rootRevision ||
                row.Entries is null && row.EntryBytes <= SequencedJournalCommitBytes)
                throw new StoreFormatException("The sequenced journal range does not match its committed rows.");
            using var document = row.Entries is null ? null : JsonDocument.Parse(row.Entries);
            var entries = document?.RootElement;
            var split = entries is { ValueKind: JsonValueKind.Array } array && array.GetArrayLength() > 0;
            var count = last - first + 1;
            if (entries is not null && count != (split ? entries.Value.GetArrayLength() : 1))
                throw new StoreFormatException("The sequenced journal range does not match its committed entry count.");
            var stop = Math.Min(last, Math.Min(upper, after + limit));
            for (var sequence = through + 1; sequence <= stop; sequence++)
            {
                var ordinal = checked((int)(sequence - first));
                items.Add(new(sequence, row.Revision!.Value, ordinal,
                    split ? entries!.Value[ordinal].GetRawText() : row.Entries, row.RecordedAt!.Value));
                through = sequence;
            }
        }
        if (through == after && through < upper)
            throw new StoreFormatException("The sequenced journal has no retained rows in its acknowledged range.");
        return new(items, head.RetainedFromSequence ?? 0, upper, through, head.LastRecordedAt,
            head.FirstRevision is not null && head.FirstRevision == head.ExpectedFirstRevision &&
            head.History == (int)Durable.History.Full, asOf);
    }
}
