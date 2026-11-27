using Microsoft.EntityFrameworkCore;

namespace Kernel.EntityFramework;

public sealed partial class EfStore
{
    private sealed record LiveRootJob(string Proof, string[] Ids);
    private sealed record LiveRootJobRow(string Id, string Kind, long Seq, long Due, bool Valid = true);

    private readonly record struct RecoverableRootJob(string? ConsumedId = null, LiveRootJob? Live = null)
    {
        internal bool KeepsRegistration => Live is not null;
    }

    private static async Task<RecoverableRootJob?> ReadRecoverableRootJobAsync(KernelDb db, string actor, int partition,
        string? registry, string? pinnedResult, string? pinnedLive, CancellationToken token)
    {
        if (registry is null) return pinnedResult is null && pinnedLive is null ? new RecoverableRootJob() : null;
        if (pinnedResult is not null)
            return pinnedLive is null && pinnedResult == await ReadConsumedRootJobAsync(db, actor, partition, registry, token)
                ? new(ConsumedId: pinnedResult) : null;
        if (pinnedLive is null || JobRegistry.Read(registry) is not [var job]) return null;

        // Opening already authenticated the bodies. The reference rows protect them until timed settlement.
        var rows = await db.Database.SqlQueryRaw<LiveRootJobRow>($"""
            SELECT m."Id", m."Kind", m."Seq", m."Due", ({ValidLiveRootJob("m.")} AND {ReferencesLiveJob("m", sameActor: true)}) IS TRUE AS "Valid"
            FROM "Messages" m WHERE m."Partition" = {"{0}"} AND m."Target" = {"{1}"} AND m."JobGeneration" = {"{2}"}
                AND {IsLiveRootJob("m.")}
            LIMIT 3
            /* bounded-live-job-metadata */
            """, partition, actor, job.Generation).ToArrayAsync(token);
        var live = DescribeLiveRootJob(rows);
        return live?.Proof == pinnedLive ? new(Live: live) : null;
    }

    private static async Task<LiveRootJob?> ReadLiveRootJobAsync(KernelDb db, string actor, int partition, string model,
        string? registry, CancellationToken token)
    {
        if (!RecordsConsumedRootJob(db) || JobRegistry.Read(registry) is not [{ Key.Length: > 0, Generation: >= 0 and < long.MaxValue } job])
            return null;
        var lockRows = db.Database.IsNpgsql() ? "FOR UPDATE" : "";
        var rows = await db.Messages.FromSqlRaw($"""
            SELECT * FROM "Messages" WHERE "Partition" = {"{0}"} AND "Target" = {"{1}"} AND "JobGeneration" = {"{2}"}
                AND {IsLiveRootJob()}
            ORDER BY "Seq", "Id" LIMIT 3 {lockRows}
            """, partition, actor, job.Generation).AsNoTracking().ToArrayAsync(token);
        var live = DescribeLiveRootJob(rows.Select(row => new LiveRootJobRow(row.Id, row.Kind, row.Seq, row.Due)).ToArray());
        if (live is null) return null;
        try
        {
            if (rows is [var result])
                return DecodeRootTimedRow(result).Read<JobFinished>().Key == job.Key ? live : null;
            var deadline = rows.Single(row => row.Kind == Kinds.JobDeadline);
            var ticket = rows.Single(row => row.Kind == Kinds.Job);
            var original = DecodeRootTimedRow(ticket);
            if (RootJobTicket.Read(original, actor, model, timed: true) is not { } issued || issued.Key != job.Key ||
                DecodeRootTimedRow(deadline) != RootJobTicket.Deadline(actor, issued))
                return null;
            return live;
        }
        catch (Exception error) when (error is System.Text.Json.JsonException or NotSupportedException or InvalidOperationException or ArgumentException)
        {
            return null;
        }
    }

    private static LiveRootJob? DescribeLiveRootJob(LiveRootJobRow[] rows)
    {
        if (rows is [{ Kind: Kinds.JobFinished, Due: 0, Valid: true } result]) return new(Proof(result), [result.Id]);
        if (rows.Length == 2 && rows.FirstOrDefault(row => row is { Kind: Kinds.Job, Due: 0, Valid: true }) is { } ticket &&
            rows.FirstOrDefault(row => row is { Kind: Kinds.JobDeadline, Due: > 0, Valid: true }) is { } deadline && deadline.Seq < ticket.Seq)
            return new(Proof(ticket) + ":" + Proof(deadline), [ticket.Id, deadline.Id]);
        return null;

        static string Proof(LiveRootJobRow row) => FormattableString.Invariant($"{row.Id}/{row.Seq}/{row.Due}");
    }

    private static string IsLiveRootJob(string prefix = "") =>
        $"""{prefix}"InboxScope" = {RootTimedJobScope} AND NOT {prefix}"Done" AND {prefix}"FoundationKeptBy" IS NULL""";

    private static string ValidLiveRootJob(string prefix = "") => $"""
        {prefix}"EventMatch" IS NULL AND {prefix}"Start" IS NULL AND {prefix}"Payload" IS NOT NULL
        AND {prefix}"Sender" = {prefix}"Target" AND {prefix}"ReplyTo" IS NULL AND {prefix}"KeepUntil" IS NULL
        """;

    private static readonly string LiveRootJobIndex = $"""
        ("Partition", "Target", "JobGeneration", "Seq", "Id") WHERE {IsLiveRootJob()}
        """;

    private static string LiveRootJobFootprint(string actor, bool journaling) => $"""
        (SELECT (count(*) = 1 AND max(CASE WHEN "Valid" AND "Kind" = '{Kinds.JobFinished}' AND "Due" = 0 THEN "Proof" END)
                = {actor}."FoundationParkLiveJob")
            OR (count(*) = 2 AND (max(CASE WHEN "Valid" AND "Kind" = '{Kinds.Job}' AND "Due" = 0 THEN "Proof" END)
                    || ':' || max(CASE WHEN "Valid" AND "Kind" = '{Kinds.JobDeadline}' AND "Due" > 0 THEN "Proof" END))
                = {actor}."FoundationParkLiveJob"
                AND max(CASE WHEN "Kind" = '{Kinds.JobDeadline}' THEN "Seq" END) < max(CASE WHEN "Kind" = '{Kinds.Job}' THEN "Seq" END))
            FROM (
                SELECT m."Kind", m."Seq", m."Due", m."Id" || '/' || CAST(m."Seq" AS text) || '/' || CAST(m."Due" AS text) AS "Proof",
                    ({ValidLiveRootJob("m.")} AND {ReferencesLiveJob("m", sameActor: true)} {(journaling ? "" : """AND m."Seen" = 0""")}) AS "Valid"
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

    private static string LiveJobReferencesColumns(string scopeType) => $"""
        "Id" text NOT NULL, "InboxScope" {scopeType} NOT NULL, "Actor" text NOT NULL,
        PRIMARY KEY ("Id", "InboxScope"),
        FOREIGN KEY ("Id", "InboxScope") REFERENCES "Messages" ("Id", "InboxScope") DEFERRABLE INITIALLY DEFERRED
        """;

    // The protected membership survives Recover. A later running job transition may remove
    // the timed rows, but committing an isolated deletion leaves an outstanding reference.
    private static string ReferencesLiveJob(string row, bool sameActor = false) => $"""
        EXISTS (SELECT 1 FROM "FoundationProtectedTimedJobs" p
            WHERE p."Id" = {row}."Id" AND p."InboxScope" = {row}."InboxScope"
                {(sameActor ? $"""AND p."Actor" = {row}."Target" """ : "")})
        """;

    // The opener establishes membership only after validating and locking the timed envelopes.
    private static Task ProtectLiveJobAsync(KernelDb db, string actor, int partition, LiveRootJob job, CancellationToken token) =>
        db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "FoundationProtectedTimedJobs" ("Id", "InboxScope", "Actor")
            SELECT "Id", "InboxScope", {actor} FROM "Messages"
            WHERE "Id" IN ({job.Ids[0]}, {job.Ids[^1]}) AND "InboxScope" = {RootTimedJobScope}
                AND "Target" = {actor} AND "Partition" = {partition}
            ON CONFLICT ("Id", "InboxScope") DO NOTHING
            """, token);

    private static string PostgresParkLiveJobGuards() => $"""
        CREATE TABLE IF NOT EXISTS "FoundationProtectedTimedJobs" ({LiveJobReferencesColumns("bigint")});
        CREATE INDEX IF NOT EXISTS "IX_FoundationProtectedTimedJobs_Actor" ON "FoundationProtectedTimedJobs" ("Actor");
        CREATE OR REPLACE FUNCTION kernel_preserve_parked_live_job() RETURNS trigger LANGUAGE plpgsql AS $fn$
        BEGIN
            IF TG_OP = 'DELETE' THEN
                IF NOT {ParkedLiveJob("OLD")} THEN RETURN OLD; END IF;
            ELSIF TG_OP = 'INSERT' THEN
                IF NEW."InboxScope" = {RootTimedJobScope} THEN
                    PERFORM 1 FROM "Instances" i WHERE i."Id" = NEW."Target" AND i."Partition" = NEW."Partition" FOR SHARE OF i;
                END IF;
                IF NOT {ParkedLiveJob("NEW")} AND NOT {ReferencesLiveJob("NEW")} THEN RETURN NEW; END IF;
            ELSIF NOT {ParkedLiveJob("OLD")} AND NOT {ParkedLiveJob("NEW")}
                AND NOT {ReferencesLiveJob("OLD")} AND NOT {ReferencesLiveJob("NEW")} THEN
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
        yield return Table("FoundationProtectedTimedJobs", LiveJobReferencesColumns("INTEGER"));
        yield return Index("IX_FoundationProtectedTimedJobs_Actor", "FoundationProtectedTimedJobs", """("Actor")""");
        yield return Guard("insert", "INSERT", $"{ParkedLiveJob("NEW")} OR {ReferencesLiveJob("NEW")}");
        yield return Guard("delete", "DELETE", ParkedLiveJob("OLD"));
        yield return Guard("update", "UPDATE OF " + LiveJobEnvelopeColumns, $"""
            {ParkedLiveJob("OLD")} OR {ParkedLiveJob("NEW")} OR {ReferencesLiveJob("OLD")} OR {ReferencesLiveJob("NEW")}
            """);
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

    // Fenced timed settlement releases the exact rows it verified. A fault applying a result instead
    // transfers its envelope from this live reference to the consumed result pin.
    private static Task ReleaseProtectedTimedJobsAsync(KernelDb db, string actor, string[] ids, CancellationToken token)
    {
        if (!RecordsConsumedRootJob(db) || ids.Length == 0) return Task.CompletedTask;
        return db.UsesSqliteRootRuntime
            ? db.Database.ExecuteSqlInterpolatedAsync($"""
                DELETE FROM "FoundationProtectedTimedJobs" WHERE "Actor" = {actor} AND "InboxScope" = {RootTimedJobScope}
                    AND "Id" IN (SELECT value FROM json_each({Json.Write(ids)}))
                """, token)
            : db.Database.ExecuteSqlInterpolatedAsync($"""
                DELETE FROM "FoundationProtectedTimedJobs" WHERE "Actor" = {actor} AND "InboxScope" = {RootTimedJobScope} AND "Id" = ANY({ids})
                """, token);
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
