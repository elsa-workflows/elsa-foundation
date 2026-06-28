using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;

namespace Elsa.Workflows.Design.Core.Services;

/// <summary>
/// Computes scoped-variable visibility across an authored activity tree (ADR 0027). For every node
/// it determines the ordered chain of visible variable scopes — nearest ancestor container first,
/// then outer containers, then the workflow scope — so callers can validate structured variable
/// references, populate authoring pickers, and resolve nearest-scope shadowing without each
/// reimplementing the traversal.
/// </summary>
public sealed class ScopedVariableResolver(IActivityStructureService structureService)
{
    /// <summary>
    /// Builds the per-node visible-scope map for the tree rooted at <paramref name="root"/>.
    /// </summary>
    /// <param name="workflowVariables">Workflow-scoped variable declarations (always visible).</param>
    /// <param name="root">The workflow root activity node.</param>
    /// <param name="maxDepth">Maximum tree depth to traverse (safety net against malformed data).</param>
    public ScopedVariableVisibility Resolve(IEnumerable<VariableDefinition> workflowVariables, ActivityNode? root, int maxDepth)
    {
        var workflowScope = new VisibleVariableScope(VariableReference.WorkflowScopeId, [.. workflowVariables]);
        var visibleScopesByNode = new Dictionary<string, IReadOnlyList<VisibleVariableScope>>(StringComparer.Ordinal);

        if (root is null)
            return new ScopedVariableVisibility(visibleScopesByNode);

        var stack = new Stack<(ActivityNode Node, int Depth, IReadOnlyList<VisibleVariableScope> VisibleScopes)>();
        stack.Push((root, 0, [workflowScope]));

        while (stack.Count > 0)
        {
            var (node, depth, visibleScopes) = stack.Pop();

            // A node sees its ancestors' scopes; its own declared variables are visible to descendants only.
            visibleScopesByNode[node.NodeId] = visibleScopes;

            if (depth >= maxDepth)
                continue;

            var declared = structureService.ProjectScopedVariables(node);
            var childScopes = declared.Count > 0
                ? new List<VisibleVariableScope> { new(node.NodeId, [.. declared]) }.Concat(visibleScopes).ToArray()
                : visibleScopes;

            foreach (var child in structureService.ProjectChildren(node).SelectMany(slot => slot.Activities))
                stack.Push((child, depth + 1, childScopes));
        }

        return new ScopedVariableVisibility(visibleScopesByNode);
    }
}

/// <summary>
/// A single variable scope visible from a node: the declaring scope identity (a container node id,
/// or the workflow-scope sentinel) and the variables it declares.
/// </summary>
public sealed record VisibleVariableScope(string ScopeId, IReadOnlyList<VariableDefinition> Variables);

/// <summary>
/// A variable visible from a node, with its declaring scope, for authoring pickers.
/// </summary>
public sealed record VisibleVariable(string ScopeId, bool IsWorkflowScope, VariableDefinition Variable);

/// <summary>
/// Backend picker contract (ADR 0027): resolves the variables visible from a selected activity so
/// authoring surfaces (Studio) show only in-scope variables by default. Wraps
/// <see cref="ScopedVariableResolver"/> over a workflow design state.
/// </summary>
public sealed class ScopedVariablePicker(ScopedVariableResolver scopedVariableResolver)
{
    public const int DefaultMaxDepth = 100;

    /// <summary>
    /// Returns the variables visible from <paramref name="nodeId"/> within
    /// <paramref name="state"/>, nearest-scope first.
    /// </summary>
    public IReadOnlyList<VisibleVariable> GetVisibleVariables(WorkflowDefinitionState state, string nodeId, int maxDepth = DefaultMaxDepth)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrEmpty(nodeId);

        return scopedVariableResolver
            .Resolve(state.Variables, state.RootActivity, maxDepth)
            .GetVisibleVariables(nodeId);
    }
}

/// <summary>
/// Per-node scoped-variable visibility produced by <see cref="ScopedVariableResolver"/>.
/// </summary>
public sealed class ScopedVariableVisibility(IReadOnlyDictionary<string, IReadOnlyList<VisibleVariableScope>> visibleScopesByNode)
{
    /// <summary>
    /// Returns the ordered visible scopes for <paramref name="nodeId"/> (nearest container first,
    /// workflow scope last), or an empty list when the node is unknown or beyond traversal depth.
    /// </summary>
    public IReadOnlyList<VisibleVariableScope> GetVisibleScopes(string nodeId) =>
        visibleScopesByNode.TryGetValue(nodeId, out var scopes) ? scopes : [];

    /// <summary>
    /// Returns the variables visible from <paramref name="nodeId"/> in nearest-scope-first order,
    /// for authoring pickers. Each entry carries its declaring scope so callers can present scope
    /// context; nearest declarations precede outer ones (shadowing-aware presentation).
    /// </summary>
    public IReadOnlyList<VisibleVariable> GetVisibleVariables(string nodeId) =>
        GetVisibleScopes(nodeId)
            .SelectMany(scope => scope.Variables.Select(variable =>
                new VisibleVariable(scope.ScopeId, StringComparer.Ordinal.Equals(scope.ScopeId, VariableReference.WorkflowScopeId), variable)))
            .ToArray();

    /// <summary>
    /// True when <paramref name="reference"/> resolves to a variable that is visible from
    /// <paramref name="nodeId"/> — i.e. its declaring scope is a visible ancestor (or the workflow
    /// scope) that declares the referenced key.
    /// </summary>
    public bool IsReferenceVisible(string nodeId, VariableReference reference)
    {
        var targetScopeId = reference.IsWorkflowScope ? VariableReference.WorkflowScopeId : reference.DeclaringScopeId;

        foreach (var scope in GetVisibleScopes(nodeId))
        {
            if (!StringComparer.Ordinal.Equals(scope.ScopeId, targetScopeId))
                continue;

            if (scope.Variables.Any(variable => StringComparer.Ordinal.Equals(variable.ReferenceKey, reference.ReferenceKey)))
                return true;
        }

        return false;
    }
}
