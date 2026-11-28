using System.Runtime.CompilerServices;
using Kernel.Durable;
using Microsoft.EntityFrameworkCore;

namespace Kernel.EntityFramework;

internal static partial class RootPublicEventPublications
{
    private const string Uncovered = """
        AND NOT EXISTS (SELECT 1 FROM "FoundationPublicEventStarts" r
            WHERE r."Tenant" = p."Tenant" AND r."Key" = p."Key" AND p."KeepUntil" >= r."RetainFrom"
                AND p."CleanupAt" = p."KeepUntil" AND p."Ordinal" > r."CatchAfter" AND p."Ordinal" <= r."CatchThrough")
        AND NOT EXISTS (SELECT 1 FROM "FoundationPublicEventLiveWaits" r
            WHERE r."Tenant" = p."Tenant" AND r."Key" = p."Key" AND p."KeepUntil" >= r."RetainFrom"
                AND p."CleanupAt" = p."KeepUntil" AND p."Ordinal" > r."CatchAfter" AND p."Ordinal" <= r."CatchThrough")
        AND NOT EXISTS (SELECT 1 FROM "FoundationPublicEventTargets" t
            WHERE t."Tenant" = p."Tenant" AND t."Publication" = p."Occurrence" AND t."Reference" IS NULL)
        """;

    internal sealed record CleanupKey(bool SourceReady, string? Tenant, string? Key, long? Ordinal);
    private sealed record CleanupPublication(bool SourceReady, string? Occurrence, long? ReceiptUntil, long? ClosedKeepUntil);

    internal static async Task<CleanupKey?> FindCleanupAsync(KernelDb db, long now, Func<FormattableString, FormattableString>? bind,
        CancellationToken token)
    {
        var next = await db.Database.SqlQuery<CleanupKey>(CleanupSql($"""
            WITH source AS MATERIALIZED (/*public-event-start-source*/), expired AS (
                SELECT p."Tenant", p."Key", p."Ordinal" FROM "FoundationPublicEventPublications" p
                WHERE p."CleanupAt" < {now} AND (SELECT "SourceReady" FROM source) /*uncovered*/
                ORDER BY p."CleanupAt", p."Ordinal" LIMIT 1
            )
            SELECT source."SourceReady", expired.* FROM source LEFT JOIN expired ON true
            """, db.UsesRootPagedPublicEvents, now, bind)).SingleAsync(token);
        RequireCleanupSource(next.SourceReady);
        return next.Ordinal is null ? null : next;
    }

    // The key lock or the SQLite writer serializes this expiry with registration. A registration
    // entitled to an event before its expiry keeps that payload until delivery or removal.
    internal static async Task<bool> CleanupAsync(KernelDb db, CleanupKey target, long now,
        Func<FormattableString, FormattableString>? bind, CancellationToken token)
    {
        var expired = await db.Database.SqlQuery<CleanupPublication>(CleanupSql($"""
            WITH source AS MATERIALIZED (/*public-event-start-source*/), expired AS (
                SELECT p."Occurrence", p."ReceiptUntil", CASE WHEN p."CleanupAt" = p."KeepUntil" THEN p."KeepUntil" END AS "ClosedKeepUntil"
                FROM "FoundationPublicEventPublications" p
                WHERE p."Tenant" = {target.Tenant} AND p."Key" = {target.Key} AND p."Ordinal" = {target.Ordinal}
                    AND p."CleanupAt" < {now} AND (SELECT "SourceReady" FROM source) /*uncovered*/
            )
            SELECT source."SourceReady", expired.* FROM source LEFT JOIN expired ON true
            """, db.UsesRootPagedPublicEvents, now, bind)).SingleAsync(token);
        RequireCleanupSource(expired.SourceReady);
        if (expired.Occurrence is null) return false;
        if (expired.ClosedKeepUntil is { } closedKeepUntil)
        {
            await AdvanceRetentionAsync(db, target.Tenant!, closedKeepUntil, bind, token);
            if (db.UsesRootPagedPublicEvents)
            {
                if (await PruneLiveReceiptPageAsync(db, expired.Occurrence, bind, token) > 0)
                    return true;
            }
            else
            {
                await db.Database.ExecuteSqlInterpolatedAsync(RootPublicEventStarts.Bind($"""
                    WITH source AS MATERIALIZED (/*public-event-start-source*/)
                    DELETE FROM "FoundationPublicEventLiveReceipts" WHERE "Occurrence" = {expired.Occurrence}
                        AND (SELECT "SourceReady" FROM source)
                    """, bind), token);
            }
        }

        if (db.UsesRootPagedPublicEvents)
        {
            if (await PruneStartPageAsync(db, target.Tenant!, expired.Occurrence, bind, token) > 0)
                return true;
        }
        else
        {
            await db.Database.ExecuteSqlInterpolatedAsync(RootPublicEventStarts.Bind($"""
                WITH source AS MATERIALIZED (/*public-event-start-source*/)
                DELETE FROM "FoundationPublicEventTargets" WHERE "Tenant" = {target.Tenant} AND "Publication" = {expired.Occurrence}
                    AND "Reference" IS NOT NULL AND (SELECT "SourceReady" FROM source)
                """, bind), token);
        }

        var mutation = expired.ReceiptUntil >= now
            ? (FormattableString)$"""
                WITH source AS MATERIALIZED (/*public-event-start-source*/)
                UPDATE "FoundationPublicEventPublications" SET "Payload" = NULL, "PayloadEscaped" = false, "KeepUntil" = NULL, "CleanupAt" = "ReceiptUntil"
                WHERE "Tenant" = {target.Tenant} AND "Key" = {target.Key} AND "Ordinal" = {target.Ordinal}
                    AND (SELECT "SourceReady" FROM source)
                """
            : $"""
                WITH source AS MATERIALIZED (/*public-event-start-source*/)
                DELETE FROM "FoundationPublicEventPublications"
                WHERE "Tenant" = {target.Tenant} AND "Key" = {target.Key} AND "Ordinal" = {target.Ordinal}
                    AND (SELECT "SourceReady" FROM source)
                """;
        RequireCleanupSource(await db.Database.ExecuteSqlInterpolatedAsync(RootPublicEventStarts.Bind(mutation, bind), token) == 1);
        return true;
    }

    // Once the horizon advances, no new wait can claim the expired occurrence. An existing entitlement
    // prevented the cleanup from starting. Repeated passes can now drop receipts without reopening coverage.
    private static Task<int> PruneLiveReceiptPageAsync(KernelDb db, string occurrence,
        Func<FormattableString, FormattableString>? bind, CancellationToken token) =>
        db.Database.ExecuteSqlInterpolatedAsync(RootPublicEventStarts.Bind($"""
            WITH source AS MATERIALIZED (/*public-event-start-source*/), expired AS MATERIALIZED (
                SELECT "Delivery" FROM "FoundationPublicEventLiveReceipts"
                WHERE "Occurrence" = {occurrence} AND (SELECT "SourceReady" FROM source)
                ORDER BY "Delivery" LIMIT {InMemoryStore.ParkPageLimit}
            )
            DELETE FROM "FoundationPublicEventLiveReceipts"
            WHERE "Occurrence" = {occurrence} AND "Delivery" IN (SELECT "Delivery" FROM expired)
            """, bind), token);

    private static Task<int> PruneStartPageAsync(KernelDb db, string tenant, string occurrence,
        Func<FormattableString, FormattableString>? bind, CancellationToken token) =>
        db.Database.ExecuteSqlInterpolatedAsync(RootPublicEventStarts.Bind($"""
            WITH source AS MATERIALIZED (/*public-event-start-source*/), candidates AS MATERIALIZED (
                SELECT "Registration", CAST(octet_length("Start") AS BIGINT) + coalesce(octet_length("Payload"), 0)
                    + octet_length("Registration") + octet_length("Key") + octet_length("Wait")
                    + octet_length("EventId") + octet_length("Reference") AS "Bytes"
                FROM "FoundationPublicEventTargets"
                WHERE "Tenant" = {tenant} AND "Publication" = {occurrence} AND "Reference" IS NOT NULL
                    AND (SELECT "SourceReady" FROM source)
                ORDER BY "Registration" LIMIT {InMemoryStore.ParkPageLimit}
            ), budget AS (
                SELECT "Registration", row_number() OVER page AS "Position", sum("Bytes") OVER page AS "Bytes"
                FROM candidates WINDOW page AS (ORDER BY "Registration" ROWS UNBOUNDED PRECEDING)
            )
            DELETE FROM "FoundationPublicEventTargets"
            WHERE "Tenant" = {tenant} AND "Publication" = {occurrence} AND "Reference" IS NOT NULL AND "Registration" IN (
                SELECT "Registration" FROM budget WHERE "Position" = 1 OR "Bytes" <= {ParkRecoveryCredit.LimitBytes})
            """, bind), token);

    private static FormattableString CleanupSql(FormattableString query, bool paged, long now,
        Func<FormattableString, FormattableString>? bind)
    {
        var clock = "{" + query.ArgumentCount + "}";
        var uncovered = paged ? Uncovered + $"""
            AND NOT EXISTS (SELECT 1 FROM "FoundationPublicEventDispatch" d
                WHERE d."Occurrence" = p."Occurrence" AND (d."Status" = 0 OR (d."Status" = 2 AND p."ReceiptUntil" >= {clock})))
            """ : Uncovered;
        return RootPublicEventStarts.Bind(FormattableStringFactory.Create(query.Format.Replace("/*uncovered*/", uncovered, StringComparison.Ordinal),
            paged ? [.. query.GetArguments(), now] : query.GetArguments()), bind);
    }

    private static void RequireCleanupSource(bool ready)
    {
        if (!ready) throw new StoreFormatException("The pinned public event source changed during cleanup.");
    }
}
