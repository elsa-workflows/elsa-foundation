using Kernel.Runtime;
using Microsoft.EntityFrameworkCore;
using Contract = Kernel.Foundation.Contracts;
using static Kernel.EntityFramework.RootAdmissionWire;

namespace Kernel.EntityFramework;

/// <summary>Exact binary key bindings and receipts for each logical creation of a caller-key root.</summary>
internal static class RootKeyedAdmission
{
    internal const string BindingTable = "FoundationKeyBindings";
    internal const string ReceiptTable = "FoundationKeyReceipts";
    internal const string Source = "/*key-source*/";

    internal sealed record Request(string Tenant, string Key, string Operation, string Kind, string? Payload,
        StartSpec? Start, long? Due, long Window);

    private sealed record StoredBinding(bool SourceReady, byte[]? KeyBytes, string? Actor, int? Partition,
        long? Generation, string? Reference, long? ReuseAfter);
    internal sealed record Life(InstanceRef Address, long Generation, string Reference);
    private sealed record Receipt(bool SourceReady, string? Fingerprint, long? Generation, string? Reference);
    private sealed record Predecessor(bool SourceReady, bool Retired, bool HasCascadeObligation);

    internal static string BindingColumns(bool sqlite) => $$"""
        "Tenant" text {{Collation(sqlite)}} NOT NULL,
        "KeyDigest" text {{Collation(sqlite)}} NOT NULL,
        "KeyBytes" {{(sqlite ? "blob" : "bytea")}} NOT NULL,
        "Actor" text {{Collation(sqlite)}} NOT NULL UNIQUE,
        "Partition" integer NOT NULL REFERENCES "Partitions" ("Id"),
        "Generation" {{(sqlite ? "integer" : "bigint")}} NOT NULL CHECK ("Generation" > 0),
        "Reference" text {{Collation(sqlite)}} NOT NULL UNIQUE REFERENCES "FoundationRootBirths" ("Reference"),
        "ReuseAfter" {{(sqlite ? "integer" : "bigint")}} NOT NULL,
        PRIMARY KEY ("Tenant", "KeyDigest")
        """;

    internal static string ReceiptColumns(bool sqlite, string bindingTable = BindingTable) => $$"""
        "Actor" text {{Collation(sqlite)}} NOT NULL REFERENCES "{{bindingTable}}" ("Actor"),
        "Operation" text {{Collation(sqlite)}} NOT NULL,
        "Fingerprint" text NOT NULL,
        "Generation" {{(sqlite ? "integer" : "bigint")}} NOT NULL CHECK ("Generation" > 0),
        "Reference" text {{Collation(sqlite)}} NOT NULL REFERENCES "FoundationRootBirths" ("Reference"),
        PRIMARY KEY ("Actor", "Operation")
        """;

    private static string Collation(bool sqlite) => sqlite ? "COLLATE BINARY" : """ COLLATE "C" """;

    internal static async Task InstallPostgresAsync(KernelDb db, CancellationToken token)
    {
        var transaction = db.Database.CurrentTransaction ?? throw new InvalidOperationException("Caller-key definitions require the install transaction.");
        await db.Database.ExecuteSqlRawAsync(RenderPostgresTables(BindingTable, ReceiptTable, "IF NOT EXISTS "), token);
        // PostgreSQL renders both schemas from the same DDL, including the indexes that implement primary and unique keys.
        var suffix = Guid.NewGuid().ToString("N");
        var twinBinding = BindingTable + "_" + suffix;
        var twinReceipt = ReceiptTable + "_" + suffix;
        const string savepoint = "kernel_keyed_definition_twin";
        await transaction.CreateSavepointAsync(savepoint, token);
        await db.Database.ExecuteSqlRawAsync(RenderPostgresTables(twinBinding, twinReceipt, ""), token);
        var valid = await db.Database.SqlQuery<bool>($"""
            WITH relation(layout, installed, oid) AS (
                VALUES ('binding', true, pg_catalog.to_regclass('"FoundationKeyBindings"')),
                       ('receipt', true, pg_catalog.to_regclass('"FoundationKeyReceipts"')),
                       ('binding', false, pg_catalog.to_regclass(pg_catalog.quote_ident({twinBinding}))),
                       ('receipt', false, pg_catalog.to_regclass(pg_catalog.quote_ident({twinReceipt})))
            ), shape AS (
                SELECT r.layout, r.installed, t.relnamespace, t.relpersistence, t.relkind, t.relrowsecurity, t.relforcerowsecurity,
                    EXISTS (SELECT 1 FROM pg_catalog.pg_inherits h WHERE h.inhrelid = r.oid OR h.inhparent = r.oid) AS inherited,
                    (SELECT jsonb_agg(jsonb_build_array(a.attname, a.atttypid, a.atttypmod, a.attcollation, a.attnotnull,
                        a.attidentity, a.attgenerated, pg_catalog.pg_get_expr(d.adbin, d.adrelid)) ORDER BY a.attnum)
                        FROM pg_catalog.pg_attribute a LEFT JOIN pg_catalog.pg_attrdef d ON d.adrelid = r.oid AND d.adnum = a.attnum
                        WHERE a.attrelid = r.oid AND a.attnum > 0 AND NOT a.attisdropped) AS column_definitions,
                    (SELECT array_agg(c.definition ORDER BY c.definition) FROM (
                        SELECT pg_catalog.format('%s %s', c.convalidated, pg_catalog.replace(pg_catalog.pg_get_constraintdef(c.oid),
                            {twinBinding}, {BindingTable})) AS definition
                        FROM pg_catalog.pg_constraint c WHERE c.conrelid = r.oid) c) AS constraint_definitions,
                    (SELECT array_agg(i.definition ORDER BY i.definition) FROM (
                        SELECT pg_catalog.format('%s %s %s %s %s %s', i.indisvalid, i.indisready, i.indisprimary, i.indisunique, i.indimmediate,
                            substring(pg_catalog.pg_get_indexdef(i.indexrelid) FROM ' USING .*$')) AS definition
                        FROM pg_catalog.pg_index i WHERE i.indrelid = r.oid) i) AS index_definitions
                FROM relation r JOIN pg_catalog.pg_class t ON t.oid = r.oid
            )
            SELECT count(*) = 2 AND bool_and(i.relnamespace = e.relnamespace AND i.relpersistence = e.relpersistence
                AND i.relkind = e.relkind AND i.relrowsecurity = e.relrowsecurity AND i.relforcerowsecurity = e.relforcerowsecurity
                AND i.inherited = e.inherited AND i.column_definitions IS NOT DISTINCT FROM e.column_definitions
                AND i.constraint_definitions IS NOT DISTINCT FROM e.constraint_definitions
                AND i.index_definitions IS NOT DISTINCT FROM e.index_definitions) AS "Value"
            FROM shape i JOIN shape e ON e.layout = i.layout AND i.installed AND NOT e.installed
            """).SingleAsync(token);
        await transaction.RollbackToSavepointAsync(savepoint, token);
        if (!valid) throw new StoreFormatException("The installed PostgreSQL caller-key tables or indexes differ from the profile definitions.");
    }

    private static string RenderPostgresTables(string bindings, string receipts, string createGuard) => $$"""
        CREATE TABLE {{createGuard}}"{{bindings}}" ({{BindingColumns(sqlite: false)}});
        CREATE TABLE {{createGuard}}"{{receipts}}" ({{ReceiptColumns(sqlite: false, bindings)}});
        """;

    internal static Request? Prepare(string tenant, Contract.Target.Key target, string id, string kind, string? payload, long? due,
        long window, string? actorType)
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

    internal static async Task<Contract.Admission?> ApplyAsync(KernelDb db, Request request, byte[] issuer, int partitions,
        HeldRootRequest? caller, Func<FormattableString, FormattableString> fence, Func<Life, CancellationToken, Task<bool>> canReceive,
        RootAdmissionPressure.Attempt? attempt, CancellationToken token)
    {
        var keyBytes = DurableKeyJson.CodeUnits(request.Key);
        var keyDigest = FoundationRootCreator.OperationDigest(request.Key);
        var binding = await db.Database.SqlQuery<StoredBinding>(fence($"""
            SELECT source."SourceReady", b."KeyBytes", b."Actor", b."Partition", b."Generation", b."Reference", b."ReuseAfter"
            FROM (/*key-source*/) source LEFT JOIN "FoundationKeyBindings" b
                ON source."SourceReady" AND b."Tenant" = {request.Tenant} AND b."KeyDigest" = {keyDigest}
            """)).SingleAsync(token);
        RequireSource(binding.SourceReady);

        // Keep stable logical identity and placement. Each life has its own ordinary physical root, so the
        // checkpoint, inbox, journal, child bindings and replies cannot resolve through a reused physical address.
        var qualified = FoundationKeyAdmission.QualifiedName(request.Tenant, request.Key);
        var partition = InstanceRef.Spread(qualified, partitions);
        var logicalActor = InstanceRef.In(partition, request.Tenant + "_key/" + keyDigest);
        FoundationKeyAdmission.Binding? bound = null;
        Life? current = null;
        if (binding.KeyBytes is { } stored)
        {
            if (!stored.AsSpan().SequenceEqual(keyBytes) || binding.Actor != logicalActor.Id || binding.Partition != partition ||
                binding.Generation is not { } generation || generation <= 0 || binding.ReuseAfter is not { } reuseAfter)
                throw new StoreFormatException("The exact caller key differs from its persisted root binding.");
            current = LifeFor(generation);
            if (binding.Reference != current.Reference)
                throw new StoreFormatException("The caller-key binding does not name the exact life at its generation.");
            bound = new(logicalActor, current.Generation, current.Reference, reuseAfter);
        }
        else if (request.Start is null)
            return new(Contract.AdmissionOutcome.Refused, Reason: Contract.RefusalReason.TargetMismatch);

        var operation = FoundationRootCreator.OperationDigest(request.Operation);
        var wire = new KeyRequest(logicalActor.Id, request.Operation, request.Kind, request.Start?.Input ?? request.Payload,
            request.Start, request.Window, request.Due);
        var fingerprint = wire.Digest;
        var previous = await db.Database.SqlQuery<Receipt>(fence($"""
            SELECT source."SourceReady", r."Fingerprint", r."Generation", r."Reference"
            FROM (/*key-source*/) source LEFT JOIN "FoundationKeyReceipts" r
                ON source."SourceReady" AND r."Actor" = {logicalActor.Id} AND r."Operation" = {operation}
            """)).SingleAsync(token);
        RequireSource(previous.SourceReady);
        if (previous.Reference is { } replay)
        {
            if (current is null || previous.Generation is not { } replayGeneration || replayGeneration <= 0 ||
                replayGeneration > current.Generation || LifeFor(replayGeneration).Reference != replay)
                throw new StoreFormatException("The caller operation receipt names another life.");
            return previous.Fingerprint == fingerprint
                ? new(Contract.AdmissionOutcome.Duplicate, new Contract.ActorRef(replay))
                : new(Contract.AdmissionOutcome.Refused, Reason: Contract.RefusalReason.Conflict);
        }

        // Consult the exact life while still holding the binding before reading the clock for a new write.
        if (request.Start is null && (current is null || !await canReceive(current, token)))
            return new(Contract.AdmissionOutcome.Refused, Reason: Contract.RefusalReason.TargetMismatch);
        FoundationKeyAdmission.Head? head = request.Start is not null && current is not null ? await ReadPredecessorAsync(current) : null;

        var sqlite = db.Database.IsSqlite();
        var now = sqlite ? await SqliteRootAdmission.ReadClockAsync(db, token) : await RootEventAdmission.ReadPostgresClockAsync(db, token);
        if (caller is not null && (!caller.IsOpen || caller.Caller.Deadline <= now)) return null;
        var replyTo = caller?.Address;
        var until = checked(now + request.Window);
        Life life;
        if (request.Start is { } spec)
        {
            var plan = FoundationKeyAdmission.PlanStart(bound, head, now, request.Window, operation);
            if (plan.Creation is not { } next)
                return plan.Outcome == FoundationKeyAdmission.Outcome.ObligationOutstanding
                    ? new(Contract.AdmissionOutcome.Refused, Reason: Contract.RefusalReason.ObligationOutstanding, RetryAfter: TimeSpan.FromSeconds(1))
                    : new(Contract.AdmissionOutcome.Refused, Reason: Contract.RefusalReason.Repeat);
            life = LifeFor(next.Generation);
            var creator = FoundationRootCreator.Create(life.Address, wire.WireId, FoundationRootCreator.OperationDigest(wire.WireId),
                spec, request.Due, replyTo);
            var (header, body) = EncodeCreator(creator);
            if (!FitsEnvelope(header, body, replyTo)) return new(Contract.AdmissionOutcome.Refused);
            attempt?.Require(partition);
            var birth = CreationProtocol.Fingerprint(creator.Start, creator.Kind, creator.Payload);
            await WriteAsync($"""
                INSERT INTO "FoundationRootBirths" ("Tenant", "Reference", "Actor", "Partition", "Creation", "Creator", "Fingerprint")
                SELECT {request.Tenant}, {life.Reference}, {life.Address.Id}, {partition}, 1, {creator.InboxKey.Id}, {birth}
                WHERE (/*key-source*/)
                """);
            if (current is null)
                await WriteAsync($"""
                    INSERT INTO "FoundationKeyBindings" ("Tenant", "KeyDigest", "KeyBytes", "Actor", "Partition", "Generation", "Reference", "ReuseAfter")
                    SELECT {request.Tenant}, {keyDigest}, {keyBytes}, {logicalActor.Id}, {partition}, {life.Generation}, {life.Reference}, {plan.ReuseAfter}
                    WHERE (/*key-source*/)
                    """);
            else
                await WriteAsync($"""
                    UPDATE "FoundationKeyBindings" SET "Generation" = {life.Generation}, "Reference" = {life.Reference}, "ReuseAfter" = {plan.ReuseAfter}
                    WHERE "Tenant" = {request.Tenant} AND "KeyDigest" = {keyDigest} AND "Actor" = {logicalActor.Id}
                        AND "Generation" = {current.Generation} AND "Reference" = {current.Reference} AND (/*key-source*/)
                    """);
            await EnqueueAsync(creator.InboxKey.Id, 0, creator.Kind, header, body, keepUntil: null);
        }
        else
        {
            life = current!;
            var (header, body, kind) = EncodeDirect(wire.WireId, 1, request.Kind, request.Payload, request.Due,
                held: replyTo is not null, keyed: true);
            if (!FitsEnvelope(header, body, replyTo)) return new(Contract.AdmissionOutcome.Refused);
            attempt?.Require(partition);
            var occurrence = $"{life.Reference}/{FoundationRootCreator.OperationDigest(wire.WireId)}/{Guid.NewGuid():N}";
            await EnqueueAsync(occurrence, InboxKey.FoundationOccurrenceScope, kind, header, body, until);
        }
        await WriteAsync($"""
            INSERT INTO "FoundationKeyReceipts" ("Actor", "Operation", "Fingerprint", "Generation", "Reference")
            SELECT {logicalActor.Id}, {operation}, {fingerprint}, {life.Generation}, {life.Reference} WHERE (/*key-source*/)
            """);
        return new(Contract.AdmissionOutcome.Accepted, new Contract.ActorRef(life.Reference));

        Life LifeFor(long generation)
        {
            var address = generation == 1 ? logicalActor :
                new InstanceRef(FormattableString.Invariant($"{logicalActor.Id}/life-{generation}"));
            return new(address, generation, FoundationRootCreator.ReferenceFor(issuer, request.Tenant, new(address.Id, 1)));
        }

        async Task<FoundationKeyAdmission.Head> ReadPredecessorAsync(Life prior)
        {
            var row = await db.Database.SqlQuery<Predecessor>(fence($"""
                SELECT source."SourceReady",
                    source."SourceReady" AND EXISTS (SELECT 1 FROM "Instances" i JOIN "FoundationRootBirths" b
                        ON b."Actor" = i."Id" AND b."Partition" = i."Partition" AND b."Creation" = 1
                            AND b."Tenant" = i."FoundationTenant" AND b."Reference" = i."FoundationReference"
                        WHERE b."Tenant" = {request.Tenant} AND b."Reference" = {prior.Reference}
                            AND i."Id" = {prior.Address.Id} AND i."Partition" = {partition} AND i."Retired") AS "Retired",
                    source."SourceReady" AND EXISTS (SELECT 1 FROM "FoundationRootChildCascades" c
                        WHERE c."Reference" = {prior.Reference}) AS "HasCascadeObligation"
                FROM (/*key-source*/) source
                """)).SingleAsync(token);
            RequireSource(row.SourceReady);
            return new(prior.Generation, row.Retired, row.HasCascadeObligation);
        }

        async Task WriteAsync(FormattableString sql) =>
            RequireSource(await db.Database.ExecuteSqlInterpolatedAsync(fence(sql), token) == 1);

        Task EnqueueAsync(string id, long scope, string kind, string header, string? payload, long? keepUntil) =>
            sqlite
                ? SqliteRootAdmission.EnqueueAsync(db, life.Address.Id, partition, id, scope, kind, header, payload, request.Due,
                    keepUntil, replyTo, token)
                : WriteAsync($"""
                    INSERT INTO "Messages" ("Id", "InboxScope", "Target", "Partition", "Kind", "Due", "Payload", "Start", "ReplyTo",
                        "Done", "Seen", "Attempts", "Trial", "JobGeneration", "KeepUntil")
                    SELECT {id}, {scope}, {life.Address.Id}, {partition}, {kind}, {request.Due ?? 0}, {payload}, {header}, {replyTo},
                        false, 0, 0, 0, 0, {keepUntil} WHERE (/*key-source*/)
                    """);
    }

    private static void RequireSource(bool ready)
    {
        if (!ready) throw new StoreFormatException("The physical caller-key admission source changed.");
    }
}
