using Elsa.Workflows.Runtime.Core.Contracts.Alterations;

namespace Elsa.Workflows.Runtime.Core.Models.Alterations;

/// <summary>
/// Per-job mutable projection used solely during complete alteration preflight. It is never durable state: handlers
/// use it to make later preflights observe earlier staged changes, and the checkpoint writer applies the staged
/// changes only after every preflight accepts.
/// </summary>
public sealed class WorkflowAlterationProjectedState : IWorkflowAlterationProjectedState
{
    private readonly IReadOnlyDictionary<Type, object> _originalStates;
    private readonly Dictionary<Type, object> _states;

    public WorkflowAlterationProjectedState(IEnumerable<object>? initialStates = null)
    {
        _states = new Dictionary<Type, object>();
        foreach (var state in initialStates ?? [])
            SetUntyped(state);
        _originalStates = new Dictionary<Type, object>(_states);
    }

    public bool TryGetOriginal<TState>(out TState? state) where TState : class
    {
        if (_originalStates.TryGetValue(typeof(TState), out var value))
        {
            state = (TState)value;
            return true;
        }

        state = null;
        return false;
    }

    public TState GetRequiredOriginal<TState>() where TState : class =>
        TryGetOriginal<TState>(out var state)
            ? state!
            : throw new InvalidOperationException($"No original state of type '{typeof(TState).FullName}' is available for this alteration job.");

    public bool TryGet<TState>(out TState? state) where TState : class
    {
        if (_states.TryGetValue(typeof(TState), out var value))
        {
            state = (TState)value;
            return true;
        }

        state = null;
        return false;
    }

    public TState GetRequired<TState>() where TState : class =>
        TryGet<TState>(out var state)
            ? state!
            : throw new InvalidOperationException($"No projected state of type '{typeof(TState).FullName}' is available for this alteration job.");

    public void Set<TState>(TState state) where TState : class => SetUntyped(state);

    private void SetUntyped(object state)
    {
        ArgumentNullException.ThrowIfNull(state);
        _states[state.GetType()] = state;
    }
}
