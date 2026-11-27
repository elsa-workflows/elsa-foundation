using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using Kernel.Foundation.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using static Kernel.EntityFramework.RootAdmissionWire;

namespace Kernel.EntityFramework;

/// <summary>Store operations for externally generated root starts in the single-host PostgreSQL preview.
/// The calling host supplies the tenant from its bound ingress scope. Schema installation is explicit.</summary>
internal sealed partial class PostgresRootAdmission(Func<KernelDb> create, TimeSpan deduplicationWindow, bool lockOrdinaryWrites = false,
    RootAdmissionPressure? pressure = null, string? scheduleActorType = null)
{
    internal RootAdmissionPressure? Pressure => pressure;

    internal bool UsesDatabaseFactory(Func<KernelDb> factory) => create == factory;

    internal sealed record Receipt(string Reference, bool Duplicate);
    private sealed record Issuer(byte[] Key, int Partitions);
    private sealed record PinnedRootSource(uint Marker, uint Namespace, Issuer? Issuer);
    private sealed record StartClaim(bool SourceReady, bool Claimed);
    private sealed record StartWrite(bool SourceReady, bool Claimed, bool Enqueued);
    private sealed record StartReceipt(bool SourceReady, string? Reference);
    // Bound the formatting and payload cost of a duplicate that loses the batched receipt claim.
    private const int BatchedStartInputCodeUnits = 8 * 1024;
    private const string ClaimReceiptSql = """
        INSERT INTO "FoundationRootReceipts" AS r ("Tenant","Operation","Reference","Until")
        SELECT {0}, {1}, {2}, floor(extract(epoch from clock_timestamp()) * 1000)::bigint + {3}
        WHERE NOT {4} AND (SELECT "SourceReady" FROM source)
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

    internal static Task<FoundationReadCursor> OpenJournalCursorAsync(KernelDb db, CancellationToken cancellationToken) =>
        OpenCursorAsync(db, RootReadIssuerSource, ReadIssuerMissing, cancellationToken);

    // Ingress opens on the strict admission source. Only reads may open while the source is renamed.
    internal static Task RequireIngressIssuerAsync(KernelDb db, CancellationToken cancellationToken) =>
        OpenCursorAsync(db, RootIssuerSource, RootIssuerMissing, cancellationToken);

    private static async Task<FoundationReadCursor> OpenCursorAsync(KernelDb db, string source, string missing,
        CancellationToken cancellationToken)
    {
        await EfStore.RequireStoreVersionsAsync(db, cancellationToken);
        try
        {
            return await ReadCursorAsync(db, source, missing, cancellationToken);
        }
        catch (PostgresException error) when (IsUnavailableSelectedSchema(error))
        {
            throw new StoreFormatException("The root read issuer schema is unreadable.", error);
        }
    }

    private const string RootIssuerSource = """
        FROM ONLY "FoundationRootIssuer" issuer
        JOIN pg_catalog.pg_class i ON i.oid = issuer.tableoid AND i.relkind = 'r'
        JOIN pg_catalog.pg_class source ON source.oid = pg_catalog.to_regclass('"Instances"')
            AND source.relnamespace = i.relnamespace AND source.relkind = 'r'
        JOIN pg_catalog.pg_class marker ON marker.oid = pg_catalog.to_regclass('"KernelStoreVersions"')
            AND marker.relnamespace = i.relnamespace AND marker.relkind = 'r'
        """;

    // Reads pin the issuer to the schema of the marker and the actor list, which every generated-root install
    // creates. Journal and inbox tails do not read the list, so a store installed before it existed is refused here
    // until the installer runs again. A checkpoint relation that resolves must share that schema, but a
    // renamed source is allowed: projections such as workflow history keep serving without it. A schema earlier
    // on the search path that holds all three relations is treated as the store; that is out of scope here.
    private const string RootReadIssuerSource = """
        FROM "FoundationRootIssuer" issuer
        JOIN pg_catalog.pg_class i ON i.oid = issuer.tableoid AND i.relkind = 'r'
        JOIN pg_catalog.pg_class marker ON marker.oid = pg_catalog.to_regclass('"KernelStoreVersions"')
            AND marker.relnamespace = i.relnamespace AND marker.relkind = 'r'
        JOIN pg_catalog.pg_class list ON list.oid = pg_catalog.to_regclass('"FoundationActorList"')
            AND list.relnamespace = i.relnamespace AND list.relkind = 'r'
        WHERE NOT EXISTS (SELECT 1 FROM pg_catalog.pg_class source
            WHERE source.oid = pg_catalog.to_regclass('"Instances"')
                AND (source.relnamespace <> i.relnamespace OR source.relkind <> 'r'))
        """;

    // Operator selections write, so they keep the strict admission source.
    private static Task<FoundationReadCursor> ReadSelectionCursorAsync(KernelDb db, CancellationToken cancellationToken) =>
        ReadCursorAsync(db, RootIssuerSource, RootIssuerMissing, cancellationToken);

    private static async Task<FoundationReadCursor> ReadCursorAsync(KernelDb db, string source, string missing,
        CancellationToken cancellationToken) =>
        new(HMACSHA256.HashData(await ReadIssuerKeyAsync(db, source, missing, cancellationToken), "elsa-preview/journal-cursor/1"u8));

    private const string RootIssuerMissing = "The root issuer is missing from the checkpoint's store schema.";
    private const string ReadIssuerMissing = "The root read issuer must share one schema with the store marker and actor list, " +
        "and an \"Instances\" relation that resolves must be a table in it.";

    internal static Task<byte[]> ReadRootIssuerKeyAsync(KernelDb db, CancellationToken cancellationToken) =>
        ReadIssuerKeyAsync(db, RootIssuerSource, RootIssuerMissing, cancellationToken);

    private static async Task<byte[]> ReadIssuerKeyAsync(KernelDb db, string source, string missing, CancellationToken cancellationToken) =>
        await db.Database.SqlQueryRaw<byte[]>("""SELECT issuer."Key" AS "Value" """ + source)
            .SingleOrDefaultAsync(cancellationToken)
        ?? throw new StoreFormatException(missing);

    internal async Task InstallAsync(CancellationToken cancellationToken = default)
    {
        await using var db = create();
        await InstallAsync(db, cancellationToken);
    }

    internal static async Task InstallAsync(KernelDb db, CancellationToken cancellationToken, bool exclusiveFormat = false)
    {
        if (!db.Database.IsNpgsql()) throw new InvalidOperationException("Root admission requires PostgreSQL.");
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await EfStore.LockCompatibleStoreFormatAsync(db, cancellationToken, exclusiveFormat || db.UsesRootTerminalSelection);
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
            CREATE INDEX IF NOT EXISTS "IX_FoundationRootInbox_Pending"
                ON "Messages" ("Partition", "Target" COLLATE "C", "Seq", "Id" COLLATE "C")
                WHERE "InboxScope" = -4 AND "EventMatch" IS NULL AND NOT "Done";
            """, cancellationToken);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO "FoundationRootIssuer" SELECT true, {RandomNumberGenerator.GetBytes(32)}, count(*)::integer FROM "Partitions"
            ON CONFLICT DO NOTHING
            """, cancellationToken);
        await EfStore.InstallFirstRootCheckpointsAsync(db, cancellationToken);
        if (db.UsesRootScheduleInventory) await RootScheduleInventory.InstallPostgresAsync(db, cancellationToken);
        if (db.UsesRootScheduleControls) await RootScheduleControls.InstallPostgresAsync(db, cancellationToken);
        if (RootScheduleOwnerMode.Applies(db)) await RootScheduleOwnerMode.InstallPostgresAsync(db, cancellationToken);
        if (db.UsesRootScheduleLineage) await RootScheduleLineage.InstallPostgresAsync(db, cancellationToken);
        if (db.UsesRootPreparedStarts) await RootPreparedStarts.InstallPostgresAsync(db, cancellationToken);
        if (db.UsesRootChildren) await EfStore.InstallRootChildCascadeAsync(db, cancellationToken);
        if (db.UsesRootStableBody)
            await db.Database.ExecuteSqlRawAsync("""
                ALTER TABLE "Instances" ADD COLUMN IF NOT EXISTS "FoundationStateWire" bytea
                """, cancellationToken);
        if (db.UsesRootParkOpening)
            await db.Database.ExecuteSqlRawAsync("""
                ALTER TABLE "Instances" ADD COLUMN IF NOT EXISTS "FoundationParkAt" bigint;
                ALTER TABLE "Messages" ADD COLUMN IF NOT EXISTS "FoundationKeptBy" smallint /*kept-by-check*/
                """.Replace("/*kept-by-check*/", EfStore.KeptByCheck, StringComparison.Ordinal)
                .Replace("/*kept-reasons*/", EfStore.KeptReasons(db), StringComparison.Ordinal), cancellationToken);
        if (db.RecordsRootParkRevision)
            await EfStore.InstallRootParkArrivalReasonAsync(db, cancellationToken);
        if (db.UsesRootParkRecovery)
            await EfStore.InstallRootParkRecoveryAsync(db, cancellationToken);
        else if (db.UsesRootParkedOperators)
            await EfStore.InstallRootParkedOutcomeAsync(db, cancellationToken);
        if (db.UsesRootParkedOperators)
            await EfStore.InstallRootParkClearingAsync(db, cancellationToken);
        if (db.UsesRootValueProtocol)
            await EfStore.InstallRootValuesAsync(db, cancellationToken);
        // The actor list and its one projector follow the profile's capabilities, terminal marks included.
        await EfStore.InstallRootActorListAsync(db, cancellationToken);
        if (db.UsesRootRetentionPolicy)
            await EfStore.InstallRootRetentionPolicyAsync(db, cancellationToken);
        if (db.UsesRootParkInspection)
            await EfStore.InstallRootParkReadModelAsync(db, cancellationToken);
        if (db.UsesRootSelfProtocol)
            await EfStore.InstallRootSelfAsync(db, cancellationToken);
        await InstallDirectReceiptsAsync(db, cancellationToken);
        if (db.UsesRootOperatorProtocol)
            await InstallOperatorReceiptsAsync(db, cancellationToken);
        await EfStore.RequireRootDefinitionsAsync(db, cancellationToken);
        await ReadInstalledPartitionCountAsync(db, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    internal static async Task<int> ReadInstalledPartitionCountAsync(KernelDb db, CancellationToken cancellationToken)
    {
        var sql = $"""
            SELECT issuer."Partitions" AS "Value" {RootIssuerSource}
            JOIN pg_catalog.pg_class placement ON placement.oid = pg_catalog.to_regclass('"Partitions"')
                AND placement.relnamespace = i.relnamespace AND placement.relkind = 'r'
            CROSS JOIN "Partitions" p
            GROUP BY issuer."Partitions"
            HAVING issuer."Partitions" = count(p."Id") AND min(p."Id") = 0 AND max(p."Id") = issuer."Partitions" - 1
            """;
        var count = await db.Database.SqlQueryRaw<int>(sql).SingleOrDefaultAsync(cancellationToken);
        if (count == 0) throw new StoreFormatException("The root issuer and installed virtual partitions must match the checkpoint schema.");
        return count;
    }

    internal async Task<Receipt> AdmitAsync(string tenant, string operation, StartSpec spec, long? due = null,
        TimeSpan? keepFor = null, CancellationToken cancellationToken = default) =>
        (await AdmitCoreAsync(tenant, operation, spec, due, keepFor, null, cancellationToken))!;

    /// <summary>
    /// A root start that holds its caller, on a profile with held replies (<see cref="RootCapabilities.ReplyProtocol"/>): the
    /// creator carries the request's reply route, and the owner answers the caller on the start as it answers one on a direct
    /// input. A repeat of the operation returns the root that operation started, as a start does, and attaches this caller
    /// to nothing. Null when the request's deadline has passed by database time: nothing is admitted.
    /// </summary>
    internal Task<Receipt?> AdmitRootRequestAsync(string tenant, string operation, StartSpec spec, string node, Request request,
        bool early, CancellationToken cancellationToken, TimeSpan? keepFor = null, ProtectRootStartAsync? protectRootStart = null)
    {
        if (request.Tenant != tenant || request.Done || string.IsNullOrWhiteSpace(node) || node.Length > 256 ||
            node.Contains('|') || node.Contains('\0') || DurableKeyJson.HasUnpairedSurrogate(node))
            throw new ArgumentException("A held root start needs an open request of its tenant and an issued reply node.", nameof(request));
        var reply = new DirectReply(request.ReplyAddress(node, early), request.Deadline);
        return protectRootStart is null
            ? AdmitCoreAsync(tenant, operation, spec, null, keepFor, reply, cancellationToken)
            : AdmitProtectedRootRequestAsync(tenant, operation, spec, keepFor, reply, protectRootStart, cancellationToken);
    }

    private async Task<Receipt?> AdmitCoreAsync(string tenant, string operation, StartSpec spec, long? due,
        TimeSpan? keepFor, DirectReply? reply, CancellationToken cancellationToken)
    {
        RequireCreator(operation, spec);
        var window = ReceiptWindow(deduplicationWindow, keepFor);

        var digest = FoundationRootCreator.OperationDigest(operation);
        var name = tenant + "_" + Guid.NewGuid().ToString("N");
        using var attempt = pressure?.Begin(tenant, RootAdmissionKind.Start);
        await using var db = create();
        RequirePressureProfile(db);
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, cancellationToken);
        var pin = db.RequiresPinnedRootNamespace
            ? await PinRootNamespaceAsync(db, loadIssuer: reply is null, cancellationToken) : null;
        if (reply is not null)
        {
            if (!db.UsesRootReplyProtocol) throw new NotSupportedException("This profile does not hold requests.");
            await EfStore.RequireStoreVersionsAsync(db, cancellationToken);
            if (!await db.Database.SqlQuery<bool>($"""
                SELECT ({reply.Deadline} > floor(extract(epoch from clock_timestamp()) * 1000)::bigint) AS "Value"
                """).SingleAsync(cancellationToken))
            {
                await transaction.RollbackAsync(cancellationToken);
                return null;
            }
        }
        var issuer = pin?.Issuer ?? await ReadIssuerAsync(db, cancellationToken);
        var preferred = InstanceRef.Spread(name, issuer.Partitions);
        var actor = InstanceRef.In(attempt?.Place(preferred, issuer.Partitions) ?? preferred, name);
        try { attempt?.Require(actor.Partition); }
        catch (RootAdmissionRefused)
        {
            // Preserve the one-round-trip claim/write on the accepted small-start path. A refused attempt
            // only reads the already installed receipt and has made no change to the store.
            var existing = await ReadStartReceiptAsync(db, pin, tenant, digest, liveOnly: true, cancellationToken);
            if (existing is null) throw;
            return new(existing, true);
        }
        var reference = FoundationRootCreator.ReferenceFor(issuer.Key, tenant, new(actor.Id, 1));
        var windowMs = (long)window.TotalMilliseconds;
        var claimFirst = spec.Input is { Length: > BatchedStartInputCodeUnits };
        if (claimFirst)
        {
            var claimSql = """
                WITH source AS MATERIALIZED (/*start-source*/), receipt AS (
                """ + ClaimReceiptSql + """

                    RETURNING "Reference"
                )
                SELECT source."SourceReady", EXISTS (SELECT 1 FROM receipt) AS "Claimed" FROM source
                """;
            var claimQuery = FormattableStringFactory.Create(claimSql, tenant, digest, reference, windowMs, false);
            var claim = (await db.Database.SqlQuery<StartClaim>(WithStartSource(claimQuery, pin)).ToListAsync(cancellationToken)).Single();
            RequireStartSource(claim.SourceReady);
            if (!claim.Claimed) return await Replay();
        }

        var creator = FoundationRootCreator.Create(actor, operation, digest, spec, due, reply?.Address);
        var fingerprint = CreationProtocol.Fingerprint(creator.Start, creator.Kind, creator.Payload)!;
        var (header, payload) = EncodeCreator(creator);
        var fits = FitsEnvelope(header, payload, reply?.Address);

        var write = await WriteStartAsync(db, pin, tenant, digest, reference, windowMs, claimFirst,
            creator, fingerprint, fits, header, payload, reply, cancellationToken);
        if (!write.Claimed) return await Replay();
        if (!fits) throw new MetadataLimitExceeded(nameof(spec), EfStore.RootContentBytes);
        if (!write.Enqueued)
            throw new InvalidOperationException("A generated root start must commit its creator with the receipt.");
        await transaction.CommitAsync(cancellationToken);
        attempt?.Committed();
        return new(reference, false);

        async Task<Receipt> Replay()
        {
            var existing = await ReadStartReceiptAsync(db, pin, tenant, digest, liveOnly: false, cancellationToken)
                ?? throw new InvalidOperationException("The conflicting root receipt no longer exists.");
            await transaction.CommitAsync(cancellationToken);
            return new(existing, true);
        }
    }

    // A live receipt wins even when the replay's encoded envelope is too large. When no receipt
    // wins, this transaction rolls back the claim if its creator cannot be stored.
    private static async Task<StartWrite> WriteStartAsync(KernelDb db, PinnedRootSource? pin, string tenant, string digest,
        string reference, long windowMs, bool claimFirst, Message creator, string fingerprint, bool fits, string header,
        string? payload, DirectReply? reply, CancellationToken cancellationToken)
    {
        var command = """
            WITH source AS MATERIALIZED (/*start-source*/), receipt AS (
            """ + ClaimReceiptSql + """

                RETURNING "Reference"
            ), accepted AS (
                SELECT "Reference" FROM receipt
                UNION ALL SELECT {2} WHERE {4} AND (SELECT "SourceReady" FROM source)
            ), birth AS (
                INSERT INTO "FoundationRootBirths" ("Tenant","Reference","Actor","Partition","Creation","Creator","Fingerprint")
                SELECT {0}, accepted."Reference", {5}, {6}, 1, {7}, {8}
                FROM accepted WHERE {9}
                RETURNING "Actor", "Partition", "Creator"
            ), creator AS (
                INSERT INTO "Messages" ("Id","InboxScope","Target","Partition","Kind","Due","Payload","Start","Done","Seen","Attempts","Trial","JobGeneration","ReplyTo")
                SELECT birth."Creator", 0, birth."Actor", birth."Partition", {10}, {11}, {12}, {13}, false, 0, 0, 0, 0, {14}
                FROM birth
                RETURNING "Id"
            )
            SELECT source."SourceReady", EXISTS (SELECT 1 FROM accepted) AS "Claimed",
                EXISTS (SELECT 1 FROM creator) AS "Enqueued"
            FROM source
            """;
        var writeQuery = FormattableStringFactory.Create(command, tenant, digest, reference, windowMs, claimFirst,
            creator.Target.Id, creator.Target.Partition, creator.InboxKey.Id, fingerprint, fits, creator.Kind, creator.Due ?? 0,
            (fits ? payload : null)!, fits ? header : "", (reply?.Address)!);
        var write = (await db.Database.SqlQuery<StartWrite>(WithStartSource(writeQuery, pin)).ToListAsync(cancellationToken)).Single();
        RequireStartSource(write.SourceReady);
        return write;
    }

    private static async Task<string?> ReadStartReceiptAsync(KernelDb db, PinnedRootSource? pin, string tenant, string digest,
        bool liveOnly, CancellationToken cancellationToken)
    {
        var result = await db.Database.SqlQuery<StartReceipt>(WithStartSource($"""
            WITH source AS MATERIALIZED (/*start-source*/)
            SELECT source."SourceReady", (SELECT r."Reference" FROM "FoundationRootReceipts" r
                WHERE source."SourceReady" AND r."Tenant" = {tenant} AND r."Operation" = {digest}
                    AND (NOT {liveOnly} OR r."Until" >= floor(extract(epoch from clock_timestamp()) * 1000)::bigint))
                AS "Reference"
            FROM source
            """, pin)).SingleAsync(cancellationToken);
        RequireStartSource(result.SourceReady);
        return result.Reference;
    }

    private static FormattableString WithStartSource(FormattableString query, PinnedRootSource? pin) =>
        WithAdmissionSource(query, pin, "/*start-source*/", 3, """('"FoundationRootReceipts"'), ('"FoundationRootBirths"'), ('"Messages"')""");

    private static FormattableString WithAdmissionSource(FormattableString query, PinnedRootSource? pin,
        string placeholder, int tableCount, string tables, bool requirePermanentDirectRelations = false)
    {
        if (pin is null)
            return FormattableStringFactory.Create(query.Format.Replace(placeholder, """SELECT true AS "SourceReady" """, StringComparison.Ordinal),
                query.GetArguments());

        // A schema rename can redirect unqualified relation names despite the earlier table locks.
        // Recheck the physical marker and target lineage in each receipt or creator statement.
        var markerId = Placeholder(query.ArgumentCount);
        var markerOid = Placeholder(query.ArgumentCount + 1);
        var namespaceOid = Placeholder(query.ArgumentCount + 2);
        const string permanentDirect = """
            AND relation.relpersistence = 'p'
            AND NOT relation.relrowsecurity AND NOT relation.relforcerowsecurity
            AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_inherits i WHERE i.inhparent = relation.oid OR i.inhrelid = relation.oid)
            """;
        var source = $$"""
            SELECT EXISTS (
                SELECT 1 FROM "KernelStoreVersions" v JOIN pg_catalog.pg_class marker ON marker.oid = v.tableoid
                WHERE v."Id" = {{markerId}} AND marker.oid::bigint = {{markerOid}}
                    AND marker.oid = pg_catalog.to_regclass('"KernelStoreVersions"')
                    AND marker.relnamespace::bigint = {{namespaceOid}}
                    AND (SELECT count(*) FROM (VALUES {{tables}}) required(name)
                        JOIN pg_catalog.pg_class relation ON relation.oid = pg_catalog.to_regclass(required.name)
                            AND relation.relkind = 'r' AND relation.relnamespace = marker.relnamespace
                            {{(requirePermanentDirectRelations ? permanentDirect : "")}}) = {{tableCount}}
            ) AS "SourceReady"
            """;
        return FormattableStringFactory.Create(query.Format.Replace(placeholder, source, StringComparison.Ordinal),
            [.. query.GetArguments(), StoreVersionEntity.MarkerId, (long)pin.Marker, (long)pin.Namespace]);

        static string Placeholder(int index) => "{" + index.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}";
    }

    private static void RequireStartSource(bool ready)
    {
        if (!ready) throw new StoreFormatException("The PostgreSQL root's pinned startup source changed before the write.");
    }

    private void RequirePressureProfile(KernelDb db)
    {
        if (pressure is not null && !db.UsesRootDueBacklog)
            throw new StoreFormatException("Admission pressure requires the due-backlog store profile.");
    }

    private async Task<Issuer> ReadIssuerAsync(KernelDb db, CancellationToken token)
    {
        if (!lockOrdinaryWrites)
            return await db.Database.SqlQueryRaw<Issuer>("""SELECT "Key", "Partitions" FROM "FoundationRootIssuer" """).SingleAsync(token);

        var issuer = await ReadGuardedAdmissionAsync(db, new("""SELECT issuer."Key", issuer."Partitions" """ + RootIssuerSource),
            ReadIssuer, token);
        return issuer ?? throw new StoreFormatException(RootIssuerMissing);
    }

    private static Issuer ReadIssuer(NpgsqlDataReader reader) => new(reader.GetFieldValue<byte[]>(0), reader.GetInt32(1));

    private static Task<TRow?> ReadGuardedAdmissionAsync<TRow>(KernelDb db, NpgsqlBatchCommand lookup,
        Func<NpgsqlDataReader, TRow> read, CancellationToken token) where TRow : class =>
        ReadGuardedAdmissionAsync<TRow?>(db, [lookup], async (reader, _, ct) =>
            await reader.ReadAsync(ct) ? read(reader) : null, token);

    private static async Task<TRow> ReadGuardedAdmissionAsync<TRow>(KernelDb db, IReadOnlyList<NpgsqlBatchCommand> operation,
        Func<NpgsqlDataReader, uint, CancellationToken, Task<TRow>> read, CancellationToken token)
    {
        var current = db.Database.CurrentTransaction!;
        try
        {
            // Lock the shared marker first. A fresh statement then samples both markers, including a revision row
            // inserted by the transaction that held the lock. Batch the statements to keep one round trip.
            var separateLock = db.SeparateRootFormatLock;
            var marker = new NpgsqlBatchCommand(StoreDefinitionsRevision.AddPostgresStampSql("""
                SELECT v."StoreSchema", v."MessageProtocol", v."JournalCodec", /*definitions-stamp*/, v.tableoid
                FROM "KernelStoreVersions" v WHERE v."Id" = $1
                """ + (separateLock ? "" : " FOR SHARE OF v"), lockDefinitions: separateLock));
            marker.Parameters.Add(new NpgsqlParameter<int> { TypedValue = StoreVersionEntity.MarkerId });
            var transaction = (NpgsqlTransaction)current.GetDbTransaction();
            await using var batch = new NpgsqlBatch(transaction.Connection, transaction);
            if (separateLock)
            {
                var gate = new NpgsqlBatchCommand("""SELECT tableoid FROM "KernelStoreVersions" WHERE "Id" = $1 FOR SHARE""");
                gate.Parameters.Add(new NpgsqlParameter<int> { TypedValue = StoreVersionEntity.MarkerId });
                batch.BatchCommands.Add(gate);
            }
            batch.BatchCommands.Add(marker);
            foreach (var command in operation) batch.BatchCommands.Add(command);
            if (db.Database.GetCommandTimeout() is { } timeout)
                batch.Timeout = timeout;
            await using var reader = await batch.ExecuteReaderAsync(token);
            uint? lockedMarker = null;
            if (separateLock)
            {
                if (!await reader.ReadAsync(token))
                    throw new StoreFormatException("The kernel store version marker is missing.");
                lockedMarker = reader.GetFieldValue<uint>(0);
                if (!await reader.NextResultAsync(token))
                    throw new StoreFormatException("The kernel store version marker is missing.");
            }
            if (!await reader.ReadAsync(token))
                throw new StoreFormatException("The kernel store version marker is missing.");
            var sampledMarker = reader.GetFieldValue<uint>(4);
            if (lockedMarker is { } oid && oid != sampledMarker)
                throw new StoreFormatException("The kernel store version marker changed its physical table.");
            EfStore.RequireSnapshotFormat(db, reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2),
                reader.IsDBNull(3) ? null : reader.GetString(3));
            if (!await reader.NextResultAsync(token))
                throw new InvalidOperationException("The guarded root lookup did not return a result.");
            return await read(reader, sampledMarker, token);
        }
        catch (PostgresException error) when (error.SqlState is PostgresErrorCodes.UndefinedTable or PostgresErrorCodes.UndefinedColumn)
        {
            // A later relation in the batch may be absent on an older store. Preserve the installed format diagnosis
            // even when draining the failed batch precedes reading its marker result.
            await current.RollbackAsync(token);
            await EfStore.RequireStoreVersionsAsync(db, token);
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
        await using var pinned = db.UsesRootParkOpening ? await EfStore.BeginPinnedRootOperationAsync(db, cancellationToken) : null;
        var removed = await db.Database.ExecuteSqlRawAsync("""
            DELETE FROM "FoundationRootReceipts" WHERE "Until" < floor(extract(epoch from clock_timestamp()) * 1000)::bigint
            """, cancellationToken);
        if (pinned is not null) await pinned.CommitAsync(cancellationToken);
        return removed;
    }

    internal static bool IsStoredCreator(string? start) => start?.StartsWith(CreatorPrefix, StringComparison.Ordinal) == true;

    internal async Task<Message?> ReadPendingCreatorAsync(string tenant, string reference, CancellationToken cancellationToken = default)
    {
        await using var db = create();
        await using var pinned = await EfStore.BeginPinnedRootOperationAsync(db, cancellationToken);
        return await ReadPendingCreatorAsync(db, tenant, reference, cancellationToken);
    }

    internal static async Task<Message?> ReadPendingCreatorAsync(KernelDb db, string tenant, string reference,
        CancellationToken cancellationToken)
    {
        var row = await db.Messages.FromSqlInterpolated($"""
            SELECT m.* FROM "Messages" m JOIN "FoundationRootBirths" b
            ON b."Creator" = m."Id" AND m."InboxScope" = 0 AND b."Actor" = m."Target"
            WHERE b."Tenant" = {tenant} AND b."Reference" = {reference} AND NOT m."Done"
            """).AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        return row is null ? null : DecodeCreator(row, db.UsesRootChildren);
    }

    internal static Message DecodeCreator(MessageEntity row, bool children = false)
    {
        if (row.JobGeneration != 0 || !IsStoredCreator(row.Start) || row.Kind != Kinds.Start || row.Id != "root:" + row.Target || !children && row.Sender is not null)
            throw new StoreException(Failure.Definite, "Invalid PostgreSQL generated root creator.");
        var stored = DurableKeyJson.Read<StoredRoot>(row.Start![CreatorPrefix.Length..]);
        var digest = FoundationRootCreator.OperationDigest(stored.Id);
        var child = row.Sender is not null;
        var validIdentity = child
            ? new InstanceRef(row.Target).ChildAddress && CreationProtocol.IsPhysicalAddress(row.Target) && stored.Spec.Creation?.Intent ==
                Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(row.Target))) &&
                (stored.Spec.Parent == row.Sender && stored.Spec.ParentCreation == 1 || stored.Spec.Parent is null && stored.Spec.ParentCreation == 0) &&
                row.ReplyTo is null && row.Due == 0 && !stored.ExplicitZeroDue
            : stored.Spec.Parent is null && stored.Spec.ParentCreation == 0 && stored.Spec.Creation?.Intent == digest;
        if (stored.Spec is not { Creation: { Generation: 1, Previous: 0 } } || !validIdentity ||
            (stored.EscapedInput ? row.Payload is not null : stored.Spec.Input is not null))
            throw new StoreException(Failure.Definite, "Invalid PostgreSQL generated root creation or input.");
        var spec = stored.Spec with { Input = stored.EscapedInput ? stored.Spec.Input : row.Payload };
        return child
            ? new Message(stored.Id, new(row.Target), Kinds.Start, spec.Input, row.Sender, spec, TargetCreation: 1, SenderCreation: 1)
                { BindFoundationChild = true, StorageId = row.Id }
            : FoundationRootCreator.Create(new(row.Target), stored.Id, digest, spec, row.Due != 0 || stored.ExplicitZeroDue ? row.Due : null, row.ReplyTo);
    }
}
