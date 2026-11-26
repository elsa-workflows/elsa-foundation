using System.Text;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Kernel.EntityFramework;

internal sealed partial class PostgresRootAdmission
{
    private const string DirectPrefix = "!kernel-direct/1:";
    private const string HeldDirectPrefix = "!kernel-held-direct/1:";
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

    internal static bool IsStoredDirect(string? value) => value?.StartsWith(DirectPrefix, StringComparison.Ordinal) == true ||
        IsHeldDirect(value);

    private static bool IsHeldDirect(string? value) => value?.StartsWith(HeldDirectPrefix, StringComparison.Ordinal) == true;

    internal enum DirectResult { Accepted, Duplicate, Refused, MissingTarget, Expired }

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
        => await AdmitDirectCoreAsync(tenant, reference, id, kind, payload, due, keepFor, null, cancellationToken);

    internal Task<DirectResult> AdmitDirectRequestAsync(string tenant, string reference, string kind, string? payload,
        string node, Request request, bool early, CancellationToken cancellationToken, TimeSpan? keepFor = null, long? dueAt = null)
    {
        if (request.Tenant != tenant || request.Done || string.IsNullOrWhiteSpace(node) || node.Length > 256 ||
            node.Contains('|') || node.Contains('\0') || DurableKeyJson.HasUnpairedSurrogate(node))
            return Task.FromResult(DirectResult.Refused);
        return AdmitDirectCoreAsync(tenant, reference, request.Id, kind, payload, dueAt, keepFor,
            new(request.ReplyAddress(node, early), request.Deadline), cancellationToken);
    }

    internal async ValueTask<long> ReadReplyTimeAsync(CancellationToken cancellationToken)
    {
        await using var db = create();
        if (!db.UsesRootReplyProtocol && !db.UsesRootOperatorReplies)
            throw new NotSupportedException("This profile does not hold requests.");
        return await db.Database.SqlQueryRaw<long>("""
            SELECT floor(extract(epoch from clock_timestamp()) * 1000)::bigint AS "Value"
            """).SingleAsync(cancellationToken);
    }

    private sealed record DirectReply(string Address, long Deadline);

    private async Task<DirectResult> AdmitDirectCoreAsync(string tenant, string reference, string id, string kind, string? payload,
        long? due, TimeSpan? keepFor, DirectReply? reply, CancellationToken cancellationToken)
    {
        if (!FoundationRootCreator.IsGeneratedReference(reference) || !FoundationMetadataLimits.IsOutsideMessageId(id) || !FoundationMetadataLimits.IsOrdinaryKind(kind) ||
            !FoundationMetadataLimits.FitsContent(payload)) return DirectResult.Refused;
        var window = keepFor is { } requested && requested < deduplicationWindow ? requested : deduplicationWindow;
        ArgumentOutOfRangeException.ThrowIfLessThan(window, TimeSpan.Zero);
        var windowMs = (long)window.TotalMilliseconds;
        var digest = FoundationRootCreator.OperationDigest(id);
        await using var db = create();
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, cancellationToken);
        if (db.RequiresPinnedRootNamespace)
            await PinRootNamespaceAsync(db, cancellationToken);
        if (reply is not null)
        {
            if (!db.UsesRootReplyProtocol) return await RefuseAsync(DirectResult.Refused);
            await EfStore.RequireStoreVersionsAsync(db, cancellationToken);
        }
        var address = lockOrdinaryWrites
            ? await GuardedRootAddressAsync(db, tenant, reference, cancellationToken)
            : await RootAddressAsync(db, tenant, reference, cancellationToken);
        if (address is null) return await RefuseAsync(DirectResult.MissingTarget);
        var status = await AdmissionStatusAsync(db, address, tenant, reference, cancellationToken, requireCheckpoint: reply is not null);
        if (status is null)
        {
            if (db.UsesRootRetirement && await HasReceiptAsync()) return await DuplicateAsync();
            return await RefuseAsync(DirectResult.MissingTarget);
        }
        var acceptsParkArrival = db.UsesRootParkArrivals && status == InstanceStatus.Faulted &&
            await db.Database.SqlQuery<bool>($"""
                SELECT EXISTS (SELECT 1 FROM "Instances" WHERE "Id" = {address.Id} AND "Partition" = {new InstanceRef(address.Id).Partition}
                    AND "FoundationTenant" = {tenant} AND "FoundationReference" = {reference} AND NOT "Retired"
                    AND "FoundationParkAt" IS NOT NULL) AS "Value"
                """).SingleAsync(cancellationToken);
        if (status != InstanceStatus.Running && !acceptsParkArrival)
        {
            // A Faulted or Cancelled checkpoint ends only in a family that ends that status, under the family's prefix.
            var terminal = status == InstanceStatus.Finished ||
                status == InstanceStatus.Faulted && db.UsesRootParkOpening ||
                status is (InstanceStatus.Faulted or InstanceStatus.Cancelled) && db.RootStateFamily.Ends(status.Value) &&
                await IsQualifiedTerminalAsync(db, address, tenant, reference, status.Value,
                    db.RootStateFamily.Prefix, cancellationToken);
            if (terminal && await HasReceiptAsync()) return await DuplicateAsync();
            return await RefuseAsync(DirectResult.Refused);
        }

        if (reply is not null && !await db.Database.SqlQuery<bool>($"""
            SELECT ({reply.Deadline} > floor(extract(epoch from clock_timestamp()) * 1000)::bigint) AS "Value"
            """).SingleAsync(cancellationToken)) return await RefuseAsync(DirectResult.Expired);

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
        var header = (reply is null ? DirectPrefix : HeldDirectPrefix) + DurableKeyJson.Write(new StoredDirect(id, address.Creation, escaped, due == 0, escapedKind));
        if (Encoding.UTF8.GetByteCount(header) + (body is null ? 0L : Encoding.UTF8.GetByteCount(body)) +
            (reply is null ? 0 : Encoding.UTF8.GetByteCount(reply.Address)) >
            16 * 1024 * 1024 - 24 * 1024)
            return await RefuseAsync(DirectResult.Refused);

        var target = new InstanceRef(address.Id);
        db.Messages.Add(new MessageEntity
        {
            Id = occurrence, InboxScope = InboxKey.FoundationOccurrenceScope, Target = target.Id, Partition = target.Partition,
            Kind = escapedKind is null ? kind : "input", Payload = body, Start = header, Due = due ?? 0,
            KeepUntil = receipt.Single(), ReplyTo = reply?.Address
        });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return DirectResult.Accepted;

        Task<bool> HasReceiptAsync() => db.Database.SqlQuery<bool>($"""
            SELECT EXISTS (SELECT 1 FROM "FoundationDirectReceipts"
                WHERE "Reference" = {reference} AND "Operation" = {digest}
                    AND "Until" >= floor(extract(epoch from clock_timestamp()) * 1000)::bigint) AS "Value"
            """).SingleAsync(cancellationToken);

        async Task<DirectResult> DuplicateAsync()
        {
            await transaction.CommitAsync(cancellationToken);
            return DirectResult.Duplicate;
        }

        async Task<DirectResult> RefuseAsync(DirectResult reason)
        {
            await transaction.RollbackAsync(cancellationToken);
            return reason;
        }
    }

    private static Task<CreationAddress?> RootAddressAsync(KernelDb db, string tenant, string reference, CancellationToken cancellationToken) =>
        db.Database.SqlQuery<CreationAddress>($"""
            SELECT "Actor" AS "Id", "Creation" FROM "FoundationRootBirths"
            WHERE "Tenant" = {tenant} AND "Reference" = {reference}
            """).SingleOrDefaultAsync(cancellationToken);

    private static Task<CreationAddress?> GuardedRootAddressAsync(KernelDb db, string tenant, string reference, CancellationToken token)
    {
        var lookup = new NpgsqlBatchCommand("""
            SELECT "Actor", "Creation" FROM "FoundationRootBirths" WHERE "Tenant" = $1 AND "Reference" = $2
            """);
        lookup.Parameters.Add(new NpgsqlParameter<string> { TypedValue = tenant });
        lookup.Parameters.Add(new NpgsqlParameter<string> { TypedValue = reference });
        return ReadGuardedAdmissionAsync(db, lookup, static reader => new CreationAddress(reader.GetString(0), reader.GetInt64(1)), token);
    }

    // AdmissionStatusAsync already holds a share lock on this exact checkpoint until the receipt lookup ends.
    private static Task<bool> IsQualifiedTerminalAsync(KernelDb db, CreationAddress address, string tenant, string reference,
        InstanceStatus status, string stateFamily, CancellationToken cancellationToken) =>
        db.Database.SqlQuery<bool>($"""
            SELECT EXISTS (SELECT 1 FROM "Instances"
                WHERE "Id" = {address.Id} AND "Partition" = {new InstanceRef(address.Id).Partition}
                    AND "FoundationTenant" = {tenant} AND "FoundationReference" = {reference} AND NOT "Retired"
                    AND "Status" = {(int)status} AND starts_with("State", {stateFamily})
                    AND "FoundationTerminalAt" IS NOT NULL AND "FoundationTerminalRevision" = "Revision") AS "Value"
            """).SingleAsync(cancellationToken);

    // A pending creator admits its first inputs. Re-read the checkpoint when the first commit wins the lock.
    private static async Task<InstanceStatus?> AdmissionStatusAsync(KernelDb db, CreationAddress address, string tenant, string reference,
        CancellationToken cancellationToken, bool requireCheckpoint = false)
    {
        var status = await LockStatusAsync();
        if (status is not null || requireCheckpoint) return status;
        var pending = await db.Database.SqlQuery<string>($"""
            SELECT m."Id" AS "Value" FROM "Messages" m
            JOIN "FoundationRootBirths" b ON b."Creator" = m."Id" AND b."Actor" = m."Target"
            WHERE b."Reference" = {reference} AND m."InboxScope" = 0 AND NOT m."Done"
                AND NOT EXISTS (SELECT 1 FROM "Instances" i WHERE i."Id" = b."Actor")
            FOR SHARE OF m
            """).ToListAsync(cancellationToken);
        return pending.Count != 0 ? InstanceStatus.Running : await LockStatusAsync();

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
        if (db.UsesRootReplyProtocol) await EfStore.RequireStoreVersionsAsync(db, cancellationToken);
        var rows = await db.Messages.FromSqlInterpolated($"""
            SELECT m.* FROM "Messages" m JOIN "FoundationRootBirths" b
                ON b."Actor" = m."Target" AND b."Partition" = m."Partition" AND b."Creation" = 1
            WHERE b."Tenant" = {tenant} AND b."Reference" = {reference} AND m."InboxScope" = {InboxKey.FoundationOccurrenceScope}
                AND m."EventMatch" IS NULL AND NOT m."Done" AND m."Id" NOT LIKE 'operator/%' ORDER BY m."Seq"
            """).AsNoTracking().ToListAsync(cancellationToken);
        return rows.Select(row => DecodeDirect(row, reference, db.UsesRootReplyProtocol)).ToArray();
    }

    internal static bool MatchesDirectOccurrence(string reference, string operation, string occurrence, bool retentionDelete = false)
    {
        if (!IsIssuedReferenceShape(reference) || operation is null) return false;
        if (retentionDelete
            ? !operation.StartsWith(RetentionOperationPrefix, StringComparison.Ordinal) ||
                !FoundationRootCreator.IsGeneratedReference(operation[RetentionOperationPrefix.Length..])
            : !FoundationMetadataLimits.IsOutsideMessageId(operation))
            return false;
        var prefix = $"{reference}/{FoundationRootCreator.OperationDigest(operation)}/";
        if (occurrence.Length != prefix.Length + 32 || !occurrence.StartsWith(prefix, StringComparison.Ordinal)) return false;
        var suffix = occurrence.AsSpan(prefix.Length);
        // Admission emits a lowercase, unhyphenated version-4 GUID.
        return suffix[12] == '4' && "89ab".Contains(suffix[16]) && suffix.IndexOfAnyExcept("0123456789abcdef") < 0;
    }

    internal static Message DecodeDirect(MessageEntity row, string reference, bool replies = false)
    {
        var held = IsHeldDirect(row.Start);
        if (held != (row.ReplyTo is not null) || held &&
            (!replies || ReplyTo.Parse(row.ReplyTo!) is not { Node.Length: > 0, Request.Length: > 0, Deadline: > 0 }))
            throw new StoreException(Failure.Definite, "Unsupported PostgreSQL public reply route.");
        if (row.JobGeneration != 0)
            throw new StoreException(Failure.Definite, "Unsupported PostgreSQL public input envelope.");
        var header = ReadDirectHeader(row.Start, reference, row.Id);
        var payload = header.EscapedPayload ? DurableKeyJson.Read<string>(row.Payload!) : row.Payload;
        return new Message(header.Operation, new(row.Target), header.EscapedKind ?? row.Kind, payload,
            Due: row.Due != 0 || header.ExplicitZeroDue ? row.Due : null, KeepUntil: row.KeepUntil, ReplyTo: row.ReplyTo, TargetCreation: header.Creation)
            { StorageId = row.Id }.WithInboxScope(row.InboxScope);
    }

    private static StoredDirect ReadDirectHeader(string? wire, string reference, string occurrence)
    {
        if (!IsStoredDirect(wire))
            throw new StoreException(Failure.Definite, "Unsupported PostgreSQL public input envelope.");
        var prefix = IsHeldDirect(wire) ? HeldDirectPrefix : DirectPrefix;
        var header = DurableKeyJson.Read<StoredDirect>(wire![prefix.Length..]);
        if (header is not { Creation: 1 } || !MatchesDirectOccurrence(reference, header.Operation, occurrence))
            throw new StoreException(Failure.Definite, "Public input occurrence differs from its admitted identity.");
        return header;
    }

    internal static (string Id, string Kind, long? Due) InspectDirectHeader(KernelDb db, string? wire, string reference,
        string occurrence, string kind, long due, bool hasReply)
    {
        var held = IsHeldDirect(wire);
        if (held != hasReply || held && !db.UsesRootReplyProtocol)
            throw new StoreException(Failure.Definite, "Unsupported PostgreSQL public reply route.");
        var header = ReadDirectHeader(wire, reference, occurrence);
        return (header.Operation, header.EscapedKind ?? kind, due != 0 || header.ExplicitZeroDue ? due : null);
    }

    internal async Task<int> SweepDirectReceiptsAsync(CancellationToken cancellationToken = default)
    {
        await using var db = create();
        await using var pinned = db.UsesRootParkOpening ? await EfStore.BeginPinnedRootOperationAsync(db, cancellationToken) : null;
        var removed = await db.Database.ExecuteSqlRawAsync("""
            DELETE FROM "FoundationDirectReceipts" WHERE "Until" < floor(extract(epoch from clock_timestamp()) * 1000)::bigint
            """, cancellationToken);
        if (pinned is not null) await pinned.CommitAsync(cancellationToken);
        return removed;
    }
}
