using Kernel.Durable;
using Kernel.Workflows;

internal static class StateSizeChecks
{
    public static async Task<int> RunAsync()
    {
        var failures = 0;
        async Task Check(string name, Func<Task> check)
        {
            try { await check(); Console.WriteLine("pass  " + name); }
            catch (Exception error) { failures++; Console.WriteLine($"FAIL  {name}: {error.Message}"); }
        }
        static void Require(bool condition, string why)
        {
            if (!condition) throw new InvalidOperationException(why);
        }
        async Task Trial(bool activate, int turns, bool drain, bool rejected, History history = History.Full, bool removeJournal = false, int pad = 0)
        {
            var memory = new InMemoryStore(1, () => 0);
            var store = new Tally(memory);
            var type = new Leveled(null, pad).Over(new WorkflowInstance([new Blob()]));
            var owner = new Worker("size-check", 0, store, type);
            if (activate)
            {
                await new DurableHost([owner]).ActivateAsync(0);
                await owner.PollAsync();
            }
            var measured = false;
            var drained = false;
            var start = new Message("start", InstanceRef.In(0, "blob"), Kinds.Start, Start: Blob.Spec(1024, turns) with { History = history });
            async Task Drain()
            {
                drained = true;
                if (drain)
                    for (var idle = 0; idle < 2;) idle = await owner.RunAsync() == TimeSpan.Zero ? 0 : idle + 1;
            }
            var refused = false;
            var chars = 0;
            try
            {
                chars = await StateSizeBenchmark.TrialAsync(owner, store, start, 1024, Drain, () =>
                {
                    measured = true;
                    if (removeJournal) memory.Journal.Remove((0, 1));
                });
            }
            catch (InvalidOperationException) { refused = true; }
            Require(refused == rejected, rejected ? "invalid trial was accepted" : "valid trial was refused");
            if (!activate) Require(!drained && !measured, "a declined Post reached the measured body");
            if (activate && rejected) Require(measured, "the persisted-outcome negative control never reached verification");
            if (!rejected) Require(measured && memory.Instances["0/blob"].Revision == 10, $"ten real commits were not measured: revision {memory.Instances.GetValueOrDefault("0/blob")?.Revision}, landed {store.Sum("commit landed")}");
            if (!rejected) Require(chars == memory.Instances["0/blob"].State.Length,
                $"reported {chars} chars differ from the persisted state length {memory.Instances["0/blob"].State.Length}");
        }
        await Check("positive trial: ten persisted commits", () => Trial(true, 10, true, false));
        await Check("negative control: owner declines Post", () => Trial(false, 10, true, true));
        await Check("negative control: accepted Post with omitted commits", () => Trial(true, 10, false, true));
        await Check("negative control: only nine commits", () => Trial(true, 9, true, true));
        await Check("negative control: missing persisted journal revision", () => Trial(true, 10, true, true, removeJournal: true));
        await Check("positive trial: Minimal history still proves ten commits", () => Trial(true, 10, true, false, History.Minimal));
        await Check("positive trial: persisted padded state size", () => Trial(true, 10, true, false, pad: 1024));
        await Check("positive trial: Minimal padded state size", () => Trial(true, 10, true, false, History.Minimal, pad: 1024));
        await Check("CLI sizes: positive kilobytes, checked bytes", () =>
        {
            Require(StateSizeBenchmark.ParseSizes("1,10,100,1000").SequenceEqual(new[] { 1024, 10240, 102400, 1024000 }), "valid sizes changed");
            foreach (var invalid in new[] { "0", "-1", "4194305", "1,0", "1,-1", "1,4194305", "", "no" })
            {
                var refused = false;
                try { StateSizeBenchmark.ParseSizes(invalid); }
                catch (Exception error) when (error is ArgumentException or FormatException or OverflowException) { refused = true; }
                Require(refused, $"invalid size '{invalid}' was accepted");
            }
            return Task.CompletedTask;
        });
        Console.WriteLine($"size checks: {9 - failures} passed, {failures} failed");
        return failures == 0 ? 0 : 1;
    }
}
