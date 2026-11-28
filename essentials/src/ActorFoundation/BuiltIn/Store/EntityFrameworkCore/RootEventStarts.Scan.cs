using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;

namespace Kernel.EntityFramework;

internal static partial class RootEventStarts
{
    private sealed record ScanTail(bool SourceReady, long? Ordinal);
    private sealed record ScanRow(bool SourceReady, long? Ordinal, string? Id, string? Slot, long? Revision,
        string? EventId, string? Key, string? Payload, string? Authority);

    internal static async Task<long> ReadPendingTailAsync(KernelDb db, string tenant,
        Func<FormattableString, FormattableString>? bind, CancellationToken token)
    {
        RequireScan(db, bind);
        var tail = await db.Database.SqlQuery<ScanTail>(Bind($"""
            WITH source AS MATERIALIZED (/*event-start-source*/), tail AS (
                SELECT "Ordinal" FROM "FoundationEventStartTargets"
                WHERE "Tenant" = {tenant} AND "Reference" IS NULL AND (SELECT "SourceReady" FROM source)
                ORDER BY "Ordinal" DESC LIMIT 1
            )
            SELECT source."SourceReady", tail."Ordinal" FROM source LEFT JOIN tail ON true
            """, bind)).SingleAsync(token);
        if (!tail.SourceReady) throw SourceChanged();
        return tail.Ordinal ?? 0;
    }

    internal static async Task<IReadOnlyList<EventStartScanItem>> ReadPendingPageAsync(KernelDb db, string tenant,
        long after, long through, int limit, Func<FormattableString, FormattableString>? bind, CancellationToken token)
    {
        RequireScan(db, bind);
        ArgumentOutOfRangeException.ThrowIfNegative(after);
        ArgumentOutOfRangeException.ThrowIfLessThan(through, after);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(limit, PageSize);
        var size = bind is not null
            ? """octet_length("Authority")::bigint + coalesce(octet_length("Payload"), 0)"""
            : """length(CAST("Authority" AS BLOB)) + coalesce(length(CAST("Payload" AS BLOB)), 0)""";
        var query = Bind($"""
            WITH source AS MATERIALIZED (/*event-start-source*/), sized AS MATERIALIZED (
                SELECT "Ordinal", /*target-bytes*/ AS "Bytes" FROM "FoundationEventStartTargets"
                WHERE "Tenant" = {tenant} AND "Reference" IS NULL AND "Ordinal" > {after} AND "Ordinal" <= {through}
                    AND (SELECT "SourceReady" FROM source)
                ORDER BY "Ordinal" LIMIT {limit}
            ), quoted AS MATERIALIZED (
                SELECT "Ordinal", sum("Bytes") OVER (ORDER BY "Ordinal") AS "Through",
                    row_number() OVER (ORDER BY "Ordinal") AS "Position" FROM sized
            ), page AS MATERIALIZED (
                SELECT t."Ordinal", t."Id", t."Slot", t."Revision", t."EventId", t."Key", t."Payload", t."Authority"
                FROM quoted q JOIN "FoundationEventStartTargets" t ON t."Tenant" = {tenant} AND t."Ordinal" = q."Ordinal"
                WHERE q."Position" = 1 OR q."Through" <= {FoundationMetadataLimits.MessageContentBytes}
            )
            SELECT source."SourceReady", page.* FROM source LEFT JOIN page ON true ORDER BY page."Ordinal"
            """, bind);
        query = FormattableStringFactory.Create(query.Format.Replace("/*target-bytes*/", size, StringComparison.Ordinal),
            query.GetArguments());
        var rows = await db.Database.SqlQuery<ScanRow>(query).ToArrayAsync(token);
        if (!rows[0].SourceReady) throw SourceChanged();
        return rows[0].Ordinal is null ? [] : rows.Select(row => new EventStartScanItem(row.Ordinal!.Value,
            Project(tenant, new(row.SourceReady, row.Id, row.Slot, row.Revision, row.EventId, row.Key, row.Payload, row.Authority, null, false)))).ToArray();
    }

    private static void RequireScan(KernelDb db, Func<FormattableString, FormattableString>? bind)
    {
        RequireTransaction(db, bind);
        if (!db.UsesRootEventStartScan) throw new StoreFormatException("An ordered Event target scan requires its qualified store profile.");
    }
}
