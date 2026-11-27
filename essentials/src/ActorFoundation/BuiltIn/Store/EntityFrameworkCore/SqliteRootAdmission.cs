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

    internal Task<int> VerifyAsync(CancellationToken token) => WriteAsync(async (db, _, ct) =>
    {
        var issuer = await ReadIssuerAsync(db, ct);
        if (!await db.Database.SqlQuery<bool>($"""
            SELECT (count(*) = {issuer.Partitions} AND min("Id") = 0 AND max("Id") = {issuer.Partitions - 1}) AS "Value"
            FROM "Partitions"
            """).SingleAsync(ct))
            throw new StoreFormatException("The SQLite root issuer and installed virtual partitions must agree.");
        return issuer.Partitions;
    }, token);

    internal async Task<Contract.Admission> StartAsync(string tenant, string operation, StartSpec spec, long? due, TimeSpan? keepFor,
        CancellationToken token) => (await StartCoreAsync(tenant, operation, spec, due, keepFor, null, token))!;

    internal async Task<string?> StartRequestAsync(HeldRootRequest request, StartSpec spec, CancellationToken token,
        RootAdmissionPressure.Attempt? reserved = null)
    {
        if (!request.IsOpen) throw new ArgumentException("A held SQLite start requires an open caller bound to its tenant.", nameof(request));
        return (await StartCoreAsync(request.Tenant, request.Caller.Id, spec, null, request.KeepFor, request, token, reserved))?.Actor?.Id;
    }

    private async Task<Contract.Admission?> StartCoreAsync(string tenant, string operation, StartSpec spec, long? due, TimeSpan? keepFor,
        HeldRootRequest? reply, CancellationToken token, RootAdmissionPressure.Attempt? reserved = null)
    {
        RequireCreator(operation, spec);
        var window = ReceiptWindow(deduplicationWindow, keepFor);
        var digest = FoundationRootCreator.OperationDigest(operation);
        using var localAttempt = reserved is null ? pressure?.Begin(tenant, RootAdmissionKind.Start) : null;
        var attempt = reserved ?? localAttempt;
        return await WriteAsync<Contract.Admission?>(async (db, ct) =>
        {
            // One clock sample, after taking the admission transaction, drives both the deadline and receipt boundary.
            var admission = await db.Database.SqlQuery<StartAdmission>(EfStore.UseSqliteRootClock($"""
                WITH clock AS MATERIALIZED (SELECT /*sqlite-root-clock*/ AS "Now")
                SELECT clock."Now", receipt."Reference", issuer."Key", issuer."Partitions"
                FROM clock
                LEFT JOIN "FoundationRootReceipts" receipt ON receipt."Tenant" = {tenant}
                    AND receipt."Operation" = {digest} AND receipt."Until" >= clock."Now"
                LEFT JOIN "FoundationRootIssuer" issuer ON receipt."Reference" IS NULL
                    AND issuer."Singleton" = 1 AND length(issuer."Key") = 32 AND issuer."Partitions" > 0
                """)).SingleAsync(ct);
            var now = admission.Now;
            if (reply is not null && reply.Caller.Deadline <= now) return null;
            if (admission.Reference is { } existing)
                return new(Contract.AdmissionOutcome.Duplicate, new Contract.ActorRef(existing));

            attempt?.RequireCapacity();
            if (admission is not { Key: { } key, Partitions: { } partitions })
                throw new StoreFormatException("The SQLite root issuer is missing or invalid.");
            var name = tenant + "_" + Guid.NewGuid().ToString("N");
            var preferred = InstanceRef.Spread(name, partitions);
            var actor = InstanceRef.In(attempt?.Place(preferred, partitions) ?? preferred, name);
            var reference = FoundationRootCreator.ReferenceFor(key, tenant, new(actor.Id, 1));
            var creator = FoundationRootCreator.Create(actor, operation, digest, spec, due, reply?.Address);
            var (header, payload) = EncodeCreator(creator);
            if (!FitsEnvelope(header, payload, creator.ReplyTo))
                throw new MetadataLimitExceeded(nameof(spec), EfStore.RootContentBytes);
            attempt?.Require(actor.Partition);

            var until = checked(now + (long)window.TotalMilliseconds);
            var fingerprint = CreationProtocol.Fingerprint(creator.Start, creator.Kind, creator.Payload)!;
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
            return new(Contract.AdmissionOutcome.Accepted, new Contract.ActorRef(reference));
        }, token, attempt);
    }

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

            var canReceive = target.Status == (int)InstanceStatus.Running || reply is null &&
                (!target.HasCheckpoint && target.Pending || db.UsesRootParkArrivals && target.Status == (int)InstanceStatus.Faulted && target.Parked);
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

    internal async ValueTask<long> ReadReplyTimeAsync(CancellationToken token)
    {
        await using var db = create();
        await db.Database.OpenConnectionAsync(token);
        await EfStore.RequireSqliteRootAdmissionAsync(db, definitions, token);
        return await ReadClockAsync(db, token);
    }

    private static Task<long> ReadClockAsync(KernelDb db, CancellationToken token) =>
        db.Database.SqlQueryRaw<long>(
            "SELECT " + SqliteCommitDialect.MillisecondClock + """ AS "Value" """).SingleAsync(token);

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
