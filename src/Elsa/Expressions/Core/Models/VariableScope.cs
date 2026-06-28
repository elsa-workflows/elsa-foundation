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
        VariableScope? parent = null,
        string? executionId = null,
        IReadOnlyDictionary<string, object?>? initialValues = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(scopeId);
        ArgumentNullException.ThrowIfNull(variablesByReferenceKey);

        ScopeId = scopeId;
        _variablesByReferenceKey = variablesByReferenceKey;
        Parent = parent;
        ExecutionId = executionId;

        if (initialValues is not null)
        {
            foreach (var (key, value) in initialValues)
                _values[key] = value;
        }
    }

    /// <summary>The declaring scope identity — a container activity node id, or the workflow-scope sentinel.</summary>
    public string ScopeId { get; }

    /// <summary>The next outer visible scope, or <c>null</c> at the workflow scope.</summary>
    public VariableScope? Parent { get; }

    /// <summary>
    /// The concrete container activity execution this scope belongs to (ADR 0027). Container
    /// variable values are isolated per execution: repeated, retried, or parallel executions of the
    /// same authored container declaration (same <see cref="ScopeId"/>) get separate
    /// <see cref="VariableScope"/> instances with distinct execution ids and value stores. Null for
    /// the workflow scope or when execution identity is not tracked.
    /// </summary>
    public string? ExecutionId { get; }

    /// <summary>
    /// Whether this scope's execution has completed. A completed scope's values are no longer live
    /// for later runtime expressions (reads/writes are rejected), but its captured values remain
    /// available via <see cref="SnapshotValues"/> as inspection/history evidence — exposure of which
    /// is gated by the runtime's configured retention/redaction policy.
    /// </summary>
    public bool IsCompleted { get; private set; }

    /// <summary>Marks this scope's execution complete; its values stop being live for later expressions.</summary>
    public void Complete() => IsCompleted = true;

    /// <summary>
    /// Captures the current values declared by this scope (for checkpoint persistence and resume
    /// recovery, and as completed-scope inspection evidence under retention/redaction policy).
    /// </summary>
    public IReadOnlyDictionary<string, object?> SnapshotValues() =>
        new Dictionary<string, object?>(_values, StringComparer.Ordinal);

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
        if (owning is null || owning.Value.Scope.IsCompleted)
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
        if (owning is null || owning.Value.Scope.IsCompleted)
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

        return FindByName(name)?.Variable;
    }

    /// <summary>
    /// Enumerates the variables visible from this scope, nearest-scope first, with shadowed
    /// (outer) same-named declarations omitted. Used to expose name-based helpers (e.g. JavaScript
    /// <c>getX()</c>/<c>setX()</c> functions) over the visible scope chain. Completed scopes are
    /// excluded.
    /// </summary>
    public IReadOnlyList<IVariable> EnumerateVisibleVariables()
    {
        var visible = new List<IVariable>();
        var seenNames = new HashSet<string>(StringComparer.Ordinal);

        for (var scope = this; scope is not null; scope = scope.Parent)
        {
            if (scope.IsCompleted)
                continue;

            foreach (var variable in scope._variablesByReferenceKey.Values)
            {
                if (seenNames.Add(variable.Name))
                    visible.Add(variable);
            }
        }

        return visible;
    }

    /// <summary>
    /// Reads a variable value by bare name through the visible scope chain (nearest declaring scope
    /// wins). Returns the assigned value if set, otherwise the default. Backs name-based script reads.
    /// </summary>
    public bool TryGetValueByName(string name, out object? value)
    {
        var found = FindByName(name);
        if (found is null)
        {
            value = null;
            return false;
        }

        value = found.Value.Scope._values.TryGetValue(found.Value.ReferenceKey, out var stored)
            ? stored
            : found.Value.Variable.DefaultValue;
        return true;
    }

    /// <summary>
    /// Assigns a variable value by bare name to the nearest visible scope that declares the name, so
    /// name-based script writes target the correct workflow or container scope. Returns false when no
    /// visible scope declares the name.
    /// </summary>
    public bool TrySetValueByName(string name, object? value)
    {
        var found = FindByName(name);
        if (found is null)
            return false;

        found.Value.Scope._values[found.Value.ReferenceKey] = value;
        return true;
    }

    private (VariableScope Scope, string ReferenceKey, IVariable Variable)? FindByName(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        for (var scope = this; scope is not null; scope = scope.Parent)
        {
            if (scope.IsCompleted)
                continue;

            foreach (var (referenceKey, variable) in scope._variablesByReferenceKey)
            {
                if (StringComparer.Ordinal.Equals(variable.Name, name))
                    return (scope, referenceKey, variable);
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
