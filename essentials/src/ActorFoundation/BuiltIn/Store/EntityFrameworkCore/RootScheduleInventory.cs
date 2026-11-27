using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;

namespace Kernel.EntityFramework;

/// <summary>A schedule's host-owned key, qualified by the tenant on every store access.</summary>
internal readonly record struct ScheduleRootKey(string Definition, string Key)
{
    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(Definition) || string.IsNullOrWhiteSpace(Key) ||
            !FoundationMetadataLimits.FitsLiveWaitName(Definition) || !FoundationMetadataLimits.FitsLiveWaitName(Key))
            throw new ArgumentException("Invalid schedule root identity.");
    }

    // Creator operations are local to each generated root. The inventory row owns deduplication by key.
    internal string CreatorOperation => "schedule/" + FoundationRootCreator.OperationDigest(Key);
}

internal static class RootScheduleInventory
{
    internal const string Table = "FoundationScheduleRoots";
    internal const string DefinitionIndex = "IX_FoundationScheduleRoots_Definition";

    internal static string Columns(bool sqlite)
    {
        var text = sqlite ? "TEXT COLLATE BINARY" : """text COLLATE "C" """;
        return $"""
            "Tenant" {text} NOT NULL,
            "Key" {text} NOT NULL,
            "Definition" {text} NOT NULL,
            "Reference" {text} NOT NULL UNIQUE REFERENCES "FoundationRootBirths" ("Reference") DEFERRABLE INITIALLY DEFERRED,
            "ActorType" {text} NOT NULL,
            PRIMARY KEY ("Tenant", "Key")
            """;
    }

    internal const string IndexedColumns = """
        ("Tenant", "Definition", "Key")
        """;

    internal static async Task InstallPostgresAsync(KernelDb db, CancellationToken token)
    {
        var sql = $"""
            CREATE TABLE IF NOT EXISTS "{Table}" ({Columns(sqlite: false)});
            CREATE INDEX IF NOT EXISTS "{DefinitionIndex}" ON "{Table}" {IndexedColumns};
            """;
        await db.Database.ExecuteSqlRawAsync(sql, token);
        await RequirePostgresDefinitionsAsync(db, token);
    }

    internal static async Task RequirePostgresDefinitionsAsync(KernelDb db, CancellationToken token)
    {
        var valid = await db.Database.SqlQueryRaw<bool>("""
            WITH relation AS (
                SELECT relation.oid, relation.relnamespace, birth.oid AS "Births"
                FROM pg_catalog.pg_class relation
                JOIN pg_catalog.pg_class birth ON birth.oid = pg_catalog.to_regclass('"FoundationRootBirths"')
                    AND birth.relnamespace = relation.relnamespace
                WHERE relation.oid = pg_catalog.to_regclass('"FoundationScheduleRoots"') AND relation.relkind = 'r'
            ), key_columns AS (
                SELECT array_agg(attribute.attname::text ORDER BY attribute.attnum) AS "Names"
                FROM relation JOIN pg_catalog.pg_attribute attribute ON attribute.attrelid = relation.oid
                WHERE attribute.attnum > 0 AND NOT attribute.attisdropped AND attribute.attnotnull
                    AND attribute.atttypid = 'pg_catalog.text'::regtype
                    AND attribute.attcollation = pg_catalog.to_regcollation('pg_catalog."C"')
            )
            SELECT EXISTS (
                SELECT 1 FROM relation, key_columns
                WHERE key_columns."Names" = ARRAY['Tenant', 'Key', 'Definition', 'Reference', 'ActorType']
                    AND (SELECT count(*) FROM pg_catalog.pg_constraint constraint_shape
                        WHERE constraint_shape.conrelid = relation.oid AND constraint_shape.convalidated AND (
                            (contype = 'p' AND conkey = ARRAY[1, 2]::smallint[] AND NOT condeferrable)
                            OR (contype = 'u' AND conkey = ARRAY[4]::smallint[] AND NOT condeferrable)
                            OR (contype = 'f' AND conkey = ARRAY[4]::smallint[] AND confrelid = relation."Births"
                                AND confkey = ARRAY[1]::smallint[] AND condeferrable AND condeferred))) = 3
                    AND EXISTS (
                        SELECT 1 FROM pg_catalog.pg_index i
                        JOIN pg_catalog.pg_class ix ON ix.oid = i.indexrelid
                        JOIN pg_catalog.pg_am method ON method.oid = ix.relam
                        WHERE i.indrelid = relation.oid AND ix.relname = 'IX_FoundationScheduleRoots_Definition'
                            AND ix.relnamespace = relation.relnamespace AND method.amname = 'btree'
                            AND i.indisvalid AND i.indisready AND i.indpred IS NULL AND i.indexprs IS NULL
                            AND i.indkey::text = '1 3 2' AND i.indoption::text = '0 0 0'
                            AND i.indcollation[0] = pg_catalog.to_regcollation('pg_catalog."C"')
                            AND i.indcollation[1] = i.indcollation[0] AND i.indcollation[2] = i.indcollation[0]
                            AND NOT EXISTS (SELECT 1 FROM unnest(i.indclass) AS op(oid)
                                JOIN pg_catalog.pg_opclass class ON class.oid = op.oid
                                WHERE class.opcname <> 'text_ops' OR class.opcnamespace <> 'pg_catalog'::regnamespace))
            ) AS "Value"
            """).SingleAsync(token);
        if (!valid) throw new StoreFormatException("The PostgreSQL schedule inventory definitions are incomplete.");
    }

    private sealed record Entry(bool SourceReady, string? Key, string? Definition, string? Reference, string? ActorType, string? BirthTenant,
        string? BirthActor, int? BirthPartition, long? BirthCreation, string? BirthCreator);
    internal sealed record Root(string Key, string Reference);
    internal sealed record Page(IReadOnlyList<Root> Roots, string? Next);

    private const string Projection = """
        schedule."Key", schedule."Definition", schedule."Reference", schedule."ActorType", birth."Tenant" AS "BirthTenant",
        birth."Actor" AS "BirthActor", birth."Partition" AS "BirthPartition", birth."Creation" AS "BirthCreation", birth."Creator" AS "BirthCreator"
        """;

    internal static async Task<string?> FindAsync(KernelDb db, string tenant, ScheduleRootKey key, string actorType, CancellationToken token,
        Func<FormattableString, FormattableString>? bindSource = null)
    {
        FormattableString query = FormattableStringFactory.Create($$"""
            WITH source AS MATERIALIZED (/*schedule-source*/)
            SELECT source."SourceReady", {{Projection}}
            FROM source
            LEFT JOIN "FoundationScheduleRoots" schedule
                ON source."SourceReady" AND schedule."Tenant" = {0} AND schedule."Key" = {1}
            LEFT JOIN "FoundationRootBirths" birth ON birth."Reference" = schedule."Reference"
            """, tenant, key.Key);
        var entry = await db.Database.SqlQuery<Entry>(Bind(query, bindSource)).SingleAsync(token);
        if (!entry.SourceReady) throw new StoreFormatException("The pinned schedule store changed during admission.");
        if (entry.Key is null) return null;
        if (entry.Key != key.Key)
            throw new StoreFormatException("A schedule root differs from its exact inventory identity.");
        return RequireReference(entry, tenant, key.Definition, actorType);
    }

    internal static async Task<Page> ListAsync(KernelDb db, string tenant, string definition, string actorType, string? after, int limit,
        CancellationToken token, Func<FormattableString, FormattableString>? bindSource = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(limit, 256);
        if (!FoundationMetadataLimits.FitsLiveWaitName(definition) || after is not null && !FoundationMetadataLimits.FitsLiveWaitName(after))
            throw new ArgumentException("Invalid schedule root inventory query.");
        var firstKey = after ?? "";
        FormattableString query = FormattableStringFactory.Create($$"""
            WITH source AS MATERIALIZED (/*schedule-source*/), page AS MATERIALIZED (
                SELECT {{Projection}}
                FROM "FoundationScheduleRoots" schedule
                LEFT JOIN "FoundationRootBirths" birth ON birth."Reference" = schedule."Reference"
                WHERE (SELECT "SourceReady" FROM source) AND schedule."Tenant" = {0} AND schedule."Definition" = {1}
                    AND schedule."Key" > {2}
                ORDER BY schedule."Key" LIMIT {3}
            )
            SELECT source."SourceReady", page.* FROM source LEFT JOIN page ON true ORDER BY page."Key"
            """, tenant, definition, firstKey, limit + 1);
        var rows = await db.Database.SqlQuery<Entry>(Bind(query, bindSource)).ToListAsync(token);
        if (rows.Any(row => !row.SourceReady)) throw new StoreFormatException("The pinned schedule store changed during inventory read.");
        if (rows is [{ Key: null }]) return new([], null);
        var entries = rows.Select(row => new Root(row.Key!, RequireReference(row, tenant, definition, actorType))).ToArray();
        return new(entries.Take(limit).ToArray(), entries.Length > limit ? entries[limit - 1].Key : null);
    }

    private static FormattableString Bind(FormattableString query, Func<FormattableString, FormattableString>? bindSource) =>
        bindSource?.Invoke(query) ?? FormattableStringFactory.Create(
            query.Format.Replace("/*schedule-source*/", """SELECT true AS "SourceReady" """, StringComparison.Ordinal), query.GetArguments());

    private static string RequireReference(Entry entry, string tenant, string definition, string actorType)
    {
        if (entry.BirthTenant != tenant || entry.Definition != definition || entry.ActorType != actorType ||
            !FoundationRootCreator.IsGeneratedReference(entry.Reference) ||
            entry.BirthCreation != 1 || entry.BirthPartition is not { } partition || entry.BirthActor is not { } actor ||
            entry.BirthCreator != "root:" + actor)
            throw new StoreFormatException("A schedule root differs from its exact inventory identity.");
        var prefix = InstanceRef.In(partition, ScheduleNamePrefix(tenant)).Id;
        if (!actor.StartsWith(prefix, StringComparison.Ordinal) || !Guid.TryParseExact(actor.AsSpan(prefix.Length), "N", out _))
            throw new StoreFormatException("A schedule root differs from its exact inventory identity.");
        return entry.Reference!;
    }

    internal static Message BuildCreator(ScheduleRootKey key, string tenant, StartSpec specification, byte[] issuer, int partitions,
        out string reference)
    {
        var name = ScheduleNamePrefix(tenant) + Guid.NewGuid().ToString("N");
        var actor = InstanceRef.In(InstanceRef.Spread(name, partitions), name);
        reference = FoundationRootCreator.ReferenceFor(issuer, tenant, new(actor.Id, 1));
        var operation = key.CreatorOperation;
        return FoundationRootCreator.Create(actor, operation, FoundationRootCreator.OperationDigest(operation), specification, null, null);
    }

    internal static string RequireActorType(string? configuredActorType)
    {
        if (string.IsNullOrWhiteSpace(configuredActorType) || !FoundationMetadataLimits.FitsName(configuredActorType))
            throw new NotSupportedException("Schedule roots require a host-bound actor type.");
        return configuredActorType;
    }

    internal static StartSpec CreatorSpecification(ScheduleRootKey key, string actorType, string typeData)
    {
        ArgumentNullException.ThrowIfNull(typeData);
        var result = new StartSpec(actorType, typeData, History: History.Minimal);
        RootAdmissionWire.RequireCreator(key.CreatorOperation, result);
        return result;
    }

    private static string ScheduleNamePrefix(string tenant) => tenant + "_schedule_";
}
