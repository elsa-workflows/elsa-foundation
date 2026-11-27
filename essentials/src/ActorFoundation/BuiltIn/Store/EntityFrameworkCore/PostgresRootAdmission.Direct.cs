using Microsoft.EntityFrameworkCore;
using Npgsql;
using static Kernel.EntityFramework.RootAdmissionWire;

namespace Kernel.EntityFramework;

internal sealed partial class PostgresRootAdmission
{
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
        IsHeldDirect(value) || IsAuthoredEvent(value) || value?.StartsWith(KeyedDirectPrefix, StringComparison.Ordinal) == true;

    private static bool IsAuthoredEvent(string? value) => value?.StartsWith(AuthoredEventDirectPrefix, StringComparison.Ordinal) == true;
    private static bool IsHeldKeyedDirect(string? value) => value?.StartsWith(HeldKeyedDirectPrefix, StringComparison.Ordinal) == true;
    private static bool IsHeldDirect(string? value) => value?.StartsWith(HeldDirectPrefix, StringComparison.Ordinal) == true ||
        IsHeldKeyedDirect(value);

    internal enum DirectResult { Accepted, Duplicate, Refused, MissingTarget, Expired }

    internal bool SupportsLiveEventWaits()
    {
        using var db = create();
        return db.UsesRootLiveEventWaits;
    }

    internal async Task<HeldRootAdmission> PublishRequestAsync(HeldRootRequest request, string key, string? payload, CancellationToken token)
    {
        if (!RootEventAdmission.IsValid(request, key, payload)) return HeldRootAdmission.Refused;
        var window = ReceiptWindow(deduplicationWindow, request.KeepFor);
        for (var retry = 0; ; retry++)
        {
            try { return await PublishRequestOnceAsync(request, key, payload, window, token); }
            catch (PostgresException error) when (retry < 2 &&
                error.SqlState is PostgresErrorCodes.DeadlockDetected or PostgresErrorCodes.SerializationFailure)
            {
                // The database rolled back the whole attempt. Keep the caller's original deadline and operation id.
                await Task.Delay(TimeSpan.FromMilliseconds(10 * (retry + 1)), token);
            }
        }
    }

    private async Task<HeldRootAdmission> PublishRequestOnceAsync(HeldRootRequest request, string key, string? payload,
        TimeSpan window, CancellationToken token)
    {
        using var attempt = pressure?.Begin(request.Tenant, RootAdmissionKind.Input);
        await using var db = create();
        RequirePressureProfile(db);
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, token);
        var pin = await PinRootNamespaceAsync(db, loadIssuer: false, token) ??
            throw new StoreFormatException("Live event requests require the pinned root source.");
        await RootEventAdmission.LockPostgresKeysAsync(db, [request.Tenant + "_" + key], token);
        var now = await RootEventAdmission.ReadPostgresClockAsync(db, token);
        var result = await RootEventAdmission.PublishAsync(db, request, key, payload, now, window, attempt, token,
            query => WithAdmissionSource(query, pin, "/*event-source*/", 5,
                """('"Events"'), ('"Subscriptions"'), ('"FoundationRootBirths"'), ('"Instances"'), ('"Messages"')"""));
        await transaction.CommitAsync(token);
        if (result == HeldRootAdmission.Accepted) attempt?.Committed();
        return result;
    }

    internal static async Task<Func<FormattableString, FormattableString>> PinAuthoredEventSourceAsync(
        KernelDb db, uint? marker, CancellationToken token)
    {
        if (!db.UsesRootAuthoredEvents || marker is null)
            throw new StoreFormatException("Authored event publication requires a pinned PostgreSQL store.");
        var pin = await PinRootNamespaceAsync(db, loadIssuer: false, token);
        if (pin?.Marker != marker)
            throw new StoreFormatException("The authored event source changed during the owned commit.");
        if (db.UsesRootEventStarts) await LockEventStartTablesAsync(db, token);
        return sql => sql.Format.Contains(RootEventStarts.Source, StringComparison.Ordinal)
            ? BindEventStarts(sql, pin)
            : WithAdmissionSource(sql, pin, "/*event-source*/", 5,
                """('"Events"'), ('"Subscriptions"'), ('"FoundationRootBirths"'), ('"Instances"'), ('"Messages"')""");
    }

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
        if (request.Tenant != tenant || request.Done || !IsValidReplyNode(node))
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

    private static bool IsValidReplyNode(string node) => !string.IsNullOrWhiteSpace(node) && node.Length <= 256 &&
        !node.Contains('|') && !node.Contains('\0') && !DurableKeyJson.HasUnpairedSurrogate(node);

    private async Task<DirectResult> AdmitDirectCoreAsync(string tenant, string reference, string id, string kind, string? payload,
        long? due, TimeSpan? keepFor, DirectReply? reply, CancellationToken cancellationToken)
    {
        if (!FoundationRootCreator.IsGeneratedReference(reference) || !FoundationMetadataLimits.IsOutsideMessageId(id) || !FoundationMetadataLimits.IsOrdinaryKind(kind) ||
            !FoundationMetadataLimits.FitsContent(payload)) return DirectResult.Refused;
        var window = ReceiptWindow(deduplicationWindow, keepFor);
        var windowMs = (long)window.TotalMilliseconds;
        var digest = FoundationRootCreator.OperationDigest(id);
        using var attempt = pressure?.Begin(tenant, RootAdmissionKind.Input);
        await using var db = create();
        RequirePressureProfile(db);
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
        if (attempt?.IsBusy == true)
        {
            if (await HasReceiptAsync()) return await DuplicateAsync();
            attempt.RequireCapacity();
        }
        var status = await AdmissionStatusAsync(db, address, tenant, reference, cancellationToken, requireCheckpoint: reply is not null);
        if (status is null)
        {
            if (db.UsesRootRetirement && await HasReceiptAsync()) return await DuplicateAsync();
            return await RefuseAsync(DirectResult.MissingTarget);
        }
        var acceptsParkArrival = db.UsesRootParkArrivals && status == InstanceStatus.Faulted && reply is null &&
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

        var target = new InstanceRef(address.Id);
        try { attempt?.Require(target.Partition); }
        catch (RootAdmissionRefused)
        {
            if (await HasReceiptAsync()) return await DuplicateAsync();
            throw;
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
        var (header, body, storedKind) = EncodeDirect(id, address.Creation, kind, payload, due, held: reply is not null);
        if (!FitsEnvelope(header, body, reply?.Address))
            return await RefuseAsync(DirectResult.Refused);

        db.Messages.Add(new MessageEntity
        {
            Id = occurrence, InboxScope = InboxKey.FoundationOccurrenceScope, Target = target.Id, Partition = target.Partition,
            Kind = storedKind, Payload = body, Start = header, Due = due ?? 0,
            KeepUntil = receipt.Single(), ReplyTo = reply?.Address
        });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        attempt?.Committed();
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
        return rows.Select(row => DecodeDirect(row, reference, db.UsesRootReplyProtocol, db.UsesRootLiveEventWaits, db.UsesRootScheduleControls,
            db.UsesRootAuthoredEvents, db.UsesRootKeyedStarts)).ToArray();
    }

    internal static bool MatchesDirectOccurrence(string reference, string operation, string occurrence, bool retentionDelete = false,
        bool keyed = false)
    {
        if (!IsIssuedReferenceShape(reference) || operation is null) return false;
        if (retentionDelete
            ? !operation.StartsWith(RetentionOperationPrefix, StringComparison.Ordinal) ||
                !FoundationRootCreator.IsGeneratedReference(operation[RetentionOperationPrefix.Length..])
            : keyed ? !operation.StartsWith("key/", StringComparison.Ordinal) || !IsIssuedReferenceShape(operation[4..])
                : !FoundationMetadataLimits.IsOutsideMessageId(operation))
            return false;
        var prefix = $"{reference}/{FoundationRootCreator.OperationDigest(operation)}/";
        if (occurrence.Length != prefix.Length + 32 || !occurrence.StartsWith(prefix, StringComparison.Ordinal)) return false;
        var suffix = occurrence.AsSpan(prefix.Length);
        // Admission emits a lowercase, unhyphenated version-4 GUID.
        return suffix[12] == '4' && "89ab".Contains(suffix[16]) && suffix.IndexOfAnyExcept("0123456789abcdef") < 0;
    }

    internal static Message DecodeDirect(MessageEntity row, string reference, bool replies = false, bool liveEventWaits = false,
        bool scheduleControls = false, bool authoredEvents = false, bool keyedStarts = false)
    {
        var held = IsHeldDirect(row.Start);
        var authoredEvent = IsAuthoredEvent(row.Start);
        if (authoredEvent && !authoredEvents)
            throw new StoreException(Failure.Definite, "Unsupported authored root event delivery.");
        if (held != (row.ReplyTo is not null) || held &&
            (!replies || ReplyTo.Parse(row.ReplyTo!) is not { Node.Length: > 0, Request.Length: > 0, Deadline: > 0 }))
            throw new StoreException(Failure.Definite, "Unsupported PostgreSQL public reply route.");
        if (row.JobGeneration != 0)
            throw new StoreException(Failure.Definite, "Unsupported PostgreSQL public input envelope.");
        var header = ReadDirectHeader(row.Start, reference, row.Id, authoredEvent, keyedStarts);
        if (IsHeldKeyedDirect(row.Start) && (row.Due != 0 || header.ExplicitZeroDue || row.Sender is not null))
            throw new StoreException(Failure.Definite, "Unsupported held keyed input envelope.");
        var payload = header.EscapedPayload ? DurableKeyJson.Read<string>(row.Payload!) : row.Payload;
        var kind = header.EscapedKind ?? row.Kind;
        if (header.ScheduleControl is { } control)
        {
            if (!scheduleControls || held || header.EventWait is not null || header.ExplicitZeroDue ||
                row is not { Due: 0, KeepUntil: not null, Sender: null, InboxScope: InboxKey.FoundationOccurrenceScope, EventMatch: null })
                throw new StoreException(Failure.Definite, "Unsupported trusted schedule control envelope.");
            RootScheduleControls.RequireIntent(control, header.Operation, kind, payload);
        }
        EventMatch? match = null;
        if (kind == Kinds.Event || header.EventWait is not null || authoredEvent)
        {
            var separator = header.EventWait?.Key?.IndexOf('_') ?? -1;
            if (!liveEventWaits || !held && !authoredEvent || kind != Kinds.Event || row.Sender is not null || row.Due != 0 ||
                header.ExplicitZeroDue || row.KeepUntil is null || header.EventWait is not { Key: { } key, Wait: { } wait } ||
                header.ScheduleControl is not null || row.InboxScope != InboxKey.FoundationOccurrenceScope || row.EventMatch is not null ||
                separator is not (> 0 and <= 64) ||
                !FoundationMetadataLimits.FitsLiveWaitName(key[(separator + 1)..]) || !FoundationMetadataLimits.FitsLiveWaitName(wait))
                throw new StoreException(Failure.Definite, "Invalid root event delivery.");
            match = new(key, header.Operation, row.Target, wait, header.Creation);
        }
        return new Message(header.Operation, new(row.Target), kind, payload,
            Due: row.Due != 0 || header.ExplicitZeroDue ? row.Due : null, KeepUntil: row.KeepUntil, ReplyTo: row.ReplyTo, TargetCreation: header.Creation)
            {
                StorageId = row.Id, ProjectedEvent = match,
                FoundationScheduleControl = header.ScheduleControl is { } trusted ? (trusted.Tenant, trusted.ActorType) : null
            }.WithInboxScope(row.InboxScope);
    }

    private static StoredDirect ReadDirectHeader(string? wire, string reference, string occurrence, bool authoredEvent, bool keyedStarts)
    {
        if (!IsStoredDirect(wire))
            throw new StoreException(Failure.Definite, "Unsupported PostgreSQL public input envelope.");
        var heldKeyed = IsHeldKeyedDirect(wire);
        var keyed = heldKeyed || wire!.StartsWith(KeyedDirectPrefix, StringComparison.Ordinal);
        if (keyed && !keyedStarts)
            throw new StoreException(Failure.Definite, "Unsupported keyed root delivery.");
        var prefix = authoredEvent ? AuthoredEventDirectPrefix : heldKeyed ? HeldKeyedDirectPrefix :
            keyed ? KeyedDirectPrefix : IsHeldDirect(wire) ? HeldDirectPrefix : DirectPrefix;
        var header = DurableKeyJson.Read<StoredDirect>(wire![prefix.Length..]);
        if (header is not { Creation: 1 } || keyed && (header.EventWait is not null || header.ScheduleControl is not null) || !(authoredEvent
                ? header.Operation is { } id && FoundationSelfSend.IsHandle(id) &&
                    header.EventWait?.Wait is { } wait && occurrence == AuthoredEventOccurrence(reference, id, wait)
                : MatchesDirectOccurrence(reference, header.Operation, occurrence, keyed: keyed)))
            throw new StoreException(Failure.Definite, "Public input occurrence differs from its admitted identity.");
        return header;
    }

    internal static (string Id, string Kind, long? Due) InspectDirectHeader(KernelDb db, string? wire, string reference,
        string occurrence, string kind, long due, bool hasReply)
    {
        var held = IsHeldDirect(wire);
        if (held != hasReply || held && !db.UsesRootReplyProtocol)
            throw new StoreException(Failure.Definite, "Unsupported PostgreSQL public reply route.");
        var authoredEvent = IsAuthoredEvent(wire);
        if (authoredEvent && !db.UsesRootAuthoredEvents)
            throw new StoreException(Failure.Definite, "Unsupported authored root event delivery.");
        var header = ReadDirectHeader(wire, reference, occurrence, authoredEvent, db.UsesRootKeyedStarts);
        if (IsHeldKeyedDirect(wire) && (due != 0 || header.ExplicitZeroDue))
            throw new StoreException(Failure.Definite, "Unsupported held keyed input envelope.");
        if (header.ScheduleControl is not null && !db.UsesRootScheduleControls)
            throw new StoreException(Failure.Definite, "Unsupported trusted schedule control envelope.");
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
