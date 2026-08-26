using Elsa.Groundwork.StorePerformance.Benchmarks.Harness;

namespace Elsa.Groundwork.StorePerformance.AdapterHost;

/// <summary>
/// Deliberately mirrors the option grammar of the matrix runner rather than inventing a second one: the
/// operator types the same flag names into <c>capture-plan</c> and <c>matrix</c>, and a value that differs
/// between the two is the single most likely cause of a fail-closed correctness rejection.
/// </summary>
internal static class HostArguments
{
    public static string? Option(string[] args, string name)
    {
        for (var index = 0; index < args.Length - 1; index++)
            if (string.Equals(args[index], name, StringComparison.Ordinal))
                return args[index + 1];
        return null;
    }

    public static IEnumerable<string> Options(string[] args, string name)
    {
        for (var index = 0; index < args.Length - 1; index++)
            if (string.Equals(args[index], name, StringComparison.Ordinal))
                yield return args[index + 1];
    }

    public static string Require(string[] args, string command, string name) =>
        Option(args, name) ?? throw new PerformanceContractException($"{command} requires {name}.");

    /// <summary>
    /// Parses repeated <c>--provider-setting name=value</c> pairs. The values become
    /// <c>RunRequest.ProviderConfiguration</c>, which <c>ArtifactSafety</c> screens for connection
    /// material — so a rejected key here is a contract violation, not a typo to work around.
    /// </summary>
    public static IReadOnlyDictionary<string, string> Settings(string[] args, string name)
    {
        var settings = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in Options(args, name).Select(entry => Pair(entry, name)))
        {
            // Rejected rather than last-one-wins: the observed configuration must match the staged
            // document's entry for entry, so a silently dropped setting surfaces much later as an opaque
            // correctness rejection instead of as the operator typo it is.
            if (!settings.TryAdd(key, value))
                throw new PerformanceContractException($"{name} '{key}' was supplied more than once.");
        }
        return settings;
    }

    private static KeyValuePair<string, string> Pair(string value, string option)
    {
        var separator = value.IndexOf('=', StringComparison.Ordinal);
        return separator > 0 && separator < value.Length - 1
            ? new KeyValuePair<string, string>(value[..separator], value[(separator + 1)..])
            : throw new PerformanceContractException($"{option} must be name=value and must be repeated for every entry.");
    }
}
