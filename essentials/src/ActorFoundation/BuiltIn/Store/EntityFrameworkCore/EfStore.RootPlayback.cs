using System.Data;
using System.Runtime.CompilerServices;
using Kernel.Foundation.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Kernel.EntityFramework;

public sealed partial class EfStore
{
    private const string PlaybackName = "FoundationJournalPlayback";
    private const string PlaybackTrigger = "kernel_project_root_playback";

    private static string PlaybackColumns(string integer, string text) => $"""
        "Partition" integer NOT NULL, "Actor" {text} NOT NULL, "Position" {integer} NOT NULL,
        "Activity" {text} NOT NULL, "Ordinal" {integer} NOT NULL CHECK ("Ordinal" >= 0),
        "Sequence" {integer} NOT NULL CHECK ("Sequence" > 0),
        "Revision" {integer} NOT NULL CHECK ("Revision" > 0),
        "CommitOrdinal" integer NOT NULL CHECK ("CommitOrdinal" >= 0),
        "Observation" text NOT NULL, "RecordedAt" {integer} NOT NULL,
        PRIMARY KEY ("Partition", "Actor", "Activity"),
        UNIQUE ("Partition", "Actor", "Ordinal"),
        UNIQUE ("Partition", "Actor", "Sequence"),
        FOREIGN KEY ("Partition", "Position") REFERENCES "{SequencedJournalName}" ("Partition", "Position") ON DELETE CASCADE
        """;

    // The Native writer reserves version 1 JSON objects for structural facts. Public authored diagnostics
    // remain JSON strings. Select one eligible first observation per exact execution in commit order.
    private static string PlaybackInsert(string entriesSql) => $"""
        INSERT INTO "{PlaybackName}" ("Partition", "Actor", "Position", "Activity", "Ordinal",
            "Sequence", "Revision", "CommitOrdinal", "Observation", "RecordedAt")
        SELECT NEW."Partition", NEW."Actor", NEW."Position", fresh."Activity",
            COALESCE((SELECT "Ordinal" FROM "{PlaybackName}" prior
                WHERE prior."Partition" = NEW."Partition" AND prior."Actor" = NEW."Actor"
                ORDER BY "Ordinal" DESC LIMIT 1), -1) + ROW_NUMBER() OVER (ORDER BY fresh."Slot"),
            NEW."FirstSequence" + fresh."Slot", NEW."Revision", fresh."Slot", fresh."Observation", NEW."RecordedAt"
        FROM (
            SELECT observation.*, ROW_NUMBER() OVER (PARTITION BY "Activity" ORDER BY "Slot") AS "First"
            FROM ({entriesSql}) observation
        ) fresh
        WHERE fresh."First" = 1 AND NOT EXISTS (
            SELECT 1 FROM "{PlaybackName}" existing WHERE existing."Partition" = NEW."Partition"
                AND existing."Actor" = NEW."Actor" AND existing."Activity" = fresh."Activity")
        """;

    private const string PlaybackRawRow = """
        j."Partition" = NEW."Partition" AND j."Position" = NEW."Position"
            AND j."Instance" = NEW."Actor" AND j."Revision" = NEW."Revision"
        """;

    private static readonly string PostgresPlayback = $"""
        CREATE TABLE "{PlaybackName}" ({PlaybackColumns("bigint", """text COLLATE "C" """)});
        CREATE INDEX kernel_playback_commit ON "{PlaybackName}" ("Partition", "Position");
        CREATE FUNCTION {PlaybackTrigger}() RETURNS trigger LANGUAGE plpgsql AS $fn$
        BEGIN
            {PlaybackInsert($"""
                SELECT slot - 1 AS "Slot", cell->>'Frame' COLLATE "C" AS "Activity",
                    jsonb_build_object('NativeStructure', 1, 'Kind', cell->'Kind', 'At', cell->'At',
                        'Frame', cell->'Frame', 'Node', cell->'Node', 'Parent', cell->'Parent')::text AS "Observation"
                FROM "Journal" j CROSS JOIN LATERAL jsonb_array_elements(
                    CASE WHEN jsonb_typeof(j."Entries"::jsonb) = 'array'
                        THEN j."Entries"::jsonb ELSE jsonb_build_array(j."Entries"::jsonb) END
                ) WITH ORDINALITY item(cell, slot)
                WHERE {PlaybackRawRow} AND jsonb_typeof(cell->'NativeStructure') = 'number' AND cell->>'NativeStructure' = '1'
                    AND jsonb_typeof(cell->'Kind') = 'number' AND cell->>'Kind' IN ('2', '3', '4', '5')
                    AND jsonb_typeof(cell->'At') = 'number' AND jsonb_typeof(cell->'Frame') = 'string'
                    AND length(cell->>'Frame') > 0 AND jsonb_typeof(cell->'Node') = 'string' AND length(cell->>'Node') > 0
                    AND coalesce(jsonb_typeof(cell->'Parent'), 'null') IN ('string', 'null')
                """)};
            RETURN NULL;
        END
        $fn$;
        CREATE TRIGGER {PlaybackTrigger} AFTER INSERT ON "{SequencedJournalName}"
            FOR EACH ROW EXECUTE FUNCTION {PlaybackTrigger}();
        """;

    private static IEnumerable<SqliteDefinition> SqlitePlaybackDefinitions()
    {
        yield return Table(PlaybackName, PlaybackColumns("INTEGER", "TEXT COLLATE BINARY"));
        yield return Index("kernel_playback_commit", PlaybackName, """("Partition", "Position")""");
        yield return new("trigger", PlaybackTrigger, SequencedJournalName, $"""
            CREATE TRIGGER {PlaybackTrigger} AFTER INSERT ON "{SequencedJournalName}"
            BEGIN
                {PlaybackInsert($"""
                    SELECT "Slot", json_extract(cell, '$.Frame') COLLATE BINARY AS "Activity",
                        json_object('NativeStructure', 1, 'Kind', json_extract(cell, '$.Kind'), 'At', json_extract(cell, '$.At'),
                            'Frame', json_extract(cell, '$.Frame'), 'Node', json_extract(cell, '$.Node'),
                            'Parent', json_extract(cell, '$.Parent')) AS "Observation"
                    FROM (SELECT CAST(item.key AS INTEGER) AS "Slot",
                            CASE WHEN item.type = 'object' THEN item.value ELSE json_object() END AS cell
                        FROM "Journal" j, json_each(CASE WHEN json_type(j."Entries") = 'array' THEN j."Entries"
                            ELSE json_array(json(j."Entries")) END) item
                        WHERE {PlaybackRawRow}) parsed
                    WHERE json_type(cell, '$.NativeStructure') = 'integer' AND json_extract(cell, '$.NativeStructure') = 1
                        AND json_type(cell, '$.Kind') = 'integer' AND json_extract(cell, '$.Kind') IN (2, 3, 4, 5)
                        AND json_type(cell, '$.At') = 'integer' AND json_type(cell, '$.Frame') = 'text'
                        AND length(json_extract(cell, '$.Frame')) > 0 AND json_type(cell, '$.Node') = 'text'
                        AND length(json_extract(cell, '$.Node')) > 0 AND coalesce(json_type(cell, '$.Parent'), 'null') IN ('text', 'null')
                    """)};
            END
            """);
    }

    internal static string PostgresPlaybackReady(string marker) => $"""
        EXISTS (SELECT 1 FROM pg_catalog.pg_class format
            JOIN pg_catalog.pg_class playback ON playback.oid = pg_catalog.to_regclass('"{PlaybackName}"')
                AND playback.relnamespace = format.relnamespace AND playback.relkind = 'r'
            JOIN pg_catalog.pg_trigger stamp ON stamp.tgrelid = pg_catalog.to_regclass('"{SequencedJournalName}"')
                AND stamp.tgname = '{PlaybackTrigger}' AND stamp.tgenabled IN ('O', 'A')
                AND stamp.tgtype = 5 AND NOT stamp.tgisinternal
            JOIN pg_catalog.pg_proc implementation ON implementation.oid = stamp.tgfoid
                AND implementation.proname = stamp.tgname AND implementation.pronamespace = format.relnamespace
            WHERE format.oid = {marker})
        """;

    private static async Task RequirePostgresPlaybackAsync(KernelDb db, CancellationToken token)
    {
        var check = FormattableStringFactory.Create($$"""
            SELECT {{PostgresPlaybackReady("marker.tableoid")}} AS "Value"
            FROM "KernelStoreVersions" marker WHERE marker."Id" = {0}
            """, StoreVersionEntity.MarkerId);
        if (!await db.Database.SqlQuery<bool>(check).SingleOrDefaultAsync(token))
            throw new StoreFormatException("The installed playback projection is incomplete.");
    }

    internal bool SupportsPlayback()
    {
        using var db = create();
        return db.UsesRootPlayback;
    }

    private sealed record PlaybackRoot(int Partition, string Actor, long RetainedAfter, long Latest, long? LastRecordedAt);
    private sealed record PlaybackRow(string Activity, long Ordinal, long Sequence, long Revision, int CommitOrdinal,
        string Observation, long RecordedAt);

    internal async Task<ActorPlaybackPage?> ReadRootPlaybackAsync(string tenant, string reference, ActorPlaybackQuery query,
        CancellationToken token)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(query.Limit, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(query.Limit, 100);
        if (query.Position < 0 || query.UpperSequence < 0 || query.RetainedAfterSequence < 0)
            throw new ArgumentOutOfRangeException(nameof(query));
        await using var db = create();
        if (Engine is not null || !db.UsesRootPlayback)
            throw new NotSupportedException("This store does not carry the playback projection.");
        if (db.UsesSqliteRootRuntime)
            return await ReadSqliteRootSnapshotAsync(db, ReadAsync, token);
        if (!postgres) throw new NotSupportedException("The playback projection requires a physical foundation store.");

        await using var snapshot = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, token);
        await RequireStoreVersionsAsync(db, token);
        await RequirePostgresSequencedJournalAsync(db, token);
        await RequirePostgresPlaybackAsync(db, token);
        var readAt = await db.Database.SqlQueryRaw<long>($"""SELECT {PostgresJournalClock} AS "Value" """).SingleAsync(token);
        var page = await ReadAsync(readAt);
        await snapshot.CommitAsync(token);
        return page;

        async Task<ActorPlaybackPage?> ReadAsync(long asOf)
        {
            var root = await db.Database.SqlQuery<PlaybackRoot>($"""
                SELECT birth."Partition", birth."Actor",
                    COALESCE((SELECT "FirstSequence" - 1 FROM "FoundationSequencedJournal" s
                        WHERE s."Partition" = birth."Partition" AND s."Actor" = birth."Actor"
                        ORDER BY s."LastSequence" LIMIT 1), 0) AS "RetainedAfter",
                    COALESCE((SELECT "LastSequence" FROM "FoundationSequencedJournal" s
                        WHERE s."Partition" = birth."Partition" AND s."Actor" = birth."Actor"
                        ORDER BY s."LastSequence" DESC LIMIT 1), 0) AS "Latest",
                    (SELECT "RecordedAt" FROM "FoundationSequencedJournal" s
                        WHERE s."Partition" = birth."Partition" AND s."Actor" = birth."Actor"
                        ORDER BY s."LastSequence" DESC LIMIT 1) AS "LastRecordedAt"
                FROM "FoundationRootBirths" birth JOIN "Instances" i ON i."Partition" = birth."Partition"
                    AND i."Id" = birth."Actor" AND i."FoundationReference" = birth."Reference"
                    AND i."FoundationTenant" = birth."Tenant"
                WHERE birth."Tenant" = {tenant} AND birth."Reference" = {reference} AND birth."Creation" = 1 AND NOT i."Retired"
                """).SingleOrDefaultAsync(token);
            if (root is null) return null;
            var upper = query.UpperSequence ?? root.Latest;
            ActorPlaybackPage Result(ActorPlaybackAvailability state, string reason) =>
                new(state, reason, root.Latest, upper, root.RetainedAfter, root.LastRecordedAt, null, null, [], asOf);
            if (upper > root.Latest || query.RetainedAfterSequence is { } retained && retained != root.RetainedAfter)
                return Result(ActorPlaybackAvailability.Invalidated, "retained-range-changed");

            var occurrences = db.Database.SqlQuery<PlaybackRow>(FormattableStringFactory.Create($$"""
                SELECT "Activity", "Ordinal", "Sequence", "Revision", "CommitOrdinal", "Observation", "RecordedAt"
                FROM "{{PlaybackName}}" WHERE "Partition" = {0} AND "Actor" = {1}
                """, root.Partition, root.Actor));
            var members = occurrences.Where(row => row.Sequence > root.RetainedAfter && row.Sequence <= upper);
            if (query.Activity is { } activity)
            {
                var scoped = await FindMemberAsync(activity);
                if (scoped is null) return Result(ActorPlaybackAvailability.Unavailable, "scope-unavailable");
                if (query.Anchor is { } exact && exact != activity)
                    return Result(ActorPlaybackAvailability.TargetUnavailable, "anchor-outside-scope");
                if (query.Position is > 0)
                    return Result(ActorPlaybackAvailability.TargetUnavailable, "position-outside-range");
                return Available(1, 0, [Entry(scoped, 0, null, null)]);
            }

            var first = await members.OrderBy(row => row.Sequence).Select(row => (long?)row.Ordinal).FirstOrDefaultAsync(token);
            if (first is null)
                return Result(ActorPlaybackAvailability.Unavailable, "occurrence-recording-unavailable");
            var last = await members.OrderByDescending(row => row.Sequence).Select(row => row.Ordinal).FirstAsync(token);
            var count = checked(last - first.Value + 1);
            var selected = query.Position ?? 0;
            if (query.Anchor is { } anchor)
            {
                var target = await FindMemberAsync(anchor);
                if (target is null) return Result(ActorPlaybackAvailability.TargetUnavailable, "anchor-outside-range");
                selected = target.Ordinal - first.Value;
            }
            if (selected < 0 || selected >= count)
                return Result(ActorPlaybackAvailability.TargetUnavailable, "position-outside-range");
            var start = first.Value + Math.Min(Math.Max(0, count - query.Limit), Math.Max(0, selected - query.Limit / 2));
            var end = Math.Min(last, start + query.Limit - 1);
            var from = Math.Max(first.Value, start - 1);
            var through = Math.Min(last, end + 1);
            var rows = await members.Where(row => row.Ordinal >= from && row.Ordinal <= through)
                .OrderBy(row => row.Ordinal).Take(query.Limit + 2).ToArrayAsync(token);
            if (rows.Length != through - from + 1 || rows.Where((row, index) => row.Ordinal != from + index).Any())
                throw new StoreFormatException("The playback index has an incomplete occurrence range.");
            var items = rows.Select((row, index) => Entry(row, row.Ordinal - first.Value,
                    index == 0 ? null : rows[index - 1].Activity, index == rows.Length - 1 ? null : rows[index + 1].Activity))
                .Where((_, index) => rows[index].Ordinal >= start && rows[index].Ordinal <= end).ToArray();
            return Available(count, selected, items);

            // Seek the exact activity key first. Testing retained bounds on that one row keeps a
            // provider from choosing the range index and scanning the frozen prefix for an anchor.
            async Task<PlaybackRow?> FindMemberAsync(string activity) =>
                await occurrences.SingleOrDefaultAsync(row => row.Activity == activity, token) is { } row &&
                    row.Sequence > root.RetainedAfter && row.Sequence <= upper ? row : null;

            ActorPlaybackPage Available(long total, long target, IReadOnlyList<ActorPlaybackEntry> items) =>
                new(ActorPlaybackAvailability.Available, null, root.Latest, upper, root.RetainedAfter, root.LastRecordedAt,
                    total, target, items, asOf);
        }

        static ActorPlaybackEntry Entry(PlaybackRow row, long position, string? previous, string? next) =>
            new(position, row.Sequence, row.Revision, row.CommitOrdinal, row.Observation, row.RecordedAt, previous, next);
    }
}
