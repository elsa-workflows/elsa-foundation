using Kernel.Foundation.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Contract = Kernel.Foundation.Contracts;
using static Kernel.EntityFramework.RootAdmissionWire;

namespace Kernel.EntityFramework;

internal sealed partial class SqliteRootAdmission(Func<KernelDb> create, TimeSpan deduplicationWindow, SqliteAdmissionDefinitions definitions,
    RootAdmissionPressure? pressure = null, string? scheduleActorType = null)
{
    private sealed record Issuer(byte[] Key, int Partitions);
    private sealed record StartAdmission(long Now, string? Reference, byte[]? Key, int? Partitions);
    private sealed record Target(string Actor, int Partition, bool HasCheckpoint, int? Status, bool Terminal, bool Pending, bool Parked, string? Header);
    private sealed class PreparationExpired : Exception;

    internal Task<int> VerifyAsync(CancellationToken token) => VerifyAsync(bindOwner: false, auxiliaryActor: null, token);

    internal Task<int> VerifyOwnerAsync(string? auxiliaryActor, CancellationToken token) =>
        VerifyAsync(bindOwner: true, auxiliaryActor, token);

    private Task<int> VerifyAsync(bool bindOwner, string? auxiliaryActor, CancellationToken token) => WriteAsync(async (db, _, ct) =>
    {
        var issuer = await ReadIssuerAsync(db, ct);
        if (!await db.Database.SqlQuery<bool>($"""
            SELECT (count(*) = {issuer.Partitions} AND min("Id") = 0 AND max("Id") = {issuer.Partitions - 1}) AS "Value"
            FROM "Partitions"
            """).SingleAsync(ct))
            throw new StoreFormatException("The SQLite root issuer and installed virtual partitions must agree.");
        if (bindOwner)
        {
            if (RootScheduleOwnerMode.Applies(db))
                await RootScheduleOwnerMode.BindAsync(db, auxiliaryActor, ct);
            else if (db.UsesRootScheduleControls && auxiliaryActor is null)
                throw new NotSupportedException("This SQLite root profile requires a schedule owner.");
        }
        return issuer.Partitions;
    }, token);

    internal async Task<Contract.Admission> StartAsync(string tenant, string operation, StartSpec spec, long? due, TimeSpan? keepFor,
        CancellationToken token) => (await StartCoreAsync(tenant, operation, spec, due, keepFor, null, token))!;

    internal async Task<string?> StartRequestAsync(HeldRootRequest request, StartSpec spec, CancellationToken token,
        RootAdmissionPressure.Attempt? reserved = null) =>
        (await StartProtectedAsync(request.Tenant, request.Caller.Id, spec, request, token, reserved, prepared: null))?.Actor?.Id;

    internal async Task<Contract.Admission?> StartProtectedAsync(string tenant, string operation, StartSpec spec, HeldRootRequest? reply,
        CancellationToken token, RootAdmissionPressure.Attempt? reserved, PreparedRootStart? prepared, string? eventTarget = null)
    {
        if (reply is { IsOpen: false }) throw new ArgumentException("A held SQLite start requires an open caller bound to its tenant.", nameof(reply));
        try
        {
            return await StartCoreAsync(tenant, operation, spec, null, reply?.KeepFor, reply, token, reserved, prepared, eventTarget);
        }
        catch (PreparationExpired) { return null; }
    }

    private async Task<Contract.Admission?> StartCoreAsync(string tenant, string operation, StartSpec spec, long? due, TimeSpan? keepFor,
        HeldRootRequest? reply, CancellationToken token, RootAdmissionPressure.Attempt? reserved = null, PreparedRootStart? prepared = null,
        string? eventTarget = null)
    {
        RequireCreator(operation, spec);
        var window = ReceiptWindow(deduplicationWindow, keepFor);
        var storedOperation = eventTarget is not null ? RootEventStarts.Operation(eventTarget) :
            prepared is not null && reply is null ? RootPreparedStarts.OneWayOperation(operation) : operation;
        var digest = FoundationRootCreator.OperationDigest(storedOperation);
        using var localAttempt = reserved is null ? pressure?.Begin(tenant, RootAdmissionKind.Start) : null;
        var attempt = reserved ?? localAttempt;
        return await WriteAsync<Contract.Admission?>(async (db, ct) =>
        {
            if (eventTarget is not null)
            {
                if (prepared is null) throw new InvalidOperationException("An event start requires a protected preparation.");
                if (await RootEventStarts.CheckTargetAsync(db, tenant, eventTarget, spec, bind: null, lockRow: true, ct) is { } settled)
                {
                    await RootPreparedStarts.ConsumeAsync(db, prepared, ct);
                    return new(Contract.AdmissionOutcome.Duplicate, new Contract.ActorRef(settled));
                }
            }
            var admission = await ReadStartAdmissionAsync(db, tenant, digest, ct);
            var now = admission.Now;
            var deadline = reply?.Caller.Deadline ?? prepared?.Deadline;
            if (deadline <= now) return null;
            if (admission.Reference is { } existing)
            {
                if (eventTarget is not null) throw new StoreFormatException("An event start receipt has no settled target.");
                if (prepared is not null)
                {
                    await RootPreparedStarts.ConsumeAsync(db, prepared, ct);
                    await RequireBeforePreparedDeadlineAsync(db, prepared, ct);
                }
                return new(Contract.AdmissionOutcome.Duplicate, new Contract.ActorRef(existing));
            }

            attempt?.RequireCapacity();
            if (admission is not { Key: { } key, Partitions: { } partitions })
                throw new StoreFormatException("The SQLite root issuer is missing or invalid.");
            var actor = prepared is null ? PlaceStart(tenant, partitions, attempt) : new InstanceRef(prepared.Actor);
            if (prepared is not null) _ = attempt?.Place(actor.Partition, partitions);
            var reference = FoundationRootCreator.ReferenceFor(key, tenant, new(actor.Id, 1));
            var creator = FoundationRootCreator.Create(actor, storedOperation, digest, spec, due, reply?.Address);
            var (header, payload) = EncodeCreator(creator);
            if (!FitsEnvelope(header, payload, creator.ReplyTo))
                throw new MetadataLimitExceeded(nameof(spec), EfStore.RootContentBytes);
            attempt?.Require(actor.Partition);

            var until = checked(now + (long)window.TotalMilliseconds);
            var fingerprint = CreationProtocol.Fingerprint(creator.Start, creator.Kind, creator.Payload)!;
            if (prepared is not null)
            {
                var expected = new PreparedRootStart(tenant, reference, actor.Id, actor.Partition, digest, fingerprint,
                    reply?.Caller.Deadline ?? prepared.Deadline);
                if (prepared != expected || !await RootPreparedStarts.ConsumeAsync(db, prepared, ct)) return null;
            }
            RequireOne(await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "FoundationRootBirths" ("Tenant", "Reference", "Actor", "Partition", "Creation", "Creator", "Fingerprint")
                VALUES ({tenant}, {reference}, {actor.Id}, {actor.Partition}, 1, {creator.InboxKey.Id}, {fingerprint})
                """, ct));
            RequireOne(await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "FoundationRootReceipts" ("Tenant", "Operation", "Reference", "Until")
                VALUES ({tenant}, {digest}, {reference}, {until})
                ON CONFLICT ("Tenant", "Operation") DO UPDATE SET "Reference" = excluded."Reference", "Until" = excluded."Until"
                """, ct));
            await EnqueueAsync(db, actor.Id, actor.Partition, creator.InboxKey.Id, 0, creator.Kind, header, payload, due,
                keepUntil: null, replyTo: creator.ReplyTo, token: ct);
            if (eventTarget is not null) await RootEventStarts.SettleAsync(db, tenant, eventTarget, spec, prepared!, bind: null, ct);
            if (prepared is not null) await RequireBeforePreparedDeadlineAsync(db, prepared, ct);
            return new(Contract.AdmissionOutcome.Accepted, new Contract.ActorRef(reference));
        }, token, attempt);
    }

    internal Task<(string? Existing, PreparedRootStart? Stage, PendingRootStart? Offer)> PrepareProtectedStartAsync(
        string tenant, string operation, StartSpec spec, HeldRootRequest? reply, RootAdmissionPressure.Attempt? attempt, CancellationToken token,
        string? eventTarget = null)
    {
        if (reply is { IsOpen: false }) throw new ArgumentException("A held SQLite start requires an open caller bound to its tenant.", nameof(reply));
        RequireCreator(operation, spec);
        var storedOperation = eventTarget is not null ? RootEventStarts.Operation(eventTarget) :
            reply is null ? RootPreparedStarts.OneWayOperation(operation) : operation;
        var digest = FoundationRootCreator.OperationDigest(storedOperation);
        return WriteAsync<(string?, PreparedRootStart?, PendingRootStart?)>(async (db, ct) =>
        {
            if (eventTarget is not null &&
                await RootEventStarts.CheckTargetAsync(db, tenant, eventTarget, spec, bind: null, lockRow: false, ct) is { } settled)
                return (settled, null, null);
            var admission = await ReadStartAdmissionAsync(db, tenant, digest, ct);
            var deadline = reply?.Caller.Deadline ?? RootPreparedStarts.OneWayDeadline(admission.Now);
            if (deadline <= admission.Now) return default;
            if (admission.Reference is { } existing)
            {
                if (eventTarget is not null) throw new StoreFormatException("An event start receipt has no settled target.");
                return (existing, null, null);
            }
            attempt?.RequireCapacity();
            if (admission is not { Key: { } key, Partitions: { } partitions })
                throw new StoreFormatException("The SQLite root issuer is missing or invalid.");
            var actor = PlaceStart(tenant, partitions, attempt);
            attempt?.Require(actor.Partition);
            attempt?.RolledBack(); // Charge the original operation only when its creator commits.
            var creator = FoundationRootCreator.Create(actor, storedOperation, digest, spec, null, reply?.Address);
            var (header, payload) = EncodeCreator(creator);
            if (!FitsEnvelope(header, payload, creator.ReplyTo))
                throw new MetadataLimitExceeded(nameof(spec), EfStore.RootContentBytes);

            var stage = new PreparedRootStart(tenant, FoundationRootCreator.ReferenceFor(key, tenant, new(actor.Id, 1)),
                actor.Id, actor.Partition, digest, CreationProtocol.Fingerprint(creator.Start, creator.Kind, creator.Payload)!, deadline);
            if (!await RootPreparedStarts.RecordAsync(db, stage, ct))
                throw new StoreFormatException("The SQLite root preparation was not recorded.");
            return (null, stage, RootPreparedStarts.Offer(stage, spec, key));
        }, token, attempt);
    }

    internal Task<EventRegistrationResult> ApplyEventStartsAsync(string tenant, string slot, long revision,
        IReadOnlyList<EventStartRegistration> registrations, CancellationToken token) =>
        WriteAsync((db, ct) => RootEventStarts.ApplyAsync(db, tenant, slot, revision, registrations, bind: null, ct), token);

    internal Task<IReadOnlyList<EventStartTarget>> ListEventStartsAsync(string tenant, string? after, CancellationToken token) =>
        InAdmissionTransactionAsync((db, ct) => RootEventStarts.ListAsync(db, tenant, after, bind: null, ct), token, readOnly: true);

    internal Task<long> ReadEventStartTailAsync(string tenant, CancellationToken token) =>
        InAdmissionTransactionAsync((db, ct) => RootEventStarts.ReadPendingTailAsync(db, tenant, bind: null, ct), token, readOnly: true);

    internal Task<IReadOnlyList<EventStartScanItem>> ReadEventStartPageAsync(string tenant, long after, long through, int limit,
        CancellationToken token) =>
        InAdmissionTransactionAsync((db, ct) => RootEventStarts.ReadPendingPageAsync(db, tenant, after, through, limit, bind: null, ct),
            token, readOnly: true);

    internal Task<(EventStartTarget? Pending, string? Settled)> ReadEventStartAsync(string tenant, string target, CancellationToken token) =>
        InAdmissionTransactionAsync((db, ct) => RootEventStarts.ReadWorkAsync(db, tenant, target, bind: null, ct), token, readOnly: true);

    internal Task<bool> ConfirmEventStartRegistrationDrainedAsync(string tenant, string slot, long revision, CancellationToken token) =>
        InAdmissionTransactionAsync((db, ct) => RootEventStarts.ConfirmDrainedAsync(db, tenant, slot, revision, bind: null, ct), token, readOnly: true);

    internal Task<(bool Expired, string? Existing)> DisarmHeldStartAsync(PreparedRootStart stage, CancellationToken token) =>
        WriteAsync(async (db, ct) =>
        {
            var admission = await ReadStartAdmissionAsync(db, stage.Tenant, stage.Operation, ct);
            await RootPreparedStarts.ConsumeAsync(db, stage, ct);
            return (admission.Now >= stage.Deadline, admission.Reference);
        }, token);

    internal Task<int> PruneSettledEventStartsAsync(string tenant, CancellationToken token) =>
        WriteAsync((db, _, ct) => RootEventStarts.PruneSettledAsync(db, tenant, bind: null, ct), token);

    internal Task<int> PruneHeldStartsAsync(Contract.BoundScope scope, CancellationToken token) =>
        WriteAsync((db, now, ct) => RootPreparedStarts.PruneAsync(db, scope.Tenant, now, ct), token);

    internal Task<RootStartProof> ConfirmHeldStartAsync(Contract.BoundScope scope, Contract.ActorRef actor, string issuance,
        CancellationToken token) =>
        WriteAsync(async (db, now, ct) =>
        {
            if (RootPreparedStarts.Verify(await ReadRootIssuerKeyAsync(db, ct), scope, actor, issuance) is not { } stage)
                return RootStartProof.Unresolved;
            await RootPreparedStarts.ExpireAsync(db, stage, now, ct);
            return await RootPreparedStarts.ReadProofAsync(db, stage, ct);
        }, token);

    private static InstanceRef PlaceStart(string tenant, int partitions, RootAdmissionPressure.Attempt? attempt)
    {
        var name = tenant + "_" + Guid.NewGuid().ToString("N");
        var preferred = InstanceRef.Spread(name, partitions);
        return InstanceRef.In(attempt?.Place(preferred, partitions) ?? preferred, name);
    }

    private static Task<StartAdmission> ReadStartAdmissionAsync(KernelDb db, string tenant, string digest, CancellationToken ct) =>
        db.Database.SqlQuery<StartAdmission>(EfStore.UseSqliteRootClock($"""
                WITH clock AS MATERIALIZED (SELECT /*sqlite-root-clock*/ AS "Now")
                SELECT clock."Now", receipt."Reference", issuer."Key", issuer."Partitions"
                FROM clock
                LEFT JOIN "FoundationRootReceipts" receipt ON receipt."Tenant" = {tenant}
                    AND receipt."Operation" = {digest} AND receipt."Until" >= clock."Now"
                LEFT JOIN "FoundationRootIssuer" issuer ON receipt."Reference" IS NULL
                    AND issuer."Singleton" = 1 AND length(issuer."Key") = 32 AND issuer."Partitions" > 0
                """)).SingleAsync(ct);

    internal async Task<Contract.Admission> SendAsync(string tenant, Contract.ActorRef reference, string id, string kind, string? payload,
        long? due, TimeSpan? keepFor, CancellationToken token) =>
        (await SendCoreAsync(tenant, reference, id, kind, payload, due, keepFor, null, token))!;

    internal async Task<HeldRootAdmission> SendRequestAsync(HeldRootRequest request, string reference, string kind, string? payload,
        CancellationToken token, RootAdmissionPressure.Attempt? reserved = null)
    {
        if (!request.IsOpen) return HeldRootAdmission.Refused;
        return (await SendCoreAsync(request.Tenant, new(reference), request.Caller.Id, kind, payload, null, request.KeepFor, request, token, reserved))?.Outcome switch
        {
            Contract.AdmissionOutcome.Accepted => HeldRootAdmission.Accepted,
            Contract.AdmissionOutcome.Duplicate => HeldRootAdmission.Duplicate,
            null => HeldRootAdmission.Expired,
            _ => HeldRootAdmission.Refused
        };
    }

    internal Task<HeldRootAdmission> PublishRequestAsync(HeldRootRequest request, string key, string? payload,
        CancellationToken token, RootAdmissionPressure.Attempt? reserved)
    {
        if (!RootEventAdmission.IsValid(request, key, payload)) return Task.FromResult(HeldRootAdmission.Refused);
        var window = ReceiptWindow(deduplicationWindow, request.KeepFor);
        return WriteAsync((db, now, ct) => RootEventAdmission.PublishAsync(db, request, key, payload, now, window, reserved, ct),
            token, reserved);
    }

    private async Task<Contract.Admission?> SendCoreAsync(string tenant, Contract.ActorRef reference, string id, string kind, string? payload,
        long? due, TimeSpan? keepFor, HeldRootRequest? reply, CancellationToken token, RootAdmissionPressure.Attempt? reserved = null)
    {
        if (!FoundationRootCreator.IsGeneratedReference(reference.Id) || !FoundationMetadataLimits.IsOutsideMessageId(id) ||
            !FoundationMetadataLimits.IsOrdinaryKind(kind) || !FoundationMetadataLimits.FitsContent(payload))
            return new(Contract.AdmissionOutcome.Refused);

        var window = ReceiptWindow(deduplicationWindow, keepFor);
        var digest = FoundationRootCreator.OperationDigest(id);
        using var localAttempt = reserved is null ? pressure?.Begin(tenant, RootAdmissionKind.Input) : null;
        var attempt = reserved ?? localAttempt;
        return await WriteAsync<Contract.Admission?>(async (db, now, ct) =>
        {
            var target = await ReadTargetAsync(db, tenant, reference.Id, ct);
            if (target is null)
                return new(Contract.AdmissionOutcome.Refused, Reason: Contract.RefusalReason.TargetMismatch);

            var canReceive = AcceptsDirectInput(db, target, reply is not null);
            if (canReceive && reply is not null && reply.Caller.Deadline <= now) return null;
            var liveReceipt = (canReceive || target.Terminal || target.Status is null && db.UsesRootRetirement)
                && await db.Database.SqlQuery<bool>($"""
                    SELECT EXISTS (SELECT 1 FROM "FoundationDirectReceipts"
                        WHERE "Reference" = {reference.Id} AND "Operation" = {digest} AND "Until" >= {now}) AS "Value"
                    """).SingleAsync(ct);
            if (liveReceipt) return new(Contract.AdmissionOutcome.Duplicate, reference);
            if (!canReceive)
                return new(Contract.AdmissionOutcome.Refused,
                    Reason: target.Status is null ? Contract.RefusalReason.TargetMismatch : null);

            var address = reply?.Address;
            var (header, body, storedKind) = EncodeDirect(id, 1, kind, payload, due, held: address is not null);
            if (!FitsEnvelope(header, body, address)) return new(Contract.AdmissionOutcome.Refused);
            attempt?.Require(target.Partition);

            var until = checked(now + (long)window.TotalMilliseconds);
            var occurrence = $"{reference.Id}/{digest}/{Guid.NewGuid():N}";
            RequireOne(await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "FoundationDirectReceipts" ("Reference", "Operation", "Until")
                VALUES ({reference.Id}, {digest}, {until})
                ON CONFLICT ("Reference", "Operation") DO UPDATE SET "Until" = excluded."Until"
                """, ct));
            await EnqueueAsync(db, target.Actor, target.Partition, occurrence, InboxKey.FoundationOccurrenceScope, storedKind,
                header, body, due, until, address, ct);
            return new(Contract.AdmissionOutcome.Accepted, reference);
        }, token, attempt);
    }

    private static bool AcceptsDirectInput(KernelDb db, Target target, bool held) =>
        target.Status == (int)InstanceStatus.Running || !held &&
        (!target.HasCheckpoint && target.Pending || db.UsesRootParkArrivals && target.Status == (int)InstanceStatus.Faulted && target.Parked);

    internal async ValueTask<long> ReadReplyTimeAsync(CancellationToken token)
    {
        await using var db = create();
        await db.Database.OpenConnectionAsync(token);
        await EfStore.RequireSqliteRootAdmissionAsync(db, definitions, token);
        return await ReadClockAsync(db, token);
    }

    internal static Task<long> ReadClockAsync(KernelDb db, CancellationToken token) =>
        db.Database.SqlQueryRaw<long>(
            "SELECT " + SqliteCommitDialect.MillisecondClock + """ AS "Value" """).SingleAsync(token);

    private static async Task RequireBeforePreparedDeadlineAsync(KernelDb db, PreparedRootStart prepared, CancellationToken token)
    {
        // Unwind the transaction so expiry cannot commit the prepared-stage deletion, birth, receipt or enqueue.
        if (await ReadClockAsync(db, token) >= prepared.Deadline) throw new PreparationExpired();
    }

    private Task<T> WriteAsync<T>(Func<KernelDb, long, CancellationToken, Task<T>> write, CancellationToken token,
        RootAdmissionPressure.Attempt? attempt = null) =>
        WriteAsync(async (db, ct) => await write(db, await ReadClockAsync(db, ct), ct), token, attempt);

    private Task<T> WriteAsync<T>(Func<KernelDb, CancellationToken, Task<T>> write, CancellationToken token,
        RootAdmissionPressure.Attempt? attempt = null)
        => InAdmissionTransactionAsync(write, token, attempt);

    private async Task<T> InAdmissionTransactionAsync<T>(Func<KernelDb, CancellationToken, Task<T>> work, CancellationToken token,
        RootAdmissionPressure.Attempt? attempt = null, bool readOnly = false)
    {
        await using var db = create();
        if (!db.UsesSqliteShippedInstall) throw new NotSupportedException("Root admission requires the SQLite shipped context.");
        if (pressure is not null && !db.UsesRootDueBacklog)
            throw new StoreFormatException("Admission pressure requires the due-backlog store profile.");
        await db.Database.OpenConnectionAsync(token);
        token.ThrowIfCancellationRequested();
        // Inventory and busy attempts only read a snapshot. Writers serialize before reading any receipt.
        await using var transaction = ((SqliteConnection)db.Database.GetDbConnection())
            .BeginTransaction(deferred: readOnly || attempt?.IsBusy == true);
        token.ThrowIfCancellationRequested();
        await using var enlistment = await db.Database.UseTransactionAsync(transaction, token);
        await EfStore.RequireSqliteRootAdmissionAsync(db, definitions, token);
        var result = await work(db, token);
        token.ThrowIfCancellationRequested();
        await transaction.CommitAsync(token);
        attempt?.Committed();
        return result;
    }

    private static async Task<Issuer> ReadIssuerAsync(KernelDb db, CancellationToken token) =>
        await db.Database.SqlQueryRaw<Issuer>("""
            SELECT "Key", "Partitions" FROM "FoundationRootIssuer"
            WHERE "Singleton" = 1 AND length("Key") = 32 AND "Partitions" > 0
            """).SingleOrDefaultAsync(token) ??
        throw new StoreFormatException("The SQLite root issuer is missing or invalid.");

    internal static async Task<byte[]> ReadRootIssuerKeyAsync(KernelDb db, CancellationToken token) =>
        (await ReadIssuerAsync(db, token)).Key;

    private static Task<Target?> ReadTargetAsync(KernelDb db, string tenant, string reference, CancellationToken token,
        bool includeRetentionHeader = false)
    {
        var family = db.RootStateFamily;
        return db.Database.SqlQuery<Target>($"""
            SELECT b."Actor", b."Partition", i."Id" IS NOT NULL AS "HasCheckpoint",
                CASE WHEN i."Partition" = b."Partition" AND i."FoundationTenant" = b."Tenant" AND i."FoundationReference" = b."Reference"
                    AND NOT i."Retired"
                    THEN i."Status" END AS "Status",
                (i."Status" = {(int)InstanceStatus.Finished} OR i."Status" = {(int)InstanceStatus.Faulted} AND {db.UsesRootParkOpening}
                    OR ((i."Status" = {(int)InstanceStatus.Faulted} AND {family.Ends(InstanceStatus.Faulted)})
                        OR (i."Status" = {(int)InstanceStatus.Cancelled} AND {family.Ends(InstanceStatus.Cancelled)}))
                        AND instr(i."State", {family.Prefix}) = 1 AND i."FoundationTerminalAt" IS NOT NULL
                        AND i."FoundationTerminalRevision" = i."Revision") IS TRUE AS "Terminal",
                EXISTS (SELECT 1 FROM "Messages" m
                    WHERE m."Id" = b."Creator" AND m."Target" = b."Actor" AND m."Partition" = b."Partition"
                        AND m."InboxScope" = 0 AND NOT m."Done") AS "Pending",
                i."FoundationParkAt" IS NOT NULL AS "Parked",
                CASE WHEN {includeRetentionHeader} THEN i."FoundationHeader" END AS "Header"
            FROM "FoundationRootBirths" b
            LEFT JOIN "Instances" i ON i."Id" = b."Actor"
            WHERE b."Tenant" = {tenant} AND b."Reference" = {reference} AND b."Creation" = 1
            """).SingleOrDefaultAsync(token);
    }

    internal static async Task EnqueueAsync(KernelDb db, string actor, int partition, string id, long scope, string kind,
        string header, string? payload, long? due, long? keepUntil, string? replyTo, CancellationToken token)
    {
        RequireOne(await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "Messages" ("Id", "InboxScope", "Target", "Partition", "Seq", "Kind", "Due", "Payload", "Start",
                "Done", "Seen", "Attempts", "Trial", "JobGeneration", "KeepUntil", "ReplyTo")
            VALUES ({id}, {scope}, {actor}, {partition}, (SELECT coalesce(max("Seq"), 0) + 1 FROM "Messages"),
                {kind}, {due ?? 0}, {payload}, {header}, 0, 0, 0, 0, 0, {keepUntil}, {replyTo})
            """, token));
    }

    private static void RequireOne(int count)
    {
        if (count != 1) throw new InvalidOperationException("A SQLite root admission must write one row per receipt, birth or message.");
    }
}
