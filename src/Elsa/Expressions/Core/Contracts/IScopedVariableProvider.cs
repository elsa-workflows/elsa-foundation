using Elsa.Expressions.Core.Models;

namespace Elsa.Expressions.Core.Contracts;

/// <summary>
/// Implemented by expression execution contexts that expose a scoped-variable chain (workflow scope
/// plus any visible ancestor container scopes, ADR 0027). Lets the variable expression handler
/// resolve a structured <see cref="VariableReference"/> to the concrete variable in its declaring
/// scope, honouring nearest-scope visibility and shadowing.
/// </summary>
public interface IScopedVariableProvider
{
    /// <summary>
    /// Resolves <paramref name="reference"/> to the variable declared by its declaring scope when
    /// that scope is visible from the current context; otherwise returns <c>false</c>.
    /// </summary>
    bool TryGetScopedVariable(VariableReference reference, out IVariable? variable);
}
