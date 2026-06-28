using Elsa.Expressions.Core.Contracts;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// One concrete container activity execution contributing a variable scope to a descendant's
/// visible scope chain (ADR 0027, #210). Layers are supplied outermost-first by the runtime, which
/// reads the declared variables from the container's compiled executable structure and the current
/// values from the container execution's persisted snapshot (its single source of truth).
/// </summary>
/// <param name="ScopeId">The declaring scope identity — the container's executable node id, or the workflow-scope sentinel.</param>
/// <param name="ExecutionId">The concrete container <c>ActivityExecutionId</c>, or <c>null</c> for the workflow scope.</param>
/// <param name="Variables">The variables this scope declares, keyed by reference key.</param>
/// <param name="Values">The current values for this scope (assigned values; absent keys fall back to defaults).</param>
public sealed record RuntimeContainerScopeLayer(
    string ScopeId,
    string? ExecutionId,
    IReadOnlyDictionary<string, IVariable> Variables,
    IReadOnlyDictionary<string, object?> Values);
