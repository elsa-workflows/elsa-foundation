using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

/// <summary>
/// Builds the per-iteration <see cref="VariableScope"/> a loop activity threads onto its body for one
/// pass (#259 — the shared primitive the <c>ForEach</c>/<c>For</c>/<c>While</c>/<c>Do</c> activities of
/// #264–#267 depend on). The scope declares a current-item variable (and optional zero-based index)
/// addressed by reference key, layered as the innermost scope on top of the loop's existing visible
/// chain (its enclosing container/workflow variables, ADR 0027).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why iterations do not collide.</b> Each produced scope reuses the loop owner's executable node id
/// as its <see cref="VariableScope.ScopeId"/> (so the same reference keys resolve every pass) but is
/// keyed by the engine <c>IterationId</c> as its <see cref="VariableScope.ExecutionId"/>. The item value
/// lives in that per-iteration scope's own value store, so pass <c>N</c> and pass <c>N+1</c> are distinct
/// <see cref="VariableScope"/> instances and one pass's current item can never overwrite or be read as
/// another's — exactly the per-execution isolation <see cref="VariableScope.ExecutionId"/> documents for
/// container scopes, applied to a loop body. The loop owner is responsible for assigning a distinct
/// <see cref="LoopIterationScopeRequest.IterationId"/> per pass (the value it also threads through
/// <see cref="ActivitySchedulingProvenance.IterationId"/> when scheduling the body), so the body
/// activity's <see cref="ActivityExecutionState.IterationId"/> and the scope agree.
/// </para>
/// <para>
/// <b>Contract for #264–#267.</b> A loop activity, for each pass: (1) chooses a distinct
/// <c>IterationId</c>; (2) calls <see cref="BuildIterationScope"/> with the loop body's enclosing scope
/// chain (the scope the runtime already threaded into the loop's own execution) as <c>parent</c>; and
/// (3) schedules the body child with that <c>IterationId</c> in
/// <see cref="ActivitySchedulingProvenance.IterationId"/>. The runtime then resolves the body's
/// <c>currentItem</c>/<c>index</c> through this scope by reference key. The item and index are read-only
/// runtime state for the pass; assigning to them is the loop owner's prerogative on the next pass, not
/// the body's. Mutations the body makes to <em>enclosing</em> (container/workflow) variables flow through
/// the unchanged ADR 0027 chain — this factory only adds the innermost iteration layer.
/// </para>
/// </remarks>
public sealed class RuntimeLoopIterationScopeFactory
{
    /// <summary>
    /// Produces the iteration scope for one loop pass: an innermost <see cref="VariableScope"/> declaring
    /// the current item (and optional index), chained to <paramref name="parent"/> (the loop body's
    /// enclosing visible scope, or <c>null</c> when the loop has no enclosing container/workflow scope).
    /// The returned scope's <see cref="VariableScope.ExecutionId"/> is the request's iteration id, which
    /// isolates this pass's values from every other pass of the same loop.
    /// </summary>
    public VariableScope BuildIterationScope(LoopIterationScopeRequest request, VariableScope? parent = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrEmpty(request.OwnerNodeId);
        ArgumentException.ThrowIfNullOrEmpty(request.IterationId);
        ArgumentException.ThrowIfNullOrEmpty(request.ItemReferenceKey);
        ArgumentException.ThrowIfNullOrEmpty(request.ItemName);

        var variables = new Dictionary<string, IVariable>(StringComparer.Ordinal)
        {
            [request.ItemReferenceKey] = new LoopIterationVariable(request.ItemName, request.ItemReferenceKey)
        };
        var values = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [request.ItemReferenceKey] = request.Item
        };

        if (!string.IsNullOrEmpty(request.IndexReferenceKey))
        {
            ArgumentException.ThrowIfNullOrEmpty(request.IndexName, nameof(request.IndexName));

            if (StringComparer.Ordinal.Equals(request.IndexReferenceKey, request.ItemReferenceKey))
                throw new ArgumentException("Loop iteration item and index cannot share a reference key.", nameof(request));

            variables[request.IndexReferenceKey] = new LoopIterationVariable(request.IndexName, request.IndexReferenceKey);
            values[request.IndexReferenceKey] = request.Index;
        }

        return new VariableScope(
            request.OwnerNodeId,
            variables,
            parent: parent,
            executionId: request.IterationId,
            initialValues: values);
    }

    /// <summary>
    /// Minimal <see cref="IVariable"/> for a loop iteration declaration. Carries the declaration's bare
    /// name and reference key (as the memory block id) without depending on the activity authoring stack,
    /// mirroring the runtime-scoped variable the container-scope factory emits.
    /// </summary>
    private sealed class LoopIterationVariable(string name, string referenceKey) : IVariable
    {
        public string Id { get; set; } = referenceKey;
        public string Name { get; set; } = name;
        public object? DefaultValue { get; set; }
        public Type? StorageDriverType { get; set; }

        public IMemoryBlock Declare() => new LoopIterationVariableBlock(DefaultValue);

        public T? Get<T>(IMemoryRegister memoryRegister, IExpressionExecutionContext context) =>
            DefaultValue is T typed ? typed : default;

        public T? Get<T>(IExpressionExecutionContext context) => DefaultValue is T typed ? typed : default;
    }

    private sealed class LoopIterationVariableBlock(object? value) : IMemoryBlock
    {
        public object? Value { get; set; } = value;
        public object? Metadata { get; set; }
    }
}
