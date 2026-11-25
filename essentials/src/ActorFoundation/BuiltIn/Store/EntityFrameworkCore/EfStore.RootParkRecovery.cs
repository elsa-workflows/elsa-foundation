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
        db.Database.ExecuteSqlRawAsync("""
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
                            AND i."FoundationParkAt" IS NULL AND i."FoundationParkRevision" IS NULL))))
                THEN RAISE EXCEPTION 'A recover outcome entry requires the checkpoint write at its revision' USING ERRCODE = '23514'; END IF;
                RETURN NULL;
            END
            $fn$;
            DROP TRIGGER IF EXISTS kernel_require_root_outcome_checkpoint ON "Journal";
            CREATE CONSTRAINT TRIGGER kernel_require_root_outcome_checkpoint AFTER INSERT ON "Journal"
                DEFERRABLE INITIALLY DEFERRED FOR EACH ROW WHEN (NEW.{RootOutcomeEntry})
                EXECUTE FUNCTION kernel_require_root_outcome_checkpoint();
            """.Replace("{RootOutcomeEntry}", RootOutcomeEntry, StringComparison.Ordinal), token);

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

    private sealed record KeptRow(string Id, short KeptBy, string Digest, string Envelope, long Bytes);

    private sealed record ParkMarkRow(long? At, long? Revision, bool Outstanding, bool Residue, string? Clearing, long? Cleared);

    // A pending row other than a direct input: the retirement lock, the parked retirement branch and the Park facts share it.
    private const string PendingResidue = """NOT m."Done" AND (m."InboxScope" <> -4 OR m."KeepUntil" IS NULL OR m."EventMatch" IS NOT NULL)""";

    // A page clears or restores at most 170 rows and 4 MiB of stored rows, and always has room for its first row.
    private static int PageLength(IEnumerable<KeptRow> rows)
    {
        var (count, bytes) = (0, 0L);
        foreach (var row in rows)
        {
            if (count > 0 && (count >= InMemoryStore.ParkPageLimit || bytes + row.Bytes > ParkRecoveryCredit.LimitBytes)) break;
            (count, bytes) = (count + 1, bytes + row.Bytes);
        }
        return count;
    }

    private static Task<List<KeptRow>> ReadKeptAsync(KernelDb db, string actor, int partition, bool lockRows, CancellationToken token,
        int limit = int.MaxValue) =>
        db.Database.SqlQueryRaw<KeptRow>($"""
            SELECT k."Id", k."FoundationKeptBy" AS "KeptBy", {KeptDigest} AS "Digest",
                {KeptEnvelopeDigest} AS "Envelope", {KeptBytes} AS "Bytes"
            FROM "Messages" k WHERE k."Target" = {"{0}"} AND k."Partition" = {"{1}"}
                AND k."InboxScope" = -4 AND k."EventMatch" IS NULL AND k."Done" AND k."FoundationKeptBy" IS NOT NULL
            ORDER BY k."Seq", k."Id" COLLATE "C" {(limit < int.MaxValue ? $"LIMIT {limit}" : "")} {(lockRows ? "FOR UPDATE" : "")}
            """, actor, partition).ToListAsync(token);

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
        // S5b: a Terminate that names a parked root's Park reads the same facts in a parked-operator set.
        if (!db.UsesRootParkRecovery && !db.UsesRootParkedOperators ||
            !polled.Messages.Any(item => item.Message.Operator?.RootParkMark is not null)) return polled;
        var messages = new List<Stored>(polled.Messages.Count);
        foreach (var item in polled.Messages)
        {
            if (item.Message is not { Operator: { RootParkMark: not null } command } message)
            {
                messages.Add(item);
                continue;
            }
            var clearing = RereadsParkFacts(db) ? """ "FoundationParkClearing" AS "Clearing", "FoundationParkCleared" AS "Cleared" """
                : """ NULL::text AS "Clearing", NULL::bigint AS "Cleared" """;
            var park = await db.Database.SqlQueryRaw<ParkMarkRow>($"""
                SELECT "FoundationParkAt" AS "At", "FoundationParkRevision" AS "Revision",
                    "Jobs" IS NOT NULL OR "Children" IS NOT NULL OR "CustomHandles" IS NOT NULL
                        OR EXISTS (SELECT 1 FROM "Subscriptions" s WHERE s."Instance" = "Instances"."Id") AS "Outstanding",
                    EXISTS (SELECT 1 FROM "Messages" m WHERE m."Target" = "Instances"."Id" AND m."Partition" = "Instances"."Partition"
                        AND {PendingResidue}) AS "Residue",
                    {clearing} FROM "Instances"
                WHERE "Id" = {"{0}"} AND "Partition" = {"{1}"} AND NOT "Retired"
                """, message.Target.Id, message.Target.Partition).SingleOrDefaultAsync(token);
            // A clearing's control reads only the next page and one more row: nothing is kept while a clearing is open.
            var kept = await ReadKeptAsync(db, message.Target.Id, message.Target.Partition, false, token,
                command.RootParkClear ? InMemoryStore.ParkPageLimit + 1 : int.MaxValue);
            // A page restores at most 170 rows and 4 MiB of stored rows, and always has room for its first restored row.
            // A recover charges only restored (Arrived) rows, since the cleared fault input is never restored. A Terminate
            // clears every kept row, so it charges them all.
            var charged = message.Kind == Kinds.Terminate ? kept.ToArray() : kept.Where(row => row.KeptBy == (short)ParkKeepReason.Arrived).ToArray();
            var page = PageLength(charged);
            var fits = page == charged.Length;
            var facts = new FoundationRootParkFacts(park is { At: { } at, Revision: { } revision } ? new(revision, at) : null,
                kept.Count(row => row.KeptBy == (short)ParkKeepReason.Arrived), kept.Count(row => row.KeptBy == (short)ParkKeepReason.Consumed),
                fits, KeptSetDigest(kept)) { Outstanding = park?.Outstanding ?? false, Residue = park?.Residue ?? false, Rereads = RereadsParkFacts(db), Clearing = park?.Clearing, Cleared = park?.Cleared ?? 0, Page = page,
                    ClearsInPages = ClearsParkInPages(db) };
            messages.Add(item with { Message = message with { Operator = command with { RootParkFacts = facts } } });
        }
        return polled with { Messages = messages };
    }

    private async Task<PreparedRoot?> PrepareRootParkRecoverAsync(WorkerRoot root, TurnCommit turn, CancellationToken token)
    {
        var command = turn.FoundationRootOperator!;
        if (turn.Row is not { FoundationDispatchHeader: { } header } row || turn.Expected is not { } expected || turn.Journal is null ||
            command.Operator is not { RootParkMark: { } mark, RootParkWindow: >= 0, RootParkFacts: { } facts } || turn.Done is not [var key] ||
            key != command.InboxKey || !root.Owns(key) || command.Target.Id != root.Actor || command.TargetCreation != 1 ||
            turn.Consumed.Count + turn.Sent.Count + turn.Subscribed.Count + turn.Unsubscribed.Count != 0 || turn.Held is { Count: > 0 } ||
            turn != new TurnCommit(root.Actor, row, expected, turn.Consumed, turn.Done, turn.Sent, turn.Subscribed, turn.Unsubscribed,
                turn.Journal, Held: turn.Held) { FoundationRootOperator = command })
            throw RootShape();
        var recovered = !turn.Journal.StartsWith("""{"Outcome":"refused",""", StringComparison.Ordinal);
        if (recovered ? row.Status != InstanceStatus.Running || facts.Mark != mark || !facts.FitsPage || turn.Journal != Json.Write(
                new { Outcome = "recovered", Operation = command.Id, Restored = facts.Arrived, Cleared = facts.Consumed, AlreadyRearmed = 0 })
            : !new[] { "target", "selection", "plan", "page" }.Any(reason => turn.Journal == Json.Write(new { Outcome = "refused", Operation = command.Id, Reason = reason })))
            throw RootShape();

        var current = await ReadFirstRootAsync(root.Tenant, root.Reference, token);
        if (current?.Revision != expected || current.Suspended ||
            current.Status != (recovered ? InstanceStatus.Faulted : row.Status)) return null;
        var next = WorkerRow(root, current) with
        {
            Status = row.Status, Revision = checked(expected + 1), Fault = recovered ? null : current.Fault, FoundationDispatchHeader = header
        };
        if (row != next || header.Tenant != root.Tenant || header.Reference != root.Reference ||
            header.Header.TypeData != current.TypeData || header.Header.Labels.Count != current.Labels.Count ||
            current.Labels.Any(pair => !header.Header.Labels.TryGetValue(pair.Key, out var value) || pair.Value != value))
            throw RootShape();
        return new(new TurnCommit(root.Actor, null, null, [], [], [], [], [], turn.Journal),
            new RootParkRecover(root, expected, command, recovered, current.Status));
    }

    private sealed record LockedRecover(long? At, long? Revision, long Generation, string? Fault, byte[] StateDigest, byte[] HeaderDigest, int StateVersion);

    // One fenced commit: the root becomes Running with its Park mark cleared, the arrivals return to the inbox as the
    // next generation's cohort, and the fault input stays settled. A refusal moves only the revision and settles the command.
    private sealed record RootParkRecover(WorkerRoot Root, long Expected, Message Command, bool Recovered, InstanceStatus Prior)
        : QualifiedRootCheckpoint(checked(Expected + 1))
    {
        internal override async Task<bool> BindCheckpointAsync(KernelDb db, CancellationToken token)
        {
            if (!db.UsesRootParkRecovery) return false;
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
            var facts = Command.Operator!.RootParkFacts!;
            if (locked is not [var prior] || (prior.At is null) != (Prior == InstanceStatus.Running)) return false;
            var kept = Recovered ? await ReadKeptAsync(db, Root.Actor, Root.Partition, true, token) : [];
            if (Recovered && (facts.Mark != new FoundationParkMark(prior.Revision ?? 0, prior.At ?? 0) || KeptSetDigest(kept) != facts.KeptDigest))
                return false;
            var envelope = Command with { Operator = Command.Operator with { RootParkFacts = null } };
            if (!await SettleRootInputsAsync(db, Root.Reference, new(Root.Actor, 1), [Command.InboxKey], [envelope], null, false, token))
                return false;

            var generation = Recovered ? checked(prior.Generation + 1) : prior.Generation;
            if (await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE "Instances" SET "Revision" = {JournalRevision}, "FoundationWriteRevision" = {JournalRevision},
                    "Status" = CASE WHEN {Recovered} THEN {(int)InstanceStatus.Running} ELSE "Status" END,
                    "Fault" = CASE WHEN {Recovered} THEN NULL ELSE "Fault" END,
                    "FoundationParkAt" = CASE WHEN {Recovered} THEN NULL ELSE "FoundationParkAt" END,
                    "FoundationParkRevision" = CASE WHEN {Recovered} THEN NULL ELSE "FoundationParkRevision" END,
                    "FoundationParkGeneration" = {generation}
                WHERE "Id" = {Root.Actor} AND "Partition" = {Root.Partition} AND "Revision" = {Expected}
                """, token) != 1) return false;

            var arrived = kept.Where(row => row.KeptBy == (short)ParkKeepReason.Arrived).ToArray();
            var cleared = kept.Where(row => row.KeptBy == (short)ParkKeepReason.Consumed).Select(row => row.Id).ToArray();
            if (arrived.Length + cleared.Length != kept.Count) return false;
            var restoredIds = arrived.Select(row => row.Id).ToArray();
            if (Recovered && !await RestoreAsync()) return false;
            long? at = Recovered ? null : prior.At, revision = Recovered ? null : prior.Revision;
            var fault = Recovered ? null : prior.Fault;
            var status = Recovered ? InstanceStatus.Running : Prior;
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
                            + {Command.Operator.RootParkWindow!.Value}) = {cleared.Length}
                    AND NOT ({Recovered} AND EXISTS (SELECT 1 FROM "Messages" WHERE "Target" = {Root.Actor}
                        AND "Partition" = {Root.Partition} AND "InboxScope" = -4 AND "FoundationKeptBy" IS NOT NULL))
                    AND EXISTS (SELECT 1 FROM "FoundationActorList" WHERE "Tenant" = {Root.Tenant} AND "Reference" = {Root.Reference}
                        AND "Status" = {(int)status} AND "ParkAt" IS NOT DISTINCT FROM {at}::bigint
                        AND "ParkFault" IS NOT DISTINCT FROM {fault}::text AND "ParkRevision" IS NOT DISTINCT FROM {revision}::bigint)
                AS "Value"
                """).SingleAsync(token);

            async Task<bool> RestoreAsync()
            {
                var window = Command.Operator.RootParkWindow!.Value;
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
                return digests.SequenceEqual(arrived.Select(row => row.Envelope));
            }
        }
    }
}
