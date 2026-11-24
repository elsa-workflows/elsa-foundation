using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;

namespace Kernel.EntityFramework;

public sealed partial class EfStore
{
    // Protocol 22 resolves a qualified root's Park in one page with Recover (Resume). A released row carries the
    // generation of the recover that restored it, and the root's current generation names the cohort that goes first.
    // An outcome entry commits only with the checkpoint write at its revision: at commit the root's row must have moved
    // to that revision and, for "recovered", be Running with its Park mark cleared. A lone entry would wedge the root.
    internal static Task InstallRootParkRecoveryAsync(KernelDb db, CancellationToken token) =>
        db.Database.ExecuteSqlRawAsync(WithDrainFragments(db, """
            ALTER TABLE "Instances" ADD COLUMN IF NOT EXISTS "FoundationParkGeneration" bigint NOT NULL DEFAULT 0
                CHECK ("FoundationParkGeneration" >= 0);
            ALTER TABLE "Messages" ADD COLUMN IF NOT EXISTS "FoundationReleasedBy" bigint
                CHECK ("FoundationReleasedBy" IS NULL OR ("FoundationReleasedBy" > 0 AND "InboxScope" = -4));
            CREATE INDEX IF NOT EXISTS "IX_FoundationRootInbox_Released" ON "Messages" ("Partition", "Target", "FoundationReleasedBy", "Seq")
                WHERE "InboxScope" = -4 AND "EventMatch" IS NULL AND NOT "Done" AND "FoundationReleasedBy" IS NOT NULL;
            CREATE INDEX IF NOT EXISTS "IX_FoundationRootJournal_Outcome" ON "Journal" ("Partition", "Instance", "Revision")
                WHERE {RootOutcomeEntry};
            CREATE OR REPLACE FUNCTION kernel_require_root_outcome_checkpoint() RETURNS trigger LANGUAGE plpgsql AS $fn$
            BEGIN
                IF EXISTS (SELECT 1 FROM "Instances" i WHERE i."Id" = NEW."Instance" AND i."FoundationReference" IS NOT NULL
                    AND NOT (i."Partition" = NEW."Partition" AND i."Revision" = NEW."Revision"
                        AND (NEW."Entries" NOT LIKE '{{"Outcome":"recovered",%' OR (i."Status" = 0 AND NOT i."Retired"
                            AND i."FoundationParkAt" IS NULL AND i."FoundationParkRevision" IS NULL /*drain-closed*/)) /*drain-opened*/))
                THEN RAISE EXCEPTION 'A recover outcome entry requires the checkpoint write at its revision' USING ERRCODE = '23514'; END IF;
                RETURN NULL;
            END
            $fn$;
            DROP TRIGGER IF EXISTS kernel_require_root_outcome_checkpoint ON "Journal";
            CREATE CONSTRAINT TRIGGER kernel_require_root_outcome_checkpoint AFTER INSERT ON "Journal"
                DEFERRABLE INITIALLY DEFERRED FOR EACH ROW WHEN (NEW.{RootOutcomeEntry})
                EXECUTE FUNCTION kernel_require_root_outcome_checkpoint();
            """.Replace("{RootOutcomeEntry}", RootOutcomeEntry, StringComparison.Ordinal), "/*drain-closed*/", "/*drain-opened*/"), token);

    // Operator outcome entries, the only journal entries that are JSON objects. The checkpoint trigger reads them
    // through the partial index above, so its predicate repeats this text exactly. Both raw commands are formatted,
    // so the literal brace is doubled.
    private const string RootOutcomeEntry = "\"Entries\" LIKE '{{\"Outcome\":%'";

    // While released rows of the current generation are pending, the poll offers only them, in Seq order.
    private const string RootReleasedCohortFilter = """
        AND NOT EXISTS (SELECT 1 FROM "Messages" c WHERE c."Partition" = m."Partition" AND c."Target" = m."Target"
            AND c."InboxScope" = -4 AND c."EventMatch" IS NULL AND NOT c."Done" AND c."FoundationReleasedBy" = i."FoundationParkGeneration"
            AND m."FoundationReleasedBy" IS DISTINCT FROM i."FoundationParkGeneration")
        """;

    // The kept envelope digest: the fields a restore keeps, read the same way when polled and when bound. The envelope
    // digest leaves out KeepUntil, which a restore sets to the ordinary window from now.
    private static string KeptDigestOf(string keepUntil, string keptBy) => $"""
        encode(sha256(convert_to(ROW(k."Seq", k."Kind", k."Start", k."Payload", k."Due", k."ReplyTo", {keepUntil}, {keptBy})::text, 'UTF8')), 'hex')
        """;
    private static readonly string KeptDigest = KeptDigestOf("""k."KeepUntil" """, """k."FoundationKeptBy" """);
    private static readonly string KeptEnvelopeDigest = KeptDigestOf("NULL::bigint", """k."FoundationKeptBy" """);

    // Each row's stored size, measured as the inbox inspection measures it: metadata plus payload in the stored encoding.
    private const string KeptBytes = """
        (octet_length(k."Id")::bigint + octet_length(k."Kind") + coalesce(octet_length(k."Start"), 0) + coalesce(octet_length(k."Payload"), 0))
        """;

    private sealed record KeptRow(long Seq, string Id, short KeptBy, string Digest, string Envelope, long Bytes);

    private sealed record ParkMarkRow(long? At, long? Revision);

    // Protocol 23 reads one reason at a time, and the arrivals only after the drain cursor and one row past a page.
    private static Task<List<KeptRow>> ReadKeptAsync(KernelDb db, string actor, int partition, bool lockRows, CancellationToken token,
        ParkKeepReason? reason = null, FoundationParkDrainState? after = null, int limit = int.MaxValue) =>
        db.Database.SqlQueryRaw<KeptRow>($"""
            SELECT k."Seq", k."Id", k."FoundationKeptBy" AS "KeptBy", {KeptDigest} AS "Digest",
                {KeptEnvelopeDigest} AS "Envelope", {KeptBytes} AS "Bytes"
            FROM "Messages" k WHERE k."Target" = {"{0}"} AND k."Partition" = {"{1}"}
                AND k."InboxScope" = -4 AND k."EventMatch" IS NULL AND k."Done" AND k."FoundationKeptBy" IS NOT NULL
                AND ({"{2}"}::smallint IS NULL OR k."FoundationKeptBy" = {"{2}"}::smallint)
                AND ({"{3}"}::bigint IS NULL OR (k."Seq", k."Id" COLLATE "C") > ({"{3}"}::bigint, {"{4}"}::text COLLATE "C"))
            ORDER BY k."Seq", k."Id" COLLATE "C" LIMIT {"{5}"} {(lockRows ? "FOR UPDATE" : "")}
            """, actor, partition, reason is { } kept ? (short)kept : DBNull.Value, after?.Seq ?? (object)DBNull.Value,
            after?.Id ?? (object)DBNull.Value, limit).ToListAsync(token);

    // A recover reads the fault input and the first page of arrivals; a drain control reads the next page after its cursor.
    private static async Task<(List<KeptRow> Consumed, List<KeptRow> Arrived)> ReadKeptPageAsync(KernelDb db, string actor, int partition,
        bool control, FoundationParkDrainState? drain, bool lockRows, CancellationToken token) =>
        (control ? [] : await ReadKeptAsync(db, actor, partition, lockRows, token, ParkKeepReason.Consumed),
            await ReadKeptAsync(db, actor, partition, lockRows, token, ParkKeepReason.Arrived, control ? drain : null, InMemoryStore.ParkPageLimit + 1));

    // A page restores at most 170 rows and 4 MiB of stored rows, and always has room for its first restored row.
    private static int PageLength(IEnumerable<KeptRow> arrived)
    {
        var (rows, bytes) = (0, 0L);
        foreach (var row in arrived)
        {
            if (rows > 0 && (rows >= InMemoryStore.ParkPageLimit || bytes + row.Bytes > ParkRecoveryCredit.LimitBytes)) break;
            (rows, bytes) = (rows + 1, bytes + row.Bytes);
        }
        return rows;
    }

    // Each field is framed by its UTF-8 length, so no id can run into the next row.
    private static string KeptSetDigest(IEnumerable<KeptRow> kept)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> length = stackalloc byte[sizeof(int)];
        foreach (var field in kept.SelectMany(row => new[] { row.Id, row.Digest }))
        {
            var bytes = Encoding.UTF8.GetBytes(field);
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(length, bytes.Length);
            hash.AppendData(length);
            hash.AppendData(bytes);
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    // A recover is decided on the Park facts read with it, and the bind refuses the commit when the kept set has changed since.
    private async Task<Polled> WithRootParkFactsAsync(KernelDb db, Polled polled, CancellationToken token)
    {
        if (!db.UsesRootParkRecovery || !polled.Messages.Any(item => item.Message.Operator?.RootParkMark is not null)) return polled;
        var messages = new List<Stored>(polled.Messages.Count);
        foreach (var item in polled.Messages)
        {
            if (item.Message is not { Operator: { RootParkMark: not null } command } message)
            {
                messages.Add(item);
                continue;
            }
            var park = await db.Database.SqlQuery<ParkMarkRow>($"""
                SELECT "FoundationParkAt" AS "At", "FoundationParkRevision" AS "Revision" FROM "Instances"
                WHERE "Id" = {message.Target.Id} AND "Partition" = {message.Target.Partition} AND NOT "Retired"
                """).SingleOrDefaultAsync(token);
            var mark = park is { At: { } at, Revision: { } revision } ? new FoundationParkMark(revision, at) : null;
            FoundationRootParkFacts facts;
            if (db.UsesRootParkDrain)
            {
                var drain = await ReadDrainAsync(db, message.Target.Id, message.Target.Partition, token);
                var (consumed, arrived) = await ReadKeptPageAsync(db, message.Target.Id, message.Target.Partition,
                    command.RootParkDrain is not null, drain, false, token);
                var page = PageLength(arrived);
                facts = new FoundationRootParkFacts(mark, page, consumed.Count, page == arrived.Count, KeptSetDigest([.. consumed, .. arrived]))
                    { Drain = drain };
            }
            else
            {
                var kept = await ReadKeptAsync(db, message.Target.Id, message.Target.Partition, false, token);
                // A page restores at most 170 rows and 4 MiB of stored rows, and always has room for its first restored row.
                // Only restored (Arrived) rows are charged; the cleared fault input is never restored.
                var restored = kept.Where(row => row.KeptBy == (short)ParkKeepReason.Arrived).ToArray();
                var fits = restored.Length <= InMemoryStore.ParkPageLimit &&
                    (restored.Length <= 1 || restored.Sum(row => row.Bytes) <= ParkRecoveryCredit.LimitBytes);
                facts = new FoundationRootParkFacts(mark, restored.Length, kept.Count(row => row.KeptBy == (short)ParkKeepReason.Consumed),
                    fits, KeptSetDigest(kept));
            }
            messages.Add(item with { Message = message with { Operator = command with { RootParkFacts = facts } } });
        }
        return polled with { Messages = messages };
    }

    private async Task<PreparedRoot?> PrepareRootParkRecoverAsync(WorkerRoot root, TurnCommit turn, CancellationToken token)
    {
        var command = turn.FoundationRootOperator!;
        if (turn.Row is not { FoundationDispatchHeader: { } header } row || turn.Expected is not { } expected ||
            command.Operator is not { RootParkMark: { } mark, RootParkWindow: >= 0, RootParkFacts: { } facts } || turn.Done is not [var key] ||
            key != command.InboxKey || !root.Owns(key) || command.Target.Id != root.Actor || command.TargetCreation != 1 ||
            turn.Consumed.Count + turn.Sent.Count + turn.Subscribed.Count + turn.Unsubscribed.Count != 0 || turn.Held is { Count: > 0 } ||
            turn != new TurnCommit(root.Actor, row, expected, turn.Consumed, turn.Done, turn.Sent, turn.Subscribed, turn.Unsubscribed,
                turn.Journal, Held: turn.Held) { FoundationRootOperator = command })
            throw RootShape();
        var control = command.Operator.RootParkDrain;
        var step = turn.Journal is null ? RecoverStep.Page
            : turn.Journal.StartsWith("""{"Outcome":"refused",""", StringComparison.Ordinal) ? RecoverStep.Refuse
            : control is not null ? RecoverStep.Complete : facts.FitsPage ? RecoverStep.Recover : RecoverStep.Open;
        var drains = step is RecoverStep.Page or RecoverStep.Complete;
        if (step switch
            {
                RecoverStep.Refuse => !(control is null ? new[] { "target", "selection", "plan", "page" } : ["selection"])
                    .Any(reason => turn.Journal == Json.Write(new { Outcome = "refused", Operation = command.Id, Reason = reason })),
                RecoverStep.Recover or RecoverStep.Open => control is not null || facts.Mark != mark || facts.Drain is not null ||
                    turn.Journal != facts.OpenJournal(command.Id),
                RecoverStep.Page => facts.Drain?.Generation != control || facts.Arrived == 0,
                _ => facts.Drain?.Generation != control || facts.Arrived != 0 || turn.Journal != facts.CompleteJournal(command.Id)
            } || step != RecoverStep.Refuse && row.Status != InstanceStatus.Running)
            throw RootShape();

        var current = await ReadFirstRootAsync(root.Tenant, root.Reference, token);
        var opens = step is RecoverStep.Recover or RecoverStep.Open;
        if (current?.Revision != expected || current.Suspended ||
            current.Status != (opens ? InstanceStatus.Faulted : drains ? InstanceStatus.Running : row.Status)) return null;
        var next = WorkerRow(root, current) with
        {
            Status = row.Status, Revision = checked(expected + 1), Fault = opens ? null : current.Fault, FoundationDispatchHeader = header
        };
        if (row != next || header.Tenant != root.Tenant || header.Reference != root.Reference ||
            header.Header.TypeData != current.TypeData || header.Header.Labels.Count != current.Labels.Count ||
            current.Labels.Any(pair => !header.Header.Labels.TryGetValue(pair.Key, out var value) || pair.Value != value))
            throw RootShape();
        return new(new TurnCommit(root.Actor, null, null, [], [], [], [], [], turn.Journal),
            new RootParkRecover(root, expected, command, step, current.Status));
    }

    // Recover resolves a page-sized Park at once. Under protocol 23 Open starts a drain, each Page restores the next page
    // after the cursor, and Complete closes the drain once no kept row is left.
    private enum RecoverStep { Refuse, Recover, Open, Page, Complete }

    private sealed record LockedRecover(long? At, long? Revision, long Generation, string? Fault, byte[] StateDigest, byte[] HeaderDigest, int StateVersion);

    // One fenced commit: the root becomes Running with its Park mark cleared, the arrivals return to the inbox as the
    // next generation's cohort, and the fault input stays settled. A refusal moves only the revision and settles the command.
    // A drain step keeps the root Running and its generation, restores one page, moves the cursor and issues the next control.
    private sealed record RootParkRecover(WorkerRoot Root, long Expected, Message Command, RecoverStep Step, InstanceStatus Prior)
        : QualifiedRootCheckpoint(checked(Expected + 1))
    {
        internal override async Task<bool> BindCheckpointAsync(KernelDb db, CancellationToken token)
        {
            var (recovered, opens) = (Step != RecoverStep.Refuse, Step is RecoverStep.Recover or RecoverStep.Open);
            if (!db.UsesRootParkRecovery || Step is not (RecoverStep.Refuse or RecoverStep.Recover) && !db.UsesRootParkDrain) return false;
            var locked = await db.Database.SqlQuery<LockedRecover>($"""
                SELECT i."FoundationParkAt" AS "At", i."FoundationParkRevision" AS "Revision", i."FoundationParkGeneration" AS "Generation",
                    i."Fault", sha256(convert_to(i."State", 'UTF8')) AS "StateDigest",
                    sha256(convert_to(i."FoundationHeader", 'UTF8')) AS "HeaderDigest", i."StateVersion"
                FROM "Instances" i JOIN "FoundationRootBirths" b
                    ON b."Actor" = i."Id" AND b."Partition" = i."Partition" AND b."Creation" = 1
                    AND b."Tenant" = i."FoundationTenant" AND b."Reference" = i."FoundationReference"
                WHERE i."Id" = {Root.Actor} AND i."Partition" = {Root.Partition} AND NOT i."Retired" AND NOT i."Suspended"
                    AND i."Status" = {(int)Prior} AND i."Revision" = {Expected}
                    AND i."FoundationTenant" = {Root.Tenant} AND i."FoundationReference" = {Root.Reference}
                FOR UPDATE OF i
                """).ToListAsync(token);
            var operation = Command.Operator!;
            var facts = operation.RootParkFacts!;
            if (locked is not [var prior] || (prior.At is null) != (Prior == InstanceStatus.Running)) return false;
            // The open drain is read under the row lock; a drain step needs the one its control was issued for.
            var drain = await ReadDrainAsync(db, Root.Actor, Root.Partition, token);
            var drains = Step is RecoverStep.Page or RecoverStep.Complete;
            if (drains ? drain is null || drain != facts.Drain || drain.Generation != operation.RootParkDrain : opens && drain is not null)
                return false;
            List<KeptRow> consumedRows = [], arrived = [];
            if (recovered && db.UsesRootParkDrain)
                (consumedRows, arrived) = await ReadKeptPageAsync(db, Root.Actor, Root.Partition, drains, drain, true, token);
            else if (recovered)
            {
                var kept = await ReadKeptAsync(db, Root.Actor, Root.Partition, true, token);
                (consumedRows, arrived) = ([.. kept.Where(row => row.KeptBy == (short)ParkKeepReason.Consumed)],
                    [.. kept.Where(row => row.KeptBy == (short)ParkKeepReason.Arrived)]);
                if (consumedRows.Count + arrived.Count != kept.Count || KeptSetDigest(kept) != facts.KeptDigest) return false;
            }
            var page = arrived.Take(db.UsesRootParkDrain ? PageLength(arrived) : arrived.Count).ToArray();
            if (recovered && (opens && facts.Mark != new FoundationParkMark(prior.Revision ?? 0, prior.At ?? 0) || page.Length != facts.Arrived ||
                db.UsesRootParkDrain && KeptSetDigest([.. consumedRows, .. arrived]) != facts.KeptDigest ||
                opens && (Step == RecoverStep.Open) == (page.Length == arrived.Count)))
                return false;
            var envelope = Command with { Operator = operation with { RootParkFacts = null } };
            if (!await SettleRootInputsAsync(db, Root.Reference, new(Root.Actor, 1), [Command.InboxKey], [envelope], null, false, token))
                return false;

            var generation = opens ? checked(prior.Generation + 1) : prior.Generation;
            // The cursor after this commit: the last restored row while the drain stays open, none once it closes.
            var cursor = Step switch
            {
                RecoverStep.Open => new FoundationParkDrainState(generation, page[^1].Seq, page[^1].Id, page.Length, consumedRows.Count),
                RecoverStep.Page => drain! with { Seq = page[^1].Seq, Id = page[^1].Id, Restored = checked(drain.Restored + page.Length) },
                RecoverStep.Refuse => drain,
                _ => null
            };
            var drainSet = db.UsesRootParkDrain ? """
                , "FoundationParkDrainSeq" = CASE WHEN {7} THEN {8} END, "FoundationParkDrainId" = CASE WHEN {7} THEN {9} END,
                "FoundationParkDrainRestored" = CASE WHEN {7} THEN {10} END, "FoundationParkDrainCleared" = CASE WHEN {7} THEN {11} END
                """ : "";
            object[] values = [JournalRevision, opens, (int)InstanceStatus.Running, generation, Root.Actor, Root.Partition, Expected];
            if (await db.Database.ExecuteSqlRawAsync($$"""
                UPDATE "Instances" SET "Revision" = {0}, "FoundationWriteRevision" = {0},
                    "Status" = CASE WHEN {1} THEN {2} ELSE "Status" END,
                    "Fault" = CASE WHEN {1} THEN NULL ELSE "Fault" END,
                    "FoundationParkAt" = CASE WHEN {1} THEN NULL ELSE "FoundationParkAt" END,
                    "FoundationParkRevision" = CASE WHEN {1} THEN NULL ELSE "FoundationParkRevision" END,
                    "FoundationParkGeneration" = {3} {{drainSet}}
                WHERE "Id" = {4} AND "Partition" = {5} AND "Revision" = {6}
                """, db.UsesRootParkDrain ? [.. values, cursor is not null, cursor?.Seq ?? 0, cursor?.Id ?? "", cursor?.Restored ?? 0, cursor?.Cleared ?? 0]
                    : values, token) != 1)
                return false;

            var cleared = consumedRows.Select(row => row.Id).ToArray();
            var restoredIds = page.Select(row => row.Id).ToArray();
            if (recovered && !await RestoreAsync()) return false;
            if (cursor is not null && Step != RecoverStep.Refuse)
            {
                // The next control of the open drain, bound to its operation, Park mark and release generation.
                var window = operation.RootParkWindow!.Value;
                var (id, header) = PostgresRootAdmission.DrainControl(Root.Reference, Command.Id,
                    opens ? facts.Mark! : operation.RootParkMark!, window, generation);
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO "Messages" ("InboxScope", "Id", "Partition", "Target", "Kind", "Start", "Due", "KeepUntil", "Done", "Seen", "Attempts", "Trial")
                    VALUES ({InboxKey.FoundationOccurrenceScope}, {id}, {Root.Partition}, {Root.Actor}, {Kinds.Recover}, {header}, 0,
                        floor(extract(epoch from transaction_timestamp()) * 1000)::bigint + {window}, false, 0, 0, 0)
                    """, token);
            }
            long? at = recovered ? null : prior.At, revision = recovered ? null : prior.Revision;
            var fault = recovered ? null : prior.Fault;
            var status = recovered ? InstanceStatus.Running : Prior;
            // Rows up to the cursor are restored; with no cursor, no kept row may remain.
            var keptBefore = cursor is null ? long.MaxValue : cursor.Seq;
            return await db.Database.SqlQuery<bool>($"""
                SELECT EXISTS (SELECT 1 FROM "Instances" WHERE "Id" = {Root.Actor} AND "Partition" = {Root.Partition}
                        AND "Revision" = {JournalRevision} AND "FoundationWriteRevision" = {JournalRevision} AND "Status" = {(int)status}
                        AND "Fault" IS NOT DISTINCT FROM {fault}::text AND "FoundationParkAt" IS NOT DISTINCT FROM {at}::bigint
                        AND "FoundationParkRevision" IS NOT DISTINCT FROM {revision}::bigint AND "FoundationParkGeneration" = {generation}
                        AND "StateVersion" = {prior.StateVersion} AND sha256(convert_to("State", 'UTF8')) = {prior.StateDigest}
                        AND sha256(convert_to("FoundationHeader", 'UTF8')) = {prior.HeaderDigest})
                    AND (SELECT count(*) FROM "Messages" WHERE "Id" = ANY({cleared}) AND "Target" = {Root.Actor}
                        AND "Partition" = {Root.Partition} AND "InboxScope" = -4 AND "Done" AND "FoundationKeptBy" IS NULL
                        AND "Payload" IS NULL AND "Start" IS NULL AND "KeepUntil" = floor(extract(epoch from transaction_timestamp()) * 1000)::bigint
                            + {operation.RootParkWindow!.Value}) = {cleared.Length}
                    AND NOT ({recovered} AND EXISTS (SELECT 1 FROM "Messages" WHERE "Target" = {Root.Actor}
                        AND "Partition" = {Root.Partition} AND "InboxScope" = -4 AND "FoundationKeptBy" IS NOT NULL
                        AND ("Seq", "Id" COLLATE "C") <= ({keptBefore}, {cursor?.Id ?? ""}::text COLLATE "C")))
                    AND EXISTS (SELECT 1 FROM "FoundationActorList" WHERE "Tenant" = {Root.Tenant} AND "Reference" = {Root.Reference}
                        AND "Status" = {(int)status} AND "ParkAt" IS NOT DISTINCT FROM {at}::bigint
                        AND "ParkFault" IS NOT DISTINCT FROM {fault}::text AND "ParkRevision" IS NOT DISTINCT FROM {revision}::bigint)
                AS "Value"
                """).SingleAsync(token) && await ReadDrainAsync(db, Root.Actor, Root.Partition, token) == cursor;

            async Task<bool> RestoreAsync()
            {
                var window = operation.RootParkWindow!.Value;
                if (await db.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE "Messages" SET "Done" = false, "FoundationKeptBy" = NULL, "Seen" = 0, "Trial" = 0, "Attempts" = 0,
                        "FoundationReleasedBy" = {generation}, "KeepUntil" = floor(extract(epoch from transaction_timestamp()) * 1000)::bigint + {window}
                    WHERE "Id" = ANY({restoredIds}) AND "Target" = {Root.Actor} AND "Partition" = {Root.Partition}
                        AND "InboxScope" = -4 AND "EventMatch" IS NULL AND "Done" AND "FoundationKeptBy" = {(short)ParkKeepReason.Arrived}
                    """, token) != restoredIds.Length) return false;
                if (await db.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE "Messages" SET "FoundationKeptBy" = NULL, "Payload" = NULL, "Start" = NULL, "ReplyTo" = NULL,
                        "KeepUntil" = floor(extract(epoch from transaction_timestamp()) * 1000)::bigint + {window}
                    WHERE "Id" = ANY({cleared}) AND "Target" = {Root.Actor} AND "Partition" = {Root.Partition}
                        AND "InboxScope" = -4 AND "EventMatch" IS NULL AND "Done" AND "FoundationKeptBy" = {(short)ParkKeepReason.Consumed}
                    """, token) != cleared.Length) return false;
                // A restored envelope keeps its kept envelope digest once the cleared kept reason is put back in, and
                // restored and cleared rows take the ordinary admission window from the transaction's time.
                var digests = await db.Database.SqlQueryRaw<string>($"""
                    SELECT {KeptDigestOf("NULL::bigint", $"{(short)ParkKeepReason.Arrived}::smallint")} AS "Value"
                    FROM "Messages" k WHERE k."Id" = ANY({"{0}"}) AND k."Target" = {"{1}"} AND k."Partition" = {"{2}"}
                        AND k."InboxScope" = -4 AND k."EventMatch" IS NULL AND NOT k."Done" AND k."FoundationKeptBy" IS NULL
                        AND k."FoundationReleasedBy" = {"{3}"} AND k."Seen" = 0 AND k."Trial" = 0 AND k."Attempts" = 0
                        AND k."KeepUntil" = floor(extract(epoch from transaction_timestamp()) * 1000)::bigint + {"{4}"}
                    ORDER BY k."Seq", k."Id" COLLATE "C"
                    """, restoredIds, Root.Actor, Root.Partition, generation, window).ToListAsync(token);
                return digests.SequenceEqual(page.Select(row => row.Envelope));
            }
        }
    }
}
