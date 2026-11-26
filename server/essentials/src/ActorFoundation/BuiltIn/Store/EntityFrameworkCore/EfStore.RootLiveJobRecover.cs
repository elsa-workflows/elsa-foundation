using Microsoft.EntityFrameworkCore;

namespace Kernel.EntityFramework;

public sealed partial class EfStore
{
    private sealed record LiveRootJob(string Proof, string[] Ids);

    private readonly record struct RecoverableRootJob(string? ConsumedId = null, LiveRootJob? Live = null)
    {
        internal bool KeepsRegistration => Live is not null;
    }

    private static async Task<RecoverableRootJob?> ReadRecoverableRootJobAsync(KernelDb db, string actor, int partition,
        string? header, string? registry, string? pinnedResult, string? pinnedLive, CancellationToken token)
    {
        if (registry is null) return pinnedResult is null && pinnedLive is null ? new RecoverableRootJob() : null;
        if (pinnedResult is not null)
            return pinnedLive is null && pinnedResult == await ReadConsumedRootJobAsync(db, actor, partition, registry, token)
                ? new(ConsumedId: pinnedResult) : null;
        if (pinnedLive is null || header is null) return null;

        var model = DurableKeyJson.Read<RootHeader>(header).Model;
        var live = await ReadLiveRootJobAsync(db, actor, partition, model, registry, token);
        return live?.Proof == pinnedLive ? new(Live: live) : null;
    }

    private static async Task<LiveRootJob?> ReadLiveRootJobAsync(KernelDb db, string actor, int partition, string model,
        string? registry, CancellationToken token)
    {
        if (!RecordsConsumedRootJob(db) || JobRegistry.Read(registry) is not [{ Key.Length: > 0, Generation: >= 0 and < long.MaxValue } job])
            return null;
        var rows = await db.Messages.FromSqlRaw($"""
            SELECT * FROM "Messages" WHERE "Partition" = {"{0}"} AND "Target" = {"{1}"} AND "JobGeneration" = {"{2}"}
                AND {IsLiveRootJob()}
            LIMIT 3
            """, partition, actor, job.Generation).AsNoTracking().ToArrayAsync(token);
        try
        {
            if (rows is [{ Kind: Kinds.JobFinished } result] && DecodeRootTimedRow(result).Read<JobFinished>().Key == job.Key)
                return new(JobRowProof(result), [result.Id]);
            if (rows.Length != 2 || rows.SingleOrDefault(row => row.Kind == Kinds.Job) is not { } ticket ||
                rows.SingleOrDefault(row => row.Kind == Kinds.JobDeadline) is not { } deadline)
                return null;
            var original = DecodeRootTimedRow(ticket);
            if (RootJobTicket.Read(original, actor, model, timed: true) is not { } issued || issued.Key != job.Key ||
                DecodeRootTimedRow(deadline) != RootJobTicket.Deadline(actor, issued))
                return null;
            return new(JobRowProof(ticket) + ":" + JobRowProof(deadline), [ticket.Id, deadline.Id]);
        }
        catch (Exception error) when (error is System.Text.Json.JsonException or NotSupportedException or InvalidOperationException or ArgumentException)
        {
            return null;
        }

        static string JobRowProof(MessageEntity row) => FormattableString.Invariant($"{row.Id}/{row.Seq}/{row.Due}");
    }

    private static string IsLiveRootJob(string prefix = "") =>
        $"""{prefix}"InboxScope" = {RootTimedJobScope} AND NOT {prefix}"Done" AND {prefix}"FoundationKeptBy" IS NULL""";

    private static string ValidLiveRootJob(string prefix = "") => $"""
        {prefix}"EventMatch" IS NULL AND {prefix}"Start" IS NULL AND {prefix}"Payload" IS NOT NULL
        AND {prefix}"Sender" = {prefix}"Target" AND {prefix}"ReplyTo" IS NULL AND {prefix}"KeepUntil" IS NULL
        """;

    private static readonly string LiveRootJobIndex = $"""
        ("Partition", "Target", "JobGeneration") WHERE {IsLiveRootJob()}
        """;

    private static string LiveRootJobFootprint(string actor, bool journaling) => $"""
        (SELECT (count(*) = 1 AND max(CASE WHEN "Valid" AND "Kind" = '{Kinds.JobFinished}' AND "Due" = 0 THEN "Proof" END)
                = {actor}."FoundationParkLiveJob")
            OR (count(*) = 2 AND (max(CASE WHEN "Valid" AND "Kind" = '{Kinds.Job}' AND "Due" = 0 THEN "Proof" END)
                    || ':' || max(CASE WHEN "Valid" AND "Kind" = '{Kinds.JobDeadline}' AND "Due" > 0 THEN "Proof" END))
                = {actor}."FoundationParkLiveJob")
            FROM (
                SELECT m."Kind", m."Due", m."Id" || '/' || CAST(m."Seq" AS text) || '/' || CAST(m."Due" AS text) AS "Proof",
                    ({ValidLiveRootJob("m.")} {(journaling ? "" : """AND m."Seen" = 0""")}) AS "Valid"
                FROM "Messages" m WHERE m."Partition" = {actor}."Partition" AND m."Target" = {actor}."Id"
                    AND m."JobGeneration" = {actor}."NextJobGeneration" - 1 AND {IsLiveRootJob("m.")}
                LIMIT 3
            ) live_job)
        """;

    private const string LiveJobEnvelopeColumns = """
        "Id", "InboxScope", "Partition", "Target", "Kind", "Seq", "Due", "JobGeneration", "EventMatch", "Done",
        "FoundationKeptBy", "Start", "Payload", "Sender", "ReplyTo", "KeepUntil"
        """;

    private const string ParkedLiveJobRefusal = "Qualified actor preserves the live timed job envelope in its Park";

    private static string ParkedLiveJob(string row) => $"""
        ({IsLiveRootJob(row + ".")} AND EXISTS (SELECT 1 FROM "Instances" i
            WHERE i."Id" = {row}."Target" AND i."Partition" = {row}."Partition" AND i."FoundationReference" IS NOT NULL
                AND i."FoundationParkAt" IS NOT NULL AND i."FoundationParkLiveJob" IS NOT NULL AND NOT i."Retired"
                AND {row}."JobGeneration" = i."NextJobGeneration" - 1))
        """;

    private static string PostgresParkLiveJobGuards() => $"""
        CREATE OR REPLACE FUNCTION kernel_preserve_parked_live_job() RETURNS trigger LANGUAGE plpgsql AS $fn$
        BEGIN
            IF TG_OP = 'DELETE' THEN
                IF NOT {ParkedLiveJob("OLD")} THEN RETURN OLD; END IF;
            ELSIF TG_OP = 'INSERT' THEN
                IF NOT {ParkedLiveJob("NEW")} THEN RETURN NEW; END IF;
            ELSIF NOT {ParkedLiveJob("OLD")} AND NOT {ParkedLiveJob("NEW")} THEN
                RETURN NEW;
            END IF;
            RAISE EXCEPTION '{ParkedLiveJobRefusal}' USING ERRCODE = '23514';
        END
        $fn$;
        CREATE OR REPLACE TRIGGER kernel_preserve_parked_live_job
        BEFORE INSERT OR DELETE OR UPDATE OF {LiveJobEnvelopeColumns} ON "Messages"
        FOR EACH ROW EXECUTE FUNCTION kernel_preserve_parked_live_job();
        """;

    private static IEnumerable<SqliteDefinition> SqliteParkLiveJobGuards()
    {
        // A key conflict can replace the existing row without running its delete trigger.
        var target = $"""
            EXISTS (SELECT 1 FROM "Messages" m WHERE m."Id" = NEW."Id" AND m."InboxScope" = NEW."InboxScope"
                AND {ParkedLiveJob("m")})
            """;
        yield return Guard("insert", "INSERT", $"{ParkedLiveJob("NEW")} OR {target}");
        yield return Guard("delete", "DELETE", ParkedLiveJob("OLD"));
        yield return Guard("update", "UPDATE OF " + LiveJobEnvelopeColumns, $"{ParkedLiveJob("OLD")} OR {ParkedLiveJob("NEW")} OR {target}");

        static SqliteDefinition Guard(string suffix, string operation, string condition)
        {
            var name = "kernel_preserve_parked_live_job_" + suffix;
            return new("trigger", name, "Messages", $"""
                CREATE TRIGGER {name} BEFORE {operation} ON "Messages"
                WHEN {condition}
                BEGIN SELECT RAISE(ABORT, '{ParkedLiveJobRefusal}'); END
                """);
        }
    }

    private static async Task<bool> BindRecoverableRootJobAsync(KernelDb db, string actor, int partition, RecoverableRootJob job,
        CancellationToken token)
    {
        if (job.Live is not { Ids: var ids }) return await ClearConsumedRootJobAsync(db, actor, partition, job.ConsumedId, token);

        // The existing ticket and deadline or published result keep their sequence, due time and generation.
        // Offer them again to the owner following the recovered cohort.
        return await db.Messages.Where(row => row.Target == actor && row.Partition == partition &&
                row.InboxScope == RootTimedJobScope && row.EventMatch == null && !row.Done && ids.Contains(row.Id))
            .ExecuteUpdateAsync(update => update.SetProperty(row => row.Seen, 0L), token) == ids.Length;
    }
}
