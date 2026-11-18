using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Design.Core.Services;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Publishing.Services;

/// <summary>
/// Finds the literal variable reads whose resolution moved between the published version and the one being published
/// (spec 1222 FR-024, R-9): the same node and input read the same name, but publish now pins it to another declaring
/// scope or key, for example because a container now declares a variable of that name. A warning, not a refusal; the
/// published version keeps its own pins and is unchanged (FR-025).
/// </summary>
internal static class ScriptReadShadowChanges
{
    public static IReadOnlyList<string> Find(WorkflowExecutable? published, WorkflowExecutable candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        if (published is null)
            return [];

        var warnings = new List<string>();
        foreach (var (nodeId, node) in candidate.NodesById.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            if (!published.NodesById.TryGetValue(nodeId, out var previous))
                continue;

            foreach (var (inputKey, binding) in node.InputBindings.OrderBy(item => item.Key, StringComparer.Ordinal))
            {
                if (binding.Expression is not { } expression ||
                    previous.InputBindings.GetValueOrDefault(inputKey)?.Expression is not { } before)
                    continue;
                Compare(nodeId, inputKey, before.Parameters, expression.Parameters, warnings);
            }

            if (ScriptBodyReadPlan.Of(previous) is { } previousPlan && ScriptBodyReadPlan.Of(node) is { } plan)
                Compare(
                    nodeId,
                    "script",
                    previousPlan.Parameters.ToDictionary(item => item.Key, item => (ExpressionParameterBinding)item.Value, StringComparer.Ordinal),
                    plan.Parameters.ToDictionary(item => item.Key, item => (ExpressionParameterBinding)item.Value, StringComparer.Ordinal),
                    warnings);
        }

        return warnings;
    }

    private static void Compare(
        string nodeId,
        string inputKey,
        IReadOnlyDictionary<string, ExpressionParameterBinding> before,
        IReadOnlyDictionary<string, ExpressionParameterBinding> after,
        ICollection<string> warnings)
    {
        foreach (var (key, binding) in after.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            if (!ExpressionParameterSurfaces.TryGetVariableName(key, out var name) ||
                binding is not VariableExpressionParameterBinding now ||
                before.GetValueOrDefault(key) is not VariableExpressionParameterBinding then ||
                (StringComparer.Ordinal.Equals(then.DeclaringScopeNodeId, now.DeclaringScopeNodeId) &&
                 StringComparer.Ordinal.Equals(then.VariableKey, now.VariableKey)))
                continue;

            warnings.Add(
                $"{ScriptVariableReadDiagnostics.ShadowChange}: The script of input '{inputKey}' on activity node '{nodeId}' reads variable '{name}', " +
                $"which now resolves to '{now.VariableKey}' in scope '{now.DeclaringScopeNodeId}' instead of '{then.VariableKey}' in scope '{then.DeclaringScopeNodeId}' " +
                "as in the published version. The nearest declaration wins; rename one of them if the script should keep reading the other.");
        }
    }
}
