using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Kernel.EntityFramework;

public sealed partial class EfStore
{
    private sealed record SqliteActorProjection(string Reference, int Status, long? EndedAt, long? TerminalRevision,
        string Header, long? ParkAt, string? ParkFault, long? ParkRevision, string? ParkResolution);

    private sealed record SqliteInboxProjection(long Seq, string Occurrence, string Kind, string? Header, long Due, int Attempts,
        long Size, bool HasReply, bool SupportedShape, short? KeptBy);

    private static async Task<(RootListItem Item, long AsOf)?> ReadSqliteRootActorAsync(KernelDb db, string tenant, string reference,
        CancellationToken token) => await ReadSqliteRootSnapshotAsync<(RootListItem, long)?>(db, async at =>
    {
        var row = await db.Database.SqlQuery<SqliteActorProjection>($"""
            SELECT "Reference", "Status", "EndedAt", "TerminalRevision", "Header", "ParkAt", "ParkFault", "ParkRevision", "ParkResolution"
            FROM "FoundationActorList" WHERE "Tenant" = {tenant} AND "Reference" = {reference}
            """).SingleOrDefaultAsync(token);
        return row is null ? null : (ToSqliteRootListItem(row), at);
    }, token);

    private static Task<RootListPage> ReadSqliteRootActorListAsync(KernelDb db, string tenant, string after, string? type,
        int? status, KeyValuePair<string, string>[] labels, long? endedBefore, bool? parked, int limit, CancellationToken token) =>
        ReadSqliteRootSnapshotAsync(db, async at =>
        {
            var encodedType = type is null ? null : DurableKeyJson.Write(type);
            var encodedLabels = DurableKeyJson.Write(labels);
            // Compare each encoded label object. Extracting its key or value as SQL text would lose escaped code units.
            var rows = await db.Database.SqlQuery<SqliteActorProjection>($"""
                SELECT l."Reference", l."Status", l."EndedAt", l."TerminalRevision", l."Header", l."ParkAt", l."ParkFault", l."ParkRevision", l."ParkResolution"
                FROM "FoundationActorList" l WHERE l."Tenant" = {tenant} AND l."Reference" > {after}
                    AND ({encodedType} IS NULL OR l."Type" COLLATE BINARY = {encodedType})
                    AND ({status} IS NULL OR l."Status" = {status})
                    AND ({endedBefore} IS NULL OR l."EndedAt" < {endedBefore})
                    AND ({parked} IS NULL OR (l."ParkAt" IS NOT NULL) = {parked})
                    AND NOT EXISTS (SELECT 1 FROM json_each({encodedLabels}) criterion
                        WHERE NOT EXISTS (SELECT 1 FROM json_each(l."Labels") label WHERE label.value COLLATE BINARY = criterion.value))
                ORDER BY l."Reference" LIMIT {limit}
                """).ToListAsync(token);
            return new RootListPage(rows.Select(ToSqliteRootListItem).ToArray(), at);
        }, token);

    private static RootListItem ToSqliteRootListItem(SqliteActorProjection row)
    {
        if (row.ParkAt is not null && row.ParkRevision is not > 0 ||
            row.ParkAt is null && row.ParkResolution is not null ||
            row.ParkResolution is not (null or "terminating" or "deleting"))
            throw new StoreFormatException("The SQLite actor projection has an incomplete Park mark or resolution.");
        return ToRootListItem(row.Reference, row.Status, row.EndedAt, row.TerminalRevision, row.Header,
            qualified: true, row.ParkAt, row.ParkFault, row.ParkRevision) with { ParkResolution = row.ParkResolution };
    }

    private static Task<RootJournalPage> ReadSqliteRootJournalAsync(KernelDb db, string tenant, string reference,
        long fromPosition, int limit, CancellationToken token) => ReadSqliteRootSnapshotAsync(db, async at =>
    {
        var rows = await db.Database.SqlQuery<RootJournalEntry>($"""
            SELECT j."Position", j."Revision", j."Entries"
            FROM "FoundationRootBirths" b JOIN "Instances" i
                ON i."Id" = b."Actor" AND i."Partition" = b."Partition"
                AND i."FoundationTenant" = b."Tenant" AND i."FoundationReference" = b."Reference"
            JOIN "Journal" j ON j."Instance" = i."Id" AND j."Partition" = i."Partition"
            WHERE b."Tenant" = {tenant} AND b."Reference" = {reference} AND b."Creation" = 1 AND NOT i."Retired"
                AND j."Position" >= {fromPosition} AND j."Revision" BETWEEN 1 AND i."Revision"
            ORDER BY j."Position" LIMIT {limit}
            """).ToListAsync(token);
        return new RootJournalPage(rows, at);
    }, token);

    private static Task<RootInboxPage> ReadSqliteRootInboxAsync(KernelDb db, string tenant, string reference, long afterSeq,
        string afterOccurrence, int limit, CancellationToken token) => ReadSqliteRootSnapshotAsync(db, async at =>
    {
        // Inspect stored direct and operator envelopes, including commands awaiting a later runtime stage.
        // Project size from the record header and validate reply framing without returning the address.
        var rows = await db.Database.SqlQuery<SqliteInboxProjection>($"""
            WITH page AS (
                SELECT m."Seq", m."Id" AS "Occurrence", m."Kind",
                    CASE WHEN octet_length(m."Start") <= {MaxRootInboxHeaderBytes} THEN m."Start" END AS "Header",
                    m."Due", m."Attempts", m."FoundationKeptBy" AS "KeptBy",
                    (octet_length(m."Id") + octet_length(m."Kind") + coalesce(octet_length(m."Start"), 0)
                        + coalesce(octet_length(m."Payload"), 0)) AS "Size",
                    m."ReplyTo", (m."JobGeneration" = 0 AND m."Sender" IS NULL AND m."KeepUntil" IS NOT NULL
                        AND (m."Id" NOT GLOB 'operator/*' OR m."Payload" IS NULL)) AS "EnvelopeShape"
                FROM "FoundationRootBirths" b JOIN "Instances" i
                    ON i."Id" = b."Actor" AND i."Partition" = b."Partition"
                    AND i."FoundationTenant" = b."Tenant" AND i."FoundationReference" = b."Reference"
                JOIN "Messages" m ON m."Target" = b."Actor" AND m."Partition" = b."Partition"
                WHERE b."Tenant" = {tenant} AND b."Reference" = {reference} AND b."Creation" = 1 AND NOT i."Retired"
                    AND m."InboxScope" = {InboxKey.FoundationOccurrenceScope} AND m."EventMatch" IS NULL
                    AND (NOT m."Done" OR m."FoundationKeptBy" = 0)
                    AND (NOT m."Done" OR i."FoundationParkAt" IS NOT NULL)
                    AND (m."Seq", m."Id") > ({afterSeq}, {afterOccurrence})
                ORDER BY m."Seq", m."Id" LIMIT {limit}
            ), route AS (
                SELECT page.*, instr("ReplyTo", '|') AS "NodeEnd",
                    substr("ReplyTo", instr("ReplyTo", '|') + 1) AS "Tail",
                    substr("ReplyTo", -6) = '|early' AS "Early"
                FROM page
            ), deadline AS (
                SELECT route.*, instr("Tail", '|') AS "DeadlineEnd",
                    trim(substr("Tail", 1, instr("Tail", '|') - 1), char(9, 10, 11, 12, 13, 32)) AS "Number"
                FROM route
            ), normalized AS (
                SELECT deadline.*, ltrim(CASE WHEN substr("Number", 1, 1) = '+' THEN substr("Number", 2) ELSE "Number" END, '0') AS "Digits"
                FROM deadline
            )
            SELECT "Seq", "Occurrence", "Kind", "Header", "Due", "Attempts", "KeptBy", "Size",
                "ReplyTo" IS NOT NULL AS "HasReply",
                ("EnvelopeShape" AND ("ReplyTo" IS NULL OR (
                    "NodeEnd" > 1 AND "DeadlineEnd" > 1
                    AND length("Tail") > "DeadlineEnd" + CASE WHEN "Early" THEN 6 ELSE 0 END
                    AND "Digits" <> '' AND "Digits" NOT GLOB '*[^0-9]*'
                    AND (length("Digits") < 19 OR length("Digits") = 19 AND "Digits" <= '9223372036854775807')
                    AND ("Occurrence" NOT GLOB 'operator/*' OR NOT "Early")
                ))) AS "SupportedShape"
            FROM normalized ORDER BY "Seq", "Occurrence"
            """).ToListAsync(token);
        return new RootInboxPage(rows.Select(Project).ToArray(), at);

        RootInboxEntry Project(SqliteInboxProjection row)
        {
            var control = row.Occurrence.StartsWith(PostgresRootAdmission.OperatorOccurrencePrefix, StringComparison.Ordinal);
            if (!row.SupportedShape || row.Seq <= 0 || row.Header is not { } header)
                throw new StoreFormatException("The SQLite root inbox has an unsupported occurrence.");
            var (id, kind, due) = control
                ? PostgresRootAdmission.InspectOperatorHeader(db, header, reference, row.Occurrence, row.Kind, row.Due, row.HasReply)
                : PostgresRootAdmission.InspectDirectHeader(db, header, reference, row.Occurrence, row.Kind, row.Due, row.HasReply);
            return new(row.Seq, row.Occurrence, id, kind, due, row.Attempts, row.Size,
                row.KeptBy is { } reason ? (ParkKeepReason)reason : null);
        }
    }, token);

    private static async Task<T> ReadSqliteRootSnapshotAsync<T>(KernelDb db, Func<long, Task<T>> read, CancellationToken token)
    {
        await db.Database.OpenConnectionAsync(token);
        // Hold the version, definitions and result in one WAL snapshot without reserving the writer.
        await using var transaction = ((SqliteConnection)db.Database.GetDbConnection()).BeginTransaction(deferred: true);
        await using var enlistment = await db.Database.UseTransactionAsync(transaction, token);
        // Reading the schema table pins the snapshot and samples its clock in one statement.
        var at = await db.Database.SqlQueryRaw<long>(
            "SELECT " + SqliteCommitDialect.MillisecondClock + """
             AS "Value" FROM (SELECT count(*) FROM main.sqlite_schema) /* root-read-snapshot-clock */
            """).SingleAsync(token);
        await RequireSqliteOperationalFormatAsync(db, token);
        return await read(at);
    }
}
