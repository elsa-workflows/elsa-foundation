using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Kernel.EntityFramework;

internal sealed partial class PostgresRootAdmission
{
    private const string OperatorPrefix = "!kernel-operator/1:";
    private sealed record StoredOperator(string Reference, string Operation, long Creation, string Kind,
        [property: JsonRequired] bool ExplicitZeroDue);
    internal const string OperatorOccurrencePrefix = "operator/";
    internal static bool IsStoredOperator(string? header) => header?.StartsWith(OperatorPrefix, StringComparison.Ordinal) == true;
    internal static bool IsOperatorOccurrence(InboxKey key) => key.Scope == InboxKey.FoundationOccurrenceScope && key.Match is null &&
        key.Id.StartsWith(OperatorOccurrencePrefix, StringComparison.Ordinal);

    private static Task InstallOperatorReceiptsAsync(KernelDb db, CancellationToken token) =>
        db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS "FoundationOperatorReceipts" (
                "Tenant" text COLLATE "C" NOT NULL, "Operation" text COLLATE "C" NOT NULL,
                "Reference" text COLLATE "C" NOT NULL REFERENCES "FoundationRootBirths" ("Reference"),
                "Until" bigint NOT NULL, PRIMARY KEY ("Tenant", "Operation"));
            CREATE INDEX IF NOT EXISTS "IX_FoundationOperatorReceipts_Until" ON "FoundationOperatorReceipts" ("Until");
            CREATE INDEX IF NOT EXISTS "IX_FoundationRootOperators_Pending"
                ON "Messages" ("Partition", "Target", "Seq") INCLUDE ("Due")
                WHERE "InboxScope" = -4 AND "Id" LIKE 'operator/%' AND NOT "Done";
            """, token);

    internal async Task<Receipt?> AdmitRootOperatorAsync(string tenant, string reference, string operation, string kind, long? due,
        TimeSpan? keepFor, CancellationToken token, string? runningSelection = null)
    {
        if (kind is not (Kinds.Suspend or Kinds.Resume or Kinds.Terminate) || !FoundationMetadataLimits.IsOutsideMessageId(operation))
            throw new ArgumentException("Expected a one-way exact-root operator command.");
        var window = keepFor is { } keep && keep < deduplicationWindow ? keep : deduplicationWindow;
        ArgumentOutOfRangeException.ThrowIfLessThan(window, TimeSpan.Zero);
        var windowMs = (long)window.TotalMilliseconds;
        var digest = FoundationRootCreator.OperationDigest(operation);
        await using var db = create();
        if (!db.UsesRootOperatorProtocol || kind == Kinds.Terminate && !db.UsesRootTermination) return null;
        var selectedTermination = db.UsesRootTerminalSelection && kind == Kinds.Terminate;
        if (selectedTermination != (runningSelection is not null)) return null;

        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, token);
        try
        {
            if (selectedTermination)
            {
                await PinSelectedOperatorSchemaAsync(db, token);
                if (!PostgresRootSelection.MatchesRunning(await ReadJournalCursorAsync(db, token), runningSelection, tenant, reference))
                    return null;
            }
            else
                await EfStore.RequireStoreVersionsAsync(db, token);
            var address = await RootAddressAsync(db, tenant, reference, token);
            if (address is null)
            {
                await tx.RollbackAsync(token);
                return null;
            }
            // Lock the selected checkpoint before reading its receipt. If the command already
            // ended this actor, the receipt committed before the owner could consume it.
            var selectedStatus = selectedTermination
                ? await AdmissionStatusAsync(db, address, tenant, reference, token, requireCheckpoint: true)
                : null;
            var prior = await db.Database.SqlQuery<string>($"""
                SELECT "Reference" AS "Value" FROM "FoundationOperatorReceipts"
                WHERE "Tenant" = {tenant} AND "Operation" = {digest} AND "Until" >= floor(extract(epoch from clock_timestamp()) * 1000)::bigint
                """).SingleOrDefaultAsync(token);
            if (prior is not null)
            {
                await tx.CommitAsync(token);
                return selectedTermination && prior != reference ? null : new(prior, true);
            }
            var status = selectedTermination ? selectedStatus : await AdmissionStatusAsync(db, address, tenant, reference, token);
            if (status != InstanceStatus.Running)
            {
                await tx.RollbackAsync(token);
                return null;
            }
            var receipt = await db.Database.SqlQuery<long>($"""
                INSERT INTO "FoundationOperatorReceipts" AS r ("Tenant","Operation","Reference","Until")
                VALUES ({tenant},{digest},{reference},floor(extract(epoch from clock_timestamp()) * 1000)::bigint + {windowMs})
                ON CONFLICT ("Tenant","Operation") DO UPDATE SET "Reference" = EXCLUDED."Reference",
                    "Until" = floor(extract(epoch from clock_timestamp()) * 1000)::bigint + {windowMs}
                WHERE r."Until" < floor(extract(epoch from clock_timestamp()) * 1000)::bigint RETURNING "Until" AS "Value"
                """).ToListAsync(token);
            if (receipt.Count == 0)
            {
                // A competing transaction recorded this operation while we qualified the requested root.
                prior = await db.Database.SqlQuery<string>($"""
                    SELECT "Reference" AS "Value" FROM "FoundationOperatorReceipts" WHERE "Tenant" = {tenant} AND "Operation" = {digest}
                    """).SingleAsync(token);
                await tx.CommitAsync(token);
                return selectedTermination && prior != reference ? null : new(prior, true);
            }
            var actor = new InstanceRef(address.Id);
            var id = $"{OperatorOccurrencePrefix}{reference}/{digest}/{Guid.NewGuid():N}";
            var header = OperatorPrefix + DurableKeyJson.Write(new StoredOperator(reference, operation, address.Creation, kind, due == 0));
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "Messages" ("InboxScope", "Id", "Partition", "Target", "Kind", "Start", "Due", "KeepUntil", "Done", "Seen", "Attempts", "Trial")
                VALUES ({InboxKey.FoundationOccurrenceScope}, {id}, {actor.Partition}, {address.Id}, {kind}, {header}, {due ?? 0}, {receipt.Single()}, false, 0, 0, 0)
                """, token);
            await tx.CommitAsync(token);
            return new(reference, false);
        }
        catch (PostgresException error) when (selectedTermination && IsUnavailableSelectedSchema(error))
        {
            throw new StoreFormatException("The selected operator admission schema is unreadable.", error);
        }
    }

    private static bool IsUnavailableSelectedSchema(PostgresException error) =>
        error.SqlState is PostgresErrorCodes.UndefinedTable or PostgresErrorCodes.UndefinedColumn
            or PostgresErrorCodes.InvalidSchemaName or PostgresErrorCodes.WrongObjectType;

    private sealed record SelectedOperatorStore(int StoreSchema, int MessageProtocol, int JournalCodec, string Namespace);

    private static async Task PinSelectedOperatorSchemaAsync(KernelDb db, CancellationToken token)
    {
        var marker = await db.Database.SqlQuery<SelectedOperatorStore>($"""
            SELECT v."StoreSchema", v."MessageProtocol", v."JournalCodec", ns.nspname AS "Namespace"
            FROM "KernelStoreVersions" v
            JOIN pg_catalog.pg_class c ON c.oid = v.tableoid
            JOIN pg_catalog.pg_namespace ns ON ns.oid = c.relnamespace
            WHERE v."Id" = {StoreVersionEntity.MarkerId}
            FOR SHARE OF v
            """).SingleOrDefaultAsync(token);
        EfStore.RequireSnapshotFormat(db.RequiredFormat, marker?.StoreSchema, marker?.MessageProtocol, marker?.JournalCodec);
        var source = marker!.Namespace;
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            SELECT pg_catalog.set_config('search_path', 'pg_catalog,' || pg_catalog.quote_ident({source}) || ',pg_temp', true)
            """, token);
        await db.Database.ExecuteSqlRawAsync("""
            LOCK TABLE "FoundationRootIssuer", "FoundationRootBirths", "Instances", "FoundationOperatorReceipts", "Messages"
            IN ACCESS SHARE MODE
            """, token);
        var sameSchema = await db.Database.SqlQuery<bool>($"""
            SELECT count(*) = 6 AS "Value"
            FROM (VALUES ('"KernelStoreVersions"'), ('"FoundationRootIssuer"'), ('"FoundationRootBirths"'),
                ('"Instances"'), ('"FoundationOperatorReceipts"'), ('"Messages"')) required(name)
            JOIN pg_catalog.pg_class c ON c.oid = pg_catalog.to_regclass(required.name)
            JOIN pg_catalog.pg_namespace ns ON ns.oid = c.relnamespace AND ns.nspname = {source}
            """).SingleAsync(token);
        if (!sameSchema)
            throw new StoreFormatException("Selected operator admission requires the source relations in one schema.");
    }

    // A candidate read. A subsequent owner must recheck the command and checkpoint at its fence before applying it.
    internal async Task<Message?> ReadDueRootOperatorAsync(string tenant, string reference, CancellationToken token)
    {
        await using var db = create();
        if (!db.UsesRootOperatorProtocol) return null;
        await EfStore.RequireStoreVersionsAsync(db, token);
        var row = await db.Messages.FromSqlInterpolated($"""
            SELECT m.* FROM "Messages" m JOIN "FoundationRootBirths" b ON b."Actor" = m."Target" AND b."Partition" = m."Partition"
            WHERE b."Tenant" = {tenant} AND b."Reference" = {reference} AND b."Creation" = 1
                AND m."InboxScope" = {InboxKey.FoundationOccurrenceScope} AND m."Id" LIKE 'operator/%' AND NOT m."Done"
                AND m."Due" <= floor(extract(epoch from clock_timestamp()) * 1000)::bigint ORDER BY m."Seq" LIMIT 1
            """).AsNoTracking().SingleOrDefaultAsync(token);
        return row is null ? null : DecodeOperator(row, reference, db.UsesRootTermination);
    }

    internal static Message DecodeOperator(MessageEntity row, string reference, bool terminations)
    {
        if (!IsOperatorOccurrence(new(row.Id, row.InboxScope)) || !IsStoredOperator(row.Start) || row.Kind is not (Kinds.Suspend or Kinds.Resume or Kinds.Terminate) ||
            row.Kind == Kinds.Terminate && !terminations || row.EventMatch is not null || row.Payload is not null || row.ReplyTo is not null || row.Sender is not null ||
            row.JobGeneration != 0 || row.KeepUntil is not { } until)
            throw new StoreException(Failure.Definite, "Unexpected stored one-way operator envelope.");
        StoredOperator? header;
        try { header = DurableKeyJson.Read<StoredOperator>(row.Start![OperatorPrefix.Length..]); }
        catch (JsonException error) { throw new StoreException(Failure.Definite, "Invalid stored operator framing.", error); }
        if (header is null) throw new StoreException(Failure.Definite, "Invalid stored operator framing.");
        if (header is not { Operation.Length: > 0 } || header.Reference != reference || header.Creation != 1 || header.Kind != row.Kind ||
            header.ExplicitZeroDue && row.Due != 0 || !MatchesDirectOccurrence(reference, header.Operation, row.Id[OperatorOccurrencePrefix.Length..]))
            throw new StoreException(Failure.Definite, "The operator occurrence differs from its bound reference and operation.");
        var suffix = row.Id.LastIndexOf('/');
        return FoundationIngressAddress.Occurrence(header.Operation, row.Id[..suffix], row.Id[(suffix + 1)..],
            new(row.Target), header.Creation, row.Kind, null, row.Due != 0 || header.ExplicitZeroDue ? row.Due : null, until, null, new(null));
    }

    internal async Task<int> SweepOperatorReceiptsAsync(CancellationToken token)
    {
        await using var db = create();
        if (!db.UsesRootOperatorProtocol) return 0;
        await EfStore.RequireStoreVersionsAsync(db, token);
        return await db.Database.ExecuteSqlRawAsync("""
            DELETE FROM "FoundationOperatorReceipts" WHERE "Until" < floor(extract(epoch from clock_timestamp()) * 1000)::bigint
            """, token);
    }
}
