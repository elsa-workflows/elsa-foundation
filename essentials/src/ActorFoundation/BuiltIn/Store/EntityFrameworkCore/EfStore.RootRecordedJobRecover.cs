using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;

namespace Kernel.EntityFramework;

public sealed partial class EfStore
{
    private static bool RecordsConsumedRootJob(KernelDb db) => db.UsesRootComposedParkRecovery && db.UsesRootTimedJobSettlement;

    private static FormattableString WithParkJobResult(KernelDb db, FormattableString update, string? consumedId) =>
        RecordsConsumedRootJob(db) && consumedId is not null
            ? FormattableStringFactory.Create(
                update.Format.Replace("/*park-job-result*/", $$""", "FoundationParkJobResult" = {{{update.ArgumentCount}}}""", StringComparison.Ordinal),
                [.. update.GetArguments(), consumedId])
            : update;

    private static FormattableString WithLiveParkJob(KernelDb db, FormattableString update, string? proof) =>
        RecordsConsumedRootJob(db) && proof is not null
            ? FormattableStringFactory.Create(
                update.Format.Replace("/*park-live-job*/", $$""", "FoundationParkLiveJob" = {{{update.ArgumentCount}}}""", StringComparison.Ordinal),
                [.. update.GetArguments(), proof])
            : update;

    private static FormattableString ClearParkJobPins(KernelDb db, FormattableString update, bool recovered) =>
        RecordsConsumedRootJob(db) && recovered
            ? FormattableStringFactory.Create(
                update.Format.Replace("/*park-job-result*/", """, "FoundationParkJobResult" = NULL, "FoundationParkLiveJob" = NULL""",
                    StringComparison.Ordinal),
                update.GetArguments())
            : update;

    private static FormattableString SelectParkJobPins(KernelDb db, FormattableString query) =>
        FormattableStringFactory.Create(query.Format.Replace("/*park-job-result*/",
                RecordsConsumedRootJob(db) ? """i."FoundationParkJobResult" """ : "CAST(NULL AS text)", StringComparison.Ordinal)
            .Replace("/*park-live-job*/", RecordsConsumedRootJob(db) ? """i."FoundationParkLiveJob" """ : "CAST(NULL AS text)",
                StringComparison.Ordinal), query.GetArguments());

    private static async Task<string?> ReadConsumedRootJobAsync(KernelDb db, string actor, int partition, string? registry,
        CancellationToken token)
    {
        if (!RecordsConsumedRootJob(db) || JobRegistry.Read(registry) is not [var job]) return null;
        var id = StoredJobId(InboxKey.JobResult(actor, job.Key, 1, job.Generation));
        // Verify the stored result identity and kept reason without opening the consumed body.
        return await db.Database.SqlQuery<string>($"""
            SELECT "Id" AS "Value" FROM "Messages"
            WHERE "Id" = {id} AND "Target" = {actor} AND "Partition" = {partition} AND "InboxScope" = {RootTimedJobScope}
                AND "Kind" = {Kinds.JobFinished} AND "JobGeneration" = {job.Generation}
                AND "EventMatch" IS NULL AND "Done" AND "Due" = 0 AND "Start" IS NULL AND "Payload" IS NOT NULL
                AND "Sender" = "Target" AND "ReplyTo" IS NULL AND "KeepUntil" IS NULL
                AND "FoundationKeptBy" = {(short)ParkKeepReason.Consumed}
            """).SingleOrDefaultAsync(token);
    }

    private static async Task<bool> ClearConsumedRootJobAsync(KernelDb db, string actor, int partition, string? consumedId,
        CancellationToken token) =>
        consumedId is null || await db.Database.ExecuteSqlInterpolatedAsync($"""
            DELETE FROM "Messages" WHERE "Id" = {consumedId} AND "Target" = {actor} AND "Partition" = {partition}
                AND "InboxScope" = {RootTimedJobScope} AND "EventMatch" IS NULL AND "Done"
                AND "FoundationKeptBy" = {(short)ParkKeepReason.Consumed}
            """, token) == 1;
}
