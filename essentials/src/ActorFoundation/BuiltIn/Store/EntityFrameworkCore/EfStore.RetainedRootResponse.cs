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

    internal static async Task InstallRetainedRootResponsesAsync(KernelDb db, CancellationToken token)
    {
        await db.Database.ExecuteSqlRawAsync(FormattableString.Invariant($"""
            CREATE TABLE IF NOT EXISTS "FoundationRootResponses" (
                "Reference" text COLLATE "C" PRIMARY KEY REFERENCES "FoundationRootBirths" ("Reference"),
                "ResponseKey" text COLLATE "C" NOT NULL CHECK (length("ResponseKey") = 64),
                "CapturedAt" bigint NOT NULL, "Present" boolean NOT NULL,
                "Payload" bytea CHECK (octet_length("Payload") <= {FoundationMetadataLimits.MessageContentBytes}
                    AND ("Present" OR "Payload" IS NULL)))
            """), token);
        await RequirePostgresRetainedRootResponsesAsync(db, token);
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
        if (!await db.Database.SqlQueryRaw<bool>("""
            SELECT EXISTS (
                SELECT 1 FROM pg_catalog.pg_class response JOIN pg_catalog.pg_class birth
                  ON birth.oid = pg_catalog.to_regclass('"FoundationRootBirths"') AND birth.relnamespace = response.relnamespace
                JOIN pg_catalog.pg_constraint source ON source.conrelid = response.oid AND source.confrelid = birth.oid
                  AND source.contype = 'f' AND source.convalidated AND source.confdeltype = 'a'
                  AND source.conkey = ARRAY[1]::smallint[] AND source.confkey = ARRAY[1]::smallint[]
                WHERE response.oid = pg_catalog.to_regclass('"FoundationRootResponses"') AND response.relkind = 'r'
                    AND response.relnamespace = (SELECT relnamespace FROM pg_catalog.pg_class
                        WHERE oid = pg_catalog.to_regclass('"Instances"')))
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
        CancellationToken token)
    {
        if (!db.UsesRetainedRootResponse || db.Database.CurrentTransaction is null)
            return null;
        if (db.Database.IsNpgsql()) await RequirePostgresRetainedRootResponsesAsync(db, token);
        return await db.Database.SqlQuery<Response>($"""
            SELECT response."CapturedAt", response."Present", response."Payload"
            FROM "FoundationRootResponses" response JOIN "FoundationRootBirths" birth USING ("Reference")
            WHERE birth."Tenant" = {birth.Tenant} AND birth."Reference" = {birth.Reference} AND birth."Actor" = {birth.Actor}
                AND birth."Partition" = {birth.Partition} AND birth."Fingerprint" = {birth.Fingerprint}
                AND birth."Creation" = 1 AND response."ResponseKey" = {responseKey}
            """).SingleOrDefaultAsync(token);
    }
}
