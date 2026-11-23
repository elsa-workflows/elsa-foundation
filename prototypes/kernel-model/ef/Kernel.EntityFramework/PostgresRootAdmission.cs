using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;

namespace Kernel.EntityFramework;

/// <summary>Store operations for externally generated root starts in the single-host PostgreSQL preview.
/// The calling host supplies the tenant from its bound ingress scope. Schema installation is explicit.</summary>
internal sealed partial class PostgresRootAdmission(Func<KernelDb> create, TimeSpan deduplicationWindow)
{
    internal sealed record Receipt(string Reference, bool Duplicate);
    private sealed record Issuer(byte[] Key, int Partitions);
    private sealed record StoredRoot(string Id, StartSpec Spec, bool EscapedInput, bool ExplicitZeroDue = false);
    private const string CreatorPrefix = "!kernel-root/1:";

    internal static bool IsIssuedReferenceShape(string? reference) =>
        FoundationRootCreator.IsGeneratedReference(reference);

    internal async Task<FoundationReadCursor> OpenJournalCursorAsync(CancellationToken cancellationToken)
    {
        await using var db = create();
        await EfStore.RequireStoreVersionsAsync(db, cancellationToken);
        var issuerKey = await db.Database.SqlQueryRaw<byte[]>("""SELECT "Key" AS "Value" FROM "FoundationRootIssuer" """)
            .SingleAsync(cancellationToken);
        return new(HMACSHA256.HashData(issuerKey, "elsa-preview/journal-cursor/1"u8));
    }

    internal async Task InstallAsync(CancellationToken cancellationToken = default)
    {
        await using var db = create();
        if (!db.Database.IsNpgsql()) throw new InvalidOperationException("Root admission requires PostgreSQL.");
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await EfStore.RequireStoreVersionsAsync(db, cancellationToken);
        // A second Embedded process can open the same preview database while the first is installing.
        await db.Database.ExecuteSqlRawAsync("""
            SELECT pg_advisory_xact_lock(48602345, 1);
            CREATE TABLE IF NOT EXISTS "FoundationRootIssuer" (
                "Singleton" boolean PRIMARY KEY CHECK ("Singleton"), "Key" bytea NOT NULL,
                "Partitions" integer NOT NULL CHECK ("Partitions" > 0));
            CREATE TABLE IF NOT EXISTS "FoundationRootBirths" (
                "Reference" text COLLATE "C" PRIMARY KEY, "Tenant" text COLLATE "C" NOT NULL,
                "Actor" text COLLATE "C" NOT NULL, "Partition" integer NOT NULL REFERENCES "Partitions" ("Id"),
                "Creation" bigint NOT NULL, "Creator" text NOT NULL, CHECK (length("Tenant") BETWEEN 1 AND 64 AND "Tenant" !~ '[^a-z0-9-]'),
                "Fingerprint" text NOT NULL, UNIQUE ("Actor", "Creation"));
            CREATE TABLE IF NOT EXISTS "FoundationRootReceipts" (
                "Tenant" text COLLATE "C" NOT NULL, "Operation" text COLLATE "C" NOT NULL,
                "Reference" text COLLATE "C" NOT NULL REFERENCES "FoundationRootBirths" ("Reference") DEFERRABLE INITIALLY DEFERRED,
                "Until" bigint NOT NULL, PRIMARY KEY ("Tenant", "Operation"));
            CREATE INDEX IF NOT EXISTS "IX_FoundationRootReceipts_Until" ON "FoundationRootReceipts" ("Until");
            """, cancellationToken);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO "FoundationRootIssuer" SELECT true, {RandomNumberGenerator.GetBytes(32)}, count(*)::integer FROM "Partitions"
            ON CONFLICT DO NOTHING
            """, cancellationToken);
        await EfStore.InstallFirstRootCheckpointsAsync(db, cancellationToken);
        await EfStore.InstallRootActorListAsync(db, cancellationToken);
        if (db.UsesRootSelfProtocol)
            await EfStore.InstallRootSelfAsync(db, cancellationToken);
        await InstallDirectReceiptsAsync(db, cancellationToken);
        if (db.UsesRootOperatorProtocol)
            await InstallOperatorReceiptsAsync(db, cancellationToken);
        var placementMatches = await db.Database.SqlQueryRaw<bool>("""
            SELECT i."Partitions" = count(p."Id") AND min(p."Id") = 0 AND max(p."Id") = i."Partitions" - 1 AS "Value"
            FROM "FoundationRootIssuer" i CROSS JOIN "Partitions" p GROUP BY i."Partitions"
            """).SingleAsync(cancellationToken);
        if (!placementMatches) throw new InvalidOperationException("The installed root placement no longer matches the virtual partitions.");
        await transaction.CommitAsync(cancellationToken);
    }

    internal async Task<Receipt> AdmitAsync(string tenant, string operation, StartSpec spec, long? due = null,
        TimeSpan? keepFor = null, CancellationToken cancellationToken = default)
    {
        if (!FoundationMetadataLimits.IsOutsideMessageId(operation) || spec is { Parent: not null } or { Creation: not null } ||
            spec.ParentCreation != 0 || !FoundationMetadataLimits.FitsName(spec.Model))
            throw new ArgumentException("Invalid root creator metadata.");
        FoundationMetadataLimits.RequireContent(spec.Definition, nameof(spec), FoundationMetadataLimits.TypeDataBytes);
        FoundationMetadataLimits.RequireContent(spec.Input, nameof(spec), FoundationMetadataLimits.StartContentBytes);
        var window = keepFor is { } requested && requested < deduplicationWindow ? requested : deduplicationWindow;
        ArgumentOutOfRangeException.ThrowIfLessThan(window, TimeSpan.Zero);

        var digest = FoundationRootCreator.OperationDigest(operation);
        var name = tenant + "_" + Guid.NewGuid().ToString("N");
        await using var db = create();
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, cancellationToken);
        var issuer = await db.Database.SqlQueryRaw<Issuer>("""SELECT "Key", "Partitions" FROM "FoundationRootIssuer" """)
            .SingleAsync(cancellationToken);
        var actor = InstanceRef.In(InstanceRef.Spread(name, issuer.Partitions), name);
        var creator = FoundationRootCreator.Create(actor, operation, digest, spec, due, null);
        var reference = FoundationRootCreator.ReferenceFor(issuer.Key, tenant, new(actor.Id, 1));
        var windowMs = (long)window.TotalMilliseconds;
        var changed = await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "FoundationRootReceipts" AS r ("Tenant","Operation","Reference","Until")
            VALUES ({tenant}, {digest}, {reference}, floor(extract(epoch from clock_timestamp()) * 1000)::bigint + {windowMs})
            ON CONFLICT ("Tenant","Operation") DO UPDATE SET "Reference" = EXCLUDED."Reference",
                "Until" = floor(extract(epoch from clock_timestamp()) * 1000)::bigint + {windowMs}
            WHERE r."Until" < floor(extract(epoch from clock_timestamp()) * 1000)::bigint
            """, cancellationToken);
        if (changed == 0)
        {
            var existing = await db.Database.SqlQuery<string>($"""
                SELECT "Reference" AS "Value" FROM "FoundationRootReceipts" WHERE "Tenant" = {tenant} AND "Operation" = {digest}
                """).SingleAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new(existing, true);
        }

        var fingerprint = CreationProtocol.Fingerprint(creator.Start, creator.Kind, creator.Payload)!;
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "FoundationRootBirths" ("Tenant","Reference","Actor","Partition","Creation","Creator","Fingerprint")
            VALUES ({tenant}, {reference}, {actor.Id}, {actor.Partition}, 1, {creator.InboxKey.Id}, {fingerprint})
            """, cancellationToken);
        // Raw PostgreSQL text avoids JSON expansion for large quoted or international inputs. Only a
        // value that PostgreSQL text cannot represent travels in the reversible JSON envelope.
        var escaped = spec.Input is { } input && (input.Contains('\0') || DurableKeyJson.HasUnpairedSurrogate(input));
        var payload = escaped ? null : spec.Input;
        var storedSpec = escaped ? creator.Start! : creator.Start! with { Input = null };
        var header = CreatorPrefix + DurableKeyJson.Write(new StoredRoot(creator.Id, storedSpec, escaped, due == 0));
        // Reserve the 24 KiB fixed frame even for the exceptional encoded representation.
        if (Encoding.UTF8.GetByteCount(header) + (payload is null ? 0 : Encoding.UTF8.GetByteCount(payload)) > 16 * 1024 * 1024 - 24 * 1024)
            throw new MetadataLimitExceeded(nameof(spec), 16 * 1024 * 1024 - 24 * 1024);
        db.Messages.Add(new MessageEntity
        {
            Id = creator.InboxKey.Id, Target = actor.Id, Partition = actor.Partition,
            Kind = creator.Kind, Due = creator.Due ?? 0,
            Payload = payload, Start = header
        });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(reference, false);
    }

    internal async Task<CreationAddress?> ResolveAsync(string tenant, string reference, CancellationToken cancellationToken = default)
    {
        await using var db = create();
        return await db.Database.SqlQuery<CreationAddress>($"""
            SELECT "Actor" AS "Id", "Creation" FROM "FoundationRootBirths" WHERE "Tenant" = {tenant} AND "Reference" = {reference} AND "Creation" = 1
            """).SingleOrDefaultAsync(cancellationToken);
    }

    internal async Task<int> SweepReceiptsAsync(CancellationToken cancellationToken = default)
    {
        await using var db = create();
        return await db.Database.ExecuteSqlRawAsync("""
            DELETE FROM "FoundationRootReceipts" WHERE "Until" < floor(extract(epoch from clock_timestamp()) * 1000)::bigint
            """, cancellationToken);
    }

    internal static bool IsStoredCreator(string? start) => start?.StartsWith(CreatorPrefix, StringComparison.Ordinal) == true;

    internal async Task<Message?> ReadPendingCreatorAsync(string tenant, string reference, CancellationToken cancellationToken = default)
    {
        await using var db = create();
        var row = await db.Messages.FromSqlInterpolated($"""
            SELECT m.* FROM "Messages" m JOIN "FoundationRootBirths" b
            ON b."Creator" = m."Id" AND m."InboxScope" = 0 AND b."Actor" = m."Target"
            WHERE b."Tenant" = {tenant} AND b."Reference" = {reference} AND NOT m."Done"
            """).AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        return row is null ? null : DecodeCreator(row);
    }

    internal static Message DecodeCreator(MessageEntity row)
    {
        if (row.JobGeneration != 0 || !IsStoredCreator(row.Start) || row.Kind != Kinds.Start || row.Id != "root:" + row.Target || row.Sender is not null)
            throw new StoreException(Failure.Definite, "Invalid PostgreSQL generated root creator.");
        var stored = DurableKeyJson.Read<StoredRoot>(row.Start![CreatorPrefix.Length..]);
        var digest = FoundationRootCreator.OperationDigest(stored.Id);
        if (stored.Spec is not { Creation: { Generation: 1, Previous: 0 } creation, Parent: null } || creation.Intent != digest ||
            (stored.EscapedInput ? row.Payload is not null : stored.Spec.Input is not null))
            throw new StoreException(Failure.Definite, "Invalid PostgreSQL generated root creation or input.");
        var spec = stored.Spec with { Input = stored.EscapedInput ? stored.Spec.Input : row.Payload };
        return FoundationRootCreator.Create(new(row.Target), stored.Id, digest, spec, row.Due != 0 || stored.ExplicitZeroDue ? row.Due : null, row.ReplyTo);
    }
}
