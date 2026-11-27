using Kernel.Runtime;
using Microsoft.EntityFrameworkCore;
using Contract = Kernel.Foundation.Contracts;
using static Kernel.EntityFramework.RootAdmissionWire;

namespace Kernel.EntityFramework;

/// <summary>Exact binary key bindings and receipts for the first-generation relational qualification.</summary>
internal static class RootKeyedAdmission
{
    internal const string BindingTable = "FoundationKeyBindings";
    internal const string ReceiptTable = "FoundationKeyReceipts";
    internal const string Source = "/*key-source*/";

    internal sealed record Request(string Tenant, string Key, string Operation, string Kind, string? Payload,
        StartSpec? Start, long? Due, long Window);

    internal sealed record Binding(bool SourceReady, byte[]? KeyBytes, string? Actor, int? Partition, string? Reference);
    private sealed record Receipt(bool SourceReady, string? Fingerprint, string? Reference);

    internal static string BindingColumns(bool sqlite) => $$"""
        "Tenant" text {{Collation(sqlite)}} NOT NULL,
        "KeyDigest" text {{Collation(sqlite)}} NOT NULL,
        "KeyBytes" {{(sqlite ? "blob" : "bytea")}} NOT NULL,
        "Actor" text {{Collation(sqlite)}} NOT NULL UNIQUE,
        "Partition" integer NOT NULL REFERENCES "Partitions" ("Id"),
        "Reference" text {{Collation(sqlite)}} NOT NULL UNIQUE REFERENCES "FoundationRootBirths" ("Reference"),
        "ReuseAfter" {{(sqlite ? "integer" : "bigint")}} NOT NULL,
        PRIMARY KEY ("Tenant", "KeyDigest")
        """;

    internal static string ReceiptColumns(bool sqlite) => $$"""
        "Actor" text {{Collation(sqlite)}} NOT NULL REFERENCES "FoundationKeyBindings" ("Actor"),
        "Operation" text {{Collation(sqlite)}} NOT NULL,
        "Fingerprint" text NOT NULL,
        "Reference" text {{Collation(sqlite)}} NOT NULL REFERENCES "FoundationRootBirths" ("Reference"),
        PRIMARY KEY ("Actor", "Operation")
        """;

    private static string Collation(bool sqlite) => sqlite ? "COLLATE BINARY" : """ COLLATE "C" """;

    internal static Task InstallPostgresAsync(KernelDb db, CancellationToken token) =>
        db.Database.ExecuteSqlRawAsync($$"""
            CREATE TABLE IF NOT EXISTS "{{BindingTable}}" ({{BindingColumns(sqlite: false)}});
            CREATE TABLE IF NOT EXISTS "{{ReceiptTable}}" ({{ReceiptColumns(sqlite: false)}});
            """, token);

    internal static Request? Prepare(string tenant, Contract.Target.Key target, string id, string kind, string? payload, long? due,
        long window, string actorType)
    {
        StartSpec? start = null;
        if (target.Spec is { } publicStart)
        {
            if (publicStart.Type != actorType || publicStart.Ephemeral) return null;
            start = new(publicStart.Type, publicStart.TypeData, publicStart.Input, History: (History)publicStart.History);
        }
        if (string.IsNullOrEmpty(target.CallerKey) || string.IsNullOrEmpty(id) ||
            !FoundationKeyAdmission.FitsRequest(target.CallerKey, id, kind, payload, start) ||
            start is null && !FoundationMetadataLimits.IsOrdinaryKind(kind))
            return null;
        return new(tenant, target.CallerKey, id, kind, payload, start, due, window);
    }

    internal static async Task<Contract.Admission> ApplyAsync(KernelDb db, Request request, byte[] issuer, int partitions, long now,
        Func<FormattableString, FormattableString> fence, Func<Binding, CancellationToken, Task<bool>> canReceive,
        RootAdmissionPressure.Attempt? attempt, CancellationToken token)
    {
        var keyBytes = DurableKeyJson.CodeUnits(request.Key);
        var keyDigest = FoundationRootCreator.OperationDigest(request.Key);
        var binding = await db.Database.SqlQuery<Binding>(fence($"""
            SELECT source."SourceReady", b."KeyBytes", b."Actor", b."Partition", b."Reference"
            FROM (/*key-source*/) source LEFT JOIN "FoundationKeyBindings" b
                ON source."SourceReady" AND b."Tenant" = {request.Tenant} AND b."KeyDigest" = {keyDigest}
            """)).SingleAsync(token);
        RequireSource(binding.SourceReady);

        // The physical keyed namespace excludes ordinary generated names, while placement hashes the same
        // tenant-qualified spelling as the in-memory contract. The original key bytes check digest collisions.
        var qualified = FoundationKeyAdmission.QualifiedName(request.Tenant, request.Key);
        var partition = InstanceRef.Spread(qualified, partitions);
        var actor = InstanceRef.In(partition, request.Tenant + "_key/" + keyDigest);
        var reference = FoundationRootCreator.ReferenceFor(issuer, request.Tenant, new(actor.Id, 1));
        if (binding.KeyBytes is { } stored)
        {
            if (!stored.AsSpan().SequenceEqual(keyBytes) || binding.Actor != actor.Id || binding.Partition != partition ||
                binding.Reference != reference)
                throw new StoreFormatException("The exact caller key differs from its persisted root binding.");
        }
        else if (request.Start is null)
            return new(Contract.AdmissionOutcome.Refused, Reason: Contract.RefusalReason.TargetMismatch);

        var operation = FoundationRootCreator.OperationDigest(request.Operation);
        var wire = new KeyRequest(actor.Id, request.Operation, request.Kind, request.Start?.Input ?? request.Payload,
            request.Start, request.Window, request.Due);
        var fingerprint = wire.Digest;
        var previous = await db.Database.SqlQuery<Receipt>(fence($"""
            SELECT source."SourceReady", r."Fingerprint", r."Reference"
            FROM (/*key-source*/) source LEFT JOIN "FoundationKeyReceipts" r
                ON source."SourceReady" AND r."Actor" = {actor.Id} AND r."Operation" = {operation}
            """)).SingleAsync(token);
        RequireSource(previous.SourceReady);
        if (previous.Reference is { } replay)
        {
            if (replay != reference) throw new StoreFormatException("The caller operation receipt names another actor.");
            return previous.Fingerprint == fingerprint
                ? new(Contract.AdmissionOutcome.Duplicate, new Contract.ActorRef(replay))
                : new(Contract.AdmissionOutcome.Refused, Reason: Contract.RefusalReason.Conflict);
        }

        var until = checked(now + request.Window);
        if (request.Start is { } spec)
        {
            // The current physical owner accepts generation one. Keep the binding even after retirement.
            if (binding.KeyBytes is not null)
                return new(Contract.AdmissionOutcome.Refused, Reason: Contract.RefusalReason.Repeat);
            var creator = FoundationRootCreator.Create(actor, wire.WireId, FoundationRootCreator.OperationDigest(wire.WireId),
                spec, request.Due, replyTo: null);
            var (header, body) = EncodeCreator(creator);
            if (!FitsEnvelope(header, body)) return new(Contract.AdmissionOutcome.Refused);
            attempt?.Require(partition);
            var birth = CreationProtocol.Fingerprint(creator.Start, creator.Kind, creator.Payload);
            await WriteAsync($"""
                INSERT INTO "FoundationRootBirths" ("Tenant", "Reference", "Actor", "Partition", "Creation", "Creator", "Fingerprint")
                SELECT {request.Tenant}, {reference}, {actor.Id}, {partition}, 1, {creator.InboxKey.Id}, {birth}
                WHERE (/*key-source*/)
                """);
            await WriteAsync($"""
                INSERT INTO "FoundationKeyBindings" ("Tenant", "KeyDigest", "KeyBytes", "Actor", "Partition", "Reference", "ReuseAfter")
                SELECT {request.Tenant}, {keyDigest}, {keyBytes}, {actor.Id}, {partition}, {reference}, {until}
                WHERE (/*key-source*/)
                """);
            await EnqueueAsync(creator.InboxKey.Id, 0, creator.Kind, header, body, keepUntil: null);
        }
        else
        {
            if (!await canReceive(binding, token))
                return new(Contract.AdmissionOutcome.Refused, Reason: Contract.RefusalReason.TargetMismatch);
            var (header, body, kind) = EncodeDirect(wire.WireId, 1, request.Kind, request.Payload, request.Due, held: false, keyed: true);
            if (!FitsEnvelope(header, body)) return new(Contract.AdmissionOutcome.Refused);
            attempt?.Require(partition);
            var occurrence = $"{reference}/{FoundationRootCreator.OperationDigest(wire.WireId)}/{Guid.NewGuid():N}";
            await EnqueueAsync(occurrence, InboxKey.FoundationOccurrenceScope, kind, header, body, until);
        }
        await WriteAsync($"""
            INSERT INTO "FoundationKeyReceipts" ("Actor", "Operation", "Fingerprint", "Reference")
            SELECT {actor.Id}, {operation}, {fingerprint}, {reference} WHERE (/*key-source*/)
            """);
        return new(Contract.AdmissionOutcome.Accepted, new Contract.ActorRef(reference));

        async Task WriteAsync(FormattableString sql) =>
            RequireSource(await db.Database.ExecuteSqlInterpolatedAsync(fence(sql), token) == 1);

        Task EnqueueAsync(string id, long scope, string kind, string header, string? payload, long? keepUntil) =>
            db.Database.IsSqlite()
                ? SqliteRootAdmission.EnqueueAsync(db, actor.Id, partition, id, scope, kind, header, payload, request.Due,
                    keepUntil, replyTo: null, token)
                : WriteAsync($"""
                    INSERT INTO "Messages" ("Id", "InboxScope", "Target", "Partition", "Kind", "Due", "Payload", "Start",
                        "Done", "Seen", "Attempts", "Trial", "JobGeneration", "KeepUntil")
                    SELECT {id}, {scope}, {actor.Id}, {partition}, {kind}, {request.Due ?? 0}, {payload}, {header},
                        false, 0, 0, 0, 0, {keepUntil} WHERE (/*key-source*/)
                    """);
    }

    private static void RequireSource(bool ready)
    {
        if (!ready) throw new StoreFormatException("The physical caller-key admission source changed.");
    }
}
