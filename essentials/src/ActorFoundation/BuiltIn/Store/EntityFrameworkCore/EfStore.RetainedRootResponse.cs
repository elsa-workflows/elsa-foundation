using Microsoft.EntityFrameworkCore;
using Npgsql;
using Response = Kernel.Foundation.Hosting.PreparedRootResponse;

namespace Kernel.EntityFramework;

public sealed partial class EfStore
{
    private static SqliteDefinition SqliteRetainedRootResponseDefinition() => Table("FoundationRootResponses", FormattableString.Invariant($"""
        "Reference" TEXT PRIMARY KEY REFERENCES "FoundationRootBirths" ("Reference"),
        "ResponseKey" TEXT NOT NULL CHECK (length("ResponseKey") = 64),
        "CapturedAt" INTEGER NOT NULL, "Present" INTEGER NOT NULL CHECK ("Present" IN (0, 1)),
        "Payload" BLOB CHECK (length("Payload") <= {FoundationMetadataLimits.MessageContentBytes}
            AND ("Present" OR "Payload" IS NULL))
        """));

    internal const int RetainedResponseAuthorityMarkerId = 5;

    private sealed record RetainedResponseInstallState(bool HasResponse, bool HasRootSchema, bool HasMarker, bool ExactMarker);

    // The base marker exists before root installation. This marker commits with the response relation.
    internal static async Task<bool> MayCreatePostgresRetainedResponseAuthorityAsync(KernelDb db, CancellationToken token)
    {
        var format = db.RequiredFormat;
        var state = await db.Database.SqlQuery<RetainedResponseInstallState>($"""
            SELECT pg_catalog.to_regclass('"FoundationRootResponses"') IS NOT NULL AS "HasResponse",
                (pg_catalog.to_regclass('"FoundationRootIssuer"') IS NOT NULL OR
                    pg_catalog.to_regclass('"FoundationRootBirths"') IS NOT NULL OR
                    pg_catalog.to_regclass('"FoundationRootReceipts"') IS NOT NULL) AS "HasRootSchema",
                EXISTS (SELECT 1 FROM ONLY "KernelStoreVersions" WHERE "Id" = {RetainedResponseAuthorityMarkerId}) AS "HasMarker",
                EXISTS (SELECT 1 FROM ONLY "KernelStoreVersions" WHERE "Id" = {RetainedResponseAuthorityMarkerId}
                    AND "StoreSchema" = {format.StoreSchema} AND "MessageProtocol" = {format.MessageProtocol}
                    AND "JournalCodec" = {format.JournalCodec}) AS "ExactMarker"
            """).SingleAsync(token);
        if (state is { HasMarker: true, ExactMarker: false })
            throw new StoreFormatException("The PostgreSQL retained root response authority belongs to another store format.");
        if (state.HasResponse) return false;
        if (state is { HasRootSchema: false, HasMarker: false }) return true;
        throw new StoreFormatException("The initialized PostgreSQL store is missing its retained root response relation.");
    }

    private static string PostgresRetainedResponseTable(string table) => FormattableString.Invariant($"""
        CREATE TABLE "{table}" (
            "Reference" text COLLATE "C" PRIMARY KEY REFERENCES "FoundationRootBirths" ("Reference"),
            "ResponseKey" text COLLATE "C" NOT NULL CHECK (length("ResponseKey") = 64),
            "CapturedAt" bigint NOT NULL, "Present" boolean NOT NULL,
            "Payload" bytea CHECK (octet_length("Payload") <= {FoundationMetadataLimits.MessageContentBytes}
                AND ("Present" OR "Payload" IS NULL)))
        """);

    internal static async Task InstallRetainedRootResponsesAsync(KernelDb db, bool createAuthority, CancellationToken token)
    {
        if (createAuthority)
            await db.Database.ExecuteSqlRawAsync(PostgresRetainedResponseTable("FoundationRootResponses"), token);
        var format = db.RequiredFormat;
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "KernelStoreVersions" ("Id", "StoreSchema", "MessageProtocol", "JournalCodec")
            VALUES ({RetainedResponseAuthorityMarkerId}, {format.StoreSchema}, {format.MessageProtocol}, {format.JournalCodec})
            ON CONFLICT ("Id") DO NOTHING
            """, token);
        await RequirePostgresRetainedRootResponsesAsync(db, token);

        await RequirePostgresTableDefinitionsAsync(db, "FoundationRootResponses", PostgresRetainedResponseTable, token);
    }

    internal static async Task RequirePostgresTableDefinitionsAsync(KernelDb db, string installed,
        Func<string, string> definition, CancellationToken token)
    {
        var transaction = db.Database.CurrentTransaction ?? throw new InvalidOperationException("Definition verification requires the install transaction.");
        const string savepoint = "kernel_root_table_definition";
        var twin = installed + "_" + Guid.NewGuid().ToString("N");
        await transaction.CreateSavepointAsync(savepoint, token);
        await db.Database.ExecuteSqlRawAsync(definition(twin), token);
        var same = await db.Database.SqlQuery<bool>($"""
            WITH pair(expected, installed) AS (
                VALUES (pg_catalog.to_regclass(pg_catalog.quote_ident({twin})),
                        pg_catalog.to_regclass(pg_catalog.quote_ident({installed})))
            ), shape AS (
                SELECT relation.oid, relation.relnamespace, relation.relhasrules,
                    array(SELECT ROW(a.attnum, a.attname, a.atttypid, a.attnotnull, a.attcollation, a.atttypmod, a.attidentity,
                            a.attgenerated, pg_catalog.pg_get_expr(d.adbin, d.adrelid))::text
                        FROM pg_catalog.pg_attribute a
                        LEFT JOIN pg_catalog.pg_attrdef d ON d.adrelid = a.attrelid AND d.adnum = a.attnum
                        WHERE a.attrelid = relation.oid AND a.attnum > 0 AND NOT a.attisdropped
                        ORDER BY a.attnum) AS columns,
                    array(SELECT ROW(c.convalidated, pg_catalog.pg_get_constraintdef(c.oid))::text
                        FROM pg_catalog.pg_constraint c WHERE c.conrelid = relation.oid ORDER BY 1) AS constraints,
                    array(SELECT ROW(i.indisunique, i.indisprimary, i.indisvalid, i.indisready, i.indimmediate,
                            substring(pg_catalog.pg_get_indexdef(i.indexrelid) FROM ' USING .*$'))::text
                        FROM pg_catalog.pg_index i WHERE i.indrelid = relation.oid ORDER BY 1) AS indexes,
                    EXISTS (SELECT 1 FROM pg_catalog.pg_trigger t WHERE t.tgrelid = relation.oid AND NOT t.tgisinternal) AS extra_triggers
                FROM pg_catalog.pg_class relation
                WHERE relation.oid IN (SELECT expected FROM pair UNION SELECT installed FROM pair)
            )
            SELECT EXISTS (
                SELECT 1 FROM pair JOIN shape e ON e.oid = pair.expected JOIN shape i ON i.oid = pair.installed
                WHERE i.relnamespace = e.relnamespace AND NOT i.relhasrules AND NOT i.extra_triggers
                    AND i.columns = e.columns AND i.constraints = e.constraints AND i.indexes = e.indexes)
            AS "Value"
            """).SingleAsync(token);
        await transaction.RollbackToSavepointAsync(savepoint, token);
        if (!same) throw new StoreFormatException($"The installed PostgreSQL {installed} definitions differ from the selected profile.");
    }

    private static async Task RequirePostgresRetainedRootResponsesAsync(KernelDb db, CancellationToken token)
    {
        try
        {
            await db.Database.ExecuteSqlRawAsync("""
                LOCK TABLE ONLY "FoundationRootResponses", ONLY "FoundationRootBirths" IN ACCESS SHARE MODE
                """, token);
        }
        catch (PostgresException error) when (error.SqlState is PostgresErrorCodes.UndefinedTable or PostgresErrorCodes.WrongObjectType)
        {
            throw new StoreFormatException("The retained root response relation is missing from the installed root schema.", error);
        }
        var format = db.RequiredFormat;
        if (!await db.Database.SqlQuery<bool>($"""
            SELECT EXISTS (
                SELECT 1 FROM pg_catalog.pg_class response JOIN pg_catalog.pg_class birth
                  ON birth.oid = pg_catalog.to_regclass('"FoundationRootBirths"') AND birth.relnamespace = response.relnamespace
                  AND birth.relkind = 'r'
                JOIN pg_catalog.pg_class instance ON instance.oid = pg_catalog.to_regclass('"Instances"')
                  AND instance.relnamespace = response.relnamespace AND instance.relkind = 'r'
                JOIN pg_catalog.pg_class marker ON marker.oid = pg_catalog.to_regclass('"KernelStoreVersions"')
                  AND marker.relnamespace = response.relnamespace AND marker.relkind = 'r'
                JOIN pg_catalog.pg_constraint source ON source.conrelid = response.oid AND source.confrelid = birth.oid
                  AND source.contype = 'f' AND source.convalidated AND source.confdeltype = 'a'
                  AND source.conkey = ARRAY[1]::smallint[] AND source.confkey = ARRAY[1]::smallint[]
                WHERE response.oid = pg_catalog.to_regclass('"FoundationRootResponses"') AND response.relkind = 'r'
                    AND response.relpersistence = 'p' AND NOT response.relrowsecurity AND NOT response.relforcerowsecurity
                    AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_inherits h WHERE response.oid IN (h.inhparent, h.inhrelid))
                    AND EXISTS (SELECT 1 FROM ONLY "KernelStoreVersions" version WHERE version.tableoid = marker.oid
                        AND version."Id" = {RetainedResponseAuthorityMarkerId} AND version."StoreSchema" = {format.StoreSchema}
                        AND version."MessageProtocol" = {format.MessageProtocol} AND version."JournalCodec" = {format.JournalCodec}))
            AS "Value"
            """).SingleAsync(token))
            throw new StoreFormatException("The retained root response relation does not belong to the installed root source.");
    }

    private sealed record RetainedRootResponseCheckpoint(WorkerRoot Root, long Revision, RetainedRootResponse Response, bool Retired = false)
        : QualifiedRootCheckpoint(0)
    {
        internal override async Task<bool> BindCheckpointAsync(KernelDb db, CancellationToken token)
        {
            if (!db.UsesRetainedRootResponse) return false;
            if (db.Database.IsNpgsql()) await RequirePostgresRetainedRootResponsesAsync(db, token);
            // The same fenced transaction must have already written this actor checkpoint.
            if (!await db.Database.SqlQuery<bool>($"""
                SELECT EXISTS (
                    SELECT 1 FROM "Instances" current
                    JOIN "FoundationRootBirths" birth ON birth."Reference" = current."FoundationReference"
                    WHERE birth."Tenant" = {Root.Tenant} AND birth."Reference" = {Root.Reference} AND
                        birth."Actor" = {Root.Actor} AND birth."Partition" = {Root.Partition} AND birth."Creation" = 1
                        AND current."Id" = birth."Actor" AND current."Partition" = birth."Partition"
                        AND current."FoundationTenant" = birth."Tenant" AND current."Revision" = {Revision} AND current."Retired" = {Retired})
                AS "Value"
                """).SingleAsync(token))
                return false;

            var inserted = await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "FoundationRootResponses" ("Reference", "ResponseKey", "CapturedAt", "Present", "Payload")
                VALUES ({Root.Reference}, {Response.Key}, {Response.CapturedAt}, {Response.Present}, {Response.Payload})
                ON CONFLICT ("Reference") DO NOTHING
                """, token);
            return inserted == 1 || await db.Database.SqlQuery<bool>($"""
                SELECT EXISTS (SELECT 1 FROM "FoundationRootResponses" WHERE "Reference" = {Root.Reference}
                    AND "ResponseKey" = {Response.Key}) AS "Value"
                """).SingleAsync(token);
        }
    }

    internal static async Task<Response?> ReadRetainedRootResponseAsync(KernelDb db, PreparedRootStart birth, string responseKey,
        CancellationToken token, Func<FormattableString, FormattableString>? bindSource = null)
    {
        if (!db.UsesRetainedRootResponse || db.Database.CurrentTransaction is null)
            return null;
        if (db.Database.IsNpgsql()) await RequirePostgresRetainedRootResponsesAsync(db, token);
        if (!await RootPreparedStarts.HasAcceptedBirthAsync(db, birth, token, bindSource))
            return null;
        return await db.Database.SqlQuery<Response>($"""
            SELECT "CapturedAt", "Present", "Payload" FROM "FoundationRootResponses"
            WHERE "Reference" = {birth.Reference} AND "ResponseKey" = {responseKey}
            """).SingleOrDefaultAsync(token);
    }
}
