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
    ScopedVariablePicker scopedVariablePicker)
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
    /// never validation errors. Delegates to the resolver's single traversal (no re-walk).
    /// </summary>
    public IReadOnlyList<ScopedVariableShadowingWarning> GetShadowingWarnings(WorkflowDefinitionState state, int maxDepth = ScopedVariablePicker.DefaultMaxDepth)
    {
        ArgumentNullException.ThrowIfNull(state);

        return scopedVariableResolver
            .Resolve(state.Variables, state.RootActivity, maxDepth)
            .GetShadowingWarnings();
    }
}

/// <summary>
/// A variable visible from a selected activity, in serializable form for the authoring picker.
/// </summary>
public sealed record VisibleVariableView(string ReferenceKey, string Name, string ScopeId, bool IsWorkflowScope);
