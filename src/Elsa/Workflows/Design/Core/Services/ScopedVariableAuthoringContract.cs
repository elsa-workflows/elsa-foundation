using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;

namespace Elsa.Workflows.Design.Core.Services;

/// <summary>
/// Backend wire contract for scoped-variable authoring (ADR 0027), consumed by the external Studio
/// client. Surfaces the variables visible from a selected activity (so pickers show only in-scope
/// variables by default) and non-blocking shadowing warnings. Workflow-scoped and container-scoped
/// declarations are presented through one uniform <see cref="VisibleVariableView"/> shape, so the
/// client can treat them as the same declaration concept.
///
/// Invalid scoped-reference diagnostics are produced by the variable-expression draft validator and
/// already flow to the client through the normal validation results — they are not duplicated here.
/// </summary>
public sealed class ScopedVariableAuthoringContract(
    ScopedVariableResolver scopedVariableResolver,
    ScopedVariablePicker scopedVariablePicker,
    IActivityStructureService structureService)
{
    /// <summary>
    /// Returns the variables visible from <paramref name="nodeId"/>, nearest-scope first, as a
    /// serializable view for the authoring picker. Wraps <see cref="ScopedVariablePicker"/>.
    /// </summary>
    public IReadOnlyList<VisibleVariableView> GetVisibleVariables(WorkflowDefinitionState state, string nodeId, int maxDepth = ScopedVariablePicker.DefaultMaxDepth)
    {
        return scopedVariablePicker
            .GetVisibleVariables(state, nodeId, maxDepth)
            .Select(visible => new VisibleVariableView(
                visible.Variable.ReferenceKey,
                visible.Variable.Name,
                visible.ScopeId,
                visible.IsWorkflowScope))
            .ToArray();
    }

    /// <summary>
    /// Returns non-blocking warnings for container scopes whose declared variable names shadow a
    /// visible ancestor declaration. Shadowing is intentional and allowed — these are advisory only,
    /// never validation errors.
    /// </summary>
    public IReadOnlyList<ScopedVariableShadowingWarning> GetShadowingWarnings(WorkflowDefinitionState state, int maxDepth = ScopedVariablePicker.DefaultMaxDepth)
    {
        ArgumentNullException.ThrowIfNull(state);

        var visibility = scopedVariableResolver.Resolve(state.Variables, state.RootActivity, maxDepth);
        var warnings = new List<ScopedVariableShadowingWarning>();

        foreach (var node in Walk(state.RootActivity, maxDepth))
        {
            var declared = structureService.ProjectScopedVariables(node);
            if (declared.Count == 0)
                continue;

            var ancestorScopes = visibility.GetVisibleScopes(node.NodeId);

            foreach (var variable in declared)
            {
                var shadowedScope = ancestorScopes.FirstOrDefault(scope =>
                    scope.Variables.Any(ancestor => StringComparer.Ordinal.Equals(ancestor.Name, variable.Name)));

                if (shadowedScope is not null)
                    warnings.Add(new ScopedVariableShadowingWarning(node.NodeId, shadowedScope.ScopeId, variable.Name, variable.ReferenceKey));
            }
        }

        return warnings;
    }

    private IEnumerable<ActivityNode> Walk(ActivityNode? root, int maxDepth)
    {
        if (root is null)
            yield break;

        var stack = new Stack<(ActivityNode Node, int Depth)>();
        stack.Push((root, 0));

        while (stack.Count > 0)
        {
            var (node, depth) = stack.Pop();
            yield return node;

            if (depth >= maxDepth)
                continue;

            foreach (var child in structureService.ProjectChildren(node).SelectMany(slot => slot.Activities))
                stack.Push((child, depth + 1));
        }
    }
}

/// <summary>
/// A variable visible from a selected activity, in serializable form for the authoring picker.
/// </summary>
public sealed record VisibleVariableView(string ReferenceKey, string Name, string ScopeId, bool IsWorkflowScope);

/// <summary>
/// A non-blocking warning that a container scope declares a variable name that shadows a visible
/// ancestor declaration of the same name (ADR 0027). Advisory only — shadowing is allowed.
/// </summary>
public sealed record ScopedVariableShadowingWarning(string ScopeId, string ShadowedScopeId, string Name, string ReferenceKey);
