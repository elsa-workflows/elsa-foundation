using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Kernel.EntityFramework;

public sealed partial class EfStore
{
    internal sealed record ParkInboxRow(long Sequence, string Id, long Scope, string Kind, long? Due, int Attempts, ParkKeepReason Reason);
    internal sealed record ParkInboxSnapshot(IReadOnlyList<ParkInboxRow> Rows, long AsOf);

    private sealed record ParkInboxProjection(bool SourceReady, bool ParkMatches, long AsOf, int? StoreSchema,
        int? MessageProtocol, int? JournalCodec, string? DefinitionStamp, long? Sequence, string? Id, long? Scope,
        string? Kind, long? Due, int? Attempts, short? Reason);

    internal bool SupportsParkInbox()
    {
        using var db = create();
        return Engine is null && db.UsesRootParkArrivals && (postgres ? db.Database.IsNpgsql() : db.UsesSqliteRootRuntime);
    }

    internal async Task<ParkInboxSnapshot?> ReadRootParkInboxAsync(string tenant, string reference, FoundationParkMark mark,
        ParkPageCursor after, int limit, CancellationToken token)
    {
        await using var db = create();
        if (Engine is not null || !db.UsesRootParkArrivals || (postgres ? !db.Database.IsNpgsql() : !db.UsesSqliteRootRuntime))
            throw new NotSupportedException("Park inbox inspection requires a composed root provider.");

        var expected = db.RequiredFormat;
        FormattableString query = $"""
            WITH marker AS (
                SELECT v."StoreSchema", v."MessageProtocol", v."JournalCodec" /*marker-namespace*/
                FROM "KernelStoreVersions" v /*marker-join*/ WHERE v."Id" = {StoreVersionEntity.MarkerId}
            ), source AS (SELECT /*source-ready*/ AS "Ready"), parked AS (
                SELECT b."Actor", b."Partition"
                FROM "FoundationRootBirths" b JOIN "Instances" i
                    ON i."Id" = b."Actor" AND i."Partition" = b."Partition"
                    AND i."FoundationReference" = b."Reference" AND i."FoundationTenant" = b."Tenant"
                WHERE b."Tenant" = {tenant} AND b."Reference" = {reference} AND b."Creation" = 1 AND NOT i."Retired"
                    AND i."Status" = {(int)InstanceStatus.Faulted}
                    AND i."FoundationParkAt" = {mark.At} AND i."FoundationParkRevision" = {mark.Revision}
                    AND (SELECT "Ready" FROM source) AND EXISTS (SELECT 1 FROM marker
                        WHERE "StoreSchema" = {expected.StoreSchema} AND "MessageProtocol" = {expected.MessageProtocol}
                            AND "JournalCodec" = {expected.JournalCodec})
            ), page AS (
                SELECT m."Seq" AS "Sequence", m."Id", m."InboxScope" AS "Scope", m."Kind", m."Due", m."Attempts",
                    m."FoundationKeptBy" AS "Reason"
                FROM parked p JOIN "Messages" m ON m."Partition" = p."Partition" AND m."Target" = p."Actor"
                WHERE m."Done" AND m."FoundationKeptBy" IS NOT NULL
                    AND m."FoundationKeptBy" IN ({(short)ParkKeepReason.Consumed}, {(short)ParkKeepReason.Arrived})
                    AND (m."Seq", m."Id" /*sort-collation*/, m."InboxScope") > ({after.Seq}, {after.Key.Id ?? ""}, {after.Key.Scope})
                ORDER BY m."Seq", m."Id" /*sort-collation*/, m."InboxScope" LIMIT {limit}
            )
            SELECT (SELECT "Ready" FROM source) AS "SourceReady", EXISTS (SELECT 1 FROM parked) AS "ParkMatches",
                /*read-clock*/ AS "AsOf", marker."StoreSchema", marker."MessageProtocol", marker."JournalCodec",
                /*definitions-stamp*/ AS "DefinitionStamp", page.*
            FROM (SELECT 1) stamp LEFT JOIN marker ON true LEFT JOIN page ON true
            ORDER BY page."Sequence", page."Id" /*sort-collation*/, page."Scope"
            """;
        var sql = query.Format
            .Replace("/*marker-namespace*/", postgres ? ", c.relnamespace" : "", StringComparison.Ordinal)
            .Replace("/*marker-join*/", postgres ? "JOIN pg_catalog.pg_class c ON c.oid = v.tableoid" : "", StringComparison.Ordinal)
            .Replace("/*source-ready*/", postgres ? """
                EXISTS (SELECT 1 FROM marker m WHERE 3 = (SELECT count(*) FROM pg_catalog.pg_class c
                    WHERE c.oid IN (to_regclass('"FoundationRootBirths"'), to_regclass('"Instances"'), to_regclass('"Messages"'))
                        AND c.relnamespace = m.relnamespace AND c.relkind = 'r'))
                """ : "true", StringComparison.Ordinal)
            .Replace("/*sort-collation*/", postgres ? "COLLATE \"C\"" : "COLLATE BINARY", StringComparison.Ordinal)
            .Replace("/*read-clock*/", postgres ? "floor(extract(epoch from statement_timestamp()) * 1000)::bigint" : "0", StringComparison.Ordinal);
        sql = postgres ? StoreDefinitionsRevision.AddPostgresStampSql(sql)
            : sql.Replace("/*definitions-stamp*/", "NULL", StringComparison.Ordinal);
        var statement = FormattableStringFactory.Create(sql, query.GetArguments());
        if (!postgres)
            return await ReadSqliteRootSnapshotAsync(db, async at => Build(await SampleAsync(), at), token);

        List<ParkInboxProjection> rows;
        try { rows = await SampleAsync(); }
        catch (PostgresException error) when (error.SqlState is PostgresErrorCodes.UndefinedTable or PostgresErrorCodes.UndefinedColumn)
        { throw new StoreFormatException("The root Park inbox has an unreadable schema.", error); }

        var first = rows[0];
        RequireSnapshotFormat(db, first.StoreSchema, first.MessageProtocol, first.JournalCodec, first.DefinitionStamp);
        if (!first.SourceReady) throw new StoreFormatException("The root Park inbox requires its source tables in the marker schema.");
        return Build(rows, first.AsOf);

        Task<List<ParkInboxProjection>> SampleAsync() => db.Database.SqlQuery<ParkInboxProjection>(statement).ToListAsync(token);

        static ParkInboxSnapshot? Build(List<ParkInboxProjection> rows, long at) => rows[0].ParkMatches
            ? new(rows.Where(row => row.Sequence.HasValue).Select(Project).ToArray(), at) : null;

        static ParkInboxRow Project(ParkInboxProjection row) => row is
            { Sequence: > 0, Id: { } id, Scope: { } scope, Kind: { } kind, Attempts: >= 0, Reason: { } reason }
            ? new(row.Sequence.Value, id, scope, kind, row.Due, row.Attempts.Value, (ParkKeepReason)reason)
            : throw new StoreFormatException("The root Park inbox has invalid kept-delivery metadata.");
    }
}
