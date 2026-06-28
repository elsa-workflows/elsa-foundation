using Elsa.Expressions.Core.Contracts;

namespace Elsa.Expressions.Core.Models;

/// <summary>
/// A runtime variable scope and its visible-ancestor chain (ADR 0027). The nearest scope points at
/// its parent scope, up to the workflow scope, so that a descendant activity can resolve both
/// structured references (by declaring scope identity + reference key) and bare names (nearest
/// declaring scope wins, allowing intentional shadowing).
///
/// Each scope owns the current values of the variables it declares. Descendant activities read and
/// assign visible ancestor variables through the chain, and because sibling branches of one
/// container execution share the same <see cref="VariableScope"/> instance they observe each
/// other's assignments. Values are ordinary in-memory runtime state, so assignment durability
/// follows the normal runtime checkpoint boundary — there is no variable-specific persistence path.
/// </summary>
public sealed class VariableScope
{
    private readonly IReadOnlyDictionary<string, IVariable> _variablesByReferenceKey;
    private readonly Dictionary<string, object?> _values = new(StringComparer.Ordinal);

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
    /// Resolves a structured reference to its declared variable: walks outward to the scope whose
    /// identity matches the reference's declaring scope, then looks the variable up by reference key.
    /// </summary>
    public bool TryResolve(VariableReference reference, out IVariable? variable)
    {
        variable = FindOwningScope(reference)?.Variable;
        return variable is not null;
    }

    /// <summary>
    /// Reads the current value of a visible variable: the assigned value if one has been set in its
    /// owning scope, otherwise the variable's default. Returns <c>false</c> when the reference's
    /// declaring scope is not visible from this scope.
    /// </summary>
    public bool TryGetValue(VariableReference reference, out object? value)
    {
        var owning = FindOwningScope(reference);
        if (owning is null)
        {
            value = null;
            return false;
        }

        value = owning.Value.Scope._values.TryGetValue(reference.ReferenceKey, out var stored)
            ? stored
            : owning.Value.Variable.DefaultValue;
        return true;
    }

    /// <summary>
    /// Assigns a value to a visible variable in its owning scope, so that sibling branches sharing
    /// that scope observe the new value. Returns <c>false</c> when the reference's declaring scope is
    /// not visible from this scope (e.g. a sibling or unrelated container) — the runtime guard that
    /// keeps scope boundaries enforceable.
    /// </summary>
    public bool TrySetValue(VariableReference reference, object? value)
    {
        var owning = FindOwningScope(reference);
        if (owning is null)
            return false;

        owning.Value.Scope._values[reference.ReferenceKey] = value;
        return true;
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

    private (VariableScope Scope, IVariable Variable)? FindOwningScope(VariableReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);

        var targetScopeId = reference.IsWorkflowScope ? VariableReference.WorkflowScopeId : reference.DeclaringScopeId;

        for (var scope = this; scope is not null; scope = scope.Parent)
        {
            if (StringComparer.Ordinal.Equals(scope.ScopeId, targetScopeId) &&
                scope._variablesByReferenceKey.TryGetValue(reference.ReferenceKey, out var variable))
            {
                return (scope, variable);
            }
        }

        return null;
    }
}
