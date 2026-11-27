using Microsoft.EntityFrameworkCore;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.EntityFramework;

internal static partial class RootPublicEventPublications
{
    internal const string Table = "FoundationPublicEventPublications";
    internal const string Horizons = "FoundationPublicEventHorizons";
    internal const string ReplayIndex = "IX_FoundationPublicEventPublications_Replay";
    internal const string ReplayColumns = """("Tenant","Key","Operation") WHERE "ReceiptUntil" IS NOT NULL """;
    internal const string RetainedIndex = "IX_FoundationPublicEventPublications_Retained";
    internal const string RetainedColumns = """("Tenant","Key","Ordinal")""";
    internal const string CleanupIndex = "IX_FoundationPublicEventPublications_Cleanup";
    internal const string CleanupColumns = """("CleanupAt","Ordinal")""";

    internal static string Columns(bool sqlite)
    {
        var text = sqlite ? "TEXT COLLATE BINARY" : """text COLLATE "C" """;
        var number = sqlite ? "INTEGER" : "bigint";
        var boolean = sqlite ? "INTEGER" : "boolean";
        var ordinal = sqlite ? "INTEGER PRIMARY KEY AUTOINCREMENT" : "bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY";
        return $"""
            "Tenant" {text} NOT NULL, "Key" {text} NOT NULL, "Operation" {text} NOT NULL,
            "Incarnation" {text} NOT NULL, "Occurrence" {text} NOT NULL UNIQUE, "EventId" {text} NOT NULL, "Payload" {text},
            "PayloadEscaped" {boolean} NOT NULL CHECK (NOT "PayloadEscaped" OR "Payload" IS NOT NULL),
            "ReceiptUntil" {number}, "KeepUntil" {number}, "CleanupAt" {number} NOT NULL,
            "Ordinal" {ordinal}, UNIQUE ("Tenant", "Key", "Operation", "Incarnation")
            """;
    }

    internal static string HorizonColumns(bool sqlite)
    {
        var text = sqlite ? "TEXT COLLATE BINARY" : """text COLLATE "C" """;
        var number = sqlite ? "INTEGER" : "bigint";
        return $""" "Tenant" {text} NOT NULL PRIMARY KEY, "Through" {number} NOT NULL CHECK ("Through" >= 0) """;
    }

    internal sealed record StoredPublication(bool SourceReady, string? Incarnation, string? EventId);

    internal static Contract.Admission? Validate(string key, string id, string? payload, Contract.AddOptions? add)
    {
        if (id.StartsWith('~') || id.Length == 0 || !FoundationMetadataLimits.FitsName(id))
            return new(Contract.AdmissionOutcome.Refused, Reason: Contract.RefusalReason.InvalidEventId);
        return key.Length == 0 || !FoundationMetadataLimits.FitsLiveWaitName(key) ||
            !RootAdmissionWire.FitsRetainedEventPayload(payload) ||
            add?.DueAt is not null || add?.KeepFor < TimeSpan.Zero
                ? new(Contract.AdmissionOutcome.Refused) : null;
    }

    internal static async Task<Guid?> ReadReplayAsync(KernelDb db, string tenant, string key, string id, string operation, long now,
        Func<FormattableString, FormattableString> bind, CancellationToken token)
    {
        var replay = await db.Database.SqlQuery<StoredPublication>(bind($"""
            WITH source AS MATERIALIZED (/*public-event-start-source*/)
            SELECT source."SourceReady", found."Incarnation", found."EventId" FROM source LEFT JOIN (
                SELECT "Incarnation", "EventId", "ReceiptUntil" FROM "FoundationPublicEventPublications"
                WHERE "Tenant" = {tenant} AND "Key" = {key} AND "Operation" = {operation} AND "ReceiptUntil" IS NOT NULL
                ORDER BY "Ordinal" DESC LIMIT 1
            ) found ON source."SourceReady" AND found."ReceiptUntil" >= {now}
            """)).ToListAsync(token);
        if (replay is not [var row] || !row.SourceReady)
            throw new StoreFormatException("The pinned public event receipt is unavailable or no longer unique.");
        if (row.Incarnation is null) return null;
        if (DurableKeyJson.Read<string>(row.EventId!) != id)
            throw new StoreFormatException("The public event receipt operation differs from its admitted identity.");
        return Guid.ParseExact(row.Incarnation, "N");
    }

    internal static async Task InsertAsync(KernelDb db, string tenant, string key, string id, string operation, Guid incarnation, string occurrence,
        string? payload, long now, long? receiveThrough, long? keepThrough, Func<FormattableString, FormattableString> bind,
        CancellationToken token)
    {
        if (receiveThrough is not null)
            await db.Database.ExecuteSqlInterpolatedAsync(bind($"""
                WITH source AS MATERIALIZED (/*public-event-start-source*/)
                UPDATE "FoundationPublicEventPublications" SET "ReceiptUntil" = NULL
                WHERE "Tenant" = {tenant} AND "Key" = {key} AND "Operation" = {operation} AND "ReceiptUntil" IS NOT NULL
                    AND (SELECT "SourceReady" FROM source)
                """), token);
        var (text, escaped) = RootAdmissionWire.EncodePayload(keepThrough is not null ? payload : null);
        if (await db.Database.ExecuteSqlInterpolatedAsync(bind($"""
                WITH source AS MATERIALIZED (/*public-event-start-source*/)
                INSERT INTO "FoundationPublicEventPublications"
                    ("Tenant","Key","Operation","Incarnation","Occurrence","EventId","Payload","PayloadEscaped","ReceiptUntil","KeepUntil","CleanupAt")
                SELECT {tenant}, {key}, {operation}, {incarnation.ToString("N")}, {occurrence}, {DurableKeyJson.Write(id)},
                    CAST({text} AS TEXT), {escaped}, CAST({receiveThrough} AS BIGINT), CAST({keepThrough} AS BIGINT), {keepThrough ?? now}
                FROM source WHERE "SourceReady"
                """), token) != 1)
            throw new StoreFormatException("The public event receipt did not land in its pinned source.");
    }

    internal static async Task RememberAuthoredAsync(KernelDb db, EventRow publication, string occurrence, long now, long until,
        Func<FormattableString, FormattableString>? bind, CancellationToken token) =>
        await InsertAsync(db, publication.FoundationTenant!, publication.Key, publication.Id, occurrence, Guid.NewGuid(), occurrence,
            publication.Payload, now, receiveThrough: null, until, sql => RootPublicEventStarts.Bind(sql, bind), token);

    // The database clock may move backward after cleanup. New registrations take a stable tenant watermark,
    // while a previously covered publication retains its registration's original threshold.
    internal static async Task<long> RetentionThresholdAsync(KernelDb db, string tenant, long now,
        Func<FormattableString, FormattableString>? bind, CancellationToken token)
    {
        await db.Database.ExecuteSqlInterpolatedAsync(RootPublicEventStarts.Bind($"""
            WITH source AS MATERIALIZED (/*public-event-start-source*/)
            INSERT INTO "FoundationPublicEventHorizons" ("Tenant", "Through")
            SELECT {tenant}, 0 FROM source WHERE "SourceReady" ON CONFLICT ("Tenant") DO NOTHING
            """, bind), token);
        var query = bind is null
            ? (FormattableString)$"""SELECT "Through" AS "Value" FROM "FoundationPublicEventHorizons" WHERE "Tenant" = {tenant}"""
            : $"""
                WITH source AS MATERIALIZED (/*public-event-start-source*/)
                SELECT h."Through" AS "Value" FROM "FoundationPublicEventHorizons" h, source
                WHERE h."Tenant" = {tenant} AND "SourceReady" FOR SHARE OF h
                """;
        var recorded = (await db.Database.SqlQuery<long>(RootPublicEventStarts.Bind(query, bind)).ToListAsync(token)).Single();
        return Math.Max(now, checked(recorded + 1));
    }

    private static async Task AdvanceRetentionAsync(KernelDb db, string tenant, long sweptThrough, Func<FormattableString, FormattableString>? bind,
        CancellationToken token)
    {
        var rows = await db.Database.ExecuteSqlInterpolatedAsync(RootPublicEventStarts.Bind($"""
            WITH source AS MATERIALIZED (/*public-event-start-source*/)
            INSERT INTO "FoundationPublicEventHorizons" ("Tenant", "Through")
            SELECT {tenant}, {sweptThrough} FROM source WHERE "SourceReady"
            ON CONFLICT ("Tenant") DO UPDATE SET "Through" = CASE
                WHEN "FoundationPublicEventHorizons"."Through" < excluded."Through" THEN excluded."Through"
                ELSE "FoundationPublicEventHorizons"."Through" END
            """, bind), token);
        RequireCleanupSource(rows == 1);
    }
}
