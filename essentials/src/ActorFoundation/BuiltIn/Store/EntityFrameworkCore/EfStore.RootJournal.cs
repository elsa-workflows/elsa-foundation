using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Kernel.EntityFramework;

public sealed partial class EfStore
{
    internal sealed record RootJournalEntry(long Position, long Revision, string Entries);
    internal sealed record RootJournalPage(IReadOnlyList<RootJournalEntry> Rows, long AsOf);
    private sealed record JournalProjection(long AsOf, long? Position, long? Revision, string? Entries,
        int? StoreSchema, int? MessageProtocol, int? JournalCodec, string? DefinitionStamp);

    // The public reader will keep physical positions inside its continuation token.
    // The lower bound is inclusive so a reader can resume within one commit's entries.
    internal async Task<RootJournalPage> ReadRootJournalAsync(string tenant, string reference, long fromPosition, int limit,
        CancellationToken cancellationToken = default)
    {
        if (!postgres || Engine is not null) throw new NotSupportedException("Qualified root journal reads require PostgreSQL.");
        ArgumentOutOfRangeException.ThrowIfNegative(fromPosition);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
        await using var db = create();
        var expected = db.RequiredFormat;
        List<JournalProjection> sampled;
        try
        {
            sampled = await db.Database.SqlQuery<JournalProjection>(StoreDefinitionsRevision.AddPostgresStamp($"""
                WITH marker AS (
                    SELECT "StoreSchema", "MessageProtocol", "JournalCodec"
                    FROM "KernelStoreVersions" WHERE "Id" = {StoreVersionEntity.MarkerId}
                ), root AS (
                    SELECT b."Actor", b."Partition", i."Revision"
                    FROM "FoundationRootBirths" b JOIN "Instances" i
                        ON i."Id" = b."Actor" AND i."Partition" = b."Partition"
                        AND i."FoundationReference" = b."Reference" AND i."FoundationTenant" = b."Tenant"
                    WHERE b."Tenant" = {tenant} AND b."Reference" = {reference} AND b."Creation" = 1 AND NOT i."Retired"
                        AND EXISTS (SELECT 1 FROM marker WHERE "StoreSchema" = {expected.StoreSchema}
                            AND "MessageProtocol" = {expected.MessageProtocol} AND "JournalCodec" = {expected.JournalCodec})
                ), page AS (
                    SELECT j."Position", j."Revision", j."Entries"
                    FROM root r JOIN "Journal" j ON j."Partition" = r."Partition" AND j."Instance" = r."Actor"
                    WHERE j."Position" >= {fromPosition} AND j."Revision" BETWEEN 1 AND r."Revision"
                    ORDER BY j."Position" LIMIT {limit}
                )
                SELECT floor(extract(epoch from statement_timestamp()) * 1000)::bigint AS "AsOf",
                    page."Position", page."Revision", page."Entries",
                    marker."StoreSchema", marker."MessageProtocol", marker."JournalCodec", /*definitions-stamp*/ AS "DefinitionStamp"
                FROM (VALUES (1)) stamp(id) LEFT JOIN page ON true LEFT JOIN marker ON true
                ORDER BY page."Position"
                """)).ToListAsync(cancellationToken);
        }
        catch (PostgresException error) when (error.SqlState is PostgresErrorCodes.UndefinedTable or PostgresErrorCodes.UndefinedColumn)
        {
            throw new StoreFormatException("The root journal has an unreadable schema.", error);
        }
        catch (PostgresException error) when (error.SqlState.StartsWith("42", StringComparison.Ordinal) &&
            error.SqlState != PostgresErrorCodes.InsufficientPrivilege)
        {
            await RequireStoreVersionsAsync(db, cancellationToken);
            throw;
        }
        var first = sampled[0];
        RequireSnapshotFormat(db, first.StoreSchema, first.MessageProtocol, first.JournalCodec, first.DefinitionStamp);
        var rows = sampled.Where(row => row.Position.HasValue)
            .Select(row => new RootJournalEntry(row.Position!.Value, row.Revision!.Value, row.Entries!)).ToArray();
        return new(rows, first.AsOf);
    }
}
