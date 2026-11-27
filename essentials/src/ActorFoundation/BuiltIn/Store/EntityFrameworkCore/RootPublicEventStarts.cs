using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Contract = Kernel.Foundation.Contracts;
using static Kernel.EntityFramework.RootAdmissionWire;

namespace Kernel.EntityFramework;

/// <summary>Public start subscriptions and the immutable creator intents selected by a committed publication.</summary>
internal static partial class RootPublicEventStarts
{
    internal const string Registrations = "FoundationPublicEventStarts";
    internal const string Targets = "FoundationPublicEventTargets";
    internal const string Source = "/*public-event-start-source*/";
    internal const string CatchUpIndex = "IX_FoundationPublicEventStarts_CatchUp";
    internal const string CatchUpColumns = """("Tenant","Key","Wait") WHERE "CatchAfter" < "CatchThrough" """;

    internal static string RegistrationColumns(bool sqlite)
    {
        var text = TextType(sqlite);
        var number = sqlite ? "INTEGER" : "bigint";
        return $"""
            "Tenant" {text} NOT NULL, "Key" {text} NOT NULL, "Wait" {text} NOT NULL,
            "Incarnation" {text} NOT NULL, "Start" {text} NOT NULL,
            "RetainAfter" {number} NOT NULL, "CatchAfter" {number} NOT NULL,
            "CatchThrough" {number} NOT NULL CHECK ("CatchAfter" <= "CatchThrough"),
            PRIMARY KEY ("Tenant", "Key", "Wait")
            """;
    }

    internal static string TargetColumns(bool sqlite)
    {
        var text = TextType(sqlite);
        return $"""
            "Tenant" {text} NOT NULL, "Publication" {text} NOT NULL, "Registration" {text} NOT NULL,
            "Key" {text} NOT NULL, "Wait" {text} NOT NULL, "EventId" {text} NOT NULL,
            "Start" {text} NOT NULL, "Payload" {text}, "Reference" {text} UNIQUE REFERENCES "FoundationRootBirths" ("Reference"),
            PRIMARY KEY ("Tenant", "Publication", "Registration")
            """;
    }

    private static string TextType(bool sqlite) => sqlite ? "TEXT COLLATE BINARY" : """text COLLATE "C" """;

    private sealed record CaptureQuote(bool SourceReady, long Count, long Bytes);
    private sealed record CapturedStart(bool SourceReady, string? Registration, string? Key, string? Wait, string? EventId,
        string? Start, string? Payload);
    private sealed record Issuer(byte[] Key, int Partitions);
    private sealed record StoredInput(string Id, long Scope, string Kind, string Header, string? Payload, long? KeepUntil);
    private sealed record PreparedStart(string Registration, InstanceRef Actor, string Reference, string Fingerprint, StoredInput Creator, StoredInput Event);
    internal sealed class FanoutExceeded(string message) : StoreException(Failure.Definite, message);

    internal static void Validate(string actorType, string key, string wait, Contract.StartSpec? spec)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentException.ThrowIfNullOrEmpty(wait);
        if (!FoundationMetadataLimits.FitsLiveWaitName(key))
            throw new MetadataLimitExceeded(nameof(key), FoundationMetadataLimits.NameBytes);
        if (!FoundationMetadataLimits.FitsLiveWaitName(wait))
            throw new MetadataLimitExceeded(nameof(wait), FoundationMetadataLimits.NameBytes);
        if (spec is null) return;
        if (spec.Type != actorType || spec.Ephemeral || !Enum.IsDefined(spec.History))
            throw new NotSupportedException("Public event subscriptions require a durable start of the configured actor type.");
        HeaderLimits.ValidateStart(spec.Type, spec.TypeData, spec.Input);
        FoundationMetadataLimits.RequireContent(spec.Input, nameof(spec), FoundationMetadataLimits.EventStartInputBytes);
    }

    internal static async Task SetAsync(KernelDb db, string tenant, string key, string wait, Contract.StartSpec? spec, long now,
        Func<FormattableString, FormattableString>? bind, CancellationToken token)
    {
        RequireTransaction(db, bind);
        var retainedAfter = spec is null ? now : await RootPublicEventPublications.RetentionThresholdAsync(db, tenant, now, bind, token);
        var command = spec is null
            ? (FormattableString)$"""
                WITH source AS MATERIALIZED (/*public-event-start-source*/)
                DELETE FROM "FoundationPublicEventStarts" WHERE "Tenant" = {tenant} AND "Key" = {key} AND "Wait" = {wait}
                    AND (SELECT "SourceReady" FROM source)
                """
            : $"""
                WITH source AS MATERIALIZED (/*public-event-start-source*/), coverage AS (
                    SELECT coalesce(max("Ordinal"), 0) AS "Through" FROM "FoundationPublicEventPublications"
                    WHERE "Tenant" = {tenant} AND "Key" = {key} AND "KeepUntil" > {retainedAfter} AND "CleanupAt" = "KeepUntil"
                        AND (SELECT "SourceReady" FROM source)
                )
                INSERT INTO "FoundationPublicEventStarts"
                    ("Tenant", "Key", "Wait", "Incarnation", "Start", "RetainAfter", "CatchAfter", "CatchThrough")
                SELECT {tenant}, {key}, {wait}, {Guid.NewGuid().ToString("N")}, {DurableKeyJson.Write(spec)}, {retainedAfter}, 0, coverage."Through"
                FROM coverage, source WHERE "SourceReady"
                ON CONFLICT ("Tenant", "Key", "Wait") DO UPDATE
                    SET "Incarnation" = excluded."Incarnation", "Start" = excluded."Start", "RetainAfter" = excluded."RetainAfter",
                        "CatchAfter" = excluded."CatchAfter", "CatchThrough" = excluded."CatchThrough"
                    WHERE "FoundationPublicEventStarts"."Start" <> excluded."Start"
                """;
        if (await db.Database.ExecuteSqlInterpolatedAsync(Bind(command, bind), token) == 0 && bind is not null)
        {
            var ready = await db.Database.SqlQuery<bool>(Bind($"""
                WITH source AS MATERIALIZED (/*public-event-start-source*/)
                SELECT "SourceReady" AS "Value" FROM source
                """, bind)).SingleAsync(token);
            RequireSource(ready);
        }
    }

    // The caller holds the key lock or the SQLite writer through the quote and copy. A replacement changes
    // only the registration; a publication that already selected its creator keeps the original template.
    internal static async Task<(int Count, long Bytes)> CaptureAsync(KernelDb db, string tenant, string key, string eventId,
        string publication, string? payload, int available, long availableBytes, Func<FormattableString, FormattableString>? bind,
        CancellationToken token)
    {
        RequireTransaction(db, bind);
        FormattableString query = $"""
            WITH source AS MATERIALIZED (/*public-event-start-source*/), candidates AS MATERIALIZED (
                SELECT /*start-bytes*/ AS "Bytes" FROM "FoundationPublicEventStarts"
                WHERE "Tenant" = {tenant} AND "Key" = {key} AND (SELECT "SourceReady" FROM source)
                ORDER BY "Wait" LIMIT {available + 1}
            )
            SELECT source."SourceReady", (SELECT count(*) FROM candidates) AS "Count",
                (SELECT CAST(coalesce(sum("Bytes"), 0) AS BIGINT) FROM candidates) AS "Bytes"
            FROM source
            """;
        var length = bind is not null ? """octet_length("Start")""" : """length(CAST("Start" AS BLOB))""";
        var quoteSql = FormattableStringFactory.Create(query.Format.Replace("/*start-bytes*/", length, StringComparison.Ordinal),
            query.GetArguments());
        var quote = await db.Database.SqlQuery<CaptureQuote>(Bind(quoteSql, bind)).SingleAsync(token);
        RequireSource(quote.SourceReady);
        if (quote.Count == 0) return default;
        var packed = payload is null ? null : DurableKeyJson.Write(payload);
        var bytes = quote.Bytes + quote.Count * (packed is null ? 0 : Encoding.UTF8.GetByteCount(packed));
        if (quote.Count > available || bytes > availableBytes)
            throw new FanoutExceeded("The event's start subscriptions exceed the atomic delivery limit.");

        var captured = await db.Database.ExecuteSqlInterpolatedAsync(Bind($"""
            WITH source AS MATERIALIZED (/*public-event-start-source*/)
            INSERT INTO "FoundationPublicEventTargets"
                ("Tenant", "Publication", "Registration", "Key", "Wait", "EventId", "Start", "Payload")
            SELECT "Tenant", {publication}, "Incarnation", "Key", "Wait", {DurableKeyJson.Write(eventId)}, "Start", CAST({packed} AS TEXT)
            FROM "FoundationPublicEventStarts"
            WHERE "Tenant" = {tenant} AND "Key" = {key} AND (SELECT "SourceReady" FROM source)
            ORDER BY "Wait"
            """, bind), token);
        RequireSource(captured == quote.Count);
        return (captured, bytes);
    }

    internal static async Task<int[]> DispatchAsync(KernelDb db, string tenant, string publication, long until,
        Func<FormattableString, FormattableString>? bind, CancellationToken token)
    {
        RequireTransaction(db, bind);
        var targets = await db.Database.SqlQuery<CapturedStart>(Bind($"""
            WITH source AS MATERIALIZED (/*public-event-start-source*/)
            SELECT source."SourceReady", t."Registration", t."Key", t."Wait", t."EventId", t."Start", t."Payload"
            FROM source LEFT JOIN "FoundationPublicEventTargets" t
                ON source."SourceReady" AND t."Tenant" = {tenant} AND t."Publication" = {publication} AND t."Reference" IS NULL
            ORDER BY t."Wait"
            """, bind)).ToListAsync(token);
        RequireSource(targets[0].SourceReady);
        if (targets is [{ Registration: null }]) return [];

        var issuer = await db.Database.SqlQuery<Issuer>(Bind($"""
            WITH source AS MATERIALIZED (/*public-event-start-source*/)
            SELECT "Key", "Partitions" FROM "FoundationRootIssuer", source WHERE source."SourceReady"
            """, bind)).SingleOrDefaultAsync(token) ?? throw new StoreFormatException("The public event root issuer is unavailable.");
        var prepared = new List<PreparedStart>(targets.Count);
        foreach (var target in targets)
        {
            var operation = "event/" + FoundationRootCreator.OperationDigest(publication + "/" + target.Registration);
            var name = tenant + "_" + operation;
            var actor = InstanceRef.In(InstanceRef.Spread(name, issuer.Partitions), name);
            var reference = FoundationRootCreator.ReferenceFor(issuer.Key, tenant, new(actor.Id, 1));
            var template = DurableKeyJson.Read<Contract.StartSpec>(target.Start!);
            var spec = new StartSpec(template.Type, template.TypeData, template.Input, History: (History)template.History);
            var creator = FoundationRootCreator.Create(actor, operation, FoundationRootCreator.OperationDigest(operation), spec, null, null);
            var fingerprint = CreationProtocol.Fingerprint(creator.Start, creator.Kind, creator.Payload)!;
            var (creatorHeader, input) = EncodeCreator(creator);
            var payload = target.Payload is { } packed ? DurableKeyJson.Read<string>(packed) : null;
            var id = DurableKeyJson.Read<string>(target.EventId!);
            var (eventHeader, eventPayload, kind) = EncodeDirect(id, 1, Kinds.Event, payload, null, held: false,
                new(target.Key!, target.Wait!), eventOccurrence: publication);
            if (!FitsEnvelope(creatorHeader, input) || !FitsEnvelope(eventHeader, eventPayload))
                throw new FanoutExceeded("The event's start or delivery exceeds the root envelope limit.");
            prepared.Add(new(target.Registration!, actor, reference, fingerprint,
                new(creator.InboxKey.Id, 0, creator.Kind, creatorHeader, input, null),
                new(PublicEventOccurrence(reference, publication, id, target.Wait!), InboxKey.FoundationOccurrenceScope,
                    kind, eventHeader, eventPayload, until)));
        }
        if (bind is null) await DispatchSqliteAsync(db, tenant, publication, prepared, token);
        else await DispatchPostgresAsync(db, tenant, publication, prepared, bind, token);
        return prepared.Select(start => start.Actor.Partition).Distinct().ToArray();
    }

    private static async Task DispatchSqliteAsync(KernelDb db, string tenant, string publication, List<PreparedStart> starts,
        CancellationToken token)
    {
        foreach (var start in starts)
        {
            RequireSource(await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "FoundationRootBirths" ("Tenant","Reference","Actor","Partition","Creation","Creator","Fingerprint")
                VALUES ({tenant}, {start.Reference}, {start.Actor.Id}, {start.Actor.Partition}, 1, {start.Creator.Id}, {start.Fingerprint})
                """, token) == 1);
            // Both messages name the same life. The creator gets the earlier sequence.
            await EnqueueAsync(start.Creator);
            await EnqueueAsync(start.Event);
            RequireSource(await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE "FoundationPublicEventTargets" SET "Reference" = {start.Reference}
                WHERE "Tenant" = {tenant} AND "Publication" = {publication} AND "Registration" = {start.Registration} AND "Reference" IS NULL
                """, token) == 1);

            Task EnqueueAsync(StoredInput row) =>
                SqliteRootAdmission.EnqueueAsync(db, start.Actor.Id, start.Actor.Partition, row.Id, row.Scope, row.Kind,
                    row.Header, row.Payload, null, row.KeepUntil, null, token);
        }
    }

    private static async Task DispatchPostgresAsync(KernelDb db, string tenant, string publication, List<PreparedStart> starts,
        Func<FormattableString, FormattableString> bind, CancellationToken token)
    {
        var references = starts.Select(start => start.Reference).ToArray();
        RequireSource(await db.Database.ExecuteSqlInterpolatedAsync(bind($"""
                WITH source AS MATERIALIZED (/*public-event-start-source*/)
                INSERT INTO "FoundationRootBirths" ("Tenant","Reference","Actor","Partition","Creation","Creator","Fingerprint")
                SELECT {tenant}, item.reference, item.actor, item.partition, 1, item.creator, item.fingerprint
                FROM unnest({references}::text[], {starts.Select(s => s.Actor.Id).ToArray()}::text[],
                    {starts.Select(s => s.Actor.Partition).ToArray()}::integer[], {starts.Select(s => s.Creator.Id).ToArray()}::text[],
                    {starts.Select(s => s.Fingerprint).ToArray()}::text[]) AS item(reference, actor, partition, creator, fingerprint)
                CROSS JOIN source WHERE "SourceReady"
                """), token) == starts.Count);

        // WITH ORDINALITY gives each creator the earlier sequence, without a database round trip per target.
        var messages = starts.SelectMany(start => new[] { (start.Actor, Input: start.Creator), (start.Actor, Input: start.Event) }).ToArray();
        RequireSource(await db.Database.ExecuteSqlInterpolatedAsync(bind($"""
                WITH source AS MATERIALIZED (/*public-event-start-source*/)
                INSERT INTO "Messages" ("Id","InboxScope","Target","Partition","Kind","Due","Payload","Start","Done","Seen",
                    "Attempts","Trial","JobGeneration","KeepUntil")
                SELECT item.id, item.scope, item.actor, item.partition, item.kind, 0, item.payload, item.header, false, 0, 0, 0, 0, item.keep
                FROM unnest({messages.Select(m => m.Input.Id).ToArray()}::text[], {messages.Select(m => m.Input.Scope).ToArray()}::bigint[],
                    {messages.Select(m => m.Actor.Id).ToArray()}::text[], {messages.Select(m => m.Actor.Partition).ToArray()}::integer[],
                    {messages.Select(m => m.Input.Kind).ToArray()}::text[], {messages.Select(m => m.Input.Header).ToArray()}::text[],
                    {messages.Select(m => m.Input.Payload).ToArray()}::text[], {messages.Select(m => m.Input.KeepUntil).ToArray()}::bigint[])
                WITH ORDINALITY AS item(id, scope, actor, partition, kind, header, payload, keep, position)
                CROSS JOIN source WHERE "SourceReady" ORDER BY item.position
                """), token) == messages.Length);
        RequireSource(await db.Database.ExecuteSqlInterpolatedAsync(bind($"""
                WITH source AS MATERIALIZED (/*public-event-start-source*/)
                UPDATE "FoundationPublicEventTargets" target SET "Reference" = item.reference
                FROM unnest({starts.Select(s => s.Registration).ToArray()}::text[], {references}::text[]) AS item(registration, reference), source
                WHERE target."Tenant" = {tenant} AND target."Publication" = {publication} AND target."Registration" = item.registration
                    AND target."Reference" IS NULL AND source."SourceReady"
                """), token) == starts.Count);
    }

    private static void RequireTransaction(KernelDb db, Func<FormattableString, FormattableString>? bind)
    {
        if (!db.UsesRootPublicEventStarts || db.Database.CurrentTransaction is null || db.Database.IsNpgsql() != (bind is not null))
            throw new StoreFormatException("Public event starts require their pinned provider transaction and store format.");
    }

    internal static FormattableString Bind(FormattableString query, Func<FormattableString, FormattableString>? bind) =>
        bind?.Invoke(query) ?? FormattableStringFactory.Create(query.Format.Replace(Source, """SELECT true AS "SourceReady" """,
            StringComparison.Ordinal), query.GetArguments());

    private static void RequireSource(bool ready)
    {
        if (!ready) throw new StoreFormatException("The source changed while selecting public event starts.");
    }
}
