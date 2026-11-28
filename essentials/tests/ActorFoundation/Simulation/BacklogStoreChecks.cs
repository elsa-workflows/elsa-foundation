namespace Kernel.Sim;

internal static class BacklogStoreChecks
{
    internal static async Task Run(Action<bool, string> check)
    {
        const long start = 1_701_700_000_000;
        var now = start;
        var store = new InMemoryStore(2, () => now);
        var backlog = (IDueBacklogStore)store;
        var left = InstanceRef.In(0, "backlog-left");
        var right = InstanceRef.In(1, "backlog-right");
        var kept = new Message("old-command", left, "work", "data", KeepUntil: start + 1_000);
        await store.AddAsync(kept);
        await store.AddAsync(new Message("no-deadline", left, "work"));
        await store.AddAsync(new Message("due-now", left, "timer", Due: start));
        await store.AddAsync(new Message("due-later", left, "timer", Due: start + 200));
        await store.AddAsync(new Message("other-partition", right, "work"));

        var l = (await store.TakeAsync(0, "first", Defaults.Lease))!;
        var r = (await store.TakeAsync(1, "first", Defaults.Lease))!;
        var leftFence = new PartitionFence(0, l.Epoch, l.Position);
        var rightFence = new PartitionFence(1, r.Epoch, r.Position);
        var settle = new TurnCommit(left.Id, null, null, [], [kept.InboxKey], [], [], [], null);
        var initial = await store.CommitAsync(CommitBatch.Owned([leftFence, rightFence], Defaults.Lease, false, [settle], [], [])
            with { DueBacklog = new Dictionary<int, int> { [0] = 2, [1] = 1 } });
        check(initial.Landed && store.Messages[kept.InboxKey].Done &&
              await backlog.CountDueBacklogAsync(0, 10) == 2 &&
              await backlog.CountDueBacklogAsync(0, 1) == 1 &&
              await backlog.CountDueBacklogAsync(1, 10) == 1,
            "the capped due-depth count excludes Done and future rows and stays within its partition");
        check(store.ReadBacklogPressure(2) is { Count: 1 } pressure && pressure.GetValueOrDefault(0) == 2 &&
              store.Journal.Count == 0 && store.Partitions.All(partition => partition.Position == 0),
            "a fenced backlog estimate marks only its partition and advances no journal");
        now = start + 200;
        check(await backlog.CountDueBacklogAsync(0, 10) == 3,
            "the store includes a timer at its due boundary");

        var unsupported = false;
        try { StoreBatchRules.Validate(Estimate(leftFence, 0, 9)); }
        catch (StoreException error) when (error.Failure == Failure.Definite) { unsupported = true; }
        check(unsupported && await Refused(Estimate(leftFence, 1, 9)) && await Refused(Estimate(leftFence, 0, -1)) &&
              store.Partitions[0].DueBacklog == 2 && store.Partitions[1].DueBacklog == 1,
            "unsupported, unfenced and negative estimates are refused without a write");

        await store.ReleaseAsync(0, l.Epoch);
        var take = (await store.TakeAsync(0, "successor", Defaults.Lease))!;
        var replacement = new PartitionFence(0, take.Epoch, take.Position);
        var stale = await store.CommitAsync(CommitBatch.Owned([leftFence, rightFence], Defaults.Lease, false, [], [], [])
            with { DueBacklog = new Dictionary<int, int> { [0] = 7, [1] = 8 } });
        var wrongPosition = await store.CommitAsync(Estimate(replacement with { Position = take.Position + 1 }, 0, 6));
        check(!stale.Landed && !wrongPosition.Landed && store.Partitions[0].DueBacklog == 2 && store.Partitions[1].DueBacklog == 1,
            "a stale member of an owned batch or a wrong position leaves every estimate unchanged");

        var publish = await store.CommitAsync(Estimate(replacement, 0, 3));
        check(publish.Landed && store.ReadBacklogPressure(3).GetValueOrDefault(0) == 3 &&
              store.Partitions[1].DueBacklog == 1 && store.Partitions[0].Position == take.Position,
            "a replacement owner publishes pressure without a journal row and leaves the other scope alone");

        CommitBatch Estimate(PartitionFence fence, int partition, int depth) =>
            CommitBatch.Owned([fence], Defaults.Lease, false, [], [], [])
                with { DueBacklog = new Dictionary<int, int> { [partition] = depth } };

        async Task<bool> Refused(CommitBatch batch)
        {
            try { await store.CommitAsync(batch); return false; }
            catch (StoreException error) when (error.Failure == Failure.Definite) { return true; }
        }
    }
}
