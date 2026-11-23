using System.Diagnostics;
using System.Globalization;
using Kernel.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace Kernel.Native.Run;

// Test-only observer on the dedicated preview database. The row written by the trigger commits with the ownership change.
internal static class PostgresClusterExit
{
    private sealed record LeaseSample(string? Owner, long Epoch, long LeaseUntil, long Observed);
    private sealed record Change(string? PreviousOwner, long PreviousEpoch, string? NextOwner, long NextEpoch,
        bool Died, long LeaseUntil, long StatementAt, long TriggerAt);

    internal static async Task ObserveAsync(Func<KernelDb> open, int partition, string peer, long epoch, string survivor,
        Process child, CancellationToken stop)
    {
        await using var db = open();
        await db.Database.ExecuteSqlRawAsync($"""
            CREATE TABLE preview_exit_changes ("PreviousOwner" text, "PreviousEpoch" bigint, "NextOwner" text,
                "NextEpoch" bigint, "Died" boolean, "LeaseUntil" bigint, "StatementAt" bigint, "TriggerAt" bigint);
            CREATE FUNCTION preview_record_exit() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
                INSERT INTO preview_exit_changes VALUES (OLD."Owner", OLD."Epoch", NEW."Owner", NEW."Epoch", NEW."Died",
                    (SELECT "LeaseUntil" FROM "Nodes" WHERE "Id" = OLD."Owner"),
                    floor(extract(epoch from statement_timestamp()) * 1000)::bigint,
                    floor(extract(epoch from clock_timestamp()) * 1000)::bigint);
                RETURN NEW;
            END $$;
            CREATE TRIGGER preview_record_exit AFTER UPDATE OF "Owner" ON "Partitions"
                FOR EACH ROW WHEN (OLD."Id" = {partition.ToString(CultureInfo.InvariantCulture)} AND OLD."Owner" IS DISTINCT FROM NEW."Owner")
                EXECUTE FUNCTION preview_record_exit();
            """, stop);
        try
        {
            child.Kill();
            await child.WaitForExitAsync(stop);
            if (child.ExitCode == 0) throw new InvalidOperationException("Expected the fixture peer to exit abruptly.");
            var left = await db.Database.SqlQuery<LeaseSample>($"""
                SELECT p."Owner", p."Epoch", n."LeaseUntil",
                    floor(extract(epoch from statement_timestamp()) * 1000)::bigint AS "Observed"
                FROM "Partitions" p JOIN "Nodes" n ON n."Id" = p."Owner" WHERE p."Id" = {partition}
                """).SingleOrDefaultAsync(stop);
            if (left is null || left.Owner != peer || left.Epoch != epoch || left.LeaseUntil <= left.Observed)
                throw new InvalidOperationException("The exited peer must still own its partition under a live lease.");

            // The time bound includes the remaining live interval, one lease and two membership windows.
            var window = TimeSpan.FromMilliseconds(left.LeaseUntil - left.Observed) + Defaults.Lease + TimeSpan.FromSeconds(4);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stop);
            deadline.CancelAfter(window);
            List<Change> changes;
            do
            {
                changes = await db.Database.SqlQueryRaw<Change>("SELECT * FROM preview_exit_changes").ToListAsync(deadline.Token);
                if (changes.Count == 0) await Task.Delay(60, deadline.Token);
            } while (changes.Count == 0);
            if (changes is not [var change] || change.PreviousOwner != peer || change.PreviousEpoch != epoch ||
                change.NextOwner != survivor || change.NextEpoch != epoch + 1 || !change.Died ||
                change.LeaseUntil < left.LeaseUntil || change.StatementAt <= change.LeaseUntil)
                throw new InvalidOperationException("Expected one direct takeover from the exited peer after its database lease expired.");
            Console.WriteLine($"ok peer exit {child.Id}, partition {partition}, live lease after exit {left.LeaseUntil - left.Observed} ms, owner update started {change.StatementAt - change.LeaseUntil} ms after expiry, row trigger ran {change.TriggerAt - change.LeaseUntil} ms after expiry, epochs {epoch}->{change.NextEpoch}");
        }
        finally
        {
            await db.Database.ExecuteSqlRawAsync("""
                DROP TRIGGER preview_record_exit ON "Partitions";
                DROP FUNCTION preview_record_exit();
                DROP TABLE preview_exit_changes;
                """, CancellationToken.None);
        }
    }
}
