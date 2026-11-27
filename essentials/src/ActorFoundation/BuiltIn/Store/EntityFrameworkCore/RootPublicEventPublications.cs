using Microsoft.EntityFrameworkCore;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.EntityFramework;

internal static class RootPublicEventPublications
{
    internal const string Table = "FoundationPublicEventPublications";

    internal static string Columns(bool sqlite)
    {
        var text = sqlite ? "TEXT COLLATE BINARY" : """text COLLATE "C" """;
        var number = sqlite ? "INTEGER" : "bigint";
        return $"""
            "Tenant" {text} NOT NULL, "Key" {text} NOT NULL, "Operation" {text} NOT NULL,
            "Incarnation" {text} NOT NULL, "EventId" {text} NOT NULL, "Payload" {text},
            "ReceiptUntil" {number} NOT NULL, "KeepUntil" {number} NOT NULL,
            PRIMARY KEY ("Tenant", "Key", "Operation", "Incarnation")
            """;
    }

    internal sealed record StoredPublication(bool SourceReady, string? Incarnation, string? EventId);

    internal static Contract.Admission? Validate(string key, string id, string? payload, Contract.AddOptions? add)
    {
        if (id.StartsWith('~') || id.Length == 0 || !FoundationMetadataLimits.FitsName(id))
            return new(Contract.AdmissionOutcome.Refused, Reason: Contract.RefusalReason.InvalidEventId);
        return key.Length == 0 || !FoundationMetadataLimits.FitsLiveWaitName(key) ||
            !FoundationMetadataLimits.FitsContent(payload, FoundationMetadataLimits.EventContentBytes) ||
            add?.DueAt is not null || add?.KeepFor < TimeSpan.Zero
                ? new(Contract.AdmissionOutcome.Refused) : null;
    }

    internal static async Task<Guid?> ReadReplayAsync(KernelDb db, string tenant, string key, string id, string operation, long now,
        Func<FormattableString, FormattableString> bind, CancellationToken token)
    {
        var replay = await db.Database.SqlQuery<StoredPublication>(bind($"""
            WITH source AS MATERIALIZED (/*public-event-start-source*/)
            SELECT source."SourceReady", found."Incarnation", found."EventId" FROM source LEFT JOIN (
                SELECT "Incarnation", "EventId" FROM "FoundationPublicEventPublications"
                WHERE "Tenant" = {tenant} AND "Key" = {key} AND "Operation" = {operation} AND "ReceiptUntil" >= {now}
            ) found ON source."SourceReady"
            """)).ToListAsync(token);
        if (replay is not [var row] || !row.SourceReady)
            throw new StoreFormatException("The pinned public event receipt is unavailable or no longer unique.");
        if (row.Incarnation is null) return null;
        if (DurableKeyJson.Read<string>(row.EventId!) != id)
            throw new StoreFormatException("The public event receipt operation differs from its admitted identity.");
        return Guid.ParseExact(row.Incarnation, "N");
    }

    internal static async Task InsertAsync(KernelDb db, string tenant, string key, string id, string operation, Guid incarnation,
        string? payload, long receiveThrough, long keepThrough, Func<FormattableString, FormattableString> bind, CancellationToken token)
    {
        var packed = payload is null ? null : DurableKeyJson.Write(payload);
        if (await db.Database.ExecuteSqlInterpolatedAsync(bind($"""
                WITH source AS MATERIALIZED (/*public-event-start-source*/)
                INSERT INTO "FoundationPublicEventPublications"
                    ("Tenant","Key","Operation","Incarnation","EventId","Payload","ReceiptUntil","KeepUntil")
                SELECT {tenant}, {key}, {operation}, {incarnation.ToString("N")}, {DurableKeyJson.Write(id)},
                    CAST({packed} AS TEXT), {receiveThrough}, {keepThrough} FROM source WHERE "SourceReady"
                """), token) != 1)
            throw new StoreFormatException("The public event receipt did not land in its pinned source.");
    }
}
