using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Kernel.EntityFramework;

public sealed partial class EfStore
{
    // This reserve covers the escaped, name-limited direct and operator headers.
    private const int MaxRootInboxHeaderBytes = 24 * 1024;

    internal sealed record RootInboxEntry(long Seq, string Occurrence, string Id, string Kind, long? Due, int Attempts, long Size);
    internal sealed record RootInboxPage(IReadOnlyList<RootInboxEntry> Rows, long AsOf);
    private sealed record InboxProjection(long AsOf, int? StoreSchema, int? MessageProtocol, int? JournalCodec, bool SourceReady,
        long? Seq, string? Occurrence, string? Kind, string? Header, long? Due, int? Attempts, long? Size, bool? HasReply,
        bool? SupportedShape);

    internal async Task<RootInboxPage> ReadRootInboxAsync(string tenant, string reference, long afterSeq, string afterOccurrence,
        int limit, CancellationToken cancellationToken)
    {
        await using var db = create();
        if (!postgres || Engine is not null || !db.Database.IsNpgsql() || db.UsesRootSelfProtocol || db.UsesRootJobProtocol)
            throw new NotSupportedException("Inbox inspection requires a PostgreSQL direct-input root profile.");

        var expected = db.RequiredFormat;
        List<InboxProjection> sampled;
        try
        {
            sampled = await db.Database.SqlQuery<InboxProjection>($"""
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
                        m."Due", m."Attempts",
                        (octet_length(m."Id")::bigint + octet_length(m."Kind") + coalesce(octet_length(m."Start"), 0)
                            + coalesce(octet_length(m."Payload"), 0)) AS "Size",
                        m."ReplyTo" IS NOT NULL AS "HasReply",
                        (m."JobGeneration" = 0 AND m."Sender" IS NULL AND m."KeepUntil" IS NOT NULL
                            AND (m."Id" NOT LIKE 'operator/%' OR m."Payload" IS NULL)
                            AND (m."ReplyTo" IS NULL OR (
                                (CASE WHEN right(m."ReplyTo", 6) = '|early' THEN left(m."ReplyTo", -6)
                                    ELSE m."ReplyTo" END) ~ '^[^|]+[|][1-9][0-9]*[|].+$'
                                AND (length(split_part(m."ReplyTo", '|', 2)) < 19 OR
                                     length(split_part(m."ReplyTo", '|', 2)) = 19 AND
                                     split_part(m."ReplyTo", '|', 2) COLLATE "C" <= '9223372036854775807')
                                AND (m."Id" NOT LIKE 'operator/%' OR right(m."ReplyTo", 6) <> '|early')
                            ))) AS "SupportedShape"
                    FROM "FoundationRootBirths" b JOIN "Instances" i
                        ON i."Id" = b."Actor" AND i."Partition" = b."Partition"
                        AND i."FoundationReference" = b."Reference" AND i."FoundationTenant" = b."Tenant"
                    JOIN "Messages" m ON m."Partition" = b."Partition" AND m."Target" COLLATE "C" = b."Actor"
                    WHERE b."Tenant" = {tenant} AND b."Reference" = {reference} AND b."Creation" = 1 AND NOT i."Retired"
                        AND m."InboxScope" = -4 AND m."EventMatch" IS NULL AND NOT m."Done"
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
                """).ToListAsync(cancellationToken);
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
            return new(row.Seq.Value, occurrence, id, publicKind, publicDue, attempts, size);
        }
    }
}
