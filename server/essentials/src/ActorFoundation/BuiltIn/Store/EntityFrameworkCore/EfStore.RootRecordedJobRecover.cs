using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;

namespace Kernel.EntityFramework;

public sealed partial class EfStore
{
    private sealed record ConsumedJobRow(string Id, long JobGeneration);

    private static async Task<string[]?> ReadConsumedRootJobsAsync(KernelDb db, string actor, int partition, string? registry,
        CancellationToken token)
    {
        if (registry is null) return [];
        if (!db.UsesRootComposedParkRecovery || !db.UsesRootTimedJobSettlement) return null;
        var registrations = JobRegistry.Read(registry);
        if (registrations.Count == 0) return null;
        var results = registrations.ToDictionary(job => StoredJobId(InboxKey.JobResult(actor, job.Key, 1, job.Generation)),
            job => job.Generation, StringComparer.Ordinal);
        var ids = results.Keys.ToArray();
        var sqlite = db.UsesSqliteRootRuntime;
        var keys = sqlite ? "\"Id\" IN (SELECT value FROM json_each({2}))" : "\"Id\" = ANY({2})";
        // Verify the stored result identity and kept reason without opening the consumed body.
        var rows = await db.Database.SqlQuery<ConsumedJobRow>(FormattableStringFactory.Create($"""
            SELECT "Id", "JobGeneration" FROM "Messages"
            WHERE "Target" = {"{0}"} AND "Partition" = {"{1}"} AND "InboxScope" = {RootTimedJobScope} AND "Kind" = '{Kinds.JobFinished}'
                AND "EventMatch" IS NULL AND "Done" AND "Due" = 0 AND "Start" IS NULL AND "Payload" IS NOT NULL
                AND "Sender" = "Target" AND "ReplyTo" IS NULL AND "KeepUntil" IS NULL
                AND "FoundationKeptBy" = {(short)ParkKeepReason.Consumed} AND {keys}
            """, actor, partition, sqlite ? Json.Write(ids) : ids)).ToArrayAsync(token);
        return rows.Length == ids.Length && rows.All(row => results[row.Id] == row.JobGeneration) ? ids : null;
    }
}
