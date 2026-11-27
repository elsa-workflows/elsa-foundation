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
        string? Kind, long? Due, int? Attempts, short? Reason, string? Header, bool? HasReply);

    internal bool SupportsParkInbox()
    {
        using var db = create();
        return Engine is null && db.UsesRootParkRecovery && (postgres ? db.Database.IsNpgsql() : db.UsesSqliteRootRuntime);
    }

    internal async Task<ParkInboxSnapshot?> ReadRootParkInboxAsync(string tenant, string reference, FoundationParkMark mark,
        ParkPageCursor after, int limit, CancellationToken token)
    {
        await using var db = create();
        if (Engine is not null || !db.UsesRootParkRecovery || (postgres ? !db.Database.IsNpgsql() : !db.UsesSqliteRootRuntime))
            throw new NotSupportedException("Park inbox inspection requires a composed root provider.");

        var expected = db.RequiredFormat;
        FormattableString query = $"""
            WITH marker AS (
                SELECT v."StoreSchema", v."MessageProtocol", v."JournalCodec" /*marker-namespace*/
                FROM "KernelStoreVersions" v /*marker-join*/ WHERE v."Id" = {StoreVersionEntity.MarkerId}
            ), source AS (SELECT /*source-ready*/ AS "Ready"), boundary AS (
                SELECT {after.Seq} AS "Sequence", {after.Key.Id ?? ""} AS "Id", {after.Key.Scope} AS "Scope", {limit} AS "Limit"
            ), parked AS (
                SELECT b."Actor", b."Partition" /*parked-pins*/
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
                /*page*/
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
            .Replace("/*parked-pins*/", postgres && RecordsConsumedRootJob(db) ? """, i."FoundationParkJobResult" AS "JobResult" """ : "", StringComparison.Ordinal)
            .Replace("/*page*/", postgres ? PostgresParkInboxPage(db) : SqliteParkInboxPage, StringComparison.Ordinal)
            .Replace("/*header-length*/", postgres ? """octet_length(m."Start")""" : """length(CAST(m."Start" AS BLOB))""", StringComparison.Ordinal)
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

        ParkInboxSnapshot? Build(List<ParkInboxProjection> rows, long at) => rows[0].ParkMatches
            ? new(rows.Where(row => row.Sequence.HasValue).Select(Project).ToArray(), at) : null;

        ParkInboxRow Project(ParkInboxProjection row)
        {
            if (row is not { Sequence: > 0, Id: { } id, Scope: { } scope, Kind: { } kind, Due: { } due,
                    Attempts: >= 0, Reason: { } reason, HasReply: { } hasReply })
                throw new StoreFormatException("The root Park inbox has invalid kept-delivery metadata.");
            var metadata = PostgresRootAdmission.IsStoredDirect(row.Header)
                ? PostgresRootAdmission.InspectDirectHeader(db, row.Header, reference, id, kind, due, hasReply)
                : PostgresRootAdmission.IsStoredOperator(row.Header)
                    ? PostgresRootAdmission.InspectOperatorHeader(db, row.Header!, reference, id, kind, due, hasReply)
                    : (Id: id, Kind: kind, Due: due == 0 ? null : (long?)due);
            return new(row.Sequence.Value, id, scope, metadata.Kind, metadata.Due, row.Attempts.Value, (ParkKeepReason)reason);
        }
    }

    private static readonly string ParkInboxProjectionSql = $"""
        m."Seq" AS "Sequence", m."Id", m."InboxScope" AS "Scope", m."Kind", m."Due", m."Attempts",
            m."FoundationKeptBy" AS "Reason",
            CASE WHEN m."InboxScope" = {InboxKey.FoundationOccurrenceScope} AND /*header-length*/ <= {MaxRootInboxHeaderBytes}
                THEN m."Start" END AS "Header", m."ReplyTo" IS NOT NULL AS "HasReply"
        """;

    private const string ParkInboxAfter = """
        m."Seq" >= b."Sequence"
        AND (m."Seq", m."Id" /*sort-collation*/, m."InboxScope") > (b."Sequence", b."Id", b."Scope")
        """;

    private static readonly string SqliteParkInboxPage = $"""
        SELECT {ParkInboxProjectionSql}
        FROM parked p JOIN "Messages" m ON m."Partition" = p."Partition" AND m."Target" = p."Actor"
        CROSS JOIN boundary b
        WHERE m."Done" AND m."FoundationKeptBy" IS NOT NULL AND m."FoundationKeptBy" IN (0, 1)
            AND {ParkInboxAfter}
        ORDER BY m."Seq", m."Id" /*sort-collation*/, m."InboxScope" LIMIT (SELECT "Limit" FROM boundary)
        """;

    private static string PostgresParkInboxPage(KernelDb db)
    {
        var scope = ParkInputPredicate(db, """m."InboxScope" """.TrimEnd());
        // The partial index orders by kept reason before sequence. Seek each reason independently, then merge the
        // two bounded streams with the exact creator and recorded-result rows that sit outside the ordinary scopes.
        var page = $"""
            SELECT q.*
            FROM parked p CROSS JOIN boundary b
            CROSS JOIN (VALUES (0::smallint), (1::smallint)) reason("Value")
            CROSS JOIN LATERAL (
                SELECT {ParkInboxProjectionSql}
                FROM "Messages" m
                WHERE m."Partition" = p."Partition" AND m."Target" COLLATE "C" = p."Actor"
                    AND {scope} AND m."EventMatch" IS NULL AND m."Done" AND m."FoundationKeptBy" IN (0, 1)
                    AND m."FoundationKeptBy" = reason."Value" AND {ParkInboxAfter}
                ORDER BY m."Seq", m."Id" COLLATE "C", m."InboxScope" LIMIT b."Limit"
            ) q
            """;
        var pins = new List<string>();
        if (db.UsesRootChildren)
            pins.Add($"""('root:' || p."Actor", 0::bigint, '{Kinds.Start}')""");
        if (RecordsConsumedRootJob(db))
            pins.Add($"""(p."JobResult", {RootTimedJobScope}::bigint, '{Kinds.JobFinished}')""");
        if (pins.Count > 0)
            page += $"""

                UNION ALL
                SELECT {ParkInboxProjectionSql}
                FROM parked p CROSS JOIN boundary b
                CROSS JOIN LATERAL (VALUES {string.Join(", ", pins)}) pin("Id", "Scope", "Kind")
                JOIN "Messages" m ON m."Id" = pin."Id" AND m."InboxScope" = pin."Scope" AND m."Kind" = pin."Kind"
                    AND m."Partition" = p."Partition" AND m."Target" = p."Actor"
                WHERE m."EventMatch" IS NULL AND m."Done" AND m."FoundationKeptBy" = {(short)ParkKeepReason.Consumed}
                    AND {ParkInboxAfter}
                """;
        return $"""
            SELECT * FROM ({page}) kept
            ORDER BY kept."Sequence", kept."Id" COLLATE "C", kept."Scope" LIMIT (SELECT "Limit" FROM boundary)
            """;
    }
}
