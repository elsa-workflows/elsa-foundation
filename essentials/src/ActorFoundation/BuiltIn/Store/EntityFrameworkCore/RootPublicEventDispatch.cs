using Microsoft.EntityFrameworkCore;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.EntityFramework;

internal static class RootPublicEventDispatch
{
    internal const string Table = "FoundationPublicEventDispatch";

    internal static string Columns(bool sqlite)
    {
        var text = sqlite ? "TEXT COLLATE BINARY" : """text COLLATE "C" """;
        var boolean = sqlite ? "INTEGER" : "boolean";
        return $"""
            "Occurrence" {text} NOT NULL PRIMARY KEY REFERENCES "FoundationPublicEventPublications" ("Occurrence") ON DELETE CASCADE,
            "Ticket" {text} NOT NULL UNIQUE, "Target" {text} NOT NULL, "Partition" integer NOT NULL CHECK ("Partition" >= 0),
            "Status" integer NOT NULL CHECK ("Status" IN (0, 1, 2)),
            "LiveThroughInstance" {text}, "LiveThroughWait" {text}, "LiveComplete" {boolean} NOT NULL,
            "StartThrough" {text}, "StartsComplete" {boolean} NOT NULL, "ProtectedSlot" {text}, "ProtectedNode" {text},
            CHECK (("LiveThroughInstance" IS NULL) = ("LiveThroughWait" IS NULL)),
            CHECK (("ProtectedSlot" IS NULL) = ("ProtectedNode" IS NULL)),
            CHECK ("LiveComplete" OR ("StartThrough" IS NULL AND NOT "StartsComplete")),
            CHECK ("StartsComplete" OR "ProtectedSlot" IS NULL)
            """;
    }

    internal sealed record Cursor(SubscriptionPosition? LiveThrough, bool LiveComplete, string? StartThrough,
        bool StartsComplete = false, RootEventStarts.MatchPosition? ProtectedThrough = null)
    {
        internal static Cursor Initial { get; } = new(null, false, null);
    }

    internal sealed record Publication(string Occurrence, string Tenant, string Key, string EventId,
        (string? Text, bool Escaped) Payload, long KeepUntil, Cursor Position);

    private sealed record WorkRow(bool SourceReady, string? Tenant, string? Key, string? EventId, string? Payload, bool? PayloadEscaped,
        long? KeepUntil, string? LiveThroughInstance, string? LiveThroughWait, bool? LiveComplete, string? StartThrough,
        bool? StartsComplete, string? ProtectedSlot, string? ProtectedNode);

    internal static async Task AddAsync(KernelDb db, string occurrence, string ticket, string target, int partition,
        Func<FormattableString, FormattableString>? bind, CancellationToken token)
    {
        var inserted = await db.Database.ExecuteSqlInterpolatedAsync(RootPublicEventStarts.Bind($"""
            WITH source AS MATERIALIZED (/*public-event-start-source*/)
            INSERT INTO "FoundationPublicEventDispatch" ("Occurrence", "Ticket", "Target", "Partition", "Status", "LiveComplete", "StartsComplete")
            SELECT {occurrence}, {ticket}, {target}, {partition}, {(int)Contract.PublicationStatus.Pending}, false, false
            FROM source WHERE "SourceReady"
            """, bind), token);
        if (inserted != 1) throw SourceChanged();
    }

    internal static async Task<Publication?> ReadPendingAsync(KernelDb db, string occurrence, string ticket, string target, int partition,
        Func<FormattableString, FormattableString>? bind, CancellationToken token)
    {
        var row = await db.Database.SqlQuery<WorkRow>(RootPublicEventStarts.Bind($"""
            WITH source AS MATERIALIZED (/*public-event-start-source*/), pending AS MATERIALIZED (
                SELECT p."Tenant", p."Key", p."EventId", p."Payload", p."PayloadEscaped", p."KeepUntil",
                    d."LiveThroughInstance", d."LiveThroughWait", d."LiveComplete", d."StartThrough",
                    d."StartsComplete", d."ProtectedSlot", d."ProtectedNode"
                FROM "FoundationPublicEventDispatch" d JOIN "FoundationPublicEventPublications" p ON p."Occurrence" = d."Occurrence"
                WHERE d."Occurrence" = {occurrence} AND d."Ticket" = {ticket} AND d."Target" = {target}
                    AND d."Partition" = {partition} AND d."Status" = {(int)Contract.PublicationStatus.Pending}
                    AND (SELECT "SourceReady" FROM source)
            )
            SELECT source."SourceReady", pending.* FROM source LEFT JOIN pending ON true
            """, bind)).SingleAsync(token);
        if (!row.SourceReady) throw SourceChanged();
        if (row.Tenant is null) return null;
        if (row.KeepUntil is not { } keepUntil || row.PayloadEscaped is not { } escaped || row.LiveComplete is not { } liveComplete ||
            row.StartsComplete is not { } startsComplete)
            throw new StoreFormatException("The pending publication is missing its payload retention or cursor state.");
        SubscriptionPosition? through = row.LiveThroughInstance is { } instance ? new(instance, row.LiveThroughWait!) : null;
        RootEventStarts.MatchPosition? protectedThrough = row.ProtectedSlot is { } slot ? new(slot, row.ProtectedNode!) : null;
        return new(occurrence, row.Tenant, row.Key!, DurableKeyJson.Read<string>(row.EventId!), (row.Payload, escaped), keepUntil,
            new(through, liveComplete, row.StartThrough, startsComplete, protectedThrough));
    }

    internal static async Task<bool> ChangeAsync(KernelDb db, Publication publication, Cursor next, Contract.PublicationStatus status,
        Func<FormattableString, FormattableString>? bind, CancellationToken token)
    {
        var before = publication.Position;
        var changed = await db.Database.ExecuteSqlInterpolatedAsync(RootPublicEventStarts.Bind($"""
            WITH source AS MATERIALIZED (/*public-event-start-source*/)
            UPDATE "FoundationPublicEventDispatch"
            SET "LiveThroughInstance" = CAST({next.LiveThrough?.Instance} AS TEXT), "LiveThroughWait" = CAST({next.LiveThrough?.Wait} AS TEXT),
                "LiveComplete" = {next.LiveComplete}, "StartThrough" = CAST({next.StartThrough} AS TEXT), "StartsComplete" = {next.StartsComplete},
                "ProtectedSlot" = CAST({next.ProtectedThrough?.Slot} AS TEXT), "ProtectedNode" = CAST({next.ProtectedThrough?.Node} AS TEXT),
                "Status" = {(int)status}
            WHERE "Occurrence" = {publication.Occurrence} AND "Status" = {(int)Contract.PublicationStatus.Pending}
                AND "LiveThroughInstance" IS NOT DISTINCT FROM CAST({before.LiveThrough?.Instance} AS TEXT)
                AND "LiveThroughWait" IS NOT DISTINCT FROM CAST({before.LiveThrough?.Wait} AS TEXT)
                AND "LiveComplete" = {before.LiveComplete} AND "StartThrough" IS NOT DISTINCT FROM CAST({before.StartThrough} AS TEXT)
                AND "StartsComplete" = {before.StartsComplete}
                AND "ProtectedSlot" IS NOT DISTINCT FROM CAST({before.ProtectedThrough?.Slot} AS TEXT)
                AND "ProtectedNode" IS NOT DISTINCT FROM CAST({before.ProtectedThrough?.Node} AS TEXT)
                AND (SELECT "SourceReady" FROM source)
            """, bind), token);
        return changed == 1;
    }

    private static StoreFormatException SourceChanged() => new("The pinned public Event dispatch source changed.");
}
