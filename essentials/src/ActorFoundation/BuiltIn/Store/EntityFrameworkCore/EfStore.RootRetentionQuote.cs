using Kernel.Foundation.Contracts;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Kernel.EntityFramework;

public sealed partial class EfStore
{
    private sealed record RetentionMillis(long? Finished, long? Cancelled, long? Faulted)
    {
        internal static RetentionMillis From(ActorRetentionWindows windows) => new(
            Milliseconds(windows.Finished), Milliseconds(windows.Cancelled), Milliseconds(windows.Faulted));

        private static long? Milliseconds(TimeSpan? window)
        {
            if (window is not { } finite) return null;
            ArgumentOutOfRangeException.ThrowIfLessThan(finite, TimeSpan.Zero, nameof(window));
            // Terminal times have millisecond resolution. Round a partial millisecond up to preserve the age boundary.
            var (whole, partial) = Math.DivRem(finite.Ticks, TimeSpan.TicksPerMillisecond);
            return whole + (partial == 0 ? 0 : 1);
        }
    }

    private sealed record PostgresRetentionQuote(long NewlyEligible, long AsOf, long ProviderNow, bool SourceReady,
        int? StoreSchema, int? MessageProtocol, int? JournalCodec, string? DefinitionStamp);

    internal async Task<ActorRetentionQuote> ReadRootRetentionQuoteAsync(string tenant, string actorType,
        ActorRetentionWindows previous, ActorRetentionWindows proposed, KeyValuePair<string, string>? definitionLabel,
        long? asOf, CancellationToken token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorType);
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(proposed);
        if (asOf is < 0) throw new ArgumentOutOfRangeException(nameof(asOf));
        if (definitionLabel is { } label && (label.Key is null || label.Value is null))
            throw new ArgumentException("An exact label requires its key and value.", nameof(definitionLabel));
        var before = RetentionMillis.From(previous);
        var after = RetentionMillis.From(proposed);
        var type = DurableKeyJson.Write(actorType);
        var encodedLabel = definitionLabel is { } exact ? DurableKeyJson.Write(exact) : null;
        await using var db = create();
        if (Engine is not null || !db.UsesRootTerminalSelection || !db.UsesRootParkInspection)
            throw new NotSupportedException("Retention planning requires a qualified terminal and Park projection.");
        if (db.UsesSqliteRootRuntime)
            return await ReadSqliteRetentionQuoteAsync(db, tenant, type, encodedLabel, before, after, asOf, token);
        if (!postgres) throw new NotSupportedException("Retention planning requires the PostgreSQL or SQLite root actor projection.");
        return await ReadPostgresRetentionQuoteAsync(db, tenant, EncodeListFilter(type),
            encodedLabel is null ? null : EncodeListFilter(encodedLabel), before, after, asOf, token);
    }

    private static long CheckQuoteClock(long? asOf, long providerNow)
    {
        if (asOf > providerNow) throw new ArgumentOutOfRangeException(nameof(asOf), "The retention quote cannot use a future clock.");
        return asOf ?? providerNow;
    }

    private static Task<ActorRetentionQuote> ReadSqliteRetentionQuoteAsync(KernelDb db, string tenant, string type, string? label,
        RetentionMillis before, RetentionMillis after, long? asOf, CancellationToken token) =>
        ReadSqliteRootSnapshotAsync(db,
            providerNow => ReadSqliteRetentionQuoteAtAsync(db, tenant, type, label, before, after, CheckQuoteClock(asOf, providerNow), token),
            token);

    private static async Task<ActorRetentionQuote> ReadSqliteRetentionQuoteAtAsync(KernelDb db, string tenant, string type,
        string? label, RetentionMillis before, RetentionMillis after, long at, CancellationToken token)
    {
        var count = await db.Database.SqlQuery<long>($"""
            WITH windows("Status", "Previous", "Proposed") AS (VALUES
                ({(int)InstanceStatus.Finished}, {before.Finished}, {after.Finished}),
                ({(int)InstanceStatus.Cancelled}, {before.Cancelled}, {after.Cancelled}),
                ({(int)InstanceStatus.Faulted}, {before.Faulted}, {after.Faulted}))
            SELECT count(*) AS "Value"
            FROM "FoundationActorList" l JOIN windows w ON w."Status" = l."Status"
            WHERE l."Tenant" = {tenant} AND l."Type" COLLATE BINARY = {type}
                AND l."ParkAt" IS NULL AND l."TerminalRevision" > 0 AND l."EndedAt" > 0
                AND l."EndedAt" <= {at} - w."Proposed"
                AND (w."Previous" IS NULL OR l."EndedAt" > {at} - w."Previous")
                AND ({label} IS NULL OR EXISTS (SELECT 1 FROM json_each(l."Labels") entry WHERE entry.value COLLATE BINARY = {label}))
            """).SingleAsync(token);
        return new ActorRetentionQuote(count, at);
    }

    private static async Task<ActorRetentionQuote> ReadPostgresRetentionQuoteAsync(KernelDb db, string tenant, string type,
        string? label, RetentionMillis before, RetentionMillis after, long? asOf, CancellationToken token, long? physicalMarker = null)
    {
        var expected = db.RequiredFormat;
        PostgresRetentionQuote row;
        try
        {
            row = await db.Database.SqlQuery<PostgresRetentionQuote>(ActorListQuery(db, ActorListProjection.Of(db), $"""
                WITH clock AS (SELECT floor(extract(epoch from statement_timestamp()) * 1000)::bigint AS "ProviderNow"),
                quote AS (SELECT "ProviderNow", coalesce({asOf}::bigint, "ProviderNow") AS "AsOf" FROM clock),
                readiness AS (SELECT /*actor-list-ready*/ AND
                    ({physicalMarker}::bigint IS NULL OR pg_catalog.to_regclass('"KernelStoreVersions"')::oid::bigint = {physicalMarker})
                    AS "SourceReady"),
                windows("Status", "Previous", "Proposed") AS (VALUES
                    ({(int)InstanceStatus.Finished}, {before.Finished}::bigint, {after.Finished}::bigint),
                    ({(int)InstanceStatus.Cancelled}, {before.Cancelled}::bigint, {after.Cancelled}::bigint),
                    ({(int)InstanceStatus.Faulted}, {before.Faulted}::bigint, {after.Faulted}::bigint))
                SELECT quote."AsOf", quote."ProviderNow", readiness."SourceReady",
                    v."StoreSchema", v."MessageProtocol", v."JournalCodec", /*definitions-stamp*/ AS "DefinitionStamp",
                    CASE WHEN readiness."SourceReady" AND quote."AsOf" <= quote."ProviderNow"
                        AND v."StoreSchema" = {expected.StoreSchema} AND v."MessageProtocol" = {expected.MessageProtocol}
                        AND v."JournalCodec" = {expected.JournalCodec}
                    THEN (SELECT count(*) FROM "FoundationActorList" l JOIN windows w ON w."Status" = l."Status"
                        WHERE l."Tenant" = {tenant} AND l."Type" = {type}::jsonb
                            AND l."ParkAt" IS NULL AND l."TerminalRevision" > 0 AND l."EndedAt" > 0
                            AND l."EndedAt" <= quote."AsOf" - w."Proposed"
                            AND (w."Previous" IS NULL OR l."EndedAt" > quote."AsOf" - w."Previous")
                            AND ({label}::text IS NULL OR EXISTS (SELECT 1 FROM jsonb_array_elements(l."Labels") entry(value)
                                WHERE entry.value = {label}::jsonb)))
                    ELSE 0 END AS "NewlyEligible"
                FROM quote CROSS JOIN readiness LEFT JOIN "KernelStoreVersions" v ON v."Id" = {StoreVersionEntity.MarkerId}
                """)).SingleAsync(token);
        }
        catch (PostgresException error) when (error.SqlState is PostgresErrorCodes.UndefinedTable or PostgresErrorCodes.UndefinedColumn)
        {
            throw new StoreFormatException("The root actor projection has an unreadable schema.", error);
        }
        catch (PostgresException error) when (error.SqlState.StartsWith("42", StringComparison.Ordinal) &&
            error.SqlState != PostgresErrorCodes.InsufficientPrivilege)
        {
            if (physicalMarker is not null)
                throw new StoreFormatException("The pinned retention projection has an unreadable schema.", error);
            await RequireStoreVersionsAsync(db, token);
            throw;
        }
        RequireSnapshotFormat(db, row.StoreSchema, row.MessageProtocol, row.JournalCodec, row.DefinitionStamp);
        RequireRootActorListSource(row.SourceReady);
        CheckQuoteClock(asOf, row.ProviderNow);
        return new(row.NewlyEligible, row.AsOf);
    }
}
