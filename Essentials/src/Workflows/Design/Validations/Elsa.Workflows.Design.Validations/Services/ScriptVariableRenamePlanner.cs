using System.Text.Json;
using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;

using Elsa.Workflows.Design.Core.Services;

namespace Elsa.Workflows.Design.Validations.Services;

/// <summary>
/// Plans how the scripts of a draft follow a variable rename (spec 1222 FR-021, FR-022, research R-9). A literal read
/// that resolves to the renamed variable is edited in place, keeping the readable name in the source. A reference the
/// planner cannot safely update is surfaced for manual repair instead of being left or rewritten wrongly: a read whose
/// language gives no source positions (Liquid), a member read whose new name is not a JavaScript identifier, a computed
/// read that could reach the variable, and a read of the new name that the rename would capture. It plans for the draft
/// only; a published version keeps its own pins (FR-025).
/// </summary>
public sealed class ScriptVariableRenamePlanner(ScriptVariableReadResolver resolver, IActivityStructureService structures) : IScriptVariableRenamePlanner
{
    public const int MaxDepth = ScopedVariablePicker.DefaultMaxDepth;

    /// <summary>
    /// Plans the rename of the variable <paramref name="referenceKey"/> declared by <paramref name="scopeId"/> (the
    /// workflow scope or a container node) to <paramref name="newName"/>. <paramref name="scriptInputKeyOf"/> names a
    /// node's script-body input (a literal script, such as RunJavaScript's <c>script</c>), or null when it has none.
    /// </summary>
    public async ValueTask<ScriptVariableRenamePlan> PlanAsync(
        WorkflowDefinitionState state,
        string scopeId,
        string referenceKey,
        string newName,
        Func<ActivityNode, CancellationToken, ValueTask<string?>>? scriptInputKeyOf = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(scopeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(referenceKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(newName);

        var visibility = new ScopedVariableResolver(structures).Resolve(state.Variables, state.RootActivity, MaxDepth, includeIterationScopes: true);
        var nodes = Walk(state.RootActivity).ToArray();
        var renamed = FindDeclaration(state, nodes, scopeId, referenceKey)
            ?? throw new ArgumentException($"No variable '{referenceKey}' is declared by scope '{scopeId}'.");
        var oldName = renamed.Name;
        var edits = new List<ScriptVariableRenameEdit>();
        var flags = new List<ScriptVariableRenameFlag>();
        if (StringComparer.Ordinal.Equals(oldName, newName))
            return new(oldName, newName, edits, flags);

        foreach (var node in nodes)
        {
            var scopes = visibility.GetVisibleScopes(node.NodeId);
            var scriptInput = scriptInputKeyOf is null ? null : await scriptInputKeyOf(node, cancellationToken);
            foreach (var input in node.Inputs)
            {
                if (ReadScript(input, scriptInput) is not { } script ||
                    resolver.Analyze(script.Language, script.Source, script.IsBody) is not { } analysis ||
                    !analysis.ReadsVariables)
                    continue;

                var readsRenamed = ResolvesTo(scopes, oldName, scopeId, referenceKey);
                var edited = script.Source;
                if (readsRenamed && analysis.LiteralNames.Contains(oldName, StringComparer.Ordinal))
                {
                    var tokens = analysis.LiteralReads.Where(read => StringComparer.Ordinal.Equals(read.Name, oldName)).ToArray();
                    if (NearerDeclarationOf(scopes, newName, scopeId, referenceKey))
                        flags.Add(new(node.NodeId, input.ReferenceKey, ScriptVariableRenameFlagReason.Retargeted,
                            $"The script reads '{oldName}', but a nearer scope declares '{newName}', so the renamed read would read that variable instead; rename one of them by hand."));
                    else if (tokens.Length == 0)
                        flags.Add(new(node.NodeId, input.ReferenceKey, ScriptVariableRenameFlagReason.NoSourcePositions,
                            $"The script reads '{oldName}', but its language gives no positions to edit; change it to '{newName}' by hand."));
                    else if (tokens.Any(token => token.IsMemberName) && !IsIdentifier(newName))
                        flags.Add(new(node.NodeId, input.ReferenceKey, ScriptVariableRenameFlagReason.NameNotAnIdentifier,
                            $"The script reads variables.{oldName}, and '{newName}' is not a JavaScript identifier; write variables['{newName}'] by hand."));
                    else
                    {
                        foreach (var token in tokens.OrderByDescending(token => token.Start))
                            edited = edited[..token.Start] + (token.IsMemberName ? newName : JsonSerializer.Serialize(newName)) + edited[token.End..];
                    }
                }

                if (readsRenamed && analysis.HasComputedRead)
                    flags.Add(new(node.NodeId, input.ReferenceKey, ScriptVariableRenameFlagReason.ComputedRead,
                        $"The script reads variables by a computed name and could read '{oldName}'; check every name it builds."));

                // A read of the new name that resolves elsewhere, or nowhere, would be captured by the renamed variable.
                if (analysis.LiteralNames.Contains(newName, StringComparer.Ordinal) &&
                    CapturesAfterRename(scopes, newName, scopeId, referenceKey))
                    flags.Add(new(node.NodeId, input.ReferenceKey, ScriptVariableRenameFlagReason.Captured,
                        $"The script reads '{newName}', which would then resolve to the renamed variable instead of what it reads now."));

                if (!ReferenceEquals(edited, script.Source))
                    edits.Add(new(node.NodeId, input.ReferenceKey, script.Source, edited));
            }
        }

        return new(oldName, newName, edits, flags);
    }

    private static bool ResolvesTo(IReadOnlyList<VisibleVariableScope> scopes, string name, string scopeId, string referenceKey)
    {
        foreach (var scope in scopes)
        {
            if (scope.Variables.FirstOrDefault(variable => StringComparer.Ordinal.Equals(variable.Name, name)) is { } variable)
                return StringComparer.Ordinal.Equals(scope.ScopeId, scopeId) && StringComparer.Ordinal.Equals(variable.ReferenceKey, referenceKey);
        }

        return false;
    }

    /// <summary>Whether, from the node, a scope nearer than the renamed variable's declares <paramref name="newName"/>.</summary>
    private static bool NearerDeclarationOf(IReadOnlyList<VisibleVariableScope> scopes, string newName, string scopeId, string referenceKey)
    {
        foreach (var scope in scopes)
        {
            if (StringComparer.Ordinal.Equals(scope.ScopeId, scopeId) &&
                scope.Variables.Any(variable => StringComparer.Ordinal.Equals(variable.ReferenceKey, referenceKey)))
                return false;
            if (scope.Variables.Any(variable => StringComparer.Ordinal.Equals(variable.Name, newName)))
                return true;
        }

        return false;
    }

    private static bool CapturesAfterRename(IReadOnlyList<VisibleVariableScope> scopes, string newName, string scopeId, string referenceKey)
    {
        foreach (var scope in scopes)
        {
            if (StringComparer.Ordinal.Equals(scope.ScopeId, scopeId) &&
                scope.Variables.Any(variable => StringComparer.Ordinal.Equals(variable.ReferenceKey, referenceKey)))
                return true;
            if (scope.Variables.Any(variable => StringComparer.Ordinal.Equals(variable.Name, newName)))
                return false;
        }

        return false;
    }

    private VariableDefinition? FindDeclaration(WorkflowDefinitionState state, IEnumerable<ActivityNode> nodes, string scopeId, string referenceKey)
    {
        var declared = StringComparer.Ordinal.Equals(scopeId, VariableReference.WorkflowScopeId)
            ? state.Variables
            : nodes.FirstOrDefault(node => StringComparer.Ordinal.Equals(node.NodeId, scopeId)) is { } container
                ? structures.ProjectScopedVariables(container)
                : [];
        return declared.FirstOrDefault(variable => StringComparer.Ordinal.Equals(variable.ReferenceKey, referenceKey));
    }

    private IEnumerable<ActivityNode> Walk(ActivityNode? root)
    {
        if (root is null)
            yield break;
        var stack = new Stack<(ActivityNode Node, int Depth)>();
        stack.Push((root, 0));
        while (stack.TryPop(out var item))
        {
            yield return item.Node;
            if (item.Depth >= MaxDepth)
                continue;
            foreach (var child in structures.ProjectChildren(item.Node).SelectMany(slot => slot.Activities).Reverse())
                stack.Push((child, item.Depth + 1));
        }
    }

    private (string Language, string Source, bool IsBody)? ReadScript(ArgumentState input, string? scriptInput)
    {
        var value = input.Value;
        if (value is null)
            return null;
        if (scriptInput is not null && StringComparer.Ordinal.Equals(input.ReferenceKey, scriptInput) &&
            (value.ExpressionType is null || StringComparer.OrdinalIgnoreCase.Equals(value.ExpressionType, "Literal")) &&
            Text(value.Value) is { Length: > 0 } body)
            return ("JavaScript", body, true);
        if (value.ExpressionType is { } language && resolver.Supports(language) && Text(value.Value) is { Length: > 0 } source)
            return (language, source, false);
        return null;
    }

    private static string? Text(object? value) => value switch
    {
        string text => text,
        ExpressionDefinition definition => definition.Source,
        JsonElement { ValueKind: JsonValueKind.String } element => element.GetString(),
        JsonElement { ValueKind: JsonValueKind.Object } element when element.TryGetProperty("source", out var source) && source.ValueKind == JsonValueKind.String => source.GetString(),
        _ => null
    };

    private static bool IsIdentifier(string value) =>
        value.Length > 0 &&
        (char.IsLetter(value[0]) || value[0] is '_' or '$') &&
        value.Skip(1).All(character => char.IsLetterOrDigit(character) || character is '_' or '$');
}
