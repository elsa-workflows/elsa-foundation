using System.Text.Json;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Core.Services;

namespace Elsa.Workflows.Design.Validations.Services;

/// <summary>
/// The default <see cref="IScriptBodyWriteAnalyzer"/>: resolves the node's literal script body with the same resolver
/// and visibility publication uses, so Studio shows the may-write set the published node pins (spec 1231 FR-036).
/// </summary>
public sealed class ScriptBodyWriteAnalyzer(ScriptVariableReadResolver resolver, IActivityStructureService structures) : IScriptBodyWriteAnalyzer
{
    public async ValueTask<ScriptBodyWriteSet?> AnalyzeAsync(
        WorkflowDefinitionState state,
        string nodeId,
        Func<ActivityNode, CancellationToken, ValueTask<string?>> scriptInputKeyOf,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeId);
        ArgumentNullException.ThrowIfNull(scriptInputKeyOf);
        if (state.RootActivity is null || Find(state.RootActivity, nodeId) is not { } node)
            return null;
        if (await scriptInputKeyOf(node, cancellationToken) is not { } scriptInput || LiteralScript(node, scriptInput) is not { Length: > 0 } script)
            return null;

        var visibility = new ScopedVariableResolver(structures)
            .Resolve(state.Variables, state.RootActivity, ScopedVariablePicker.DefaultMaxDepth, includeIterationScopes: true);
        if (resolver.Resolve("JavaScript", script, visibility.GetVisibleScopes(nodeId), scriptBody: true) is not { } plan)
            return null;
        return new ScriptBodyWriteSet(
            plan.MayWrite().Select(write => new ScriptBodyWriteTarget(write.Name, write.ScopeId, write.Variable.ReferenceKey)).ToArray(),
            plan.HasComputedWrite);
    }

    private ActivityNode? Find(ActivityNode root, string nodeId)
    {
        var pending = new Stack<ActivityNode>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var node = pending.Pop();
            if (StringComparer.Ordinal.Equals(node.NodeId, nodeId))
                return node;
            foreach (var child in structures.ProjectChildren(node).SelectMany(slot => slot.Activities))
                pending.Push(child);
        }

        return null;
    }

    /// <summary>The script body when it is authored as a literal; a script from an expression has no body to analyze.</summary>
    internal static string? LiteralScript(ActivityNode node, string scriptInput)
    {
        var value = node.Inputs?.FirstOrDefault(input => StringComparer.Ordinal.Equals(input.ReferenceKey, scriptInput))?.Value;
        if (value is null || value.ExpressionType is { } type && !string.Equals(type, "Literal", StringComparison.OrdinalIgnoreCase))
            return null;
        return value.Value switch
        {
            JsonElement { ValueKind: JsonValueKind.String } element => element.GetString(),
            _ => null
        };
    }
}
