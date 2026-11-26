using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;

namespace Kernel.EntityFramework;

public sealed partial class EfStore
{
    private sealed record SqliteKeptEnvelope(string Id, long InboxScope, long Seq, short KeptBy, long Bytes, long TotalRows,
        string? Kind, string? Header, string? Payload, long? Due, string? ReplyTo, long? KeepUntil, long? JobGeneration, string? Sender);

    private sealed record SqliteKeptRow(string Id, long Scope, KeptRow Facts);

    private static Task<ParkMarkRow?> ReadSqliteParkMarkAsync(KernelDb db, string actor, int partition, CancellationToken token) =>
        db.Database.SqlQuery<ParkMarkRow>($"""
            SELECT i."FoundationParkAt" AS "At", i."FoundationParkRevision" AS "Revision",
                (i."Jobs" IS NOT NULL OR i."Children" IS NOT NULL OR i."CustomHandles" IS NOT NULL OR EXISTS (
                    SELECT 1 FROM "Subscriptions" s WHERE s."Instance" = i."Id" AND s."Partition" = i."Partition")) AS "Outstanding",
                EXISTS (SELECT 1 FROM "Messages" m WHERE m."Target" = i."Id" AND m."Partition" = i."Partition"
                    AND NOT m."Done" AND (m."InboxScope" <> {InboxKey.FoundationOccurrenceScope}
                        OR m."KeepUntil" IS NULL OR m."EventMatch" IS NOT NULL)) AS "Residue",
                i."FoundationParkClearing" AS "Clearing", i."FoundationParkCleared" AS "Cleared"
            FROM "Instances" i WHERE i."Id" = {actor} AND i."Partition" = {partition} AND NOT i."Retired"
            """).SingleOrDefaultAsync(token);

    private static async Task<List<SqliteKeptRow>> ReadSqliteKeptAsync(KernelDb db, string actor, int partition, CancellationToken token)
    {
        var kept = new List<SqliteKeptRow>();
        // Quote up to one full page and a sentinel using stored lengths. The join opens only the quoted envelopes, including a
        // singleton larger than the byte allowance so it can make progress. The total binds growth past the sentinel.
        var envelopes = db.Database.SqlQuery<SqliteKeptEnvelope>($"""
            WITH kept AS MATERIALIZED (
                SELECT m."Id", m."InboxScope", m."Seq", m."FoundationKeptBy" AS "KeptBy",
                    (octet_length(m."Id") + octet_length(m."Kind") + coalesce(octet_length(m."Start"), 0)
                        + coalesce(octet_length(m."Payload"), 0) + coalesce(octet_length(m."ReplyTo"), 0)) AS "Bytes",
                    count(*) OVER () AS "TotalRows"
                FROM "Messages" m WHERE m."Target" = {actor} AND m."Partition" = {partition} AND m."EventMatch" IS NULL
                    AND m."Done" AND m."FoundationKeptBy" IS NOT NULL
                    AND m."InboxScope" IN (0, {InboxKey.FoundationOccurrenceScope}, {RootTimedJobScope})
                ORDER BY m."Seq", m."Id", m."InboxScope" LIMIT {InMemoryStore.ParkPageLimit + 1}
            ), quote AS (
                SELECT kept.*, row_number() OVER step AS "Ordinal", sum("Bytes") OVER step AS "QuoteBytes"
                FROM kept WINDOW step AS (ORDER BY "Seq", "Id", "InboxScope" ROWS UNBOUNDED PRECEDING)
            )
            SELECT q."Id", q."InboxScope", q."Seq", q."KeptBy", q."Bytes", q."TotalRows",
                m."Kind", m."Start" AS "Header", m."Payload", m."Due", m."ReplyTo", m."KeepUntil", m."JobGeneration", m."Sender"
            FROM quote q LEFT JOIN "Messages" m ON m."Id" = q."Id" AND m."InboxScope" = q."InboxScope" AND
                (q."Ordinal" = 1 OR (q."Ordinal" <= {InMemoryStore.ParkPageLimit} AND q."QuoteBytes" <= {ParkRecoveryCredit.LimitBytes}))
            ORDER BY q."Ordinal" /* bounded-park-page */
            """);
        await foreach (var row in envelopes.AsAsyncEnumerable().WithCancellation(token))
        {
            var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(DurableKeyJson.Write(row))));
            kept.Add(new(row.Id, row.InboxScope, new(row.Id, row.KeptBy, digest, digest, row.Bytes)));
        }
        return kept;
    }

    private static async Task<List<SqliteKeptRow>?> BindSqliteParkPageAsync(KernelDb db, WorkerRoot root, long expected, bool suspended,
        FoundationParkMark mark, string? clearing, long cleared, string digest, int count, bool ends, Message command, CancellationToken token)
    {
        if (!await db.Database.SqlQuery<bool>($"""
            SELECT EXISTS (SELECT 1 FROM "Instances" i JOIN "FoundationRootBirths" b
                    ON b."Actor" = i."Id" AND b."Partition" = i."Partition"
                    AND b."Tenant" = i."FoundationTenant" AND b."Reference" = i."FoundationReference"
                WHERE b."Tenant" = {root.Tenant} AND b."Reference" = {root.Reference} AND b."Creation" = 1
                    AND i."Id" = {root.Actor} AND i."Partition" = {root.Partition} AND i."Revision" = {expected}
                    AND i."Status" = {(int)InstanceStatus.Faulted} AND NOT i."Retired" AND i."Suspended" = {suspended}
                    AND i."FoundationParkAt" = {mark.At} AND i."FoundationParkRevision" = {mark.Revision}
                    AND i."FoundationParkClearing" IS NOT DISTINCT FROM {clearing} AND coalesce(i."FoundationParkCleared", 0) = {cleared}
                    AND i."FoundationTerminalAt" IS NULL AND i."FoundationTerminalRevision" IS NULL AND i."Fault" IS NOT NULL
                    AND i."Jobs" IS NULL AND i."Children" IS NULL AND i."CustomHandles" IS NULL
                    AND NOT EXISTS (SELECT 1 FROM "Subscriptions" WHERE "Instance" = i."Id" AND "Partition" = i."Partition")) AS "Value"
            """).SingleAsync(token)) return null;

        var kept = await ReadSqliteKeptAsync(db, root.Actor, root.Partition, token);
        var facts = kept.Select(row => row.Facts).ToArray();
        if (KeptSetDigest(facts) != digest || PageLength(facts) != count || ends != (count == facts.Length) ||
            !await SettleSqliteRootInputsAsync(db, root.Reference, new(root.Actor, 1), [command.InboxKey], [command], suspended: true, token))
            return null;
        return kept.GetRange(0, count);
    }

    private static async Task<bool> ClearSqliteParkPageAsync(KernelDb db, WorkerRoot root, List<SqliteKeptRow> page, long window, CancellationToken token)
    {
        var until = checked(await ReadSqliteParkClockAsync(db, token) + window);
        foreach (var group in page.GroupBy(row => row.Scope))
        {
            var ids = Json.Write(group.Select(row => row.Id).ToArray());
            // A creator has no outside delivery receipt. Its immutable birth and start receipt remain, and the consumed
            // creator itself leaves the mailbox as it would have done on a successful first turn.
            if (group.Key == 0)
            {
                if (await db.Database.ExecuteSqlInterpolatedAsync($"""
                    DELETE FROM "Messages" WHERE "Target" = {root.Actor} AND "Partition" = {root.Partition}
                        AND "InboxScope" = 0 AND "Done" AND "FoundationKeptBy" IS NOT NULL AND "EventMatch" IS NULL
                        AND "Id" IN (SELECT value FROM json_each({ids}))
                    """, token) != group.Count()) return false;
            }
            else if (await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE "Messages" SET "FoundationKeptBy" = NULL, "Payload" = NULL, "Start" = NULL, "ReplyTo" = NULL, "KeepUntil" = {until}
                WHERE "Target" = {root.Actor} AND "Partition" = {root.Partition} AND "InboxScope" = {group.Key}
                    AND "Done" AND "FoundationKeptBy" IS NOT NULL AND "EventMatch" IS NULL
                    AND "Id" IN (SELECT value FROM json_each({ids}))
                """, token) != group.Count()) return false;
        }
        return true;
    }

    private static async Task<bool> BindSqliteParkedTerminateAsync(KernelDb db, RootParkedTerminate turn, CancellationToken token)
    {
        var root = turn.Root;
        var page = await BindSqliteParkPageAsync(db, root, turn.Expected, turn.Suspended, turn.Mark, turn.Clearing, turn.Cleared,
            turn.KeptDigest, turn.Count, turn.Ends, turn.Command, token);
        var window = turn.Command.Operator!.RootParkWindow!.Value;
        if (page is null || !await ClearSqliteParkPageAsync(db, root, page, window, token)) return false;
        var total = checked(turn.Cleared + page.Count);
        var revision = turn.JournalRevision;
        var discard = turn.Ends && turn.Command.Kind == Kinds.Delete;
        var status = turn.Ends && !discard ? InstanceStatus.Cancelled : InstanceStatus.Faulted;
        var changed = turn.Ends ? await db.Database.ExecuteSqlInterpolatedAsync(UseSqliteRootClock($"""
            UPDATE "Instances" SET "Revision" = {revision}, "FoundationWriteRevision" = {revision},
                "Status" = {(int)status}, "Suspended" = {discard && turn.Suspended}, "FoundationParkAt" = NULL,
                "FoundationParkRevision" = NULL, "FoundationParkClearing" = NULL, "FoundationParkCleared" = NULL,
                "FoundationTerminalRevision" = {revision}, "FoundationTerminalAt" = /*sqlite-root-clock*/
            WHERE "Id" = {root.Actor} AND "Partition" = {root.Partition} AND "Revision" = {turn.Expected}
            """), token) : await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE "Instances" SET "Revision" = {revision}, "FoundationWriteRevision" = {revision},
                "FoundationParkClearing" = {turn.Command.Id}, "FoundationParkCleared" = {total}
            WHERE "Id" = {root.Actor} AND "Partition" = {root.Partition} AND "Revision" = {turn.Expected}
            """, token);
        if (changed != 1) return false;
        if (turn.Ends) return true;
        if (turn.Clearing is null)
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE "Messages" SET "Seen" = 0, "Trial" = 0
                WHERE "Target" = {root.Actor} AND "Partition" = {root.Partition} AND "InboxScope" = {InboxKey.FoundationOccurrenceScope}
                    AND "EventMatch" IS NULL AND NOT "Done" AND "Id" NOT GLOB 'operator/*'
                """, token);
        return await EnqueueSqliteParkControlAsync(db, root, turn.Command, turn.Mark, window, delay: 0, token);
    }

    // A refusal settles the exact operator envelope and leaves the Park and all kept inputs intact.
    private static async Task<bool> BindSqliteParkedRefusalAsync(KernelDb db, RootParkedRefusal refusal, CancellationToken token)
    {
        var root = refusal.Root;
        var command = refusal.Command;
        var mark = command.Operator?.RootParkMark;
        if (!await SettleSqliteRootInputsAsync(db, root.Reference, new(root.Actor, 1), [command.InboxKey],
                [command], suspended: true, token)) return false;
        if (await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE "Instances" SET "Revision" = {refusal.JournalRevision}, "FoundationWriteRevision" = {refusal.JournalRevision}
            WHERE "Id" = {root.Actor} AND "Partition" = {root.Partition} AND "Revision" = {refusal.Expected}
                AND "Status" = {(int)InstanceStatus.Faulted} AND NOT "Retired" AND "Suspended" = {refusal.Suspended}
                AND "FoundationParkAt" > 0 AND "FoundationParkRevision" > 0 AND "Fault" IS NOT NULL
                AND "FoundationTerminalAt" IS NULL AND "FoundationTerminalRevision" IS NULL
                AND "FoundationTenant" = {root.Tenant} AND "FoundationReference" = {root.Reference}
                AND (NOT {refusal.Retries} OR "FoundationParkClearing" = {command.Id}
                    AND "FoundationParkAt" = {mark?.At} AND "FoundationParkRevision" = {mark?.Revision})
            """, token) != 1) return false;
        if (!refusal.Retries) return true;
        var entry = Json.Write(new { Outcome = "refused", Operation = command.Id, Reason = "unavailable" });
        var previous = await db.Database.SqlQuery<int>($"""
            SELECT count(*) AS "Value" FROM "Journal" WHERE "Instance" = {root.Actor} AND "Partition" = {root.Partition}
                AND "Entries" = {entry} AND "Revision" < {refusal.JournalRevision}
            """).SingleAsync(token);
        return await EnqueueSqliteParkControlAsync(db, root, command, mark!, command.Operator!.RootParkWindow!.Value,
            ParkRetryDelay(previous), token);
    }

    private static async Task<bool> EnqueueSqliteParkControlAsync(KernelDb db, WorkerRoot root, Message command,
        FoundationParkMark mark, long window, long delay, CancellationToken token)
    {
        var (id, header) = PostgresRootAdmission.ClearControl(root.Reference, command.Id, command.Kind, mark, window, command.Operator!.RootThenDelete);
        var at = await ReadSqliteParkClockAsync(db, token);
        var due = delay == 0 ? 0 : checked(at + delay);
        var until = checked(at + window);
        await SqliteRootAdmission.EnqueueAsync(db, root.Actor, root.Partition, id, InboxKey.FoundationOccurrenceScope,
            command.Kind, header, null, due, until, replyTo: null, token);
        return true;
    }

    private static Task<long> ReadSqliteParkClockAsync(KernelDb db, CancellationToken token) => db.Database.SqlQueryRaw<long>(
        "SELECT " + SqliteCommitDialect.MillisecondClock + """ AS "Value" """).SingleAsync(token);
}
