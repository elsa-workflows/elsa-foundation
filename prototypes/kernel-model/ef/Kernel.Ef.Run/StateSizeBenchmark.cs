using Kernel.Durable;

internal static class StateSizeBenchmark
{
    public const int Turns = 10;

    public static int[] ParseSizes(string text) => text.Split(',').Select(value =>
    {
        if (!int.TryParse(value, out var kb) || kb <= 0)
            throw new ArgumentException("sizes must be comma-separated positive integer kilobytes");
        try { return checked(kb * 1024); }
        catch (OverflowException) { throw new ArgumentException("a sizes value overflows its byte count"); }
    }).ToArray();

    public static async Task<int> TrialAsync(Worker owner, Tally store, Message start, int bytes,
        Func<Task> drain, Action measured)
    {
        var commits = store.Sum("commit landed");
        var units = store.Units;
        if (!owner.Post(start, fresh: true))
            throw new InvalidOperationException($"size trial {start.Target.Id}: owner declined start");
        await drain();
        measured();                    // stop the clock and capture counters before verification reads
        var landed = store.Sum("commit landed") - commits;
        var written = store.Units - units;
        var row = (await store.LoadAsync([start.Target.Id])).SingleOrDefault();
        if (landed != Turns || written != Turns || row is not { Revision: Turns, Status: InstanceStatus.Finished }
            || Json.Read<BlobState>(row.State) is not { Left: 0 } state || state.Data.Length != bytes)
            throw new InvalidOperationException($"size trial {start.Target.Id}: expected {Turns} committed turns and a finished revision {Turns}; got {landed} commits, {written} rows, revision {row?.Revision}, status {row?.Status}");
        if (row.History != History.Minimal)
        {
            var journal = (await store.ReadJournalAsync(start.Target.Partition, 0, int.MaxValue))
                .Where(j => j.Instance == row.Id).Select(j => j.Revision);
            if (!journal.SequenceEqual(Enumerable.Range(1, Turns).Select(i => (long)i)))
                throw new InvalidOperationException($"size trial {row.Id}: persisted journal does not contain revisions 1 through {Turns}");
        }
        return row.State.Length;      // the measured payload includes any configured Leveled pad
    }
}
