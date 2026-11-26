using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Kernel.EntityFramework;

public sealed partial class EfStore
{
    private sealed record SqliteActorProjection(string Reference, int Status, long? EndedAt, long? TerminalRevision,
        string Header, long? ParkAt, string? ParkFault, long? ParkRevision);

    private sealed record SqliteInboxProjection(long Seq, string Occurrence, string Kind, string? Header, long Due, int Attempts,
        long Size, string? ReplyTo, bool SupportedShape, short? KeptBy);

    private static async Task<(RootListItem Item, long AsOf)?> ReadSqliteRootActorAsync(KernelDb db, string tenant, string reference,
        CancellationToken token) => await ReadSqliteRootSnapshotAsync<(RootListItem, long)?>(db, async at =>
    {
        var row = await db.Database.SqlQuery<SqliteActorProjection>($"""
            SELECT "Reference", "Status", "EndedAt", "TerminalRevision", "Header", "ParkAt", "ParkFault", "ParkRevision"
            FROM "FoundationActorList" WHERE "Tenant" = {tenant} AND "Reference" = {reference}
            """).SingleOrDefaultAsync(token);
        if (row is null) return null;
        if (row.ParkAt is not null && row.ParkRevision is not > 0)
            throw new StoreFormatException("The SQLite actor projection is missing its Park opening revision.");
        return (ToRootListItem(row.Reference, row.Status, row.EndedAt, row.TerminalRevision, row.Header,
            qualified: true, row.ParkAt, row.ParkFault, row.ParkRevision), at);
    }, token);

    private static Task<RootInboxPage> ReadSqliteRootInboxAsync(KernelDb db, string tenant, string reference, long afterSeq,
        string afterOccurrence, int limit, CancellationToken token) => ReadSqliteRootSnapshotAsync(db, async at =>
    {
        // Inspection reads delivery metadata. Size is computed in SQLite without materializing payloads.
        var rows = await db.Database.SqlQuery<SqliteInboxProjection>($"""
            SELECT m."Seq", m."Id" AS "Occurrence", m."Kind",
                CASE WHEN length(CAST(m."Start" AS BLOB)) <= {MaxRootInboxHeaderBytes} THEN m."Start" END AS "Header",
                m."Due", m."Attempts", m."FoundationKeptBy" AS "KeptBy",
                (length(CAST(m."Id" AS BLOB)) + length(CAST(m."Kind" AS BLOB)) +
                    coalesce(length(CAST(m."Start" AS BLOB)), 0) + coalesce(length(CAST(m."Payload" AS BLOB)), 0)) AS "Size",
                m."ReplyTo",
                (m."JobGeneration" = 0 AND m."Sender" IS NULL AND m."KeepUntil" IS NOT NULL
                    AND (m."Id" NOT GLOB 'operator/*' OR m."Payload" IS NULL)) AS "SupportedShape"
            FROM "FoundationRootBirths" b JOIN "Instances" i
                ON i."Id" = b."Actor" AND i."Partition" = b."Partition"
                AND i."FoundationTenant" = b."Tenant" AND i."FoundationReference" = b."Reference"
            JOIN "Messages" m ON m."Target" = b."Actor" AND m."Partition" = b."Partition"
            WHERE b."Tenant" = {tenant} AND b."Reference" = {reference} AND b."Creation" = 1 AND NOT i."Retired"
                AND m."InboxScope" = {InboxKey.FoundationOccurrenceScope} AND m."EventMatch" IS NULL
                AND (NOT m."Done" OR (m."FoundationKeptBy" = {(int)ParkKeepReason.Consumed} AND i."FoundationParkAt" IS NOT NULL))
                AND (m."Seq", m."Id") > ({afterSeq}, {afterOccurrence})
            ORDER BY m."Seq", m."Id" LIMIT {limit}
            """).ToListAsync(token);
        return new RootInboxPage(rows.Select(Project).ToArray(), at);

        RootInboxEntry Project(SqliteInboxProjection row)
        {
            var control = row.Occurrence.StartsWith(PostgresRootAdmission.OperatorOccurrencePrefix, StringComparison.Ordinal);
            if (!row.SupportedShape || row.Seq <= 0 || row.Header is not { } header ||
                row.ReplyTo is { } reply && (control ? !PostgresRootAdmission.ValidOperatorReply(reply)
                    : ReplyTo.Parse(reply) is not { Node.Length: > 0, Request.Length: > 0, Deadline: > 0 }))
                throw new StoreFormatException("The SQLite root inbox has an unsupported occurrence.");
            var (id, kind, due) = control
                ? PostgresRootAdmission.InspectOperatorHeader(db, header, reference, row.Occurrence, row.Kind, row.Due, row.ReplyTo is not null)
                : PostgresRootAdmission.InspectDirectHeader(db, header, reference, row.Occurrence, row.Kind, row.Due, row.ReplyTo is not null);
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
        await RequireSqliteOperationalFormatAsync(db, token);
        var at = await db.Database.SqlQueryRaw<long>(
            "SELECT " + SqliteCommitDialect.MillisecondClock + """ AS "Value" """).SingleAsync(token);
        return await read(at);
    }
}
