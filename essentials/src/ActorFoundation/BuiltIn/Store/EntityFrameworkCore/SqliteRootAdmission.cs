using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Contract = Kernel.Foundation.Contracts;
using static Kernel.EntityFramework.RootAdmissionWire;

namespace Kernel.EntityFramework;

internal sealed partial class SqliteRootAdmission(Func<KernelDb> create, TimeSpan deduplicationWindow, SqliteAdmissionDefinitions definitions)
{
    private sealed record Issuer(byte[] Key, int Partitions);
    private sealed record Target(string Actor, int Partition, bool HasCheckpoint, int? Status, bool Terminal, bool Pending);

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

    internal async Task<string?> StartRequestAsync(HeldRootRequest request, StartSpec spec, CancellationToken token)
    {
        if (!request.IsOpen) throw new ArgumentException("A held SQLite start requires an open caller bound to its tenant.", nameof(request));
        return (await StartCoreAsync(request.Tenant, request.Caller.Id, spec, null, request.KeepFor, request, token))?.Actor?.Id;
    }

    private Task<Contract.Admission?> StartCoreAsync(string tenant, string operation, StartSpec spec, long? due, TimeSpan? keepFor,
        HeldRootRequest? reply, CancellationToken token)
    {
        RequireCreator(operation, spec);
        var window = ReceiptWindow(deduplicationWindow, keepFor);
        var digest = FoundationRootCreator.OperationDigest(operation);
        return WriteAsync<Contract.Admission?>(async (db, now, ct) =>
        {
            if (reply is not null && reply.Caller.Deadline <= now) return null;
            var existing = await db.Database.SqlQuery<string>($"""
                SELECT "Reference" AS "Value" FROM "FoundationRootReceipts"
                WHERE "Tenant" = {tenant} AND "Operation" = {digest} AND "Until" >= {now}
                """).SingleOrDefaultAsync(ct);
            if (existing is not null)
                return new(Contract.AdmissionOutcome.Duplicate, new Contract.ActorRef(existing));

            var issuer = await ReadIssuerAsync(db, ct);
            var name = tenant + "_" + Guid.NewGuid().ToString("N");
            var actor = InstanceRef.In(InstanceRef.Spread(name, issuer.Partitions), name);
            var reference = FoundationRootCreator.ReferenceFor(issuer.Key, tenant, new(actor.Id, 1));
            var creator = FoundationRootCreator.Create(actor, operation, digest, spec, due, reply?.Address);
            var (header, payload) = EncodeCreator(creator);
            if (!FitsEnvelope(header, payload, creator.ReplyTo))
                throw new MetadataLimitExceeded(nameof(spec), EfStore.RootContentBytes);

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
        }, token);
    }

    internal async Task<Contract.Admission> SendAsync(string tenant, Contract.ActorRef reference, string id, string kind, string? payload,
        long? due, TimeSpan? keepFor, CancellationToken token) =>
        (await SendCoreAsync(tenant, reference, id, kind, payload, due, keepFor, null, token))!;

    internal async Task<HeldRootAdmission> SendRequestAsync(HeldRootRequest request, string reference, string kind, string? payload,
        CancellationToken token)
    {
        if (!request.IsOpen) return HeldRootAdmission.Refused;
        return (await SendCoreAsync(request.Tenant, new(reference), request.Caller.Id, kind, payload, null, request.KeepFor, request, token))?.Outcome switch
        {
            Contract.AdmissionOutcome.Accepted => HeldRootAdmission.Accepted,
            Contract.AdmissionOutcome.Duplicate => HeldRootAdmission.Duplicate,
            null => HeldRootAdmission.Expired,
            _ => HeldRootAdmission.Refused
        };
    }

    private Task<Contract.Admission?> SendCoreAsync(string tenant, Contract.ActorRef reference, string id, string kind, string? payload,
        long? due, TimeSpan? keepFor, HeldRootRequest? reply, CancellationToken token)
    {
        if (!FoundationRootCreator.IsGeneratedReference(reference.Id) || !FoundationMetadataLimits.IsOutsideMessageId(id) ||
            !FoundationMetadataLimits.IsOrdinaryKind(kind) || !FoundationMetadataLimits.FitsContent(payload))
            return Task.FromResult<Contract.Admission?>(new(Contract.AdmissionOutcome.Refused));

        var window = ReceiptWindow(deduplicationWindow, keepFor);
        var digest = FoundationRootCreator.OperationDigest(id);
        return WriteAsync<Contract.Admission?>(async (db, now, ct) =>
        {
            var target = await ReadTargetAsync(db, tenant, reference.Id, ct);
            if (target is null)
                return new(Contract.AdmissionOutcome.Refused, Reason: Contract.RefusalReason.TargetMismatch);

            var canReceive = target.Status == (int)InstanceStatus.Running || reply is null && !target.HasCheckpoint && target.Pending;
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
        }, token);
    }

    internal async ValueTask<long> ReadReplyTimeAsync(CancellationToken token)
    {
        await using var db = create();
        await db.Database.OpenConnectionAsync(token);
        await EfStore.RequireSqliteRootAdmissionAsync(db, token);
        await definitions.RequireAsync(db, token);
        return await ReadClockAsync(db, token);
    }

    private static Task<long> ReadClockAsync(KernelDb db, CancellationToken token) =>
        db.Database.SqlQueryRaw<long>(
            "SELECT " + SqliteCommitDialect.MillisecondClock + """ AS "Value" """).SingleAsync(token);

    private async Task<T> WriteAsync<T>(Func<KernelDb, long, CancellationToken, Task<T>> write, CancellationToken token)
    {
        await using var db = create();
        if (!db.UsesSqliteShippedInstall) throw new NotSupportedException("Root admission requires the SQLite shipped context.");
        await db.Database.OpenConnectionAsync(token);
        token.ThrowIfCancellationRequested();
        // Competing callers serialize before any receipt or checkpoint is read. SQLite cannot upgrade a stale read snapshot.
        await using var transaction = ((SqliteConnection)db.Database.GetDbConnection()).BeginTransaction(deferred: false);
        token.ThrowIfCancellationRequested();
        await using var enlistment = await db.Database.UseTransactionAsync(transaction, token);
        await EfStore.RequireSqliteRootAdmissionAsync(db, token);
        await definitions.RequireAsync(db, token);
        var now = await ReadClockAsync(db, token);
        var result = await write(db, now, token);
        token.ThrowIfCancellationRequested();
        await transaction.CommitAsync(token);
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

    private static Task<Target?> ReadTargetAsync(KernelDb db, string tenant, string reference, CancellationToken token)
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
                        AND m."InboxScope" = 0 AND NOT m."Done") AS "Pending"
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
