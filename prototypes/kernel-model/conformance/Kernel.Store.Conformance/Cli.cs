namespace Kernel.Store.Conformance;

/// <summary>
/// The suite's command line, for a provider's own runner:
/// <c>semantic [name filter]</c>, <c>stress [seconds] [messages]</c>, <c>stress-broken [seconds] [messages]</c>, <c>cost</c>.
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
            case ["stress" or "stress-broken", .. var rest]:
                var options = new StressOptions { BrokenFence = args[0] == "stress-broken" };
                if (rest.Length > 0) options = options with { Seconds = int.Parse(rest[0]) };
                if (rest.Length > 1) options = options with { Messages = int.Parse(rest[1]) };
                if (rest.Length > 2) options = options with { FaultRate = double.Parse(rest[2], System.Globalization.CultureInfo.InvariantCulture) };
                Console.WriteLine($"{provider.Name}: stress {options.Seconds} s, {options.Messages} messages, fault rate {options.FaultRate}{(options.BrokenFence ? ", FENCE BROKEN ON PURPOSE" : "")}");
                return await Stress.RunAsync(provider, options);
            case ["cost"]:
                return await Cost.RunAsync(provider);
            default:
                Console.Error.WriteLine("semantic [filter] | semantic-broken [filter] | stress [seconds] [messages] [fault rate] | stress-broken ... | cost");
                return 2;
        }
    }
}
