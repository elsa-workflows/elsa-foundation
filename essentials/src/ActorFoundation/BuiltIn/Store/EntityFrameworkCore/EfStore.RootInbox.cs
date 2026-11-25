using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Kernel.EntityFramework;

public sealed partial class EfStore
{
    // This reserve covers the escaped, name-limited direct and operator headers.
    private const int MaxRootInboxHeaderBytes = 24 * 1024;

    internal sealed record RootInboxEntry(long Seq, string Occurrence, string Id, string Kind, long? Due, int Attempts, long Size, ParkKeepReason? KeptBy);
    internal sealed record RootInboxPage(IReadOnlyList<RootInboxEntry> Rows, long AsOf);
    private sealed record InboxProjection(long AsOf, int? StoreSchema, int? MessageProtocol, int? JournalCodec, bool SourceReady,
        long? Seq, string? Occurrence, string? Kind, string? Header, long? Due, int? Attempts, long? Size, bool? HasReply,
        bool? SupportedShape, short? KeptBy);

    internal async Task<RootInboxPage> ReadRootInboxAsync(string tenant, string reference, long afterSeq, string afterOccurrence,
        int limit, CancellationToken cancellationToken)
    {
        await using var db = create();
        // A composed Park set inspects its direct and operator rows beside self-sends and jobs, which it never lists.
        if (!postgres || Engine is not null || !db.Database.IsNpgsql() ||
            (db.UsesRootSelfProtocol || db.UsesRootJobProtocol) && !db.UsesRootParkInspection)
            throw new NotSupportedException("Inbox inspection requires a PostgreSQL direct-input root profile.");

        var expected = db.RequiredFormat;
        List<InboxProjection> sampled;
        try
        {
            FormattableString query = $"""
                WITH marker AS (
                    SELECT v."StoreSchema", v."MessageProtocol", v."JournalCodec", c.relnamespace
                    FROM "KernelStoreVersions" v JOIN pg_catalog.pg_class c ON c.oid = v.tableoid
                    WHERE v."Id" = {StoreVersionEntity.MarkerId}
                ), source AS (
                    SELECT EXISTS (
                        SELECT 1 FROM marker m WHERE 3 = (
                            SELECT count(*) FROM pg_catalog.pg_class c
                            WHERE c.oid IN (to_regclass('"FoundationRootBirths"'), to_regclass('"Instances"'), to_regclass('"Messages"'))
                                AND c.relnamespace = m.relnamespace AND c.relkind = 'r')
                    ) AS "SourceReady"
                ), page AS (
                    SELECT m."Seq", m."Id" AS "Occurrence", m."Kind",
                        CASE WHEN octet_length(m."Start") <= {MaxRootInboxHeaderBytes} THEN m."Start" END AS "Header",
                        m."Due", m."Attempts", /*kept-by*/ AS "KeptBy",
                        (octet_length(m."Id")::bigint + octet_length(m."Kind") + coalesce(octet_length(m."Start"), 0)
                            + coalesce(octet_length(m."Payload"), 0)) AS "Size",
                        m."ReplyTo" IS NOT NULL AS "HasReply",
                        (m."JobGeneration" = 0 AND m."Sender" IS NULL AND m."KeepUntil" IS NOT NULL
                            AND (m."Id" NOT LIKE 'operator/%' OR m."Payload" IS NULL)
                            AND (m."ReplyTo" IS NULL OR (
                                (CASE WHEN right(m."ReplyTo", 6) = '|early' THEN left(m."ReplyTo", -6)
                                    ELSE m."ReplyTo" END) ~ '^[^|]+[|][^|]+[|].+$'
                                AND reply.deadline ~ '^[+]?[0-9]+$' AND normalized.digits <> ''
                                AND (length(normalized.digits) < 19 OR length(normalized.digits) = 19 AND
                                     normalized.digits COLLATE "C" <= '9223372036854775807')
                                AND (m."Id" NOT LIKE 'operator/%' OR right(m."ReplyTo", 6) <> '|early')
                            ))) AS "SupportedShape"
                    FROM "FoundationRootBirths" b JOIN "Instances" i
                        ON i."Id" = b."Actor" AND i."Partition" = b."Partition"
                        AND i."FoundationReference" = b."Reference" AND i."FoundationTenant" = b."Tenant"
                    JOIN "Messages" m ON m."Partition" = b."Partition" AND m."Target" COLLATE "C" = b."Actor"
                    CROSS JOIN LATERAL (
                        SELECT btrim(split_part(m."ReplyTo", '|', 2), ' ' || chr(9) || chr(10) || chr(11) || chr(12) || chr(13)) AS deadline
                    ) reply
                    CROSS JOIN LATERAL (SELECT ltrim(reply.deadline, '+0') AS digits) normalized
                    WHERE b."Tenant" = {tenant} AND b."Reference" = {reference} AND b."Creation" = 1 AND NOT i."Retired"
                        AND m."InboxScope" = -4 AND m."EventMatch" IS NULL
                        AND (NOT m."Done" OR /*kept*/)
                        AND (NOT m."Done" OR /*root-parked*/)
                        AND (m."Seq", m."Id" COLLATE "C") > ({afterSeq}, {afterOccurrence})
                        AND (SELECT "SourceReady" FROM source)
                        AND EXISTS (SELECT 1 FROM marker WHERE "StoreSchema" = {expected.StoreSchema}
                            AND "MessageProtocol" = {expected.MessageProtocol} AND "JournalCodec" = {expected.JournalCodec})
                    ORDER BY m."Seq", m."Id" COLLATE "C" LIMIT {limit}
                )
                SELECT floor(extract(epoch from statement_timestamp()) * 1000)::bigint AS "AsOf",
                    marker."StoreSchema", marker."MessageProtocol", marker."JournalCodec",
                    (SELECT "SourceReady" FROM source) AS "SourceReady", page.*
                FROM (VALUES (1)) stamp(id) LEFT JOIN page ON true LEFT JOIN marker ON true
                ORDER BY page."Seq", page."Occurrence" COLLATE "C"
                """;
            var kept = db.UsesRootParkArrivals ? """m."FoundationKeptBy" IN (0, 1)"""
                : db.UsesRootParkInspection ? """m."FoundationKeptBy" = 0""" : "false";
            var keptBy = db.UsesRootParkInspection ? """m."FoundationKeptBy" """ : "NULL::smallint";
            var rootParked = db.UsesRootParkInspection ? """i."FoundationParkAt" IS NOT NULL""" : "false";
            var sql = query.Format.Replace("/*kept*/", kept, StringComparison.Ordinal)
                .Replace("/*kept-by*/", keptBy, StringComparison.Ordinal)
                .Replace("/*root-parked*/", rootParked, StringComparison.Ordinal);
            sampled = await db.Database.SqlQuery<InboxProjection>(FormattableStringFactory.Create(sql, query.GetArguments()))
                .ToListAsync(cancellationToken);
        }
        catch (PostgresException error) when (error.SqlState is PostgresErrorCodes.UndefinedTable or PostgresErrorCodes.UndefinedColumn)
        {
            throw new StoreFormatException("The root inbox has an unreadable schema.", error);
        }
        catch (PostgresException error) when (error.SqlState == PostgresErrorCodes.FeatureNotSupported ||
            error.SqlState.StartsWith("42", StringComparison.Ordinal) && error.SqlState != PostgresErrorCodes.InsufficientPrivilege)
        {
            await RequireStoreVersionsAsync(db, cancellationToken);
            throw;
        }

        var first = sampled[0];
        RequireSnapshotFormat(expected, first.StoreSchema, first.MessageProtocol, first.JournalCodec);
        if (!first.SourceReady) throw new StoreFormatException("The root inbox requires its source tables in the marker schema.");

        return new(sampled.Where(row => row.Seq.HasValue).Select(Project).ToArray(), first.AsOf);

        RootInboxEntry Project(InboxProjection row)
        {
            if (row.SupportedShape != true || row is not { Seq: > 0, Occurrence: { } occurrence, Kind: { } kind, Header: { } header,
                    Due: { } due, Attempts: { } attempts, Size: { } size, HasReply: { } hasReply })
                throw new StoreFormatException("The root inbox has an unsupported occurrence.");
            var (id, publicKind, publicDue) = occurrence.StartsWith(PostgresRootAdmission.OperatorOccurrencePrefix, StringComparison.Ordinal)
                ? PostgresRootAdmission.InspectOperatorHeader(db, header, reference, occurrence, kind, due, hasReply)
                : PostgresRootAdmission.InspectDirectHeader(db, header, reference, occurrence, kind, due, hasReply);
            return new(row.Seq.Value, occurrence, id, publicKind, publicDue, attempts, size, row.KeptBy is { } reason ? (ParkKeepReason)reason : null);
        }
    }
}
