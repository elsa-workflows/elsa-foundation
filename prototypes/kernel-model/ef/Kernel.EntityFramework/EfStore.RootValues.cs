using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;

namespace Kernel.EntityFramework;

public sealed partial class EfStore
{
    internal bool RootValuesEnabled { get; private set; }

    internal static Task InstallRootValuesAsync(KernelDb db, CancellationToken token) =>
        db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS "FoundationRootValues" (
                "Actor" text COLLATE "C" NOT NULL REFERENCES "Instances" ("Id") ON DELETE CASCADE,
                "Creation" bigint NOT NULL,
                "Digest" text COLLATE "C" NOT NULL CHECK (length("Digest") = 64 AND "Digest" !~ '[^0-9A-F]'),
                "Payload" bytea NOT NULL,
                PRIMARY KEY ("Actor", "Creation", "Digest"),
                FOREIGN KEY ("Actor", "Creation") REFERENCES "FoundationRootBirths" ("Actor", "Creation"));
            """, token);

    internal async Task EnableRootValuesAsync(CancellationToken token)
    {
        await using var db = create();
        if (!db.UsesRootValueProtocol || !db.Database.IsNpgsql())
            throw new StoreFormatException("Generated-root values require the PostgreSQL qualification profile.");
        await RequireStoreVersionsAsync(db, token);
        if (!await db.Database.SqlQueryRaw<bool>("""
            SELECT to_regclass('"FoundationRootValues"') IS NOT NULL AS "Value"
            """).SingleAsync(token))
            throw new StoreFormatException("The generated-root value table is missing.");
        lock (_workerBinding)
        {
            if (_ordinaryRoots is not null)
                throw new InvalidOperationException("The PostgreSQL owner is already bound.");
            RootValuesEnabled = true;
        }
    }

    internal async ValueTask<byte[]?> ReadRootValueAsync(FoundationDispatchHeader header, CreationAddress actor,
        FoundationValueReference reference, CancellationToken token)
    {
        if (!RootValuesEnabled) throw new StoreFormatException("The PostgreSQL owner has no value capability.");
        if (reference.Sha256 is not { Length: 64 } digest ||
            digest.AsSpan().IndexOfAnyExcept("0123456789ABCDEF") >= 0 || reference.Length < 0) return null;
        await using var db = create();
        await RequireStoreVersionsAsync(db, token);
        var payload = await db.Database.SqlQuery<byte[]>($"""
            SELECT v."Payload" AS "Value" FROM "FoundationRootValues" v
            JOIN "FoundationRootBirths" b ON b."Actor" = v."Actor" AND b."Creation" = v."Creation"
            JOIN "Instances" i ON i."Id" = b."Actor" AND i."Partition" = b."Partition"
                AND i."FoundationReference" = b."Reference" AND i."FoundationTenant" = b."Tenant"
            WHERE b."Reference" = {header.Reference} AND b."Tenant" = {header.Tenant}
                AND v."Actor" = {actor.Id} AND v."Creation" = {actor.Creation} AND NOT i."Retired"
                AND v."Digest" = {digest} AND octet_length(v."Payload") = {reference.Length}
            """).SingleOrDefaultAsync(token);
        if (payload is not null && Convert.ToHexString(SHA256.HashData(payload)) != digest)
            throw new StoreException(Failure.Definite, "The persisted value does not match its digest.");
        return payload;
    }

    private static async Task<bool> WriteRootValuesAsync(KernelDb db, CreationAddress actor,
        IReadOnlyList<FoundationValue> values, CancellationToken token)
    {
        if (!db.UsesRootValueProtocol) return false;
        foreach (var value in values)
        {
            var payload = value.Read();
            var digest = value.Reference.Sha256;
            var matches = await db.Database.SqlQuery<bool>($"""
                WITH proposed AS (
                    SELECT {actor.Id}::text AS "Actor", {actor.Creation}::bigint AS "Creation",
                        {digest}::text AS "Digest", {payload}::bytea AS "Payload"
                ), inserted AS (
                    INSERT INTO "FoundationRootValues" ("Actor", "Creation", "Digest", "Payload")
                    SELECT "Actor", "Creation", "Digest", "Payload" FROM proposed
                    ON CONFLICT ("Actor", "Creation", "Digest") DO NOTHING RETURNING 1
                )
                SELECT EXISTS (SELECT 1 FROM inserted) OR EXISTS (
                    SELECT 1 FROM "FoundationRootValues" v JOIN proposed p ON v."Actor" = p."Actor"
                        AND v."Creation" = p."Creation" AND v."Digest" = p."Digest" AND v."Payload" = p."Payload") AS "Value"
                """).ToListAsync(token);
            if (!matches.Single()) throw new StoreException(Failure.Definite, "A stored digest must describe an immutable value.");
        }
        return true;
    }
}
