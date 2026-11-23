using Microsoft.EntityFrameworkCore;

namespace Kernel.EntityFramework;

public sealed partial class EfStore
{
    internal sealed record RootJournalEntry(long Position, long Revision, string Entries);
    internal sealed record RootJournalPage(IReadOnlyList<RootJournalEntry> Rows, long AsOf);
    private sealed record JournalProjection(long AsOf, long? Position, long? Revision, string? Entries);

    // The public reader will keep physical positions inside its continuation token.
    // The lower bound is inclusive so a reader can resume within one commit's entries.
    internal async Task<RootJournalPage> ReadRootJournalAsync(string tenant, string reference, long fromPosition, int limit,
        CancellationToken cancellationToken = default)
    {
        if (!postgres || Engine is not null) throw new NotSupportedException("Qualified root journal reads require PostgreSQL.");
        ArgumentOutOfRangeException.ThrowIfNegative(fromPosition);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
        await using var db = create();
        var sampled = await db.Database.SqlQuery<JournalProjection>($"""
            WITH root AS (
                SELECT b."Actor", b."Partition", i."Revision"
                FROM "FoundationRootBirths" b JOIN "Instances" i
                    ON i."Id" = b."Actor" AND i."Partition" = b."Partition"
                    AND i."FoundationReference" = b."Reference" AND i."FoundationTenant" = b."Tenant"
                WHERE b."Tenant" = {tenant} AND b."Reference" = {reference} AND b."Creation" = 1 AND NOT i."Retired"
            ), page AS (
                SELECT j."Position", j."Revision", j."Entries"
                FROM root r JOIN "Journal" j ON j."Partition" = r."Partition" AND j."Instance" = r."Actor"
                WHERE j."Position" >= {fromPosition} AND j."Revision" BETWEEN 1 AND r."Revision"
                ORDER BY j."Position" LIMIT {limit}
            )
            SELECT floor(extract(epoch from statement_timestamp()) * 1000)::bigint AS "AsOf",
                page."Position", page."Revision", page."Entries"
            FROM (VALUES (1)) stamp(id) LEFT JOIN page ON true
            ORDER BY page."Position"
            """).ToListAsync(cancellationToken);
        var rows = sampled.Where(row => row.Position.HasValue)
            .Select(row => new RootJournalEntry(row.Position!.Value, row.Revision!.Value, row.Entries!)).ToArray();
        return new(rows, sampled[0].AsOf);
    }
}
