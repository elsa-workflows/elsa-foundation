using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Kernel.EntityFramework;

/// <summary>Store operations for externally generated root starts in the single-host PostgreSQL preview.
/// The calling host supplies the tenant from its bound ingress scope. Schema installation is explicit.</summary>
internal sealed partial class PostgresRootAdmission(Func<KernelDb> create, TimeSpan deduplicationWindow, bool lockOrdinaryWrites = false)
{
    internal sealed record Receipt(string Reference, bool Duplicate);
    private sealed record Issuer(byte[] Key, int Partitions);
    private sealed record StartWrite(bool Claimed, bool Enqueued);
    private sealed record StoredRoot(string Id, StartSpec Spec, bool EscapedInput, bool ExplicitZeroDue = false);
    private const string CreatorPrefix = "!kernel-root/1:";
    // Bound the formatting and payload cost of a duplicate that loses the batched receipt claim.
    private const int BatchedStartInputCodeUnits = 8 * 1024;
    private const string ClaimReceiptSql = """
        INSERT INTO "FoundationRootReceipts" AS r ("Tenant","Operation","Reference","Until")
        SELECT {0}, {1}, {2}, floor(extract(epoch from clock_timestamp()) * 1000)::bigint + {3}
        WHERE NOT {4}
        ON CONFLICT ("Tenant","Operation") DO UPDATE SET "Reference" = EXCLUDED."Reference",
            "Until" = floor(extract(epoch from clock_timestamp()) * 1000)::bigint + {3}
        WHERE r."Until" < floor(extract(epoch from clock_timestamp()) * 1000)::bigint
        """;

    internal static bool IsIssuedReferenceShape(string? reference) =>
        FoundationRootCreator.IsGeneratedReference(reference);

    internal static void RequireActorType(string actorType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorType);
        if (!FoundationMetadataLimits.FitsName(actorType))
            throw new ArgumentException("Invalid PostgreSQL root actor type.", nameof(actorType));
    }

    internal async Task<FoundationReadCursor> OpenJournalCursorAsync(CancellationToken cancellationToken)
    {
        await using var db = create();
        return await OpenJournalCursorAsync(db, cancellationToken);
    }

    internal static async Task<FoundationReadCursor> OpenJournalCursorAsync(KernelDb db, CancellationToken cancellationToken)
    {
        await EfStore.RequireStoreVersionsAsync(db, cancellationToken);
        var issuerKey = await db.Database.SqlQueryRaw<byte[]>("""SELECT "Key" AS "Value" FROM "FoundationRootIssuer" """)
            .SingleAsync(cancellationToken);
        return new(HMACSHA256.HashData(issuerKey, "elsa-preview/journal-cursor/1"u8));
    }

    internal async Task InstallAsync(CancellationToken cancellationToken = default)
    {
        await using var db = create();
        await InstallAsync(db, cancellationToken);
    }

    internal static async Task InstallAsync(KernelDb db, CancellationToken cancellationToken, bool exclusiveFormat = false)
    {
        if (!db.Database.IsNpgsql()) throw new InvalidOperationException("Root admission requires PostgreSQL.");
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await EfStore.LockCompatibleStoreFormatAsync(db, cancellationToken, exclusiveFormat || db.UsesRootStableBody);
        await EfStore.RequirePreviewInstallSchemaAsync(db, cancellationToken);
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
        if (db.UsesRootStableBody)
            await db.Database.ExecuteSqlRawAsync("""
                ALTER TABLE "Instances" ADD COLUMN IF NOT EXISTS "FoundationStateWire" bytea
                """, cancellationToken);
        if (db.UsesRootValueProtocol)
            await EfStore.InstallRootValuesAsync(db, cancellationToken);
        await EfStore.InstallRootActorListAsync(db, cancellationToken);
        if (db.UsesRootStableBody)
            await EfStore.InstallRootTerminalMarksAsync(db, cancellationToken);
        if (db.UsesRootSelfProtocol)
            await EfStore.InstallRootSelfAsync(db, cancellationToken);
        await InstallDirectReceiptsAsync(db, cancellationToken);
        if (db.UsesRootOperatorProtocol)
            await InstallOperatorReceiptsAsync(db, cancellationToken);
        await ReadInstalledPartitionCountAsync(db, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    internal static async Task<int> ReadInstalledPartitionCountAsync(KernelDb db, CancellationToken cancellationToken)
    {
        var count = await db.Database.SqlQueryRaw<int>("""
            SELECT i."Partitions" AS "Value" FROM "FoundationRootIssuer" i CROSS JOIN "Partitions" p
            GROUP BY i."Partitions"
            HAVING i."Partitions" = count(p."Id") AND min(p."Id") = 0 AND max(p."Id") = i."Partitions" - 1
            """).SingleOrDefaultAsync(cancellationToken);
        if (count == 0) throw new InvalidOperationException("The installed root placement no longer matches the virtual partitions.");
        return count;
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
        var issuer = await ReadIssuerAsync(db, cancellationToken);
        var actor = InstanceRef.In(InstanceRef.Spread(name, issuer.Partitions), name);
        var reference = FoundationRootCreator.ReferenceFor(issuer.Key, tenant, new(actor.Id, 1));
        var windowMs = (long)window.TotalMilliseconds;
        var claimFirst = spec.Input is { Length: > BatchedStartInputCodeUnits };
        if (claimFirst && await db.Database.ExecuteSqlRawAsync(ClaimReceiptSql,
                new object[] { tenant, digest, reference, windowMs, false }, cancellationToken) == 0)
            return await Replay();

        var creator = FoundationRootCreator.Create(actor, operation, digest, spec, due, null);
        var fingerprint = CreationProtocol.Fingerprint(creator.Start, creator.Kind, creator.Payload)!;
        // Raw PostgreSQL text avoids JSON expansion for large quoted or international inputs. Only a
        // value that PostgreSQL text cannot represent travels in the reversible JSON envelope.
        var escaped = spec.Input is { } input && (input.Contains('\0') || DurableKeyJson.HasUnpairedSurrogate(input));
        var payload = escaped ? null : spec.Input;
        var storedSpec = escaped ? creator.Start! : creator.Start! with { Input = null };
        var header = CreatorPrefix + DurableKeyJson.Write(new StoredRoot(creator.Id, storedSpec, escaped, due == 0));
        const int contentLimit = 16 * 1024 * 1024 - 24 * 1024;
        var fits = Encoding.UTF8.GetByteCount(header) + (payload is null ? 0 : Encoding.UTF8.GetByteCount(payload)) <= contentLimit;

        // A live receipt wins even when the replay's encoded envelope is too large. When no receipt
        // wins, this transaction rolls back the claim if its creator cannot be stored.
        var command = """
            WITH receipt AS (
            """ + ClaimReceiptSql + """

                RETURNING "Reference"
            ), accepted AS (
                SELECT "Reference" FROM receipt
                UNION ALL SELECT {2} WHERE {4}
            ), birth AS (
                INSERT INTO "FoundationRootBirths" ("Tenant","Reference","Actor","Partition","Creation","Creator","Fingerprint")
                SELECT {0}, accepted."Reference", {5}, {6}, 1, {7}, {8}
                FROM accepted WHERE {9}
                RETURNING "Actor", "Partition", "Creator"
            ), creator AS (
                INSERT INTO "Messages" ("Id","InboxScope","Target","Partition","Kind","Due","Payload","Start","Done","Seen","Attempts","Trial","JobGeneration")
                SELECT birth."Creator", 0, birth."Actor", birth."Partition", {10}, {11}, {12}, {13}, false, 0, 0, 0, 0
                FROM birth
                RETURNING "Id"
            )
            SELECT EXISTS (SELECT 1 FROM accepted) AS "Claimed", EXISTS (SELECT 1 FROM creator) AS "Enqueued"
            """;
        var write = (await db.Database.SqlQueryRaw<StartWrite>(command, tenant, digest, reference, windowMs, claimFirst,
            actor.Id, actor.Partition, creator.InboxKey.Id, fingerprint, fits, creator.Kind, creator.Due ?? 0,
            (fits ? payload : null)!, fits ? header : "").ToListAsync(cancellationToken)).Single();
        if (!write.Claimed) return await Replay();
        if (!fits) throw new MetadataLimitExceeded(nameof(spec), contentLimit);
        if (!write.Enqueued)
            throw new InvalidOperationException("A generated root start must commit its creator with the receipt.");
        await transaction.CommitAsync(cancellationToken);
        return new(reference, false);

        async Task<Receipt> Replay()
        {
            var existing = await db.Database.SqlQuery<string>($"""
                SELECT "Reference" AS "Value" FROM "FoundationRootReceipts" WHERE "Tenant" = {tenant} AND "Operation" = {digest}
                """).SingleAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new(existing, true);
        }
    }

    private async Task<Issuer> ReadIssuerAsync(KernelDb db, CancellationToken token)
    {
        if (!lockOrdinaryWrites)
            return await db.Database.SqlQueryRaw<Issuer>("""SELECT "Key", "Partitions" FROM "FoundationRootIssuer" """).SingleAsync(token);

        var issuer = await ReadGuardedAdmissionAsync(db, new("""SELECT "Key", "Partitions" FROM "FoundationRootIssuer" """),
            static reader => new Issuer(reader.GetFieldValue<byte[]>(0), reader.GetInt32(1)), token);
        return issuer ?? throw new InvalidOperationException("The generated-root issuer is missing.");
    }

    private static async Task<TRow?> ReadGuardedAdmissionAsync<TRow>(KernelDb db, NpgsqlBatchCommand lookup,
        Func<NpgsqlDataReader, TRow> read, CancellationToken token) where TRow : class
    {
        var current = db.Database.CurrentTransaction!;
        try
        {
            // PostgreSQL executes the first statement before it parses or binds the second. The batch holds
            // the marker before touching either source table, matching the lock order of schema migration.
            var marker = new NpgsqlBatchCommand("""
                SELECT "StoreSchema", "MessageProtocol", "JournalCodec" FROM "KernelStoreVersions"
                WHERE "Id" = $1 FOR SHARE
                """);
            marker.Parameters.Add(new NpgsqlParameter<int> { TypedValue = StoreVersionEntity.MarkerId });
            var transaction = (NpgsqlTransaction)current.GetDbTransaction();
            await using var batch = new NpgsqlBatch(transaction.Connection, transaction) { BatchCommands = { marker, lookup } };
            if (db.Database.GetCommandTimeout() is { } timeout)
                batch.Timeout = timeout;
            await using var reader = await batch.ExecuteReaderAsync(token);
            if (!await reader.ReadAsync(token))
                throw new StoreFormatException("The kernel store version marker is missing.");
            EfStore.RequireSnapshotFormat(db.RequiredFormat, reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2));
            if (!await reader.NextResultAsync(token))
                throw new InvalidOperationException("The guarded root lookup did not return a result.");
            return await reader.ReadAsync(token) ? read(reader) : null;
        }
        catch (PostgresException error) when (error.SqlState is PostgresErrorCodes.UndefinedTable or PostgresErrorCodes.UndefinedColumn)
        {
            throw new StoreFormatException("The generated-root admission schema is unreadable.", error);
        }
        catch (PostgresException error) when (
            error.SqlState == PostgresErrorCodes.FeatureNotSupported ||
            error.SqlState.StartsWith("42", StringComparison.Ordinal) && error.SqlState != PostgresErrorCodes.InsufficientPrivilege)
        {
            // A shape error can surface while the batch is being read or drained, even on a cached plan.
            await current.RollbackAsync(token);
            await EfStore.RequireStoreVersionsAsync(db, token);
            throw;
        }
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
