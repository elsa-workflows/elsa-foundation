using Kernel.Sim;

namespace Kernel.Store.Conformance;

/// <summary>
/// The reference runs of FINDINGS section 5, on any provider that counts its round trips. Polls are not counted for
/// runs started on the owner: a host polls on its own schedule whatever the load. The owner polls once before the
/// runs, so it is caught up and hands over in memory.
/// </summary>
public static class Cost
{
    public static async Task<int> RunAsync(IStoreProvider provider, TextWriter? output = null)
    {
        output ??= Console.Out;
        await using var provided = await provider.CreateAsync(2, new StoreSetup(Counting: true));
        var counter = provided.Counter ?? throw new InvalidOperationException($"{provider.Name} does not count its round trips");
        var store = provided.Store;
        var owner = new Worker("owner", 0, store, new WorkflowInstance(Stress.Models()));
        await new DurableHost([owner]).ActivateAsync(owner.Partition);
        await owner.PollAsync();
        var n = 0;
        var keep = (await store.PollAsync(0, 0, TimeSpan.Zero, 1)).Now + 3_600_000;
        Message Start(string mode, int count = 10) => new($"m{++n}", InstanceRef.In(0, $"{mode}{n}"), Kinds.Start, Start: Sequence.Spec(count, mode));

        output.WriteLine($"{"run",-56} {"round trips",11} {"commits",7}  what");
        async Task Row(string name, Func<Task> body)
        {
            counter.Reset();
            await body();
            var reading = counter.Read();
            output.WriteLine($"{name,-56} {reading.RoundTrips,11} {reading.Commits,7}  {reading.Detail}");
        }

        foreach (var mode in new[] { Sequence.Ephemeral, Sequence.Balanced, Sequence.Durable })
            await Row($"ten no-ops, {mode}, started on the owner", async () =>
            {
                owner.Post(Start(mode), fresh: true);
                await Drain(owner, poll: false);
            });
        foreach (var mode in new[] { Sequence.Ephemeral, Sequence.Balanced, Sequence.Durable })
            await Row($"ten no-ops, {mode}, started from another node", async () =>
            {
                await store.AddAsync(Start(mode) with { KeepUntil = keep });    // the other node's insert is counted too
                await Drain(owner, poll: true);
            });
        await Row("parent with three children, same partition", async () =>
        {
            owner.Post(new Message($"m{++n}", InstanceRef.In(0, $"parent{n}"), Kinds.Start, Start: new StartSpec("parent", "p", Json.Write(new ParentInput(3, 0)))), fresh: true);
            await Drain(owner, poll: false);
        });
        await Row("100 balanced instances started at once", async () =>
        {
            for (var i = 0; i < 100; i++) owner.Post(Start(Sequence.Balanced), fresh: true);
            await Drain(owner, poll: false);
        });

        var journal = await store.ReadJournalAsync(0, 0, 100_000);
        var rows = await store.LoadAsync(journal.Select(j => j.Instance).Distinct().ToArray());
        var left = (await store.PollAsync(0, long.MaxValue, TimeSpan.FromDays(1), 10_000)).Messages.Count;
        var unfinished = rows.Count(r => r.Status != InstanceStatus.Finished);
        output.WriteLine($"check: {rows.Count} instances written, {unfinished} not finished, {left} messages left, {journal.Count} journal rows");
        return unfinished == 0 && left == 0 ? 0 : 1;
    }

    /// <summary>A host that runs passes of one scope until two in a row find nothing to do.</summary>
    public static async Task Drain(IScopeActivation scope, bool poll)
    {
        if (poll) await scope.WokenAsync(null);      // the first pass reads the inbox
        for (var idle = 0; idle < 2;) idle = await scope.RunAsync() == TimeSpan.Zero ? 0 : idle + 1;
    }
}
