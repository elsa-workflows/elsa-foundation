namespace Kernel.Sim;

internal static class BacklogWorkerChecks
{
    internal static async Task Run(Action<bool, string> check)
    {
        await Publication(check, ordinaryCommit: false);
        await Publication(check, ordinaryCommit: true);
    }

    private static async Task Publication(Action<bool, string> check, bool ordinaryCommit)
    {
        const long start = 1_700_000_000_000;
        var elapsed = 0L;
        var store = new InMemoryStore(2, () => start + elapsed);
        var receipts = new List<CommitBatch>();
        var wire = new CountingStore(store, () => { }, observeCommit: (batch, landed) => { if (landed) receipts.Add(batch); });
        var type = new WorkflowInstance([]);
        var options = new WorkerOptions
        {
            Monotonic = () => elapsed,
            Backlog = new(4, 2, TimeSpan.FromMilliseconds(5), TimeSpan.FromMilliseconds(10)),
            Partitions = 2
        };
        var host = DurableHost.ForNode("backlog-worker", wire, type, 2, options);
        await using var owner = (await host.ActivateAsync(0))!;
        await owner.WokenAsync(null);
        await owner.RunAsync();

        for (var i = 0; i < 6; i++)
            await store.AddAsync(new Message($"input-{i}", InstanceRef.In(0, $"missing-{i}"), "work"));
        await store.AddAsync(new Message("other", InstanceRef.In(1, "unowned"), "work"));

        elapsed = 5;
        if (ordinaryCommit) await owner.WokenAsync(null);
        await owner.RunAsync();
        if (ordinaryCommit)
        {
            check(receipts is [var hitchhike] && hitchhike.Seen.Count > 0 && hitchhike.DueBacklog?.GetValueOrDefault(0) == 5 &&
                  store.Partitions[0].DueBacklog == 5 && store.Partitions[1].DueBacklog == 0,
                "an observed depth estimate rides the next ordinary fenced commit");
            return;
        }

        check(receipts.Count == 0 && store.Partitions[0].DueBacklog == 0,
            "a sampled crossing waits while its partition has been idle for less than one heartbeat");
        elapsed = 10;
        await owner.RunAsync();
        check(receipts is [var pressure] && pressure.Fenced is [{ Partition: 0 }] &&
              pressure.Turns.Count == 0 && pressure.Seen.Count == 0 && pressure.Trial.Count == 0 &&
              pressure.DueBacklog?.GetValueOrDefault(0) == 5 && store.Partitions[0].DueBacklog == 5 &&
              store.Partitions[1].DueBacklog == 0 && store.Partitions[0].Position == 0,
            "at one heartbeat an idle owned partition publishes a capped high crossing without a turn or a journal row");

        await owner.DisposeAsync();
        var otherHost = DurableHost.ForNode("backlog-successor", wire, type, 2, options);
        await using var successor = (await otherHost.ActivateAsync(0))!;
        var inherited = receipts.Count;
        await successor.RunAsync();
        check(receipts.Count == inherited && store.Partitions[0].DueBacklog == 5,
            "a replacement owner keeps a published band and writes no duplicate estimate");

        elapsed = 15;
        for (var i = 0; i < 8; i++)
        {
            await successor.WokenAsync(null);
            await successor.RunAsync();
        }
        check(await ((IDueBacklogStore)store).CountDueBacklogAsync(0, 10) == 0 &&
              store.Partitions[0].DueBacklog == 5 && store.Partitions[0].Position > 0,
            "pending inputs drain even while the published estimate is above high water");

        elapsed = 20;
        var before = receipts.Count;
        await successor.RunAsync();
        check(receipts.Count == before && store.Partitions[0].DueBacklog == 5,
            "a low crossing waits until one heartbeat from the last ordinary commit");
        elapsed = 25;
        await successor.RunAsync();
        check(receipts.Count == before + 1 && receipts[^1].DueBacklog?.GetValueOrDefault(0) == 0 &&
              receipts[^1].Positions == 0 && store.ReadBacklogPressure(2).Count == 0,
            "the idle owner publishes the low crossing and removes the partition from the pressure list");
        elapsed = 50;
        await successor.RunAsync();
        check(receipts.Count == before + 1,
            "a sample in the same pressure band writes no idle commit");
    }
}
