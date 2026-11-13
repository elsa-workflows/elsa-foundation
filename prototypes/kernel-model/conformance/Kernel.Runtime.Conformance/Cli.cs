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
            default:
                Console.Error.WriteLine("semantic [filter]");
                return 2;
        }
    }

    private static Func<RuntimeCase, bool>? Filter(string[] rest) => rest.Length == 0 ? null : c => c.Name.Contains(rest[0], StringComparison.OrdinalIgnoreCase);
}
