using Elsa.Expressions.Core.Contracts;

namespace Elsa.Expressions.Core.Models;

/// <summary>
/// A runtime variable scope and its visible-ancestor chain (ADR 0027). The nearest scope points at
/// its parent scope, up to the workflow scope, so that a descendant activity can resolve both
/// structured references (by declaring scope identity + reference key) and bare names (nearest
/// declaring scope wins, allowing intentional shadowing).
/// </summary>
public sealed class VariableScope
{
    private readonly IReadOnlyDictionary<string, IVariable> _variablesByReferenceKey;

    public VariableScope(
        string scopeId,
        IReadOnlyDictionary<string, IVariable> variablesByReferenceKey,
        VariableScope? parent = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(scopeId);
        ArgumentNullException.ThrowIfNull(variablesByReferenceKey);

        ScopeId = scopeId;
        _variablesByReferenceKey = variablesByReferenceKey;
        Parent = parent;
    }

    /// <summary>The declaring scope identity — a container activity node id, or the workflow-scope sentinel.</summary>
    public string ScopeId { get; }

    /// <summary>The next outer visible scope, or <c>null</c> at the workflow scope.</summary>
    public VariableScope? Parent { get; }

    /// <summary>
    /// Resolves a structured reference: walks outward to the scope whose identity matches the
    /// reference's declaring scope, then looks the variable up by reference key within that scope.
    /// </summary>
    public bool TryResolve(VariableReference reference, out IVariable? variable)
    {
        ArgumentNullException.ThrowIfNull(reference);

        var targetScopeId = reference.IsWorkflowScope ? VariableReference.WorkflowScopeId : reference.DeclaringScopeId;

        for (var scope = this; scope is not null; scope = scope.Parent)
        {
            if (StringComparer.Ordinal.Equals(scope.ScopeId, targetScopeId) &&
                scope._variablesByReferenceKey.TryGetValue(reference.ReferenceKey, out var found))
            {
                variable = found;
                return true;
            }
        }

        variable = null;
        return false;
    }

    /// <summary>
    /// Resolves a bare variable name nearest-scope first, so a name declared by an inner container
    /// shadows the same name in an outer scope. Used by name-based convenience access (e.g. scripts).
    /// </summary>
    public IVariable? ResolveByName(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        for (var scope = this; scope is not null; scope = scope.Parent)
        {
            foreach (var variable in scope._variablesByReferenceKey.Values)
            {
                if (StringComparer.Ordinal.Equals(variable.Name, name))
                    return variable;
            }
        }

        return null;
    }
}
