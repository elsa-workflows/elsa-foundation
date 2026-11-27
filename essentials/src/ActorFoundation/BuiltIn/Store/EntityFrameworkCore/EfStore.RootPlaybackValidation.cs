namespace Kernel.EntityFramework;

public sealed partial class EfStore
{
    // The owning transaction verifies its exact append independently of the projector. The row
    // may already have been retired in the same transaction, in which case its indexes cascade.
    private static string PostgresPlaybackValidationBody => ("\n" + $"""
        BEGIN
            IF NOT EXISTS (
                SELECT 1 FROM pg_catalog.pg_class selected
                WHERE selected.oid = TG_RELID
                    AND pg_catalog.to_regclass('"{SequencedJournalName}"') = TG_RELID
                    AND (SELECT count(*) FROM (VALUES ('"Journal"'), ('"{PlaybackName}"'),
                            ('"{PlaybackInvalidName}"'), ('"{PlaybackScopesName}"')) expected(name)
                        JOIN pg_catalog.pg_class source ON source.oid = pg_catalog.to_regclass(expected.name)
                            AND source.relnamespace = selected.relnamespace AND source.relkind = 'r') = 4
                    AND (NOT EXISTS (
                            SELECT 1 FROM "{SequencedJournalName}" live
                            WHERE live."Partition" = NEW."Partition" AND live."Position" = NEW."Position")
                        OR ({PostgresPlaybackAppendMatches}))
            ) THEN
                RAISE EXCEPTION 'The retained Playback projection does not match its journal append'
                    USING ERRCODE = '23514', CONSTRAINT = '{PlaybackScopeGuard}';
            END IF;
            RETURN NULL;
        END
        """ + "\n").ReplaceLineEndings("\n");

    private static string PostgresPlaybackValidation => $"""
        CREATE FUNCTION {PlaybackScopeGuard}() RETURNS trigger LANGUAGE plpgsql
            AS $fn${PostgresPlaybackValidationBody}$fn$;
        CREATE CONSTRAINT TRIGGER {PlaybackScopeGuard} AFTER INSERT ON "{SequencedJournalName}"
            DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION {PlaybackScopeGuard}();
        """;

    private static readonly Lazy<string> PostgresPlaybackValidatorDigest = new(() =>
        PostgresPlaybackDigest(PostgresPlaybackValidationBody));

    private static string PostgresPlaybackValidationReady => $"""
        EXISTS (SELECT 1 FROM pg_catalog.pg_trigger guard
            JOIN pg_catalog.pg_proc body ON body.oid = guard.tgfoid
                AND body.proname = guard.tgname AND body.pronamespace = format.relnamespace
            WHERE guard.tgrelid = stamp.tgrelid AND guard.tgname = '{PlaybackScopeGuard}'
                AND guard.tgenabled IN ('O', 'A') AND guard.tgtype = 5 AND NOT guard.tgisinternal
                AND guard.tgconstraint <> 0 AND guard.tgdeferrable AND guard.tginitdeferred
                AND guard.tgqual IS NULL AND guard.tgnargs = 0 AND body.proconfig IS NULL AND NOT body.prosecdef
                AND pg_catalog.encode(pg_catalog.sha256(pg_catalog.convert_to(body.prosrc, 'UTF8')), 'hex') =
                    '{PostgresPlaybackValidatorDigest.Value}')
        """;

    private static string PostgresPlaybackAppendMatches => $"""
        {PostgresPlaybackRowsMatch(PlaybackFirstRows(PostgresPlaybackObservations, bounded: true), $"""
            SELECT "Partition", "Actor", "Position", "Activity", "Ordinal", "Sequence", "Revision",
                "CommitOrdinal", "Observation", "RecordedAt"
            FROM "{PlaybackName}" WHERE "Partition" = NEW."Partition" AND "Position" = NEW."Position"
            """)}
        AND {PostgresPlaybackRowsMatch(PlaybackInvalidRows(PostgresPlaybackObservations), $"""
            SELECT "Partition", "Actor", "Position", "Sequence"
            FROM "{PlaybackInvalidName}" WHERE "Partition" = NEW."Partition" AND "Position" = NEW."Position"
            """)}
        AND {PostgresPlaybackRowsMatch(PostgresPlaybackScopeRows, $"""
            SELECT p."Partition", p."Actor", member."Scope", p."Activity", member."Ordinal", member."Sequence", member."Known"
            FROM "{PlaybackName}" p CROSS JOIN LATERAL (
                SELECT m."Scope", m."Ordinal", m."Sequence", m."Known" FROM "{PlaybackScopesName}" m
                WHERE m."Partition" = p."Partition" AND m."Actor" = p."Actor" AND m."Activity" = p."Activity"
                LIMIT 257
            ) member
            WHERE p."Partition" = NEW."Partition" AND p."Position" = NEW."Position"
            """)}
        """;

    // Compare complete values and multiplicities in both directions. The only inputs are the
    // inserted position, its fresh observations and at most 255 earlier ancestors per observation.
    private static string PostgresPlaybackRowsMatch(string expected, string actual) => $"""
        NOT EXISTS (
            WITH expected AS MATERIALIZED ({expected}), actual AS MATERIALIZED ({actual})
            (TABLE expected EXCEPT ALL TABLE actual) UNION ALL (TABLE actual EXCEPT ALL TABLE expected))
        """;

    private static readonly string PostgresPlaybackScopeLayout = $"""
        (SELECT count(*) FROM (VALUES
                ('{PlaybackScopeKey}', '1 2 3 4', true, true, NULL),
                ('{PlaybackScopeRank}', '1 2 3 5', true, false, NULL),
                ('{PlaybackScopeSequence}', '1 2 3 6', true, false, NULL),
                ('{PlaybackScopeMemberIndex}', '1 2 4', false, false, NULL),
                ('{PlaybackUnknownScopeIndex}', '1 2 6', false, false, '("Known" = 0)')
            ) expected(name, columns, unique_index, primary_index, predicate)
            JOIN pg_catalog.pg_class index_name ON index_name.relname = expected.name
                AND index_name.relnamespace = scopes.relnamespace
            JOIN pg_catalog.pg_index idx ON idx.indexrelid = index_name.oid AND idx.indrelid = scopes.oid
            JOIN pg_catalog.pg_am method ON method.oid = index_name.relam AND method.amname = 'btree'
            WHERE idx.indisvalid AND idx.indisready AND idx.indislive AND idx.indisunique = expected.unique_index
                AND idx.indisprimary = expected.primary_index AND idx.indkey::text = expected.columns
                AND idx.indnkeyatts = idx.indnatts
                AND pg_catalog.pg_get_expr(idx.indpred, idx.indrelid) IS NOT DISTINCT FROM expected.predicate) = 5
        AND EXISTS (SELECT 1 FROM pg_catalog.pg_constraint owner
            WHERE owner.conname = '{PlaybackScopeActivity}' AND owner.contype = 'f' AND owner.convalidated
                AND owner.conrelid = scopes.oid AND owner.confrelid = playback.oid
                AND owner.conkey = ARRAY[1, 2, 4]::smallint[] AND owner.confkey = ARRAY[1, 2, 4]::smallint[]
                AND owner.confdeltype = 'c' AND owner.confupdtype = 'a' AND NOT owner.condeferrable
                AND (SELECT count(*) FROM pg_catalog.pg_trigger check_trigger
                    WHERE check_trigger.tgconstraint = owner.oid AND check_trigger.tgisinternal
                        AND check_trigger.tgenabled IN ('O', 'A')) = 4)
        """;
}
