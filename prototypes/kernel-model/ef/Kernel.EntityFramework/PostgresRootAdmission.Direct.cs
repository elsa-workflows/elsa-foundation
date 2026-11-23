using System.Text;
using Microsoft.EntityFrameworkCore;

namespace Kernel.EntityFramework;

internal sealed partial class PostgresRootAdmission
{
    private const string DirectPrefix = "!kernel-direct/1:";
    private sealed record StoredDirect(string Operation, long Creation, bool EscapedPayload, bool ExplicitZeroDue,
        string? EscapedKind = null);

    private static Task InstallDirectReceiptsAsync(KernelDb db, CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS "FoundationDirectReceipts" (
                "Reference" text COLLATE "C" NOT NULL REFERENCES "FoundationRootBirths" ("Reference"),
                "Operation" text COLLATE "C" NOT NULL,
                "Until" bigint NOT NULL,
                PRIMARY KEY ("Reference", "Operation"));
            CREATE INDEX IF NOT EXISTS "IX_FoundationDirectReceipts_Until" ON "FoundationDirectReceipts" ("Until");
            """, cancellationToken);

    internal static bool IsStoredDirect(string? value) => value?.StartsWith(DirectPrefix, StringComparison.Ordinal) == true;

    internal enum DirectResult { Accepted, Duplicate, Refused, MissingTarget }

    /// <returns>True for a new send, false for a duplicate and null for an unavailable exact life or invalid input.</returns>
    internal async Task<bool?> AdmitDirectAsync(string tenant, string reference, string id, string kind, string? payload = null,
        long? due = null, TimeSpan? keepFor = null, CancellationToken cancellationToken = default)
        => await AdmitDirectResultAsync(tenant, reference, id, kind, payload, due, keepFor, cancellationToken) switch
        {
            DirectResult.Accepted => true,
            DirectResult.Duplicate => false,
            _ => null
        };

    internal async Task<DirectResult> AdmitDirectResultAsync(string tenant, string reference, string id, string kind, string? payload = null,
        long? due = null, TimeSpan? keepFor = null, CancellationToken cancellationToken = default)
    {
        if (!FoundationRootCreator.IsGeneratedReference(reference) || !FoundationMetadataLimits.IsOutsideMessageId(id) || !FoundationMetadataLimits.IsOrdinaryKind(kind) ||
            !FoundationMetadataLimits.FitsContent(payload)) return DirectResult.Refused;
        var window = keepFor is { } requested && requested < deduplicationWindow ? requested : deduplicationWindow;
        ArgumentOutOfRangeException.ThrowIfLessThan(window, TimeSpan.Zero);
        var windowMs = (long)window.TotalMilliseconds;
        var digest = FoundationRootCreator.OperationDigest(id);
        await using var db = create();
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, cancellationToken);
        var address = await db.Database.SqlQuery<CreationAddress>($"""
            SELECT "Actor" AS "Id", "Creation" FROM "FoundationRootBirths"
            WHERE "Tenant" = {tenant} AND "Reference" = {reference}
            """).SingleOrDefaultAsync(cancellationToken);
        if (address is null) return await RefuseAsync(DirectResult.MissingTarget);
        var status = await LockStatusAsync();
        if (status is null)
        {
            var pending = await db.Database.SqlQuery<string>($"""
                SELECT m."Id" AS "Value" FROM "Messages" m
                JOIN "FoundationRootBirths" b ON b."Creator" = m."Id" AND b."Actor" = m."Target"
                WHERE b."Reference" = {reference} AND m."InboxScope" = 0 AND NOT m."Done"
                    AND NOT EXISTS (SELECT 1 FROM "Instances" i WHERE i."Id" = b."Actor")
                FOR SHARE OF m
                """).ToListAsync(cancellationToken);
            // If the creator disappeared while the SELECT waited on its lock, the first checkpoint may now be live.
            if (pending.Count == 0)
            {
                status = await LockStatusAsync();
                if (status is null) return await RefuseAsync(DirectResult.MissingTarget);
            }
        }

        if (status is not (null or InstanceStatus.Running))
        {
            if (status == InstanceStatus.Finished && await db.Database.SqlQuery<bool>($"""
                SELECT EXISTS (SELECT 1 FROM "FoundationDirectReceipts"
                    WHERE "Reference" = {reference} AND "Operation" = {digest}
                        AND "Until" >= floor(extract(epoch from clock_timestamp()) * 1000)::bigint) AS "Value"
                """).SingleAsync(cancellationToken))
            {
                await transaction.CommitAsync(cancellationToken);
                return DirectResult.Duplicate;
            }
            return await RefuseAsync(DirectResult.Refused);
        }

        var receipt = await db.Database.SqlQuery<long>($"""
            INSERT INTO "FoundationDirectReceipts" AS r ("Reference", "Operation", "Until")
            VALUES ({reference}, {digest}, floor(extract(epoch from clock_timestamp()) * 1000)::bigint + {windowMs})
            ON CONFLICT ("Reference", "Operation") DO UPDATE
                SET "Until" = floor(extract(epoch from clock_timestamp()) * 1000)::bigint + {windowMs}
            WHERE r."Until" < floor(extract(epoch from clock_timestamp()) * 1000)::bigint
            RETURNING "Until" AS "Value"
            """).ToListAsync(cancellationToken);
        if (receipt.Count == 0)
        {
            await transaction.CommitAsync(cancellationToken);
            return DirectResult.Duplicate;
        }

        var occurrence = $"{reference}/{digest}/{Guid.NewGuid():N}";
        var escaped = payload is not null && (payload.Contains('\0') || DurableKeyJson.HasUnpairedSurrogate(payload));
        var body = escaped ? DurableKeyJson.Write(payload) : payload;
        var escapedKind = kind.Contains('\0') ? kind : null;
        var header = DirectPrefix + DurableKeyJson.Write(new StoredDirect(id, address.Creation, escaped, due == 0, escapedKind));
        if (Encoding.UTF8.GetByteCount(header) + (body is null ? 0L : Encoding.UTF8.GetByteCount(body)) >
            16 * 1024 * 1024 - 24 * 1024)
            return await RefuseAsync(DirectResult.Refused);

        var target = new InstanceRef(address.Id);
        db.Messages.Add(new MessageEntity
        {
            Id = occurrence, InboxScope = InboxKey.FoundationOccurrenceScope, Target = target.Id, Partition = target.Partition,
            Kind = escapedKind is null ? kind : "input", Payload = body, Start = header, Due = due ?? 0, KeepUntil = receipt.Single()
        });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return DirectResult.Accepted;

        async Task<DirectResult> RefuseAsync(DirectResult reason)
        {
            await transaction.RollbackAsync(cancellationToken);
            return reason;
        }

        Task<InstanceStatus?> LockStatusAsync() =>
            db.Database.SqlQuery<InstanceStatus?>($"""
                SELECT "Status" AS "Value" FROM "Instances"
                WHERE "Id" = {address.Id} AND "FoundationTenant" = {tenant} AND "FoundationReference" = {reference} AND NOT "Retired"
                FOR SHARE
                """).SingleOrDefaultAsync(cancellationToken);
    }

    internal async Task<IReadOnlyList<Message>> ReadPendingDirectAsync(string tenant, string reference,
        CancellationToken cancellationToken = default)
    {
        if (!FoundationRootCreator.IsGeneratedReference(reference)) return [];
        await using var db = create();
        var rows = await db.Messages.FromSqlInterpolated($"""
            SELECT m.* FROM "Messages" m JOIN "FoundationRootBirths" b ON b."Actor" = m."Target"
            WHERE b."Tenant" = {tenant} AND b."Reference" = {reference} AND m."InboxScope" = {InboxKey.FoundationOccurrenceScope}
                AND NOT m."Done" ORDER BY m."Seq"
            """).AsNoTracking().ToListAsync(cancellationToken);
        return rows.Select(DecodeDirect).ToArray();
    }

    internal static Message DecodeDirect(MessageEntity row)
    {
        if (!IsStoredDirect(row.Start))
            throw new StoreException(Failure.Definite, "Unsupported PostgreSQL public input envelope.");
        var header = DurableKeyJson.Read<StoredDirect>(row.Start![DirectPrefix.Length..]);
        var payload = header.EscapedPayload ? DurableKeyJson.Read<string>(row.Payload!) : row.Payload;
        return new Message(header.Operation, new(row.Target), header.EscapedKind ?? row.Kind, payload,
            Due: row.Due != 0 || header.ExplicitZeroDue ? row.Due : null, KeepUntil: row.KeepUntil, TargetCreation: header.Creation)
            { StorageId = row.Id }.WithInboxScope(row.InboxScope);
    }

    internal async Task<int> SweepDirectReceiptsAsync(CancellationToken cancellationToken = default)
    {
        await using var db = create();
        return await db.Database.ExecuteSqlRawAsync("""
            DELETE FROM "FoundationDirectReceipts" WHERE "Until" < floor(extract(epoch from clock_timestamp()) * 1000)::bigint
            """, cancellationToken);
    }
}
