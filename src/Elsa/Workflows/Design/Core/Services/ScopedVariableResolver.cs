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
