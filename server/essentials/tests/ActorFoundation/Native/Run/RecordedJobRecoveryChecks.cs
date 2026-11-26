using Kernel.Durable;
using Kernel.EntityFramework;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Kernel.Native.Run;

internal static class RecordedJobRecoveryChecks
{
    internal static async Task AssertInstalledGuardsAsync(KernelDb db, EfStore.RootImage park, Action<bool, string> check,
        CancellationToken token)
    {
        var actor = park.Actor.Id;
        var partition = new InstanceRef(actor).Partition;
        var revision = checked(park.Revision + 1);
        var entries = Json.Write(new { Outcome = "recovered", Operation = "guard-probe", Restored = 0, Cleared = 1, AlreadyRearmed = 0 });
        await using (var write = await db.Database.BeginTransactionAsync(token))
        {
            await InsertRecoverAsync();
            await ExpectRefusalAsync(check, BindRunningAsync, "the installed checkpoint guard requires removal of the pinned consumed result");
            await write.RollbackAsync(token);
        }
        await using (var write = await db.Database.BeginTransactionAsync(token))
        {
            await RenameKeptResultAsync();
            await ExpectRefusalAsync(check, InsertRecoverAsync,
                "the installed journal guard requires the pinned result identity as well as the job generation");
            await write.RollbackAsync(token);
        }
        await using (var write = await db.Database.BeginTransactionAsync(token))
        {
            await InsertRecoverAsync();
            await RenameKeptResultAsync();
            await ExpectRefusalAsync(check, BindRunningAsync, "changing a result ID after journaling leaves a result for the same generation");
            await write.RollbackAsync(token);
        }

        async Task RenameKeptResultAsync()
        {
            var row = await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE "Messages" SET "Id" = 'other-recorded-result'
                WHERE "Target" = {actor} AND "Partition" = {partition} AND "InboxScope" = {EfStore.RootTimedJobScope}
                    AND "Kind" = {Kinds.JobFinished} AND "Done"
                """, token);
            if (row != 1) throw new InvalidOperationException("Expected one kept job result in the guard fixture.");
        }

        Task<int> BindRunningAsync() => BindWithoutRegistrationAsync(db, actor, partition, revision, token);
        Task<int> InsertRecoverAsync() => InsertRecoverEntryAsync(db, actor, partition, revision, entries, token);
    }

    internal static async Task AssertLiveJobGuardsAsync(KernelDb db, EfStore.RootImage park, bool published, Action<bool, string> check,
        CancellationToken token)
    {
        var actor = park.Actor.Id;
        var partition = new InstanceRef(actor).Partition;
        var revision = checked(park.Revision + 1);
        var entries = Json.Write(new { Outcome = "recovered", Operation = "guard-live", Restored = 0, Cleared = 1, AlreadyRearmed = 0 });
        await using (var write = await db.Database.BeginTransactionAsync(token))
        {
            await InsertRecoverAsync();
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE "Messages" SET "Seen" = 0 WHERE "Target" = {actor} AND "Partition" = {partition}
                    AND "InboxScope" = {EfStore.RootTimedJobScope} AND NOT "Done"
                """, token);
            await ExpectRefusalAsync(check, () => BindWithoutRegistrationAsync(db, actor, partition, revision, token),
                "the installed checkpoint guard requires the pending job registration to stay unchanged");
            await write.RollbackAsync(token);
        }
        var kind = published ? Kinds.JobFinished : Kinds.Job;
        await RefuseChangeAsync(() => db.Database.ExecuteSqlInterpolatedAsync($"""
            DELETE FROM "Messages" WHERE "Target" = {actor} AND "Partition" = {partition}
                AND "InboxScope" = {EfStore.RootTimedJobScope} AND "Kind" = {kind}
            """, token), "the installed guard retains the required live timed row");
        await RefuseChangeAsync(() => db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE "Messages" SET "Id" = 'different-pending-job'
            WHERE "Target" = {actor} AND "Partition" = {partition}
                AND "InboxScope" = {EfStore.RootTimedJobScope} AND "Kind" = {kind}
            """, token), "the installed guard preserves the exact live job storage identity");
        await RefuseChangeAsync(() => db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE "Messages" SET "Payload" = 'null'
            WHERE "Target" = {actor} AND "Partition" = {partition}
                AND "InboxScope" = {EfStore.RootTimedJobScope} AND "Kind" = {kind}
            """, token), "the installed guard preserves the pending timed envelope body");
        if (db.Database.IsSqlite())
            await RefuseChangeAsync(() => db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT OR REPLACE INTO "Messages" ("Id", "InboxScope", "Partition", "Target", "Kind", "Seq", "Due", "Seen",
                    "Attempts", "Trial", "Done", "JobGeneration")
                SELECT "Id", "InboxScope", "Partition", 'different-target', "Kind", "Seq", "Due", "Seen", "Attempts", "Trial", "Done", "JobGeneration"
                FROM "Messages" WHERE "Target" = {actor} AND "Partition" = {partition}
                    AND "InboxScope" = {EfStore.RootTimedJobScope} AND "Kind" = {kind}
                """, token), "a SQLite key conflict also preserves the live timed row");
        if (!published)
            await RefuseChangeAsync(() => db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE "Messages" SET "Due" = "Due" + 1 WHERE "Target" = {actor} AND "Partition" = {partition}
                    AND "InboxScope" = {EfStore.RootTimedJobScope} AND "Kind" = {Kinds.JobDeadline}
                """, token), "the installed guard preserves the original deadline after journaling Recover");

        Task<int> InsertRecoverAsync() => InsertRecoverEntryAsync(db, actor, partition, revision, entries, token);

        async Task RefuseChangeAsync(Func<Task<int>> update, string description)
        {
            await using var write = await db.Database.BeginTransactionAsync(token);
            await InsertRecoverAsync();
            await ExpectRefusalAsync(check, update, description);
            await write.RollbackAsync(token);
        }
    }

    private static Task<int> BindWithoutRegistrationAsync(KernelDb db, string actor, int partition, long revision, CancellationToken token) =>
        db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE "Instances" SET "Revision" = {revision}, "FoundationWriteRevision" = {revision},
                "Status" = {(int)InstanceStatus.Running}, "Fault" = NULL, "Jobs" = NULL,
                "FoundationParkAt" = NULL, "FoundationParkRevision" = NULL, "FoundationParkJobResult" = NULL, "FoundationParkLiveJob" = NULL,
                "FoundationParkGeneration" = "FoundationParkGeneration" + 1
            WHERE "Id" = {actor} AND "Partition" = {partition}
            """, token);

    private static Task<int> InsertRecoverEntryAsync(KernelDb db, string actor, int partition, long revision, string entries,
        CancellationToken token) => db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "Journal" ("Partition", "Position", "Instance", "Revision", "Entries")
            SELECT {partition}, coalesce(max("Position"), 0) + 1, {actor}, {revision}, {entries}
            FROM "Journal" WHERE "Partition" = {partition}
            """, token);

    private static async Task ExpectRefusalAsync(Action<bool, string> check, Func<Task<int>> action, string description)
    {
        try { await action(); }
        catch (PostgresException error) when (error.SqlState == "23514" &&
            (error.MessageText.StartsWith("Qualified actor", StringComparison.Ordinal) ||
                error.MessageText == "A recovered journal entry requires the parked checkpoint it resolves"))
        {
            check(true, description);
            return;
        }
        catch (SqliteException error) when (error.SqliteErrorCode == 19 && error.Message.Contains("Qualified actor", StringComparison.Ordinal))
        {
            check(true, description);
            return;
        }
        check(false, description);
    }

}
