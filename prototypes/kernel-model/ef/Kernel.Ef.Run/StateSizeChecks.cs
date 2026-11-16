using Kernel.Durable;
using Kernel.Workflows;

internal static class StateSizeChecks
{
    public static async Task<int> RunAsync()
    {
        var failures = 0;
        var checks = 0;
        async Task Check(string name, Func<Task> check)
        {
            checks++;
            try { await check(); Console.WriteLine("pass  " + name); }
            catch (Exception error) { failures++; Console.WriteLine($"FAIL  {name}: {error.Message}"); }
        }
        static void Require(bool condition, string why)
        {
            if (!condition) throw new InvalidOperationException(why);
        }
        async Task Trial(bool activate, int turns, bool drain, bool rejected, History history = History.Full,
            bool removeJournal = false, int pad = 0, History? level = null, History? corruptHistory = null,
            bool extraJournal = false, bool corruptEntries = false)
        {
            var memory = new InMemoryStore(1, () => 0);
            var store = new Tally(memory);
            var shape = new Leveled(level, pad);
            var type = shape.Over(new WorkflowInstance([new Blob()]));
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
                chars = await StateSizeBenchmark.TrialAsync(owner, store, start, 1024, shape.Level ?? start.Start!.History, Drain, () =>
                {
                    measured = true;
                    if (removeJournal) memory.Journal.Remove((0, 1));
                    if (corruptHistory is { } wrong)
                    {
                        memory.Instances["0/blob"] = memory.Instances["0/blob"] with { History = wrong };
                        memory.Journal.Clear();
                        if (wrong != History.Minimal)
                            for (var i = 1; i <= 10; i++) memory.Journal[(0, i)] = new(0, i, "0/blob", i, "[]");
                    }
                    if (extraJournal) memory.Journal[(0, 1)] = new(0, 1, "0/blob", 1, "[]");
                    if (corruptEntries) memory.Journal[(0, 1)] = memory.Journal[(0, 1)] with { Entries = "[\"unexpected\"]" };
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
        await Check("positive trial: State history", () => Trial(true, 10, true, false, History.State));
        await Check("positive trial: target overrides Full with Minimal", () => Trial(true, 10, true, false, level: History.Minimal));
        await Check("positive trial: target overrides Minimal with Full", () => Trial(true, 10, true, false, History.Minimal, level: History.Full));
        await Check("positive trial: target overrides Full with State", () => Trial(true, 10, true, false, level: History.State));
        await Check("negative control: requested Full replaced by Minimal and no journal", () => Trial(true, 10, true, true, level: History.Full, corruptHistory: History.Minimal));
        await Check("negative control: requested Minimal replaced by Full and ten journal rows", () => Trial(true, 10, true, true, History.Minimal, level: History.Minimal, corruptHistory: History.Full));
        await Check("negative control: Minimal has an unexpected journal row", () => Trial(true, 10, true, true, History.Minimal, extraJournal: true));
        await Check("negative control: State is missing a journal revision", () => Trial(true, 10, true, true, History.State, removeJournal: true));
        await Check("negative control: Full has unexpected journal content", () => Trial(true, 10, true, true, corruptEntries: true));
        await Check("negative control: State has unexpected journal content", () => Trial(true, 10, true, true, History.State, corruptEntries: true));
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
        Console.WriteLine($"size checks: {checks - failures} passed, {failures} failed");
        return failures == 0 ? 0 : 1;
    }
}
