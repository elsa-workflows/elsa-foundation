using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;

namespace Kernel.EntityFramework;

/// <summary>Occurrence membership begins with an actor's durable birth and settles with its retirement tombstone.</summary>
internal static class RootScheduleLineage
{
    internal const string Table = "FoundationScheduleLineage";
    internal const string CohortIndex = "IX_FoundationScheduleLineage_Cohort";

    internal const string IndexedColumns = """
        ("Tenant", "RootReference", "Retired")
        """;

    internal static string Columns(bool sqlite)
    {
        var text = sqlite ? "TEXT" : """text COLLATE "C" """;
        var boolean = sqlite ? "INTEGER" : "boolean";
        var provesBoolean = sqlite ? """CHECK ("Retired" IN (0, 1))""" : "";
        return $$"""
            "Tenant" {{text}} NOT NULL,
            "Reference" {{text}} NOT NULL PRIMARY KEY REFERENCES "FoundationRootBirths" ("Reference"),
            "RootReference" {{text}} NOT NULL REFERENCES "FoundationRootBirths" ("Reference"),
            "Retired" {{boolean}} NOT NULL DEFAULT false {{provesBoolean}}
            """;
    }

    internal static async Task InstallPostgresAsync(KernelDb db, CancellationToken token)
    {
        await db.Database.ExecuteSqlRawAsync($"""
            CREATE TABLE IF NOT EXISTS "{Table}" ({Columns(sqlite: false)});
            CREATE INDEX IF NOT EXISTS "{CohortIndex}" ON "{Table}" {IndexedColumns};
            """, token);
        var ready = await db.Database.SqlQueryRaw<bool>($"""
            SELECT {PostgresReady("marker.oid")} AS "Value"
            FROM pg_catalog.pg_class marker WHERE marker.oid = pg_catalog.to_regclass('"KernelStoreVersions"')
            """).SingleAsync(token);
        if (!ready) throw new StoreFormatException("The PostgreSQL schedule lineage definitions are incomplete.");
    }

    internal static string PostgresReady(string markerOid) => $$"""
        EXISTS (
            SELECT 1 FROM pg_catalog.pg_class lineage
            JOIN pg_catalog.pg_class marker ON marker.oid = {{markerOid}} AND marker.relnamespace = lineage.relnamespace
            JOIN pg_catalog.pg_class births ON births.oid = pg_catalog.to_regclass('"FoundationRootBirths"')
                AND births.relnamespace = lineage.relnamespace
            WHERE lineage.oid = pg_catalog.to_regclass('"FoundationScheduleLineage"') AND lineage.relkind = 'r'
                AND (SELECT array_agg(attribute.attname::text ORDER BY attribute.attnum)
                    FROM pg_catalog.pg_attribute attribute
                    WHERE attribute.attrelid = lineage.oid AND attribute.attnum BETWEEN 1 AND 4
                        AND NOT attribute.attisdropped AND attribute.attnotnull
                        AND (attribute.attnum < 4 AND attribute.atttypid = 'pg_catalog.text'::regtype
                            AND attribute.attcollation = pg_catalog.to_regcollation('pg_catalog."C"')
                            OR attribute.attnum = 4 AND attribute.atttypid = 'pg_catalog.bool'::regtype)
                    ) = ARRAY['Tenant', 'Reference', 'RootReference', 'Retired']
                AND (SELECT count(DISTINCT (shape.contype, shape.conkey)) FROM pg_catalog.pg_constraint shape
                    WHERE shape.conrelid = lineage.oid AND shape.convalidated AND NOT shape.condeferrable AND (
                        shape.contype = 'p' AND shape.conkey = ARRAY[2]::smallint[]
                        OR shape.contype = 'f' AND shape.conkey IN (ARRAY[2]::smallint[], ARRAY[3]::smallint[])
                            AND shape.confrelid = births.oid AND shape.confkey = ARRAY[1]::smallint[]
                            AND shape.confdeltype = 'a')) = 3
                AND EXISTS (
                    SELECT 1 FROM pg_catalog.pg_index indexed
                    JOIN pg_catalog.pg_class candidate ON candidate.oid = indexed.indexrelid AND candidate.relnamespace = lineage.relnamespace
                    JOIN pg_catalog.pg_am method ON method.oid = candidate.relam AND method.amname = 'btree'
                    WHERE indexed.indrelid = lineage.oid AND candidate.relname = 'IX_FoundationScheduleLineage_Cohort'
                        AND indexed.indisvalid AND indexed.indisready AND indexed.indpred IS NULL AND indexed.indexprs IS NULL
                        AND indexed.indkey::text = '1 3 4' AND indexed.indoption::text = '0 0 0'
                        AND indexed.indcollation[0] = pg_catalog.to_regcollation('pg_catalog."C"')
                        AND indexed.indcollation[1] = indexed.indcollation[0] AND indexed.indcollation[2] = 0
                        AND ARRAY(SELECT classes.opcname::text FROM unnest(indexed.indclass) WITH ORDINALITY entries(oid, position)
                            JOIN pg_catalog.pg_opclass classes ON classes.oid = entries.oid
                                AND classes.opcnamespace = 'pg_catalog'::regnamespace ORDER BY entries.position)
                            = ARRAY['text_ops', 'text_ops', 'bool_ops']))
        """;

    internal static async Task<bool> BindChildrenAsync(KernelDb db, string tenant, string parentReference,
        string[] childReferences, CancellationToken token)
    {
        // Root children share their parent's partition even when detached. The owner fence covers the parent
        // checkpoint, the queued creators, and every child added to the cohort.
        var parent = await ReadMemberAsync(db, tenant, parentReference, token);
        if (parent is null) return true;
        if (parent.Retired != false) return false;

        FormattableString insert = db.UsesSqliteRootRuntime
            ? (FormattableString)$"""
                WITH source AS MATERIALIZED (/*lineage-source*/)
                INSERT INTO "FoundationScheduleLineage" ("Tenant", "Reference", "RootReference")
                SELECT {tenant}, value, {parent.RootReference} FROM json_each({DurableKeyJson.Write(childReferences)}), source
                WHERE source."Ready"
                """
            : $"""
                WITH source AS MATERIALIZED (/*lineage-source*/)
                INSERT INTO "FoundationScheduleLineage" ("Tenant", "Reference", "RootReference")
                SELECT {tenant}, reference, {parent.RootReference} FROM unnest({childReferences}::text[]) AS children(reference), source
                WHERE source."Ready"
                """;
        return await db.Database.ExecuteSqlInterpolatedAsync(BindCommitSource(db, insert), token) == childReferences.Length;
    }

    internal static async Task<bool> MarkRetiredAsync(KernelDb db, string tenant, string reference, string actor, int partition,
        long position, CancellationToken token)
    {
        var member = await ReadMemberAsync(db, tenant, reference, token);
        if (member is null) return true;
        if (member.Retired != false) return false;

        FormattableString update = $"""
            WITH source AS MATERIALIZED (/*lineage-source*/)
            UPDATE "FoundationScheduleLineage" SET "Retired" = true
            WHERE "Tenant" = {tenant} AND "Reference" = {reference} AND NOT "Retired"
                AND (SELECT "Ready" FROM source)
                AND EXISTS (SELECT 1 FROM "Journal" WHERE "Instance" = {actor} AND "Partition" = {partition}
                    AND "Position" = {position} AND "Revision" = -1 AND "Entries" = '["delete"]')
            """;
        return await db.Database.ExecuteSqlInterpolatedAsync(BindCommitSource(db, update), token) == 1;
    }

    private sealed record Member(bool SourceReady, string? Tenant, string? RootReference, bool? Retired);

    private static async Task<Member?> ReadMemberAsync(KernelDb db, string tenant, string reference, CancellationToken token)
    {
        FormattableString query = $"""
            WITH source AS MATERIALIZED (/*lineage-source*/)
            SELECT source."Ready" AS "SourceReady", member."Tenant", member."RootReference", member."Retired"
            FROM source LEFT JOIN "FoundationScheduleLineage" member ON source."Ready" AND member."Reference" = {reference}
            """;
        var member = await db.Database.SqlQuery<Member>(BindCommitSource(db, query)).SingleAsync(token);
        if (!member.SourceReady || member.RootReference is not null && member.Tenant != tenant)
            throw new StoreFormatException("The owned schedule lineage source does not match its actor.");
        return member.RootReference is null ? null : member;
    }

    private static FormattableString BindCommitSource(KernelDb db, FormattableString query)
    {
        if (db.UsesSqliteRootRuntime)
            return FormattableStringFactory.Create(query.Format.Replace("/*lineage-source*/", """SELECT true AS "Ready" """,
                StringComparison.Ordinal), query.GetArguments());
        var marker = db.ScheduleLineageCommitMarker ?? throw new StoreFormatException("The PostgreSQL lineage owner has no pinned store.");
        var source = $$"""
            SELECT EXISTS (
                SELECT 1 FROM pg_catalog.pg_class marker
                JOIN pg_catalog.pg_class lineage ON lineage.oid = pg_catalog.to_regclass('"FoundationScheduleLineage"')
                    AND lineage.relnamespace = marker.relnamespace
                JOIN pg_catalog.pg_class journal ON journal.oid = pg_catalog.to_regclass('"Journal"')
                    AND journal.relnamespace = marker.relnamespace
                WHERE marker.oid = pg_catalog.to_regclass('"KernelStoreVersions"') AND marker.oid::bigint = {{{query.ArgumentCount}}})
            AS "Ready"
            """;
        return FormattableStringFactory.Create(query.Format.Replace("/*lineage-source*/", source, StringComparison.Ordinal),
            [.. query.GetArguments(), (long)marker]);
    }

    internal static FormattableString RequireRootMembershipForProof(FormattableString query, string tenant, string reference) =>
        FormattableStringFactory.Create(query.Format.Replace("/*cohort-proof*/", $$"""
            AND EXISTS (SELECT 1 FROM "FoundationScheduleLineage"
                WHERE "Tenant" = {{{query.ArgumentCount}}} AND "Reference" = {{{query.ArgumentCount + 1}}}
                    AND "RootReference" = {{{query.ArgumentCount + 1}}} AND "Retired")
            AND NOT EXISTS (SELECT 1 FROM "FoundationScheduleLineage"
                WHERE "Tenant" = {{{query.ArgumentCount}}} AND "RootReference" = {{{query.ArgumentCount + 1}}} AND NOT "Retired")
            """, StringComparison.Ordinal), [.. query.GetArguments(), tenant, reference]);
}
