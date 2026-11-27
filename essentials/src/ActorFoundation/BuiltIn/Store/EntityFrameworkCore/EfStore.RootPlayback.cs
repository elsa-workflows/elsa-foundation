using System.Data;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Kernel.Foundation.Contracts;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Kernel.EntityFramework;

public sealed partial class EfStore
{
    private const string PlaybackName = "FoundationJournalPlayback";
    private const string PlaybackInvalidName = "FoundationJournalPlaybackInvalid";
    private const string PlaybackTrigger = "kernel_project_root_playback";
    private static string PostgresPlaybackSource(bool descendants) => $"""
        pg_catalog.concat_ws('/', {PostgresPlaybackSourceRelations}{(descendants
            ? $", pg_catalog.to_regclass('\"{PlaybackScopesName}\"')::oid" : "")})
        """;

    private const string PostgresPlaybackSourceRelations = """
        pg_catalog.to_regclass('"KernelStoreVersions"')::oid,
            pg_catalog.to_regclass('"FoundationRootBirths"')::oid, pg_catalog.to_regclass('"Instances"')::oid,
            pg_catalog.to_regclass('"FoundationSequencedJournal"')::oid, pg_catalog.to_regclass('"FoundationJournalPlayback"')::oid,
            pg_catalog.to_regclass('"FoundationJournalPlaybackInvalid"')::oid
        """;
    private static readonly string TimestampRange = FormattableString.Invariant(
        $"BETWEEN {DateTimeOffset.MinValue.ToUnixTimeMilliseconds()} AND {DateTimeOffset.MaxValue.ToUnixTimeMilliseconds()}");
    private const string PlaybackFields = "'NativeStructure', 'Kind', 'At', 'Frame', 'Node', 'Parent'";

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

    private static string PlaybackInvalidColumns(string integer, string text) => $"""
        "Partition" integer NOT NULL, "Actor" {text} NOT NULL, "Position" {integer} NOT NULL,
        "Sequence" {integer} NOT NULL CHECK ("Sequence" > 0),
        PRIMARY KEY ("Partition", "Actor", "Sequence"),
        UNIQUE ("Partition", "Position"),
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
            WHERE observation."Disposition" = 1
        ) fresh
        WHERE fresh."First" = 1 AND NOT EXISTS (
            SELECT 1 FROM "{PlaybackName}" existing WHERE existing."Partition" = NEW."Partition"
                AND existing."Actor" = NEW."Actor" AND existing."Activity" = fresh."Activity")
        """;

    private static string PlaybackInvalidInsert(string observations) => $"""
        INSERT INTO "{PlaybackInvalidName}" ("Partition", "Actor", "Position", "Sequence")
        SELECT NEW."Partition", NEW."Actor", NEW."Position", NEW."FirstSequence" + MIN(invalid."Slot")
        FROM ({observations}) invalid WHERE invalid."Disposition" = 2
        HAVING COUNT(*) > 0
        """;

    private const string PlaybackRawRow = """
        j."Partition" = NEW."Partition" AND j."Position" = NEW."Position"
            AND j."Instance" = NEW."Actor" AND j."Revision" = NEW."Revision"
        """;

    private static string PlaybackObservations(string cells, Func<string, string> type, Func<string, string> value,
        string number, string text, string timestamp, string objectFields, string projection)
    {
        var markerType = type("NativeStructure");
        var kind = value("Kind");
        var frame = $"({type("Frame")} = '{text}' AND length({value("Frame")}) > 0)";
        var node = $"({type("Node")} = '{text}' AND length({value("Node")}) > 0)";
        string Null(string field) => $"COALESCE({type(field)}, 'null') = 'null'";
        var outcome = $"{kind} IN ('2', '3', '4', '5')";
        return $"""
            SELECT "Slot", {value("Frame")} AS "Activity", {projection} AS "Observation",
                CASE WHEN {markerType} IS NULL THEN 0
                    WHEN {markerType} = '{number}' AND {value("NativeStructure")} = '1' AND {type("Kind")} = '{number}'
                        AND {timestamp} AND NOT EXISTS (
                            SELECT 1 FROM {objectFields} field WHERE field.key IN ({PlaybackFields})
                            GROUP BY field.key HAVING COUNT(*) > 1)
                        AND (
                            ({kind} IN ('0', '-0') AND {Null("Frame")} AND {Null("Node")} AND {Null("Parent")}) OR
                            ({kind} = '1' AND {frame} AND {Null("Node")} AND {Null("Parent")}) OR
                            ({outcome} AND {frame} AND {node} AND COALESCE({type("Parent")}, 'null') IN ('{text}', 'null')))
                        THEN CASE WHEN {outcome} THEN 1 ELSE 0 END
                    ELSE 2 END AS "Disposition"
            FROM ({cells}) parsed
            """;
    }

    // Keep the JSON number lexemes on PostgreSQL. jsonb would normalize exponent notation before
    // validating At, making records playable that Native's integral JSON decoder refuses.
    private static readonly string PostgresPlaybackObservations = PlaybackObservations($"""
        SELECT slot - 1 AS "Slot", cell
        FROM "Journal" j CROSS JOIN LATERAL json_array_elements(
            CASE WHEN json_typeof(j."Entries"::json) = 'array'
                THEN j."Entries"::json ELSE json_build_array(j."Entries"::json) END
        ) WITH ORDINALITY item(cell, slot)
        WHERE {PlaybackRawRow}
        """, key => $"json_typeof(cell->'{key}')", key => $"(cell->>'{key}') COLLATE \"C\"", "number", "string", $"""
        (CASE WHEN json_typeof(cell->'At') = 'number' AND length(cell->>'At') <= 16
            AND cell->>'At' ~ '^-?(0|[1-9][0-9]*)$' THEN (cell->>'At')::bigint {TimestampRange} ELSE false END)
        """, "json_each(CASE WHEN json_typeof(cell) = 'object' THEN cell ELSE json_build_object() END)", """
        json_build_object('NativeStructure', 1, 'Kind', cell->'Kind', 'At', cell->'At',
            'Frame', cell->'Frame', 'Node', cell->'Node', 'Parent', cell->'Parent')::text
        """);

    private static readonly string SqlitePlaybackObservations = PlaybackObservations($"""
        SELECT CAST(item.key AS INTEGER) AS "Slot", CASE WHEN item.type = 'object' THEN item.value ELSE json_object() END AS cell
        FROM "Journal" j, json_each(CASE WHEN json_type(j."Entries") = 'array' THEN j."Entries"
            ELSE json_array(json(j."Entries")) END) item
        WHERE {PlaybackRawRow}
        """, key => $"json_type(cell, '$.{key}')", key => $"CAST(json_extract(cell, '$.{key}') AS TEXT) COLLATE BINARY",
        "integer", "text", $"(typeof(json_extract(cell, '$.At')) = 'integer' AND json_extract(cell, '$.At') {TimestampRange})",
        "json_each(cell)", """
        json_object('NativeStructure', 1, 'Kind', json_extract(cell, '$.Kind'), 'At', json_extract(cell, '$.At'),
            'Frame', json_extract(cell, '$.Frame'), 'Node', json_extract(cell, '$.Node'), 'Parent', json_extract(cell, '$.Parent'))
        """);

    private static string PostgresPlaybackDefinitions(bool descendants) => $"""
        CREATE TABLE "{PlaybackName}" ({PlaybackColumns("bigint", """text COLLATE "C" """)});
        CREATE TABLE "{PlaybackInvalidName}" ({PlaybackInvalidColumns("bigint", """text COLLATE "C" """)});
        CREATE INDEX kernel_playback_commit ON "{PlaybackName}" ("Partition", "Position");
        {(descendants ? PostgresPlaybackScopes : "")}
        CREATE FUNCTION {PlaybackTrigger}() RETURNS trigger LANGUAGE plpgsql AS $fn$
        BEGIN
            {PlaybackInsert(PostgresPlaybackObservations)};
            {PlaybackInvalidInsert(PostgresPlaybackObservations)};
            {(descendants ? PostgresPlaybackScopeInsert + ";" : "")}
            RETURN NULL;
        END
        $fn$;
        CREATE TRIGGER {PlaybackTrigger} AFTER INSERT ON "{SequencedJournalName}"
            FOR EACH ROW EXECUTE FUNCTION {PlaybackTrigger}();
        """;

    private static IEnumerable<SqliteDefinition> SqlitePlaybackDefinitions(bool descendants)
    {
        yield return Table(PlaybackName, PlaybackColumns("INTEGER", "TEXT COLLATE BINARY"));
        yield return Table(PlaybackInvalidName, PlaybackInvalidColumns("INTEGER", "TEXT COLLATE BINARY"));
        yield return Index("kernel_playback_commit", PlaybackName, """("Partition", "Position")""");
        if (descendants)
            foreach (var definition in SqlitePlaybackScopes()) yield return definition;
        yield return new("trigger", PlaybackTrigger, SequencedJournalName, $"""
            CREATE TRIGGER {PlaybackTrigger} AFTER INSERT ON "{SequencedJournalName}"
            BEGIN
                {PlaybackInsert(SqlitePlaybackObservations)};
                {PlaybackInvalidInsert(SqlitePlaybackObservations)};{(descendants ? $"\n{SqlitePlaybackScopeInsert};" : "")}
            END
            """);
    }

    internal static string PostgresPlaybackReady(string marker, bool descendants) => $"""
        EXISTS (SELECT 1 FROM pg_catalog.pg_class format
            JOIN pg_catalog.pg_class playback ON playback.oid = pg_catalog.to_regclass('"{PlaybackName}"')
                AND playback.relnamespace = format.relnamespace AND playback.relkind = 'r'
            JOIN pg_catalog.pg_class invalid ON invalid.oid = pg_catalog.to_regclass('"{PlaybackInvalidName}"')
                AND invalid.relnamespace = format.relnamespace AND invalid.relkind = 'r'
            JOIN pg_catalog.pg_trigger stamp ON stamp.tgrelid = pg_catalog.to_regclass('"{SequencedJournalName}"')
                AND stamp.tgname = '{PlaybackTrigger}' AND stamp.tgenabled IN ('O', 'A')
                AND stamp.tgtype = 5 AND NOT stamp.tgisinternal
            JOIN pg_catalog.pg_proc implementation ON implementation.oid = stamp.tgfoid
                AND implementation.proname = stamp.tgname AND implementation.pronamespace = format.relnamespace
            WHERE format.oid = {marker}
            {(descendants ? PostgresPlaybackScopeReady : "")})
        """;

    private static async Task RequirePostgresPlaybackAsync(KernelDb db, CancellationToken token)
    {
        var check = FormattableStringFactory.Create($$"""
            SELECT {{PostgresPlaybackReady("marker.tableoid", db.UsesRootPlaybackDescendants)}} AS "Value"
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

    internal bool SupportsDescendantPlayback()
    {
        using var db = create();
        return db.UsesRootPlaybackDescendants;
    }

    private sealed record PlaybackRoot(int Partition, string Actor, long RetainedAfter, long Latest, long? LastRecordedAt);
    private sealed record PlaybackOrdinal(long Ordinal);
    private sealed record PlaybackInvalid(long Sequence);
    private sealed record PlaybackSource(string? Stamp);
    private sealed record PlaybackRow(string Activity, long Ordinal, long Sequence, long Revision, int CommitOrdinal,
        string Observation, long RecordedAt);
    private const string PlaybackRowsSql = """
        SELECT "Activity", "Ordinal", "Sequence", "Revision", "CommitOrdinal", "Observation", "RecordedAt"
        FROM "FoundationJournalPlayback" WHERE "Partition" = {0} AND "Actor" = {1}
        """;

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
        if (query.IncludeDescendants && query.Activity is null)
            throw new ArgumentException("Descendant playback requires an exact activity scope.", nameof(query));
        if (query.IncludeDescendants && !db.UsesRootPlaybackDescendants)
            throw new NotSupportedException("This store does not carry the descendant playback projection.");
        var physicalSource = PostgresPlaybackSource(db.UsesRootPlaybackDescendants);
        string? source = null;
        if (db.UsesSqliteRootRuntime)
            return await ReadSqliteRootSnapshotAsync(db, ReadAsync, token);
        if (!postgres) throw new NotSupportedException("The playback projection requires a physical foundation store.");

        await using var snapshot = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, token);
        var marker = await PostgresRootAdmission.PinPlaybackReadSourcesAsync(db, token);
        var stamp = FormattableStringFactory.Create($$"""
            SELECT CASE WHEN marker.tableoid = {0}::oid THEN {{physicalSource}} END AS "Stamp"
            FROM "KernelStoreVersions" marker WHERE marker."Id" = {1}
            """, marker, StoreVersionEntity.MarkerId);
        source = (await db.Database.SqlQuery<PlaybackSource>(stamp).SingleOrDefaultAsync(token))?.Stamp
            ?? throw new StoreFormatException("The playback read changed its physical source.");
        var readAt = await db.Database.SqlQueryRaw<long>($"""SELECT {PostgresJournalClock} AS "Value" """).SingleAsync(token);
        var page = await ReadAsync(readAt);
        await snapshot.CommitAsync(token);
        return page;

        async Task<ActorPlaybackPage?> ReadAsync(long asOf)
        {
            var roots = await ReadRowsAsync<PlaybackRoot>($"""
                /* playback root bounds */
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
                """);
            var root = roots.SingleOrDefault();
            if (root is null) return null;
            var upper = query.UpperSequence ?? root.Latest;
            ActorPlaybackPage Result(ActorPlaybackAvailability state, string reason) =>
                new(state, reason, root.Latest, upper, root.RetainedAfter, root.LastRecordedAt, null, null, [], asOf);
            if (upper > root.Latest || query.RetainedAfterSequence is { } retained && retained != root.RetainedAfter)
                return Result(ActorPlaybackAvailability.Invalidated, "retained-range-changed");
            var invalid = await ReadRowsAsync<PlaybackInvalid>($"""
                SELECT "Sequence" FROM "FoundationJournalPlaybackInvalid"
                WHERE "Partition" = {root.Partition} AND "Actor" = {root.Actor}
                    AND "Sequence" > {root.RetainedAfter} AND "Sequence" <= {upper}
                ORDER BY "Sequence" LIMIT 1
                """);
            if (invalid.Length != 0)
                return Result(ActorPlaybackAvailability.Unavailable, "occurrence-observation-unknown");

            var scope = query.Activity;
            PlaybackOrdinal? scopeStart = null;
            if (scope is not null)
            {
                var scoped = await FindMemberAsync(scope);
                if (scoped is null) return Result(ActorPlaybackAvailability.Unavailable, "scope-unavailable");
                if (!query.IncludeDescendants)
                {
                    if (query.Anchor is { } exact && exact != scope)
                        return Result(ActorPlaybackAvailability.TargetUnavailable, "anchor-outside-scope");
                    if (query.Position is > 0)
                        return Result(ActorPlaybackAvailability.TargetUnavailable, "position-outside-range");
                    return Available(1, 0, [Entry(scoped, 0, null, null)]);
                }

                var unknown = await ReadRowsAsync<PlaybackInvalid>($"""
                    SELECT "Sequence" FROM "FoundationJournalPlaybackScopes"
                    WHERE "Partition" = {root.Partition} AND "Actor" = {root.Actor} AND "Known" = 0
                        AND "Sequence" > {root.RetainedAfter} AND "Sequence" <= {upper}
                    ORDER BY "Sequence" LIMIT 1
                    """);
                if (unknown.Length != 0)
                    return Result(ActorPlaybackAvailability.Unavailable, "scope-ancestry-unknown");
                scopeStart = await FindScopedOrdinalAsync(scope, scoped)
                    ?? throw new StoreFormatException("The playback index is missing its scope's first occurrence.");
            }

            var first = scopeStart ?? (await ReadRowsAsync<PlaybackOrdinal>(PlaybackOrdinalBound(root, scope, upper, last: false))).SingleOrDefault();
            if (first is null)
                return Result(ActorPlaybackAvailability.Unavailable, "occurrence-recording-unavailable");
            var last = (await ReadRowsAsync<PlaybackOrdinal>(PlaybackOrdinalBound(root, scope, upper, last: true))).Single().Ordinal;
            var count = checked(last - first.Ordinal + 1);
            var selected = query.Position ?? 0;
            if (query.Anchor is { } anchor)
            {
                var target = await FindMemberAsync(anchor);
                if (target is null) return Result(ActorPlaybackAvailability.TargetUnavailable, "anchor-outside-range");
                var ordinal = scope is null ? new PlaybackOrdinal(target.Ordinal) : await FindScopedOrdinalAsync(scope, target);
                if (ordinal is null) return Result(ActorPlaybackAvailability.TargetUnavailable, "anchor-outside-scope");
                selected = ordinal.Ordinal - first.Ordinal;
            }
            if (selected < 0 || selected >= count)
                return Result(ActorPlaybackAvailability.TargetUnavailable, "position-outside-range");
            var start = first.Ordinal + Math.Min(Math.Max(0, count - query.Limit), Math.Max(0, selected - query.Limit / 2));
            var end = Math.Min(last, start + query.Limit - 1);
            var from = Math.Max(first.Ordinal, start - 1);
            var through = Math.Min(last, end + 1);
            var rankRange = PlaybackRankRange(root, scope, from, through, upper, query.Limit + 2);
            var rows = await ReadRowsAsync<PlaybackRow>(rankRange);
            if (rows.Length != through - from + 1 || rows.Where((row, index) => row.Ordinal != from + index).Any())
                throw new StoreFormatException("The playback index has an incomplete occurrence range.");
            var items = rows.Select((row, index) => Entry(row, row.Ordinal - first.Ordinal,
                    index == 0 ? null : rows[index - 1].Activity, index == rows.Length - 1 ? null : rows[index + 1].Activity))
                .Where((_, index) => rows[index].Ordinal >= start && rows[index].Ordinal <= end).ToArray();
            return Available(count, selected, items);

            // Seek the exact activity key first. Testing retained bounds on that one row keeps a
            // provider from choosing the range index and scanning the frozen prefix for an anchor.
            async Task<PlaybackRow?> FindMemberAsync(string activity) =>
                (await ReadRowsAsync<PlaybackRow>(FormattableStringFactory.Create(
                    "/* playback exact anchor */\n" + PlaybackRowsSql + """ AND "Activity" = {2} LIMIT 2""",
                    root.Partition, root.Actor, activity))).SingleOrDefault() is { } row &&
                    row.Sequence > root.RetainedAfter && row.Sequence <= upper ? row : null;

            async Task<PlaybackOrdinal?> FindScopedOrdinalAsync(string scopeId, PlaybackRow member) =>
                (await ReadRowsAsync<PlaybackOrdinal>($"""
                    SELECT "Ordinal" FROM "FoundationJournalPlaybackScopes" WHERE "Partition" = {root.Partition}
                        AND "Actor" = {root.Actor} AND "Scope" = {scopeId} AND "Activity" = {member.Activity}
                        AND "Sequence" = {member.Sequence}
                    """)).SingleOrDefault();

            ActorPlaybackPage Available(long total, long target, IReadOnlyList<ActorPlaybackEntry> items) =>
                new(ActorPlaybackAvailability.Available, null, root.Latest, upper, root.RetainedAfter, root.LastRecordedAt,
                    total, target, items, asOf);
        }

        async Task<T[]> ReadRowsAsync<T>(FormattableString query) where T : class
        {
            if (source is null) return await db.Database.SqlQuery<T>(query).ToArrayAsync(token);
            // Every data statement checks the pinned identities. A schema rename can change
            // unqualified name resolution even while the transaction still holds table locks.
            var sourceParameter = "{" + query.ArgumentCount.ToString(CultureInfo.InvariantCulture) + "}";
            var guard = FormattableStringFactory.Create($$"""
                WITH physical AS MATERIALIZED (
                    SELECT {{physicalSource}} = {{sourceParameter}} AS "Matches"
                )
                SELECT CASE WHEN physical."Matches" THEN (
                    SELECT COALESCE(pg_catalog.json_agg(hit), '[]'::json)::text FROM ({{query.Format}}) hit
                ) END AS "Value" FROM physical
                """, [.. query.GetArguments(), source]);
            try
            {
                var payload = await db.Database.SqlQuery<string?>(guard).SingleAsync(token);
                return payload is null ? throw new StoreFormatException("The playback read changed its physical source.")
                    : JsonSerializer.Deserialize<T[]>(payload) ?? throw new StoreFormatException("The playback read returned invalid data.");
            }
            catch (PostgresException error) when (error.SqlState is "42P01" or "42703" or "3F000")
            {
                throw new StoreFormatException("The playback read changed its physical source.", error);
            }
        }

        static ActorPlaybackEntry Entry(PlaybackRow row, long position, string? previous, string? next) =>
            new(position, row.Sequence, row.Revision, row.CommitOrdinal, row.Observation, row.RecordedAt, previous, next);
    }
}
