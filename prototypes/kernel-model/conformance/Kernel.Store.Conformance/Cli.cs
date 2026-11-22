namespace Kernel.Store.Conformance;

/// <summary>
/// The suite's command line, for a provider's own runner:
/// <c>semantic [name filter]</c>, <c>semantic-flaw Flaw filter [expected failure]</c>, <c>stress [seconds] [messages]</c>, <c>stress-broken [seconds] [messages]</c>, <c>cost</c>.
/// </summary>
public static class Cli
{
    public static async Task<int> RunAsync(IStoreProvider provider, string[] args)
    {
        switch (args)
        {
            case ["semantic", .. var rest]:
                var outcomes = await Semantics.RunAsync(provider, filter: rest.Length == 0 ? null : c => c.Name.Contains(rest[0], StringComparison.OrdinalIgnoreCase));
                return outcomes.Any(o => !o.Passed && !o.Skipped && o.Case.Level == Level.Required) ? 1 : 0;
            case ["semantic-broken", .. var rest]:
                // The semantic tests with the fence broken on purpose: the fence's cases must fail.
                var broken = await Semantics.RunAsync(provider, new StoreSetup(BrokenFence: true), rest.Length == 0 ? null : c => c.Name.Contains(rest[0], StringComparison.OrdinalIgnoreCase));
                return broken.Any(o => !o.Passed && !o.Skipped && o.Case.Level == Level.Required) ? 0 : 1;
            case ["semantic-flaw", var name, var filter, .. var rest]:
                // K1 slice 7: a store defect switched on (the in-memory store's). Every case the filter matches must fail,
                // and, given an expected text, each with it in its detail: a case that fails on something else is no catch.
                var flawed = await Semantics.RunAsync(provider, new StoreSetup(Flaws: Enum.Parse<Flaw>(name)), c => c.Name.Contains(filter, StringComparison.OrdinalIgnoreCase));
                var expected = rest.Length > 0 ? rest[0] : null;
                var caught = flawed.Count(o => !o.Passed && !o.Skipped && (expected is null || o.Detail.Contains(expected, StringComparison.Ordinal)));
                Console.WriteLine($"{name}: {caught} of {flawed.Count} cases matching \"{filter}\" failed{(expected is null ? "" : $" with \"{expected}\"")}; every one must");
                return flawed.Count > 0 && caught == flawed.Count ? 0 : 1;
            case ["stress" or "stress-broken", .. var rest]:
                var options = new StressOptions { BrokenFence = args[0] == "stress-broken" };
                if (rest.Length > 0) options = options with { Seconds = int.Parse(rest[0]) };
                if (rest.Length > 1) options = options with { Messages = int.Parse(rest[1]) };
                if (rest.Length > 2) options = options with { FaultRate = double.Parse(rest[2], System.Globalization.CultureInfo.InvariantCulture) };
                if (rest.Length > 3 && rest[3].Split('-') is [var min, var max]) options = options with { KillEvery = (int.Parse(min), int.Parse(max)) };
                Console.WriteLine($"{provider.Name}: stress {options.Seconds} s, {options.Messages} messages, fault rate {options.FaultRate}, a kill every {options.KillEvery.Min}-{options.KillEvery.Max} ms{(options.BrokenFence ? ", FENCE BROKEN ON PURPOSE" : "")}");
                return await Stress.RunAsync(provider, options);
            case ["cost"]:
                return await Cost.RunAsync(provider);
            default:
                Console.Error.WriteLine("semantic [filter] | semantic-broken [filter] | stress [seconds] [messages] [fault rate] [kill every min-max ms] | stress-broken ... | cost");
                return 2;
        }
    }
}
