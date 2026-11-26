using System.Text.Json;
using Elsa3.Migration.Tooling.Report;
using Elsa3.Models;

namespace Elsa3.Migration.Tooling;

/// <summary>
/// Lists the workflows that converted definitions dispatch or execute but the export does not hold (spec 1237 FR-018
/// item 5). They are Take into account, not refusals: the target may already live in the Elsa 4 host.
/// </summary>
internal static class MissingTargetDetector
{
    // The activities that start another workflow by its definition id, as the collection analyzer names them (spec 246).
    private static readonly HashSet<string> DispatchTypes = new(StringComparer.Ordinal)
    {
        "Elsa.DispatchWorkflow",
        "Elsa.ExecuteWorkflow",
        "Elsa.BulkDispatchWorkflows"
    };

    /// <param name="converted">The definitions that convert.</param>
    /// <param name="exported">Every definition in the export, selected or not.</param>
    public static IReadOnlyList<ReportMissingTarget> Detect(IEnumerable<Elsa3WorkflowDefinition> converted, IEnumerable<Elsa3WorkflowDefinition> exported)
    {
        var known = exported.Select(definition => definition.DefinitionId).ToHashSet(StringComparer.Ordinal);
        var missing = new SortedDictionary<string, SortedSet<string>>(StringComparer.Ordinal);

        foreach (var definition in converted)
        {
            foreach (var activity in Enumerate(definition.Root))
            {
                if (!DispatchTypes.Contains(activity.Type) || ReadLiteral(activity.AdditionalProperties, "workflowDefinitionId") is not { Length: > 0 } target || known.Contains(target))
                    continue;

                if (!missing.TryGetValue(target, out var referencedBy))
                    missing[target] = referencedBy = new SortedSet<string>(StringComparer.Ordinal);
                referencedBy.Add(definition.DefinitionId);
            }
        }

        return missing.Select(entry => new ReportMissingTarget(entry.Key, [.. entry.Value])).ToArray();
    }

    private static IEnumerable<Elsa3Activity> Enumerate(Elsa3Activity? root)
    {
        if (root is null)
            yield break;

        var stack = new Stack<Elsa3Activity>();
        stack.Push(root);
        while (stack.TryPop(out var current))
        {
            yield return current;
            foreach (var child in current.Activities ?? [])
                stack.Push(child);
        }
    }

    /// <summary>
    /// A literal definition id only: a plain string, or an input whose expression is a <c>Literal</c>. An id computed by
    /// a script is no identity the report may name, so it is not listed.
    /// </summary>
    private static string? ReadLiteral(IDictionary<string, JsonElement>? properties, string name)
    {
        if (properties is null || !properties.TryGetValue(name, out var value))
            return null;
        if (value.ValueKind == JsonValueKind.String)
            return value.GetString();
        if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty("expression", out var expression) || expression.ValueKind != JsonValueKind.Object)
            return null;
        var isLiteral = expression.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String
                        && string.Equals(type.GetString(), "Literal", StringComparison.OrdinalIgnoreCase);
        return isLiteral && expression.TryGetProperty("value", out var literal) && literal.ValueKind == JsonValueKind.String ? literal.GetString() : null;
    }
}
