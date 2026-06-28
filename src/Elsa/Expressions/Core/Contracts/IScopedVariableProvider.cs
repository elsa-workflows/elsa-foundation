using Elsa.Expressions.Core.Models;

namespace Elsa.Expressions.Core.Contracts;

/// <summary>
/// Implemented by expression execution contexts that expose a scoped-variable chain (workflow scope
/// plus any visible ancestor container scopes, ADR 0027). Lets the variable expression handler
/// resolve and assign a structured <see cref="VariableReference"/> in its declaring scope, honouring
/// nearest-scope visibility and shadowing.
/// </summary>
public interface IScopedVariableProvider
{
    /// <summary>
    /// Reads the current value of <paramref name="reference"/> when its declaring scope is visible
    /// from the current context (assigned value if set, otherwise the variable's default);
    /// otherwise returns <c>false</c>.
    /// </summary>
    bool TryGetScopedVariableValue(VariableReference reference, out object? value);

    /// <summary>
    /// Assigns <paramref name="value"/> to <paramref name="reference"/> in its owning scope when that
    /// scope is visible from the current context; otherwise returns <c>false</c> (the scope-boundary
    /// guard that rejects assignment to sibling or unrelated container scopes).
    /// </summary>
    bool TrySetScopedVariableValue(VariableReference reference, object? value);
}
