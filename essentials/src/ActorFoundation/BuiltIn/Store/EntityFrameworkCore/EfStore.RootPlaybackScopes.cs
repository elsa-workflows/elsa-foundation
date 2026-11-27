using System.Runtime.CompilerServices;

namespace Kernel.EntityFramework;

public sealed partial class EfStore
{
    private const string PlaybackScopesName = "FoundationJournalPlaybackScopes";
    private const string PlaybackScopeKey = "kernel_playback_scope_key";
    private const string PlaybackScopeRank = "kernel_playback_scope_rank";
    private const string PlaybackScopeSequence = "kernel_playback_scope_sequence";
    private const string PlaybackScopeMemberIndex = "kernel_playback_scope_member";
    private const string PlaybackUnknownScopeIndex = "kernel_playback_scope_unknown";

    private static string PlaybackScopeColumns(string integer, string text) => $"""
        "Partition" integer NOT NULL, "Actor" {text} NOT NULL, "Scope" {text} NOT NULL, "Activity" {text} NOT NULL,
        "Ordinal" {integer} NOT NULL CHECK ("Ordinal" >= 0),
        "Sequence" {integer} NOT NULL CHECK ("Sequence" > 0),
        "Known" integer NOT NULL CHECK ("Known" = 1 OR ("Known" = 0 AND "Scope" = "Activity")),
        CONSTRAINT {PlaybackScopeKey} PRIMARY KEY ("Partition", "Actor", "Scope", "Activity"),
        CONSTRAINT {PlaybackScopeRank} UNIQUE ("Partition", "Actor", "Scope", "Ordinal"),
        CONSTRAINT {PlaybackScopeSequence} UNIQUE ("Partition", "Actor", "Scope", "Sequence"),
        FOREIGN KEY ("Partition", "Actor", "Activity")
            REFERENCES "{PlaybackName}" ("Partition", "Actor", "Activity") ON DELETE CASCADE
        """;

    private const string PlaybackScopeMemberColumns = """("Partition", "Actor", "Activity")""";
    private const string PlaybackUnknownScopeColumns = """("Partition", "Actor", "Sequence") WHERE "Known" = 0""";

    private static readonly string PostgresPlaybackScopes = $"""
        CREATE TABLE "{PlaybackScopesName}" ({PlaybackScopeColumns("bigint", """text COLLATE "C" """)});
        CREATE INDEX {PlaybackScopeMemberIndex} ON "{PlaybackScopesName}" {PlaybackScopeMemberColumns};
        CREATE INDEX {PlaybackUnknownScopeIndex} ON "{PlaybackScopesName}" {PlaybackUnknownScopeColumns};
        """;

    private static IEnumerable<SqliteDefinition> SqlitePlaybackScopes()
    {
        yield return Table(PlaybackScopesName, PlaybackScopeColumns("INTEGER", "TEXT COLLATE BINARY"));
        yield return Index(PlaybackScopeMemberIndex, PlaybackScopesName, PlaybackScopeMemberColumns);
        yield return Index(PlaybackUnknownScopeIndex, PlaybackScopesName, PlaybackUnknownScopeColumns);
    }

    // Each first observation commits its immutable path using only earlier observations of the same
    // actor. An unresolved or overlong path records an unknown self row. A later parent arrival cannot
    // make an older frozen window appear complete.
    private static string PlaybackScopeInsert(string initialParent, string ancestorParent) => $"""
        INSERT INTO "{PlaybackScopesName}" ("Partition", "Actor", "Scope", "Activity", "Ordinal", "Sequence", "Known")
        WITH RECURSIVE ancestry ("Activity", "Sequence", "Scope", "AncestorSequence", "Parent", "Depth") AS (
            SELECT p."Activity", p."Sequence", p."Activity", p."Sequence", {initialParent}, 0
            FROM "{PlaybackName}" p
            WHERE p."Partition" = NEW."Partition" AND p."Actor" = NEW."Actor" AND p."Position" = NEW."Position"
            UNION ALL
            SELECT a."Activity", a."Sequence", parent."Activity", parent."Sequence", {ancestorParent}, a."Depth" + 1
            FROM ancestry a JOIN "{PlaybackName}" parent ON parent."Partition" = NEW."Partition"
                AND parent."Actor" = NEW."Actor" AND parent."Activity" = a."Parent"
                AND parent."Sequence" < a."AncestorSequence"
            WHERE a."Depth" < 255
        ), verified AS (
            SELECT "Activity", MAX(CASE WHEN "Parent" IS NULL THEN 1 ELSE 0 END) AS "Known"
            FROM ancestry GROUP BY "Activity"
        )
        SELECT NEW."Partition", NEW."Actor", a."Scope", a."Activity",
            COALESCE((SELECT "Ordinal" FROM "{PlaybackScopesName}" prior
                WHERE prior."Partition" = NEW."Partition" AND prior."Actor" = NEW."Actor" AND prior."Scope" = a."Scope"
                ORDER BY "Ordinal" DESC LIMIT 1), -1) + ROW_NUMBER() OVER (PARTITION BY a."Scope" ORDER BY a."Sequence"),
            a."Sequence", v."Known"
        FROM ancestry a JOIN verified v ON v."Activity" = a."Activity"
        WHERE v."Known" = 1 OR a."Depth" = 0
        """;

    private static readonly string PostgresPlaybackScopeInsert = PlaybackScopeInsert(
        """(p."Observation"::json->>'Parent') COLLATE "C" """,
        """(parent."Observation"::json->>'Parent') COLLATE "C" """);

    private static readonly string SqlitePlaybackScopeInsert = PlaybackScopeInsert(
        """json_extract(p."Observation", '$.Parent') COLLATE BINARY""",
        """json_extract(parent."Observation", '$.Parent') COLLATE BINARY""");

    private static readonly string PostgresPlaybackScopeReady = $"""
        AND EXISTS (SELECT 1 FROM pg_catalog.pg_class scopes
                WHERE scopes.oid = pg_catalog.to_regclass('"{PlaybackScopesName}"')
                    AND scopes.relnamespace = format.relnamespace AND scopes.relkind = 'r'
                    AND (SELECT count(*) FROM pg_catalog.pg_index idx
                        JOIN pg_catalog.pg_class index_name ON index_name.oid = idx.indexrelid
                        WHERE idx.indrelid = scopes.oid AND idx.indisvalid AND idx.indisready AND index_name.relname IN
                        ('{PlaybackScopeKey}', '{PlaybackScopeRank}', '{PlaybackScopeSequence}',
                         '{PlaybackScopeMemberIndex}', '{PlaybackUnknownScopeIndex}')) = 5)
        AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_trigger legacy
            WHERE legacy.tgrelid = stamp.tgrelid AND legacy.tgname = '{PlaybackTrigger}'
                AND legacy.tgenabled IN ('O', 'A'))
        """;

    private const string ScopedPlaybackRowsSql = """
        /* playback scoped rank */
        SELECT p."Activity", member."Ordinal", member."Sequence", p."Revision", p."CommitOrdinal", p."Observation", p."RecordedAt"
        FROM "FoundationJournalPlaybackScopes" member JOIN "FoundationJournalPlayback" p
          ON p."Partition" = member."Partition" AND p."Actor" = member."Actor"
          AND p."Activity" = member."Activity" AND p."Sequence" = member."Sequence"
        WHERE member."Partition" = {0} AND member."Actor" = {1} AND member."Scope" = {2}
        """;

    private static FormattableString PlaybackOrdinalBound(PlaybackRoot root, string? scope, long upper, bool last)
    {
        var direction = last ? "DESC" : "ASC";
        return scope is null
            ? FormattableStringFactory.Create($$"""
                SELECT "Ordinal" FROM "FoundationJournalPlayback"
                WHERE "Partition" = {0} AND "Actor" = {1} AND "Sequence" > {2} AND "Sequence" <= {3}
                ORDER BY "Sequence" {{direction}} LIMIT 1
                """, root.Partition, root.Actor, root.RetainedAfter, upper)
            : FormattableStringFactory.Create($$"""
                /* playback scoped retained bound */
                SELECT "Ordinal" FROM "FoundationJournalPlaybackScopes"
                WHERE "Partition" = {0} AND "Actor" = {1} AND "Scope" = {2} AND "Sequence" > {3} AND "Sequence" <= {4}
                ORDER BY "Sequence" {{direction}} LIMIT 1
                """, root.Partition, root.Actor, scope, root.RetainedAfter, upper);
    }

    private static FormattableString PlaybackRankRange(PlaybackRoot root, string? scope, long from, long through, long upper, int limit) =>
        scope is null
            ? FormattableStringFactory.Create(PlaybackRowsSql + """

                AND "Ordinal" BETWEEN {2} AND {3} AND "Sequence" > {4} AND "Sequence" <= {5}
                ORDER BY "Ordinal" LIMIT {6}
                """, root.Partition, root.Actor, from, through, root.RetainedAfter, upper, limit)
            : FormattableStringFactory.Create(ScopedPlaybackRowsSql + """

                AND member."Ordinal" BETWEEN {3} AND {4} AND member."Sequence" > {5} AND member."Sequence" <= {6}
                ORDER BY member."Ordinal" LIMIT {7}
                """, root.Partition, root.Actor, scope, from, through, root.RetainedAfter, upper, limit);
}
