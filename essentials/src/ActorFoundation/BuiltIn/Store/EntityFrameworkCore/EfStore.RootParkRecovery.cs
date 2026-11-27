using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;

namespace Kernel.EntityFramework;

public sealed partial class EfStore
{
    private static string ParkInputScopes(KernelDb db) => db.UsesRootPrivateParkInputs
        ? $"{InboxKey.FoundationOccurrenceScope}, {RootSelfScope}" : $"{InboxKey.FoundationOccurrenceScope}";

    private static string ParkInputPredicate(KernelDb db, string column) => db.UsesRootPrivateParkInputs
        ? $"{column} IN ({ParkInputScopes(db)})" : $"{column} = {InboxKey.FoundationOccurrenceScope}";

    private static string WithParkInputScopes(KernelDb db, string sql) =>
        sql.Replace("/*park-input-scopes*/", ParkInputScopes(db), StringComparison.Ordinal);

    private static FormattableString WithParkInputScopeParameters(KernelDb db, FormattableString query) =>
        FormattableStringFactory.Create(WithParkInputScopes(db, query.Format), query.GetArguments());

    // Protocol 22 resolves a qualified root's Park in one page with Recover (Resume). A released row carries the
    // generation of the recover that restored it, and the root's current generation names the cohort that goes first.
    // An outcome entry commits only with the checkpoint write at its revision: at commit the root's row must have moved
    // to that revision and, for "recovered", be Running with its Park mark cleared. A lone entry would wedge the root.
    internal static Task InstallRootParkRecoveryAsync(KernelDb db, CancellationToken token) =>
        db.Database.ExecuteSqlRawAsync("""
            ALTER TABLE "Instances" ADD COLUMN IF NOT EXISTS "FoundationParkGeneration" bigint NOT NULL DEFAULT 0
                CHECK ("FoundationParkGeneration" >= 0);
            /*consumed-job-result-definitions*/
            ALTER TABLE "Messages" ADD COLUMN IF NOT EXISTS "FoundationReleasedBy" bigint
                CHECK ("FoundationReleasedBy" IS NULL OR ("FoundationReleasedBy" > 0 AND /*park-input-scope*/));
            CREATE INDEX IF NOT EXISTS "IX_FoundationRootInbox_Released" ON "Messages" ("Partition", "Target", "FoundationReleasedBy", "Seq")
                WHERE /*park-input-scope*/ AND "EventMatch" IS NULL AND NOT "Done" AND "FoundationReleasedBy" IS NOT NULL;
            /*recovery-kept-index*/
            /*composed-waiting-index*/
            CREATE INDEX IF NOT EXISTS "IX_FoundationRootJournal_Outcome" ON "Journal" ("Partition", "Instance", "Revision")
                WHERE {RootOutcomeEntry};
            CREATE OR REPLACE FUNCTION kernel_require_root_outcome_checkpoint() RETURNS trigger LANGUAGE plpgsql AS $fn$
            BEGIN
                IF EXISTS (SELECT 1 FROM "Instances" i WHERE i."Id" = NEW."Instance" AND i."FoundationReference" IS NOT NULL
                    AND NOT (i."Partition" = NEW."Partition" AND i."Revision" = NEW."Revision"
                        AND (NEW."Entries" NOT LIKE '{{"Outcome":"recovered",%' OR (i."Status" = 0 AND NOT i."Retired"
                            AND i."FoundationParkAt" IS NULL AND i."FoundationParkRevision" IS NULL))
                        /*parked-operator-outcomes*/))
                THEN RAISE EXCEPTION 'A recover outcome entry requires the checkpoint write at its revision' USING ERRCODE = '23514'; END IF;
                RETURN NULL;
            END
            $fn$;
            DROP TRIGGER IF EXISTS kernel_require_root_outcome_checkpoint ON "Journal";
            CREATE CONSTRAINT TRIGGER kernel_require_root_outcome_checkpoint AFTER INSERT ON "Journal"
                DEFERRABLE INITIALLY DEFERRED FOR EACH ROW WHEN (NEW.{RootOutcomeEntry})
                EXECUTE FUNCTION kernel_require_root_outcome_checkpoint();
            """.Replace("{RootOutcomeEntry}", RootOutcomeEntry, StringComparison.Ordinal)
            .Replace("/*park-input-scope*/", ParkInputPredicate(db, "\"InboxScope\""), StringComparison.Ordinal)
            .Replace("/*consumed-job-result-definitions*/", RecordsConsumedRootJob(db)
                ? $"""
                    ALTER TABLE "Instances" ADD COLUMN IF NOT EXISTS "FoundationParkJobResult" text,
                        ADD COLUMN IF NOT EXISTS "FoundationParkLiveJob" text;
                    CREATE INDEX IF NOT EXISTS "IX_FoundationRootInbox_RecordedJobResult" ON "Messages" {RecordedJobResultIndex};
                    CREATE INDEX IF NOT EXISTS "IX_FoundationRootInbox_LiveJob" ON "Messages" {LiveRootJobIndex};
                    {PostgresParkLiveJobGuards()}
                    """ : "", StringComparison.Ordinal)
            .Replace("/*recovery-kept-index*/", $"""
                CREATE INDEX IF NOT EXISTS "IX_FoundationRootInbox_RecoveryKept" ON "Messages"
                    {RecoveryKeptIndex.Replace("/*park-input-scope*/", ParkInputPredicate(db, "\"InboxScope\""), StringComparison.Ordinal)};
                """, StringComparison.Ordinal)
            .Replace("/*composed-waiting-index*/", db.UsesRootComposedParkRecovery ? $"""
                CREATE INDEX IF NOT EXISTS "IX_FoundationRootInbox_CohortGroups" ON "Messages" ("Partition", "Seen", "Due", "Seq")
                    INCLUDE ("Target")
                    WHERE {CohortWaitingWork};
                """ : "", StringComparison.Ordinal)
            .Replace("/*parked-operator-outcomes*/",
                db.UsesRootComposedParkRecovery ? "AND " + RootParkedOutcomeCheckpoint + StoppedParkOutcomeCheckpoint : "",
                StringComparison.Ordinal), token);

    // Operator outcome entries, the only journal entries that are JSON objects. The checkpoint trigger reads them
    // through the partial index above, so its predicate repeats this text exactly. Both raw commands are formatted,
    // so the literal brace is doubled.
    private const string RootOutcomeEntry = "\"Entries\" LIKE '{{\"Outcome\":%'";

    // While released rows of the current generation are pending, the poll offers only them, in Seq order.
    private static string RootReleasedCohortFilter(KernelDb db) => " AND " + WithParkInputScopes(db, NoEarlierReleasedCohort);
    private const string NoEarlierReleasedCohort = """
        NOT EXISTS (SELECT 1 FROM "Messages" c WHERE c."Partition" = m."Partition" AND c."Target" = m."Target"
            AND c."InboxScope" IN (/*park-input-scopes*/) AND c."EventMatch" IS NULL AND NOT c."Done" AND c."FoundationReleasedBy" = i."FoundationParkGeneration"
            AND m."FoundationReleasedBy" IS DISTINCT FROM i."FoundationParkGeneration")
        """;

    private bool SamplesAuthoredCohortWithholding(KernelDb db) => db.UsesRootComposedParkRecovery &&
        _ordinaryRoots is { Discover: true, AuthoredWithholds: not null };

    private string RootPollCohortFilter(KernelDb db)
    {
        if (!db.UsesRootParkRecovery) return "";
        if (!db.UsesRootComposedParkRecovery) return RootReleasedCohortFilter(db);
        var withheld = SamplesAuthoredCohortWithholding(db) ? $$"""
            OR (m."InboxScope" = {{InboxKey.FoundationOccurrenceScope}} AND m."Id" LIKE 'operator/%'
                AND EXISTS (SELECT 1 FROM unnest({4}::text[], {5}::bigint[]) AS withheld("Actor", "Revision")
                    WHERE withheld."Actor" = i."Id" AND withheld."Revision" = i."Revision"))
            """ : "";
        return $"""
            AND (i."FoundationParkAt" IS NOT NULL
                OR (i."Suspended" AND m."InboxScope" = {InboxKey.FoundationOccurrenceScope} AND m."Id" LIKE 'operator/%')
                {withheld} OR {WithParkInputScopes(db, NoEarlierReleasedCohort)})
            """;
    }

    private sealed record CohortSample(long Epoch, long Through = -1, long ReadyThrough = 0, long Seen = 0,
        long Due = long.MinValue, long Seq = 0, long ScanSeq = 0, bool ScanFirst = false);
    private sealed record CohortFence(int Partition, long Epoch, long Through, long ReadyThrough, long Seen, long Due, long Seq, long ScanSeq,
        bool ScanFirst);
    private sealed record CohortCandidate(int Partition, long Through, long ReadyThrough, long? SeenGroup, long? DueGroup,
        long? Seq, bool More, int Spare, long? ScanTail, string? Actor, long? Revision, string? Tenant, string? Reference, string? Header);
    private readonly ConcurrentDictionary<int, CohortSample> _cohortSamples = new();
    private int _cohortPartitionWindow;

    private static readonly string CohortWaitingWork = $"""
        "EventMatch" IS NULL AND NOT "Done" AND "InboxScope" = {InboxKey.FoundationOccurrenceScope} AND "Id" LIKE 'operator/%'
        """;
    private static readonly string CohortEligibleRoots = $"""
        SELECT i."Partition", i."Id" AS "Actor", i."Revision", i."FoundationTenant" AS "Tenant", i."FoundationReference" AS "Reference",
            i."FoundationHeader" AS "Header"
        FROM (SELECT DISTINCT "Partition", "Target" FROM candidates) w
        JOIN "Instances" i ON i."Id" = w."Target" AND i."Partition" = w."Partition"
        WHERE i."Status" = {(int)InstanceStatus.Running} AND NOT i."Retired"
            AND i."FoundationReference" IS NOT NULL AND i."FoundationParkGeneration" > 0
            AND EXISTS (SELECT 1 FROM "Messages" c
                WHERE c."Partition" = i."Partition" AND c."Target" = i."Id" AND c."EventMatch" IS NULL AND NOT c."Done"
                    AND c."InboxScope" IN (/*park-input-scopes*/) AND c."FoundationReleasedBy" = i."FoundationParkGeneration")
        """;

    // Only roots with pending released inputs and waiting controls need an authored decision. The main poll accepts
    // each decision at the same revision. Select an unseen epoch and ready due group with separate index seeks.
    // Inside that group, the sequence ceiling keeps later admissions outside the bounded page.
    private async Task<(string[] Actors, long[] Revisions)> ReadWithheldCohortsAsync(KernelDb db, IReadOnlyCollection<PartitionFence> fences,
        int limit, CancellationToken token)
    {
        // Bound the whole owner's sample. Rotate the included partitions when there are fewer slots than fences.
        var ordered = fences.OrderBy(fence => fence.Partition).ToArray();
        var width = Math.Min(limit, ordered.Length);
        if (width == 0) return ([], []);
        var offset = width == ordered.Length ? 0 :
            (int)((uint)(Interlocked.Add(ref _cohortPartitionWindow, width) - width) % ordered.Length);
        var windows = new List<CohortFence>(width);
        var sampled = new Dictionary<int, (long Epoch, CohortSample Previous)>(width);
        for (var index = 0; index < width; index++)
        {
            var fence = ordered[(offset + index) % ordered.Length];
            var sample = _cohortSamples.GetOrAdd(fence.Partition, _ => new(fence.Epoch));
            if (sample.Epoch > fence.Epoch) continue;
            var cursor = sample.Epoch == fence.Epoch ? sample : new(fence.Epoch);
            windows.Add(new(fence.Partition, fence.Epoch, cursor.Through, cursor.ReadyThrough, cursor.Seen, cursor.Due, cursor.Seq,
                cursor.ScanSeq, cursor.ScanFirst));
            sampled.Add(fence.Partition, (fence.Epoch, sample));
        }
        if (windows.Count == 0) return ([], []);
        var template = db.UsesSqliteRootRuntime
            ? SqliteCohortCandidates(windows, limit / width)
            : PostgresCohortCandidates(windows, limit / width);
        var sql = FormattableStringFactory.Create(WithParkInputScopes(db, template.Format), template.GetArguments());
        var candidates = await db.Database.SqlQuery<CohortCandidate>(sql).ToArrayAsync(token);
        foreach (var page in candidates.GroupBy(candidate => candidate.Partition))
        {
            var (epoch, previous) = sampled[page.Key];
            var row = page.First();
            var cursor = previous.Epoch == epoch ? previous : new(epoch);
            var scanned = row is { DueGroup: not null, Spare: > 0 };
            var scan = scanned ? row.ScanTail ?? 0 : cursor.ScanSeq;
            // SQLite can reuse a swept high sequence. A full grouped page yields its next turn to the
            // sequence scan, where newer admissions cannot overtake a still-pending lower sequence.
            var useScanNext = row.Spare == 0;
            // The sequence scan can finish even while later admissions keep creating empty due groups.
            // Refresh the ceiling and clock once either scan has covered the old range.
            var next = row switch
            {
                _ when row.SeenGroup is null || scanned && row.ScanTail is null => new CohortSample(epoch),
                { SeenGroup: { } seen, DueGroup: null } => new(epoch, row.Through, row.ReadyThrough, checked(seen + 1), ScanSeq: scan),
                _ when cursor.ScanFirst => cursor with { ScanSeq = scan, ScanFirst = false },
                { SeenGroup: { } seen, DueGroup: { } due, Seq: { } last, More: true } =>
                    new(epoch, row.Through, row.ReadyThrough, seen, due, last, scan, useScanNext),
                _ => new(epoch, row.Through, row.ReadyThrough, row.SeenGroup!.Value, checked(row.DueGroup!.Value + 1),
                    ScanSeq: scan, ScanFirst: useScanNext)
            };
            _cohortSamples.AddOrUpdate(page.Key, next, (_, current) => current.Epoch < epoch || ReferenceEquals(current, previous) ? next : current);
        }
        var actors = new List<string>();
        var revisions = new List<long>();
        foreach (var candidate in candidates)
        {
            if (candidate is not { Actor: { } actor, Revision: { } revision, Header: { } encoded,
                    Reference: { } reference, Tenant: { } tenant }) continue;
            var header = DurableKeyJson.Read<RootHeader>(encoded);
            if (!AuthoredWithholding(new(new(reference), header.Model, tenant, Foundation.Contracts.ActorStatus.Running,
                    null, null, null, header.TypeData, header.Labels.ToDictionary(StringComparer.Ordinal)))) continue;
            actors.Add(actor);
            revisions.Add(revision);
        }
        return (actors.ToArray(), revisions.ToArray());
    }

    // The kept envelope digest: the fields a restore keeps, read the same way when polled and when bound. The envelope
    // digest leaves out KeepUntil, which a restore sets to the ordinary window from now.
    private static string KeptDigestOf(KernelDb db, string keepUntil, string keptBy) => $"""
        encode(sha256(convert_to(ROW(k."Seq", k."Kind", k."Start", k."Payload", k."Due", k."ReplyTo", {keepUntil}, {keptBy}
            {(db.UsesRootPrivateParkInputs ? """, k."InboxScope", k."Sender", k."JobGeneration", encode(k."FoundationSelfContent", 'base64')""" : "")}
            )::text, 'UTF8')), 'hex')
        """;
    private static string KeptDigest(KernelDb db) => KeptDigestOf(db, """k."KeepUntil" """, """k."FoundationKeptBy" """);
    private static string KeptEnvelopeDigest(KernelDb db) => KeptDigestOf(db, "NULL::bigint", """k."FoundationKeptBy" """);

    // Each row's stored size, measured as the inbox inspection measures it: metadata plus payload in the stored encoding.
    private static string KeptBytes(KernelDb db) => $"""
        (CAST(octet_length(k."Id") AS bigint) + octet_length(k."Kind") + coalesce(octet_length(k."Start"), 0) + coalesce(octet_length(k."Payload"), 0)
            {(db.UsesRootPrivateParkInputs && db.Database.IsNpgsql() ? """+ coalesce(octet_length(k."FoundationSelfContent"), 0)""" : "")})
        """;

    private sealed record KeptRow(string Id, long Scope, short KeptBy, string Digest, string Envelope, long Bytes);

    private sealed record ParkMarkRow(long? At, long? Revision, bool Outstanding, bool Residue, string? Clearing, long? Cleared,
        string? Jobs, string? ParkJobResult, string? ParkLiveJob);

    // A pending row other than a direct input: the parked retirement branch and the Park facts share it, and the C# retirement
    // lock (EfStore.RootRetirement.cs) states the same condition with the direct scope as a parameter.
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
            SELECT k."Id", k."InboxScope" AS "Scope", k."FoundationKeptBy" AS "KeptBy", {KeptDigest(db)} AS "Digest",
                {KeptEnvelopeDigest(db)} AS "Envelope", {KeptBytes(db)} AS "Bytes"
            FROM "Messages" k WHERE k."Target" = {"{0}"} AND k."Partition" = {"{1}"}
                AND {ParkInputPredicate(db, """k."InboxScope" """.TrimEnd())} AND k."EventMatch" IS NULL AND k."Done" AND k."FoundationKeptBy" IS NOT NULL
            ORDER BY k."Seq", k."Id" COLLATE "C" {(limit < int.MaxValue ? $"LIMIT {limit}" : "")} {(lockRows ? "FOR UPDATE" : "")}
            """, actor, partition).ToListAsync(token);

    // The arrival quote can stop at its limit without scanning pending rows or the consumed reason.
    private const string RecoveryKeptIndex = """
        ("Partition", "Target" COLLATE "C", "FoundationKeptBy", "Seq", "Id" COLLATE "C")
        WHERE /*park-input-scope*/ AND "EventMatch" IS NULL AND "Done" AND "FoundationKeptBy" IN (0, 1)
        """;

    private sealed record PostgresRecoveryRow(bool Fits, string? Id, long? Scope, short? KeptBy, string? Digest, string? Envelope, long? Bytes);

    // Quote at most one page plus its overflow row before opening any arrival body. Consumed inputs are cleared
    // rather than released, so only their metadata is included in the recovery proof.
    private static async Task<(bool Fits, List<KeptRow> Kept)> ReadPostgresRecoveryRowsAsync(
        KernelDb db, string actor, int partition, bool lockRows, CancellationToken token)
    {
        var filter = $"""
            k."Target" COLLATE "C" = {"{0}"} AND k."Partition" = {"{1}"}
            AND {ParkInputPredicate(db, """k."InboxScope" """.TrimEnd())} AND k."EventMatch" IS NULL AND k."Done"
            """;
        var arrived = $"""k."FoundationKeptBy" = {(short)ParkKeepReason.Arrived}""";
        var rows = await db.Database.SqlQueryRaw<PostgresRecoveryRow>($"""
            WITH arrivals AS MATERIALIZED (
                SELECT {KeptBytes(db)} AS "Bytes"
                FROM "Messages" k WHERE {filter} AND {arrived}
                ORDER BY k."Seq", k."Id" COLLATE "C" LIMIT {InMemoryStore.ParkPageLimit + 1}
            ), quote AS MATERIALIZED (
                SELECT count(*) <= {InMemoryStore.ParkPageLimit}
                    AND (count(*) <= 1 OR sum("Bytes") <= {ParkRecoveryCredit.LimitBytes}) AS "Fits"
                FROM arrivals
            ), kept AS MATERIALIZED (
                SELECT k."Id", k."InboxScope" AS "Scope", k."Seq", k."FoundationKeptBy" AS "KeptBy",
                    CASE WHEN {arrived} THEN {KeptDigest(db)}
                        ELSE encode(sha256(convert_to(ROW(k."Seq", k."Kind", k."Due", k."KeepUntil")::text, 'UTF8')), 'hex') END AS "Digest",
                    CASE WHEN {arrived} THEN {KeptEnvelopeDigest(db)} ELSE '' END AS "Envelope",
                    CASE WHEN {arrived} THEN {KeptBytes(db)} ELSE 0 END AS "Bytes"
                FROM "Messages" k WHERE (SELECT "Fits" FROM quote) AND {filter}
                    AND k."FoundationKeptBy" IN ({(short)ParkKeepReason.Consumed}, {(short)ParkKeepReason.Arrived})
                {(lockRows ? "FOR UPDATE OF k" : "")}
            )
            SELECT quote."Fits", kept."Id", kept."Scope", kept."KeptBy", kept."Digest", kept."Envelope", kept."Bytes"
            FROM quote LEFT JOIN kept ON true
            ORDER BY kept."Seq", kept."Id" COLLATE "C"
            """, actor, partition).ToListAsync(token);
        return (rows[0].Fits, rows.Where(row => row.Id is not null).Select(row =>
            new KeptRow(row.Id!, row.Scope!.Value, row.KeptBy!.Value, row.Digest!, row.Envelope!, row.Bytes!.Value)).ToList());
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
        // S5b: a Terminate that names a parked root's Park reads the same facts in a parked-operator set.
        if (!db.UsesRootParkRecovery && !db.UsesRootParkedOperators ||
            !polled.Messages.Any(item => item.Message.Operator?.RootParkMark is not null || db.UsesRootChildren && IsRootChildTerminate(item.Message)))
            return polled;
        var messages = new List<Stored>(polled.Messages.Count);
        foreach (var item in polled.Messages)
        {
            var message = item.Message;
            var childTerminate = db.UsesRootChildren && IsRootChildTerminate(message);
            var endsAttachedChild = db.UsesRootChildren && message.Kind == Kinds.Terminate && message.Operator?.RootThenDelete != true;
            if (message.Operator is not { RootParkMark: not null } && !childTerminate)
            {
                messages.Add(item);
                continue;
            }
            var clearing = RereadsParkFacts(db) ? """ "FoundationParkClearing" AS "Clearing", "FoundationParkCleared" AS "Cleared" """
                : """ NULL::text AS "Clearing", NULL::bigint AS "Cleared" """;
            var jobPin = RecordsConsumedRootJob(db) ? """ "FoundationParkJobResult" """ : "NULL::text";
            var livePin = RecordsConsumedRootJob(db) ? """ "FoundationParkLiveJob" """ : "NULL::text";
            var park = db.UsesSqliteRootRuntime
                ? await ReadSqliteParkMarkAsync(db, message.Target.Id, message.Target.Partition, token)
                : await db.Database.SqlQueryRaw<ParkMarkRow>($"""
                SELECT "FoundationParkAt" AS "At", "FoundationParkRevision" AS "Revision",
                    "Jobs" IS NOT NULL OR "Children" IS NOT NULL AND NOT ({"{2}"} AND "Parent" IS NOT NULL)
                        OR "CustomHandles" IS NOT NULL
                        OR EXISTS (SELECT 1 FROM "Subscriptions" s WHERE s."Instance" = "Instances"."Id") AS "Outstanding",
                    EXISTS (SELECT 1 FROM "Messages" m WHERE m."Target" = "Instances"."Id" AND m."Partition" = "Instances"."Partition"
                        AND {PendingResidue}) AS "Residue",
                    {clearing}, "Jobs", {jobPin} AS "ParkJobResult", {livePin} AS "ParkLiveJob" FROM "Instances"
                WHERE "Id" = {"{0}"} AND "Partition" = {"{1}"} AND NOT "Retired"
                """, message.Target.Id, message.Target.Partition, endsAttachedChild).SingleOrDefaultAsync(token);
            var command = message.Operator;
            if (childTerminate)
            {
                if (park is not { At: { } parkAt, Revision: { } parkRevision })
                {
                    messages.Add(item);
                    continue;
                }
                command = new(null) { RootParkMark = new(parkRevision, parkAt), RootParkWindow = (long)TimeSpan.FromHours(1).TotalMilliseconds };
            }
            if (command is null) throw RootShape();
            var recover = message.Kind == Kinds.Recover;
            var quoteFits = true;
            List<KeptRow> kept;
            if (db.UsesSqliteRootRuntime)
            {
                kept = (recover
                    ? await ReadSqliteRecoveryRowsAsync(db, message.Target.Id, message.Target.Partition, null, token)
                    : await ReadSqliteKeptAsync(db, message.Target.Id, message.Target.Partition, !command.RootParkClear, token))
                    .Select(row => row.Facts).ToList();
            }
            else if (recover)
                (quoteFits, kept) = await ReadPostgresRecoveryRowsAsync(db, message.Target.Id, message.Target.Partition, false, token);
            else
            {
                // While the clearing is open, its next control reads one page and its overflow row.
                var limit = command.RootParkClear ? InMemoryStore.ParkPageLimit + 1 : int.MaxValue;
                kept = await ReadKeptAsync(db, message.Target.Id, message.Target.Partition, false, token, limit);
            }
            var charged = recover ? kept.Where(row => row.KeptBy == (short)ParkKeepReason.Arrived).ToArray() : kept.ToArray();
            var page = PageLength(charged);
            var fits = quoteFits && page == charged.Length;
            var refusals = fits && command.RootParkClear && message.Kind == Kinds.Delete && park?.Clearing == message.Id
                ? await ReadParkProtectionRefusalsAsync(db, message.Target.Id, message.Target.Partition, message.Id, long.MaxValue, token) : 0;
            // Check the registered footprint even if the arrivals exceed one page, so a valid plan is refused for page size.
            var job = message.Kind == Kinds.Recover && park is { At: not null, Clearing: null, Jobs: { } registry }
                ? await ReadRecoverableRootJobAsync(db, message.Target.Id, message.Target.Partition, registry, park.ParkJobResult,
                    park.ParkLiveJob, token)
                : null;
            var timers = recover && park is { At: not null, Clearing: null } && fits
                ? await ReadRootParkTimersAsync(db, message.Target.Id, message.Target.Partition, RootSelfIssuer, lockRows: false, token)
                : null;
            var facts = new FoundationRootParkFacts(park is { At: { } at, Revision: { } revision } ? new(revision, at) : null,
                kept.Count(row => row.KeptBy == (short)ParkKeepReason.Arrived),
                kept.Count(row => row.KeptBy == (short)ParkKeepReason.Consumed) + (db.UsesSqliteRootRuntime || job?.ConsumedId is null ? 0 : 1),
                fits, KeptSetDigest(kept)) { Outstanding = park?.Outstanding ?? false, Residue = park?.Residue ?? false, Rereads = RereadsParkFacts(db), Clearing = park?.Clearing, Cleared = park?.Cleared ?? 0, Page = page,
                    ClearsInPages = db.UsesSqliteRootRuntime || ClearsParkInPages(db), ProtectionRefusals = refusals,
                    RecoverableJobRegistry = job is null ? null : park?.Jobs, KeepsRegisteredJob = job?.KeepsRegistration == true,
                    RestorableTimers = timers };
            messages.Add(item with { Message = message with { Operator = command with { RootParkFacts = facts } } });
        }
        return polled with { Messages = messages };
    }

    private async Task<PreparedRoot?> PrepareRootParkRecoverAsync(WorkerRoot root, TurnCommit turn, CancellationToken token)
    {
        var command = turn.FoundationRootOperator!;
        var rearms = turn.FoundationRootTimerRearms;
        if (turn.Row is not { FoundationDispatchHeader: { } header } row || turn.Expected is not { } expected || turn.Journal is null ||
            command.Operator is not { RootParkMark: { } mark, RootParkWindow: >= 0, RootParkFacts: { } facts } || turn.Done is not [var key] ||
            key != command.InboxKey || !root.Owns(key) || command.Target.Id != root.Actor || command.TargetCreation != 1 ||
            turn.Consumed.Count + turn.Sent.Count + turn.Subscribed.Count + turn.Unsubscribed.Count != 0 || turn.Held is { Count: > 0 } ||
            turn != new TurnCommit(root.Actor, row, expected, turn.Consumed, turn.Done, turn.Sent, turn.Subscribed, turn.Unsubscribed,
                turn.Journal, Held: turn.Held) { FoundationRootOperator = command, FoundationRootTimerRearms = rearms })
            throw RootShape();
        var recovered = !turn.Journal.StartsWith("""{"Outcome":"refused",""", StringComparison.Ordinal);
        if (rearms is not null && (!recovered || RootSelfIssuer is not { RearmsTimers: true } ||
            rearms.Length is 0 or > InMemoryStore.MaxRestoreWaits || facts.RestorableTimers is null ||
            rearms.Sum(FoundationRootParkTimer.RearmBytes) > InMemoryStore.MaxRestoreBytes))
            throw RootShape();
        if (recovered ? row.Status != InstanceStatus.Running || facts.Mark != mark || !facts.FitsPage || turn.Journal != Json.Write(
                new { Outcome = "recovered", Operation = command.Id, Restored = facts.Arrived, Cleared = facts.Consumed, AlreadyRearmed = 0 })
            : !new[] { "target", "selection", "clearing", "plan", "page" }.Any(reason => turn.Journal == Json.Write(new { Outcome = "refused", Operation = command.Id, Reason = reason })))
            throw RootShape();

        var current = await ReadFirstRootAsync(root.Tenant, root.Reference, token);
        if (current?.Revision != expected || recovered && current.Suspended ||
            current.Status != (recovered ? InstanceStatus.Faulted : row.Status)) return null;
        var next = WorkerRow(root, current) with
        {
            Status = row.Status, Revision = checked(expected + 1), Fault = recovered ? null : current.Fault,
            Jobs = recovered && !facts.KeepsRegisteredJob ? null : current.Jobs, FoundationDispatchHeader = header
        };
        if (row != next || header.Tenant != root.Tenant || header.Reference != root.Reference ||
            header.Header.TypeData != current.TypeData || header.Header.Labels.Count != current.Labels.Count ||
            current.Labels.Any(pair => !header.Header.Labels.TryGetValue(pair.Key, out var value) || pair.Value != value))
            throw RootShape();
        return new(new TurnCommit(root.Actor, null, null, [], [], [], [], [], turn.Journal),
            new RootParkRecover(root, expected, command, recovered, current.Status,
                current.Status == InstanceStatus.Running && (current.Suspended || AuthoredWithholding(root, current)),
                RootSelfIssuer, rearms ?? []));
    }

    private sealed record LockedRecover(long? At, long? Revision, long Generation, string? Fault, byte[] StateDigest, byte[] HeaderDigest,
        int StateVersion, long? NextJobGeneration, string? ParkJobResult, string? ParkLiveJob);

    // One fenced commit: the root becomes Running with its Park mark cleared, the arrivals return to the inbox as the
    // next generation's cohort, and the fault input stays settled. A refusal moves only the revision and settles the command.
    private sealed record RootParkRecover(WorkerRoot Root, long Expected, Message Command, bool Recovered, InstanceStatus Prior,
        bool WithheldInputs, FoundationSelfIssuer? SelfIssuer, IReadOnlyList<ParkTimerRecipe> Rearms)
        : QualifiedRootCheckpoint(checked(Expected + 1))
    {
        internal override async Task<bool> BindCheckpointAsync(KernelDb db, CancellationToken token)
        {
            if (!db.UsesRootParkRecovery) return false;
            if (db.UsesSqliteRootRuntime) return await BindSqliteRootRecoverAsync(db, this, token);
            var facts = Command.Operator!.RootParkFacts!;
            FormattableString query = $"""
                SELECT i."FoundationParkAt" AS "At", i."FoundationParkRevision" AS "Revision", i."FoundationParkGeneration" AS "Generation",
                    i."Fault", sha256(convert_to(i."State", 'UTF8')) AS "StateDigest",
                    sha256(convert_to(i."FoundationHeader", 'UTF8')) AS "HeaderDigest", i."StateVersion", i."NextJobGeneration",
                    /*park-job-result*/ AS "ParkJobResult", /*park-live-job*/ AS "ParkLiveJob"
                FROM "Instances" i JOIN "FoundationRootBirths" b
                    ON b."Actor" = i."Id" AND b."Partition" = i."Partition" AND b."Creation" = 1
                    AND b."Tenant" = i."FoundationTenant" AND b."Reference" = i."FoundationReference"
                WHERE i."Id" = {Root.Actor} AND i."Partition" = {Root.Partition} AND NOT i."Retired"
                    AND (NOT {Recovered} OR NOT i."Suspended") AND i."Status" = {(int)Prior} AND i."Revision" = {Expected}
                    AND (NOT {Recovered} OR i."Jobs" IS NOT DISTINCT FROM {facts.RecoverableJobRegistry}::text /*clearing*/)
                    AND i."FoundationTenant" = {Root.Tenant} AND i."FoundationReference" = {Root.Reference}
                FOR UPDATE OF i
                """;
            var locked = await db.Database.SqlQuery<LockedRecover>(SelectParkJobPins(db, FormattableStringFactory.Create(
                query.Format.Replace("/*clearing*/", db.UsesRootComposedParkRecovery ? """AND i."FoundationParkClearing" IS NULL""" : "",
                    StringComparison.Ordinal), query.GetArguments()))).ToListAsync(token);
            if (locked is not [var prior] || (prior.At is null) != (Prior == InstanceStatus.Running)) return false;
            List<KeptRow> kept = [];
            if (Recovered)
            {
                var read = await ReadPostgresRecoveryRowsAsync(db, Root.Actor, Root.Partition, true, token);
                kept = read.Kept;
                if (!read.Fits || facts.Mark != new FoundationParkMark(prior.Revision ?? 0, prior.At ?? 0) ||
                    KeptSetDigest(kept) != facts.KeptDigest) return false;
                if (await ReadRootParkTimersAsync(db, Root.Actor, Root.Partition, SelfIssuer, lockRows: true, token) != facts.RestorableTimers)
                    return false;
            }
            var footprint = Recovered
                ? await ReadRecoverableRootJobAsync(db, Root.Actor, Root.Partition, facts.RecoverableJobRegistry, prior.ParkJobResult,
                    prior.ParkLiveJob, token)
                : new RecoverableRootJob();
            if (footprint is not { } job || Recovered && (job.KeepsRegistration != facts.KeepsRegisteredJob ||
                kept.Count(row => row.KeptBy == (short)ParkKeepReason.Consumed) + (job.ConsumedId is null ? 0 : 1) != facts.Consumed)) return false;
            if (!await BindRootTimerRearmsAsync(db, Root, JournalRevision, SelfIssuer, Rearms, facts.RestorableTimers, token))
                return false;
            var envelope = Command with { Operator = Command.Operator with { RootParkFacts = null } };
            if (!await SettleRootInputsAsync(db, Root.Reference, new(Root.Actor, 1), [Command.InboxKey], [envelope], SelfIssuer,
                WithheldInputs || db.UsesRootComposedParkRecovery && facts.Clearing is not null, token,
                parked: Prior == InstanceStatus.Faulted))
                return false;

            if (!await BindRecoverableRootJobAsync(db, Root.Actor, Root.Partition, job, token)) return false;

            var generation = Recovered ? checked(prior.Generation + 1) : prior.Generation;
            var update = ClearParkJobPins(db, $"""
                UPDATE "Instances" SET "Revision" = {JournalRevision}, "FoundationWriteRevision" = {JournalRevision},
                    "Status" = CASE WHEN {Recovered} THEN {(int)InstanceStatus.Running} ELSE "Status" END,
                    "Fault" = CASE WHEN {Recovered} THEN NULL ELSE "Fault" END,
                    "Jobs" = CASE WHEN {Recovered && !facts.KeepsRegisteredJob} THEN NULL ELSE "Jobs" END,
                    "FoundationParkAt" = CASE WHEN {Recovered} THEN NULL ELSE "FoundationParkAt" END,
                    "FoundationParkRevision" = CASE WHEN {Recovered} THEN NULL ELSE "FoundationParkRevision" END,
                    "FoundationParkGeneration" = {generation} /*park-job-result*/
                WHERE "Id" = {Root.Actor} AND "Partition" = {Root.Partition} AND "Revision" = {Expected}
                """, Recovered);
            if (await db.Database.ExecuteSqlInterpolatedAsync(update, token) != 1) return false;

            var arrived = kept.Where(row => row.KeptBy == (short)ParkKeepReason.Arrived).ToArray();
            var cleared = kept.Where(row => row.KeptBy == (short)ParkKeepReason.Consumed).ToArray();
            if (arrived.Length + cleared.Length != kept.Count) return false;
            var window = Command.Operator.RootParkWindow!.Value;
            if (Recovered && (!await RestoreAsync() ||
                !await ClearPostgresParkRowsAsync(db, Root, cleared, window, token))) return false;
            // The worker forgets inputs it read before Recover so the released cohort gets the first poll.
            // Pending calls and controls must become visible again even when they were read before the Park.
            if (Recovered && db.UsesRootComposedParkRecovery)
                await db.Database.ExecuteSqlInterpolatedAsync(WithParkInputScopeParameters(db, $"""
                    UPDATE "Messages" SET "Seen" = 0
                    WHERE "Target" = {Root.Actor} AND "Partition" = {Root.Partition} AND "InboxScope" IN (/*park-input-scopes*/)
                        AND "EventMatch" IS NULL AND NOT "Done" AND "Seen" <> 0
                    """), token);
            if (Recovered && !await VerifyClearedPostgresParkRowsAsync(db, Root, cleared, window, token)) return false;
            long? at = Recovered ? null : prior.At, revision = Recovered ? null : prior.Revision;
            var fault = Recovered ? null : prior.Fault;
            var status = Recovered ? InstanceStatus.Running : Prior;
            var registry = facts.KeepsRegisteredJob ? facts.RecoverableJobRegistry : null;
            return await db.Database.SqlQuery<bool>(WithParkInputScopeParameters(db, $"""
                SELECT EXISTS (SELECT 1 FROM "Instances" WHERE "Id" = {Root.Actor} AND "Partition" = {Root.Partition}
                        AND "Revision" = {JournalRevision} AND "FoundationWriteRevision" = {JournalRevision} AND "Status" = {(int)status}
                        AND "Fault" IS NOT DISTINCT FROM {fault}::text AND "FoundationParkAt" IS NOT DISTINCT FROM {at}::bigint
                        AND "FoundationParkRevision" IS NOT DISTINCT FROM {revision}::bigint AND "FoundationParkGeneration" = {generation}
                        AND (NOT {Recovered} OR "Jobs" IS NOT DISTINCT FROM {registry}::text)
                        AND "NextJobGeneration" IS NOT DISTINCT FROM {prior.NextJobGeneration}::bigint
                        AND "StateVersion" = {prior.StateVersion} AND sha256(convert_to("State", 'UTF8')) = {prior.StateDigest}
                        AND sha256(convert_to("FoundationHeader", 'UTF8')) = {prior.HeaderDigest})
                    AND NOT ({Recovered} AND EXISTS (SELECT 1 FROM "Messages" WHERE "Target" = {Root.Actor}
                        AND "Partition" = {Root.Partition} AND "InboxScope" IN (/*park-input-scopes*/) AND "FoundationKeptBy" IS NOT NULL))
                    AND EXISTS (SELECT 1 FROM "FoundationActorList" WHERE "Tenant" = {Root.Tenant} AND "Reference" = {Root.Reference}
                        AND "Status" = {(int)status} AND "ParkAt" IS NOT DISTINCT FROM {at}::bigint
                        AND "ParkFault" IS NOT DISTINCT FROM {fault}::text AND "ParkRevision" IS NOT DISTINCT FROM {revision}::bigint)
                AS "Value"
                """)).SingleAsync(token);

            async Task<bool> RestoreAsync()
            {
                foreach (var scope in arrived.GroupBy(row => row.Scope))
                {
                    var ids = scope.Select(row => row.Id).ToArray();
                    var isSelf = scope.Key == RootSelfScope;
                    if (await db.Database.ExecuteSqlInterpolatedAsync($"""
                        UPDATE "Messages" SET "Done" = false, "FoundationKeptBy" = NULL, "Seen" = 0, "Trial" = 0, "Attempts" = 0,
                            "FoundationReleasedBy" = {generation}, "KeepUntil" = CASE WHEN {isSelf} THEN NULL
                                ELSE floor(extract(epoch from transaction_timestamp()) * 1000)::bigint + {window} END
                        WHERE "Id" = ANY({ids}) AND "Target" = {Root.Actor} AND "Partition" = {Root.Partition}
                            AND "InboxScope" = {scope.Key} AND "EventMatch" IS NULL AND "Done" AND "FoundationKeptBy" = {(short)ParkKeepReason.Arrived}
                        """, token) != ids.Length) return false;

                    var digests = await db.Database.SqlQueryRaw<string>($"""
                        SELECT {KeptDigestOf(db, "NULL::bigint", $"{(short)ParkKeepReason.Arrived}::smallint")} AS "Value"
                        FROM "Messages" k WHERE k."Id" = ANY({"{0}"}) AND k."Target" = {"{1}"} AND k."Partition" = {"{2}"}
                            AND k."InboxScope" = {"{3}"} AND k."EventMatch" IS NULL AND NOT k."Done" AND k."FoundationKeptBy" IS NULL
                            AND k."FoundationReleasedBy" = {"{4}"} AND k."Seen" = 0 AND k."Trial" = 0 AND k."Attempts" = 0
                            AND k."KeepUntil" IS NOT DISTINCT FROM CASE WHEN {"{5}"} THEN NULL
                                ELSE floor(extract(epoch from transaction_timestamp()) * 1000)::bigint + {"{6}"} END
                        ORDER BY k."Seq", k."Id" COLLATE "C"
                        """, ids, Root.Actor, Root.Partition, scope.Key, generation, isSelf, window).ToListAsync(token);
                    if (!digests.SequenceEqual(scope.Select(row => row.Envelope))) return false;
                }
                return true;
            }
        }
    }

    private static async Task<bool> ClearPostgresParkRowsAsync(KernelDb db, WorkerRoot root, IReadOnlyList<KeptRow> rows,
        long window, CancellationToken token)
    {
        foreach (var scope in rows.GroupBy(row => row.Scope))
        {
            var ids = scope.Select(row => row.Id).ToArray();
            var self = scope.Key == RootSelfScope;
            FormattableString update = self ? (FormattableString)$"""
                DELETE FROM "Messages" WHERE "Id" = ANY({ids}) AND "Target" = {root.Actor} AND "Partition" = {root.Partition}
                    AND "InboxScope" = {scope.Key} AND "EventMatch" IS NULL AND "Done" AND "FoundationKeptBy" IS NOT NULL
                """ : $"""
                UPDATE "Messages" SET "FoundationKeptBy" = NULL, "Payload" = NULL, "Start" = NULL, "ReplyTo" = NULL,
                    "KeepUntil" = floor(extract(epoch from transaction_timestamp()) * 1000)::bigint + {window}
                WHERE "Id" = ANY({ids}) AND "Target" = {root.Actor} AND "Partition" = {root.Partition}
                    AND "InboxScope" = {scope.Key} AND "EventMatch" IS NULL AND "Done" AND "FoundationKeptBy" IS NOT NULL
                """;
            if (await db.Database.ExecuteSqlInterpolatedAsync(update, token) != ids.Length)
                return false;
        }
        return true;
    }

    private static async Task<bool> VerifyClearedPostgresParkRowsAsync(KernelDb db, WorkerRoot root, IReadOnlyList<KeptRow> rows,
        long window, CancellationToken token)
    {
        foreach (var scope in rows.GroupBy(row => row.Scope))
        {
            var ids = scope.Select(row => row.Id).ToArray();
            var expected = scope.Key == RootSelfScope ? 0 : ids.Length;
            if (!await db.Database.SqlQuery<bool>($"""
                SELECT count(*) FILTER (WHERE "FoundationKeptBy" IS NULL AND "Done" AND "Payload" IS NULL AND "Start" IS NULL
                    AND "ReplyTo" IS NULL AND "KeepUntil" = floor(extract(epoch from transaction_timestamp()) * 1000)::bigint + {window}) = {expected}
                    AND count(*) = {expected} AS "Value"
                FROM "Messages" WHERE "Target" = {root.Actor} AND "Partition" = {root.Partition} AND "InboxScope" = {scope.Key}
                    AND "EventMatch" IS NULL AND "Id" = ANY({ids})
                """).SingleAsync(token)) return false;
        }
        return true;
    }
}
