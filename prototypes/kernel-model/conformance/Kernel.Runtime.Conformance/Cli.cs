using System.Globalization;

namespace Kernel.Runtime.Conformance;

/// <summary>
/// The suite's command line, for a runtime's own runner:
/// <c>semantic [filter]</c>, <c>semantic-broken [filter]</c>, <c>stress [seconds] [messages]</c>, <c>stress-broken [seconds] [messages]</c>.
/// The <c>-broken</c> forms break the store's fence on purpose: the fence's cases and the stress test must then fail,
/// which shows that the runtime really did activate a scope twice at once.
/// </summary>
public static class Cli
{
    public static async Task<int> RunAsync(IRuntimeProvider runtime, IStoreProvider store, string[] args)
    {
        switch (args)
        {
            case ["semantic", .. var rest]:
                var outcomes = await Cases.RunAsync(runtime, store, filter: Filter(rest));
                if (outcomes.Count == 0) Console.Error.WriteLine("no case matches the filter");
                return outcomes.Count == 0 || outcomes.Any(o => !o.Passed && o.Case.Level == Level.Required) ? 1 : 0;
            case ["semantic-broken", .. var rest]:
                var broken = await Cases.RunAsync(runtime, store, brokenFence: true, filter: c => c.Fence && (Filter(rest)?.Invoke(c) ?? true));
                // Each fence case must fail on what the fence guards: a precondition that did not hold, or a timeout, is no catch.
                var caught = broken.Count(o => o.Caught);
                Console.WriteLine($"fence broken on purpose: {caught} of {broken.Count} fence cases failed on the fence, as they must");
                return broken.Count > 0 && caught == broken.Count ? 0 : 1;
            case ["stress" or "stress-broken", .. var rest]:
                var options = new RuntimeStressOptions { BrokenFence = args[0] == "stress-broken" };
                if (rest.Length > 0) options = options with { Seconds = int.Parse(rest[0], CultureInfo.InvariantCulture) };
                if (rest.Length > 1) options = options with { Messages = int.Parse(rest[1], CultureInfo.InvariantCulture) };
                Console.WriteLine($"{runtime.Name} on {store.Name}: stress {options.Seconds} s, {options.Messages} messages, {options.NodesPerSide} nodes on each of two sides, " +
                    $"lease {options.Lease.TotalMilliseconds} ms, fault rate {options.FaultRate}{(options.BrokenFence ? ", FENCE BROKEN ON PURPOSE" : "")}");
                var result = await RuntimeStress.RunAsync(runtime, store, options);
                // A run that never held two activations at once, or whose fence never refused one, tested nothing.
                if (!result.Exercised) return 1;
                return options.BrokenFence ? result.Broken > 0 ? 0 : 1 : result.Violations == 0 ? 0 : 1;
            default:
                Console.Error.WriteLine("semantic [filter] | semantic-broken [filter] | stress [seconds] [messages] | stress-broken [seconds] [messages]");
                return 2;
        }
    }

    private static Func<RuntimeCase, bool>? Filter(string[] rest) => rest.Length == 0 ? null : c => c.Name.Contains(rest[0], StringComparison.OrdinalIgnoreCase);
}
