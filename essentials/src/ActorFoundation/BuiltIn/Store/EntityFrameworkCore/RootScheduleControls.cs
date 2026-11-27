using System.Globalization;
using System.Text.Json;
using Kernel.Foundation.Contracts;
using Microsoft.EntityFrameworkCore;
using static Kernel.EntityFramework.RootAdmissionWire;

namespace Kernel.EntityFramework;

internal sealed record ScheduleControlIntent(ScheduleRootKey Key, long Revision, ScheduleControlKind Kind, string? SetHeader)
{
    internal void Validate()
    {
        Key.Validate();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(Revision);
        if (Kind == ScheduleControlKind.Stop && SetHeader is null) return;
        if (Kind != ScheduleControlKind.Set || SetHeader is null ||
            !FoundationMetadataLimits.FitsContent(SetHeader, FoundationMetadataLimits.TypeDataBytes))
            throw new ArgumentException("Invalid managed schedule control header.", nameof(SetHeader));

        using var document = JsonDocument.Parse(SetHeader, new JsonDocumentOptions { AllowDuplicateProperties = false });
        if (document.RootElement is not { ValueKind: JsonValueKind.Object } header ||
            !header.TryGetProperty("Revision", out var revision) || revision.ValueKind != JsonValueKind.Number ||
            !revision.TryGetInt64(out var value) || value != Revision ||
            !header.TryGetProperty("Authority", out var authority) || authority.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(authority.GetString()) ||
            !header.TryGetProperty("Schedule", out var schedule) || schedule.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("The managed schedule header does not match its control revision and authority.", nameof(SetHeader));
    }

    internal string KindName => Kind == ScheduleControlKind.Set ? "set" : "stop";
    internal string MessageKind => Kind == ScheduleControlKind.Set ? "schedule.set" : "schedule.stop";
    internal string Operation => "schedule-control/" + Revision.ToString(CultureInfo.InvariantCulture);
    internal string Payload => SetHeader ?? Json.Write(new { Revision });
    internal string Fingerprint(string tenant, string actorType) =>
        FoundationRootCreator.OperationDigest(DurableKeyJson.Write(new { tenant, actorType, Key.Definition, Key.Key, Revision, KindName, SetHeader }));

    internal (string Id, string Header, string? Payload, string Kind) Encode(string tenant, string actorType, string reference)
    {
        var metadata = new StoredScheduleControl(tenant, actorType, Key.Definition, Key.Key, Revision, Kind, Fingerprint(tenant, actorType));
        var (header, body, kind) = EncodeDirect(Operation, 1, MessageKind, Payload, null, held: false, scheduleControl: metadata);
        if (!FitsEnvelope(header, body))
            throw new Kernel.Durable.MetadataLimitExceeded(nameof(SetHeader), EfStore.RootContentBytes);
        return ($"{reference}/{FoundationRootCreator.OperationDigest(Operation)}/{Guid.NewGuid():N}", header, body, kind);
    }
}

internal static class RootScheduleControls
{
    internal const string Table = "FoundationScheduleControls";
    internal const long InboxRetentionMilliseconds = 3_600_000;

    internal static string Columns(bool sqlite)
    {
        var text = sqlite ? "TEXT COLLATE BINARY" : """text COLLATE "C" """;
        var revision = sqlite ? "INTEGER" : "bigint";
        return $"""
            "Tenant" {text} NOT NULL,
            "Key" {text} NOT NULL,
            "Definition" {text} NOT NULL,
            "ActorType" {text} NOT NULL,
            "Revision" {revision} NOT NULL,
            "Kind" {text} NOT NULL,
            "Fingerprint" {text} NOT NULL,
            "Reference" {text} REFERENCES "FoundationScheduleRoots" ("Reference") DEFERRABLE INITIALLY DEFERRED,
            PRIMARY KEY ("Tenant", "Key")
            """;
    }

    internal static async Task InstallPostgresAsync(KernelDb db, CancellationToken token)
    {
        await db.Database.ExecuteSqlRawAsync($"""CREATE TABLE IF NOT EXISTS "{Table}" ({Columns(sqlite: false)})""", token);
        await RequirePostgresDefinitionsAsync(db, token);
    }

    internal static async Task RequirePostgresDefinitionsAsync(KernelDb db, CancellationToken token)
    {
        var valid = await db.Database.SqlQueryRaw<bool>("""
            WITH relation AS (
                SELECT r.oid, inventory.oid AS "Inventory"
                FROM pg_catalog.pg_class r
                JOIN pg_catalog.pg_class inventory ON inventory.oid = pg_catalog.to_regclass('"FoundationScheduleRoots"')
                    AND inventory.relnamespace = r.relnamespace
                WHERE r.oid = pg_catalog.to_regclass('"FoundationScheduleControls"') AND r.relkind = 'r'
            ), columns AS (
                SELECT array_agg(a.attname::text ORDER BY a.attnum) AS "Names",
                    bool_and(CASE WHEN a.attnum = 5 THEN a.attnotnull AND a.atttypid = 'pg_catalog.int8'::regtype
                        ELSE a.attnotnull = (a.attnum <> 8) AND a.atttypid = 'pg_catalog.text'::regtype
                            AND a.attcollation = pg_catalog.to_regcollation('pg_catalog."C"') END) AS "TypesValid"
                FROM relation JOIN pg_catalog.pg_attribute a ON a.attrelid = relation.oid
                WHERE a.attnum > 0 AND NOT a.attisdropped
            )
            SELECT EXISTS (SELECT 1 FROM relation, columns
                WHERE columns."TypesValid" AND columns."Names" = ARRAY['Tenant','Key','Definition','ActorType','Revision','Kind','Fingerprint','Reference']
                    AND (SELECT count(*) FROM pg_catalog.pg_constraint c WHERE c.conrelid = relation.oid AND c.convalidated AND (
                        (c.contype = 'p' AND c.conkey = ARRAY[1,2]::smallint[] AND NOT c.condeferrable)
                        OR (c.contype = 'f' AND c.conkey = ARRAY[8]::smallint[] AND c.confrelid = relation."Inventory"
                            AND c.confkey = ARRAY[4]::smallint[] AND c.condeferrable AND c.condeferred))) = 2
            ) AS "Value"
            """).SingleAsync(token);
        if (!valid) throw new StoreFormatException("The PostgreSQL schedule control definitions are incomplete.");
    }

    internal static void RequireIntent(StoredScheduleControl control, string operation, string kind, string? payload)
    {
        var intent = new ScheduleControlIntent(new(control.Definition, control.Key), control.Revision, control.Kind,
            control.Kind == ScheduleControlKind.Set ? payload : null);
        try
        {
            if (control.Tenant is not { Length: >= 1 and <= 64 } ||
                control.Tenant.Any(c => c is not (>= 'a' and <= 'z' or >= '0' and <= '9' or '-')))
                throw new ArgumentException("Invalid schedule control tenant.");
            RootScheduleInventory.RequireActorType(control.ActorType);
            intent.Validate();
        }
        catch (Exception error) when (error is ArgumentException or JsonException or NotSupportedException)
        {
            throw new StoreException(Failure.Definite, "Invalid trusted schedule control.", error);
        }
        if (operation != intent.Operation || kind != intent.MessageKind || payload != intent.Payload ||
            control.Fingerprint != intent.Fingerprint(control.Tenant, control.ActorType))
            throw new StoreException(Failure.Definite, "The trusted schedule control differs from its recorded intent.");
    }

    internal sealed record Watermark(bool SourceReady, string? Tenant, string? Key, string? Definition, string? ActorType,
        long? Revision, string? Kind, string? Fingerprint, string? Reference)
    {
        internal ScheduleControlResult? Classify(string tenant, string actorType, ScheduleControlIntent intent, string? reference)
        {
            if (!SourceReady) throw new StoreFormatException("The pinned schedule control store changed during admission.");
            if (Revision is null)
            {
                if (reference is not null) throw new StoreFormatException("The schedule root has no control watermark.");
                return null;
            }
            if (Tenant != tenant || Key != intent.Key.Key || Definition != intent.Key.Definition || ActorType != actorType ||
                Revision <= 0 || Kind is not ("set" or "stop") || Reference != reference ||
                Kind == "set" && Reference is null || Fingerprint is null || !FoundationRootCreator.IsGeneratedReference(Fingerprint))
                throw new StoreFormatException("The schedule control watermark differs from its scoped identity.");
            if (Revision > intent.Revision) return new(ScheduleControlOutcome.Stale, ActorReference);
            if (Revision < intent.Revision) return null;
            return new(Kind == intent.KindName && Fingerprint == intent.Fingerprint(tenant, actorType)
                ? ScheduleControlOutcome.Duplicate : ScheduleControlOutcome.Conflict, ActorReference);
        }

        private ActorRef? ActorReference => Reference is null ? null : new(Reference);
    }

    internal static Task<Watermark> ReadAsync(KernelDb db, string tenant, ScheduleRootKey key, CancellationToken token,
        Func<FormattableString, FormattableString>? bind = null)
    {
        FormattableString read = $"""
            WITH source AS MATERIALIZED (/*schedule-source*/)
            SELECT source."SourceReady", c."Tenant", c."Key", c."Definition", c."ActorType",
                c."Revision", c."Kind", c."Fingerprint", c."Reference"
            FROM source LEFT JOIN "FoundationScheduleControls" c
                ON source."SourceReady" AND c."Tenant" = {tenant} AND c."Key" = {key.Key}
            """;
        return db.Database.SqlQuery<Watermark>(RootScheduleInventory.Bind(read, bind)).SingleAsync(token);
    }
    internal static async Task WriteAsync(KernelDb db, string tenant, string actorType, ScheduleControlIntent intent, string? reference,
        Watermark prior, CancellationToken token, Func<FormattableString, FormattableString>? bind = null)
    {
        var fingerprint = intent.Fingerprint(tenant, actorType);
        FormattableString write = prior.Revision is null
            ? $"""
                WITH source AS MATERIALIZED (/*schedule-source*/)
                INSERT INTO "FoundationScheduleControls" ("Tenant", "Key", "Definition", "ActorType", "Revision", "Kind", "Fingerprint", "Reference")
                SELECT {tenant}, {intent.Key.Key}, {intent.Key.Definition}, {actorType}, {intent.Revision}, {intent.KindName}, {fingerprint}, {reference}
                FROM source WHERE source."SourceReady"
                ON CONFLICT ("Tenant", "Key") DO NOTHING
                """
            : $"""
                WITH source AS MATERIALIZED (/*schedule-source*/)
                UPDATE "FoundationScheduleControls" SET "Revision" = {intent.Revision}, "Kind" = {intent.KindName},
                    "Fingerprint" = {fingerprint}, "Reference" = {reference}
                WHERE (SELECT "SourceReady" FROM source) AND "Tenant" = {tenant} AND "Key" = {intent.Key.Key}
                    AND "Definition" = {intent.Key.Definition} AND "ActorType" = {actorType}
                    AND "Revision" = {prior.Revision} AND "Revision" < {intent.Revision} AND "Kind" = {prior.Kind}
                    AND "Fingerprint" = {prior.Fingerprint} AND coalesce("Reference", '') = {prior.Reference ?? ""}
                """;
        if (await db.Database.ExecuteSqlInterpolatedAsync(RootScheduleInventory.Bind(write, bind), token) != 1)
            throw new StoreFormatException("The schedule control watermark changed before its atomic write.");
    }
}
