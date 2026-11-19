using Elsa.Expressions.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// The expression value adapter versions an executable node is pinned to (ADR 0046 V12, spec 1228 FR-028). A node that
/// uses a language with a registered <see cref="ExpressionValueAdapter"/>, through an input expression or a script body
/// (<see cref="ScriptActivityLanguage"/>), carries <c>runtime.valueAdapter.&lt;Language&gt;</c> in its metadata, which is
/// part of the executable's hashed content. A published executable keeps the version it was published with.
/// </summary>
public static class ExecutableNodeValueAdapters
{
    /// <summary>The node was published with no pin, or another version, for an adapter this host runs.</summary>
    public const string MismatchCode = "VF-ADPT-001";

    public const string KeyPrefix = "runtime.valueAdapter.";

    public static string Key(string language) => KeyPrefix + language;

    /// <summary>The adapters a node needs: each registered language its input expressions or its script body use.</summary>
    public static IReadOnlyList<ExpressionValueAdapter> Required(
        string activityType,
        IEnumerable<RuntimeInputBinding> inputBindings,
        IEnumerable<ExpressionValueAdapter> adapters,
        IEnumerable<ScriptActivityLanguage> scriptActivities)
    {
        ArgumentNullException.ThrowIfNull(inputBindings);
        ArgumentNullException.ThrowIfNull(adapters);
        ArgumentNullException.ThrowIfNull(scriptActivities);
        var languages = inputBindings
            .Select(binding => binding.Expression?.Language)
            .OfType<string>()
            .Concat(scriptActivities
                .Where(script => StringComparer.Ordinal.Equals(script.ActivityType, activityType))
                .Select(script => script.Language))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return adapters
            .Where(adapter => languages.Contains(adapter.Language))
            .DistinctBy(adapter => adapter.Language, StringComparer.OrdinalIgnoreCase)
            .OrderBy(adapter => adapter.Language, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>Adds the pins of <paramref name="required"/> to node metadata.</summary>
    public static IReadOnlyDictionary<string, string> Pin(IReadOnlyDictionary<string, string> metadata, IReadOnlyList<ExpressionValueAdapter> required)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        if (required.Count == 0)
            return metadata;
        var pinned = new Dictionary<string, string>(metadata, StringComparer.Ordinal);
        foreach (var adapter in required)
            pinned[Key(adapter.Language)] = adapter.Version;
        return pinned;
    }

    /// <summary>Refuses to run a node whose pin is missing or differs from the version this host runs.</summary>
    public static void Ensure(ExecutableNode node, IEnumerable<ExpressionValueAdapter> adapters, IEnumerable<ScriptActivityLanguage> scriptActivities)
    {
        ArgumentNullException.ThrowIfNull(node);
        foreach (var adapter in Required(node.ActivityType, node.InputBindings.Values, adapters, scriptActivities))
        {
            var pinned = node.Metadata.GetValueOrDefault(Key(adapter.Language));
            if (StringComparer.Ordinal.Equals(pinned, adapter.Version))
                continue;
            throw new InvalidOperationException(
                $"{MismatchCode}: Executable node '{node.ExecutableNodeId}' was published with " +
                (pinned is null ? $"no {adapter.Language} value adapter version" : $"{adapter.Language} value adapter version '{pinned}'") +
                $", and this host runs version '{adapter.Version}'. Republish the workflow.");
        }
    }
}
