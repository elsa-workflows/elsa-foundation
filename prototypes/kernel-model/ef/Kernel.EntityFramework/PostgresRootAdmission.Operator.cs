using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;

namespace Kernel.EntityFramework;

internal sealed partial class PostgresRootAdmission
{
    private const string OperatorPrefix = "!kernel-operator/1:";
    private sealed record StoredOperator(string Reference, string Operation, long Creation, string Kind,
        [property: JsonRequired] bool ExplicitZeroDue);
    internal static bool IsStoredOperator(string? header) => header?.StartsWith(OperatorPrefix, StringComparison.Ordinal) == true;

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
        TimeSpan? keepFor, CancellationToken token)
    {
        if (kind is not (Kinds.Suspend or Kinds.Resume) || !FoundationMetadataLimits.IsOutsideMessageId(operation))
            throw new ArgumentException("Expected a one-way exact-root suspension command.");
        var window = keepFor is { } keep && keep < deduplicationWindow ? keep : deduplicationWindow;
        ArgumentOutOfRangeException.ThrowIfLessThan(window, TimeSpan.Zero);
        var windowMs = (long)window.TotalMilliseconds;
        var digest = FoundationRootCreator.OperationDigest(operation);
        await using var db = create();
        if (!db.UsesRootOperatorProtocol) return null;
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, token);
        await EfStore.RequireStoreVersionsAsync(db, token);
        var address = await RootAddressAsync(db, tenant, reference, token);
        if (address is null)
        {
            await tx.RollbackAsync(token);
            return null;
        }
        var prior = await db.Database.SqlQuery<string>($"""
            SELECT "Reference" AS "Value" FROM "FoundationOperatorReceipts"
            WHERE "Tenant" = {tenant} AND "Operation" = {digest} AND "Until" >= floor(extract(epoch from clock_timestamp()) * 1000)::bigint
            """).SingleOrDefaultAsync(token);
        if (prior is not null)
        {
            await tx.CommitAsync(token);
            return new(prior, true);
        }
        if (await AdmissionStatusAsync(db, address, tenant, reference, token) != InstanceStatus.Running)
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
            return new(prior, true);
        }
        var actor = new InstanceRef(address.Id);
        var id = $"operator/{reference}/{digest}/{Guid.NewGuid():N}";
        var header = OperatorPrefix + DurableKeyJson.Write(new StoredOperator(reference, operation, address.Creation, kind, due == 0));
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "Messages" ("InboxScope", "Id", "Partition", "Target", "Kind", "Start", "Due", "KeepUntil", "Done", "Seen", "Attempts", "Trial")
            VALUES ({InboxKey.FoundationOccurrenceScope}, {id}, {actor.Partition}, {address.Id}, {kind}, {header}, {due ?? 0}, {receipt.Single()}, false, 0, 0, 0)
            """, token);
        await tx.CommitAsync(token);
        return new(reference, false);
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
        if (row is null) return null;
        if (!IsStoredOperator(row.Start) || row.Kind is not (Kinds.Suspend or Kinds.Resume) ||
            row.EventMatch is not null || row.Payload is not null || row.ReplyTo is not null || row.Sender is not null ||
            row.JobGeneration != 0 || row.KeepUntil is not { } until)
            throw new StoreException(Failure.Definite, "Unexpected stored one-way operator envelope.");
        StoredOperator? header;
        try { header = DurableKeyJson.Read<StoredOperator>(row.Start![OperatorPrefix.Length..]); }
        catch (JsonException error) { throw new StoreException(Failure.Definite, "Invalid stored operator framing.", error); }
        if (header is null) throw new StoreException(Failure.Definite, "Invalid stored operator framing.");
        if (header is not { Operation.Length: > 0 } || header.Reference != reference || header.Creation != 1 || header.Kind != row.Kind ||
            header.ExplicitZeroDue && row.Due != 0 || !MatchesDirectOccurrence(reference, header.Operation, row.Id["operator/".Length..]))
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
