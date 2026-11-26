using Microsoft.EntityFrameworkCore;

namespace Kernel.EntityFramework;

public sealed partial class EfStore
{
    private readonly record struct RecoverableRootJob(string? ConsumedId = null, string[]? LiveIds = null)
    {
        internal bool KeepsRegistration => LiveIds is not null;
    }

    private sealed record LiveRootJobRow(string Id, string Kind, long Due, bool Valid);

    private static async Task<RecoverableRootJob?> ReadRecoverableRootJobAsync(KernelDb db, string actor, int partition,
        string? registry, string? pinnedResult, CancellationToken token)
    {
        if (registry is null) return pinnedResult is null ? new RecoverableRootJob() : null;
        if (pinnedResult is not null)
            return pinnedResult == await ReadConsumedRootJobAsync(db, actor, partition, registry, token)
                ? new(ConsumedId: pinnedResult) : null;
        if (!RecordsConsumedRootJob(db) || JobRegistry.Read(registry) is not [var job]) return null;

        var ticket = StoredJobId(InboxKey.Ticket(actor, job.Key, 1, job.Generation));
        var deadline = StoredJobId(InboxKey.Issued(DurableTurn.FoundationJobDeadlineId(actor, 1, job.Generation)));
        var result = StoredJobId(InboxKey.JobResult(actor, job.Key, 1, job.Generation));
        var rows = await db.Database.SqlQueryRaw<LiveRootJobRow>($"""
            SELECT "Id", "Kind", "Due", ({ValidLiveRootJob()}) IS TRUE AS "Valid"
            FROM "Messages" WHERE "Partition" = {"{0}"} AND "Target" = {"{1}"} AND "JobGeneration" = {"{2}"}
                AND {IsLiveRootJob()}
            LIMIT 3
            """, partition, actor, job.Generation).ToArrayAsync(token);
        if (rows is [{ Kind: Kinds.JobFinished, Due: 0, Valid: true } recorded] && recorded.Id == result ||
            rows.Length == 2 && rows.Any(row => row is { Kind: Kinds.Job, Due: 0, Valid: true } && row.Id == ticket) &&
                rows.Any(row => row is { Kind: Kinds.JobDeadline, Due: > 0, Valid: true } && row.Id == deadline))
            return new(LiveIds: rows.Select(row => row.Id).ToArray());
        return null;
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
        (SELECT (count(*) = 1 AND count(CASE WHEN "Valid" AND "Kind" = '{Kinds.JobFinished}' AND "Due" = 0 THEN 1 END) = 1)
            OR (count(*) = 2 AND count(CASE WHEN "Valid" AND "Kind" = '{Kinds.Job}' AND "Due" = 0 THEN 1 END) = 1
                AND count(CASE WHEN "Valid" AND "Kind" = '{Kinds.JobDeadline}' AND "Due" > 0 THEN 1 END) = 1)
            FROM (
                SELECT m."Kind", m."Due", ({ValidLiveRootJob("m.")}
                    {(journaling ? "" : """AND m."Seen" = 0""")}) AS "Valid"
                FROM "Messages" m WHERE m."Partition" = {actor}."Partition" AND m."Target" = {actor}."Id"
                    AND m."JobGeneration" = {actor}."NextJobGeneration" - 1 AND {IsLiveRootJob("m.")}
                LIMIT 3
            ) live_job)
        """;

    private static async Task<bool> BindRecoverableRootJobAsync(KernelDb db, string actor, int partition, RecoverableRootJob job,
        CancellationToken token)
    {
        if (job.LiveIds is not { } ids) return await ClearConsumedRootJobAsync(db, actor, partition, job.ConsumedId, token);

        // The existing ticket and deadline or published result keep their sequence, due time and generation.
        // Offer them again to the owner following the recovered cohort.
        return await db.Messages.Where(row => row.Target == actor && row.Partition == partition &&
                row.InboxScope == RootTimedJobScope && row.EventMatch == null && !row.Done && ids.Contains(row.Id))
            .ExecuteUpdateAsync(update => update.SetProperty(row => row.Seen, 0L), token) == ids.Length;
    }
}
