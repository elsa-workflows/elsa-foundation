using Elsa.Expressions.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// The expression value adapter versions an executable is pinned to (ADR 0046 V12, spec 1228 FR-028). An executable
/// that uses a language with a registered <see cref="ExpressionValueAdapter"/>, through an input expression, a variable's
/// initial binding or a script body (<see cref="ScriptActivityLanguage"/>), carries one pin per language in its
/// compatibility metadata, <c>&lt;language&gt;.valueAdapter</c> (for JavaScript, <c>javascript.valueAdapter</c>). The
/// behavioral hash covers the pins, so a republish under another version is another artifact. An executable that uses no
/// such language carries none, so its hash is unchanged. A published executable keeps the version it was published with;
/// the runtime refuses to run one whose pin is missing or differs.
/// </summary>
public static class ExecutableValueAdapters
{
    /// <summary>The executable was published with no pin, or another version, for an adapter this host runs.</summary>
    public const string MismatchCode = "VF-ADPT-001";

    public const string KeySuffix = ".valueAdapter";

    /// <summary>The compatibility metadata key of a language's pin, such as <c>javascript.valueAdapter</c>.</summary>
    public static string Key(string language)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(language);
        return language.ToLowerInvariant() + KeySuffix;
    }

    /// <summary>The pins in compatibility metadata, by key, ordered.</summary>
    public static IReadOnlyList<KeyValuePair<string, string>> Pins(IReadOnlyDictionary<string, string> compatibilityMetadata)
    {
        ArgumentNullException.ThrowIfNull(compatibilityMetadata);
        return compatibilityMetadata
            .Where(item => item.Key.EndsWith(KeySuffix, StringComparison.Ordinal))
            .OrderBy(item => item.Key, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>
    /// The pins an executable needs: one per registered language its nodes' input expressions, its variables' initial
    /// bindings or its script activities use.
    /// </summary>
    public static IReadOnlyDictionary<string, string> Required(
        IEnumerable<ExecutableNode> nodes,
        IEnumerable<RuntimeVariableDeclaration>? workflowVariables,
        IEnumerable<ExpressionValueAdapter> adapters,
        IEnumerable<ScriptActivityLanguage> scriptActivities)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(adapters);
        ArgumentNullException.ThrowIfNull(scriptActivities);
        var registered = adapters.ToArray();
        if (registered.Length == 0)
            return new Dictionary<string, string>(StringComparer.Ordinal);

        var scripts = scriptActivities.ToLookup(script => script.ActivityType, script => script.Language, StringComparer.Ordinal);
        var nodeArray = nodes as IReadOnlyCollection<ExecutableNode> ?? nodes.ToArray();
        var languages = nodeArray
            .SelectMany(node => node.InputBindings.Values.Select(binding => binding.Expression?.Language).Concat(scripts[node.ActivityType]))
            .Concat((workflowVariables ?? []).Select(variable => variable.InitialBinding?.Expression?.Language))
            .OfType<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return registered
            .Where(adapter => languages.Contains(adapter.Language))
            .DistinctBy(adapter => adapter.Language, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(adapter => Key(adapter.Language), adapter => adapter.Version, StringComparer.Ordinal);
    }

    /// <summary>Compatibility metadata with <paramref name="pins"/> added.</summary>
    public static IReadOnlyDictionary<string, string> Stamp(IReadOnlyDictionary<string, string> compatibilityMetadata, IReadOnlyDictionary<string, string> pins)
    {
        ArgumentNullException.ThrowIfNull(compatibilityMetadata);
        ArgumentNullException.ThrowIfNull(pins);
        if (pins.Count == 0)
            return compatibilityMetadata;
        var stamped = new Dictionary<string, string>(compatibilityMetadata, StringComparer.Ordinal);
        foreach (var (key, version) in pins)
            stamped[key] = version;
        return stamped;
    }

    /// <summary>
    /// Refuses to run an executable that uses a language whose adapter this host runs, when its pin is missing or differs.
    /// </summary>
    public static void Ensure(WorkflowExecutable executable, IEnumerable<ExpressionValueAdapter> adapters, IEnumerable<ScriptActivityLanguage> scriptActivities)
    {
        ArgumentNullException.ThrowIfNull(executable);
        foreach (var (key, version) in Required(executable.Nodes, executable.WorkflowVariables, adapters, scriptActivities))
        {
            var pinned = executable.CompatibilityMetadata.GetValueOrDefault(key);
            if (StringComparer.Ordinal.Equals(pinned, version))
                continue;
            var language = key[..^KeySuffix.Length];
            throw new InvalidOperationException(
                $"{MismatchCode}: Workflow executable '{executable.Identity.ArtifactId}' was published with " +
                (pinned is null ? $"no {language} value adapter version" : $"{language} value adapter version '{pinned}'") +
                $", and this host runs version '{version}'. Republish the workflow.");
        }
    }
}
