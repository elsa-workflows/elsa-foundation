using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Constants;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// Immutable engine decision returned by a structural activity invocation.
/// </summary>
public sealed class RuntimeStructuralContinuation
{
    private RuntimeStructuralContinuation(
        RuntimeStructuralContinuationKind kind,
        OutcomeSelection? selection,
        ActivityFault? fault,
        string? cancellationReason,
        RuntimeStructuralStateUpdate? stateUpdate = null,
        RuntimeChildControlBatch? control = null)
    {
        Kind = kind;
        Selection = selection;
        Fault = fault;
        CancellationReason = cancellationReason;
        StateUpdate = stateUpdate;
        Control = control;
    }

    public RuntimeStructuralContinuationKind Kind { get; }
    public bool IsComplete => Kind == RuntimeStructuralContinuationKind.Complete;
    public bool IsDeferred => Kind == RuntimeStructuralContinuationKind.Defer;
    /// <summary>The outcome selection of a completion; <see langword="null"/> for any other kind.</summary>
    public OutcomeSelection? Selection { get; }

    /// <summary>The names a routing parent follows for a completion (<c>[Done]</c>, the names, or none); empty otherwise.</summary>
    public IReadOnlyList<string> OutcomeNames => Selection?.ResolveNames() ?? [];
    public ActivityFault? Fault { get; }
    public string? CancellationReason { get; }
    /// <summary>One typed structural-state document staged for the continuation checkpoint.</summary>
    public RuntimeStructuralStateUpdate? StateUpdate { get; }

    /// <summary>Optional authored control staged separately from private semantic state and runtime fault diagnostics.</summary>
    public RuntimeChildControlBatch? Control { get; }

    public static RuntimeStructuralContinuation Defer { get; } = new(RuntimeStructuralContinuationKind.Defer, null, null, null);

    /// <summary>Completes without choosing a branch: an unspecified selection, which routes <c>Done</c>.</summary>
    public static RuntimeStructuralContinuation Complete() => Complete(OutcomeSelection.Unspecified);

    /// <summary>Completes and selects one named outcome.</summary>
    public static RuntimeStructuralContinuation Complete(string outcomeName)
    {
        if (string.IsNullOrWhiteSpace(outcomeName))
            throw new ArgumentException("Structural completion outcome name cannot be blank.", nameof(outcomeName));

        return Complete(OutcomeSelection.Named(outcomeName));
    }

    /// <summary>Completes with an explicit selection (unspecified, named or empty).</summary>
    public static RuntimeStructuralContinuation Complete(OutcomeSelection selection)
    {
        ArgumentNullException.ThrowIfNull(selection);
        return new RuntimeStructuralContinuation(RuntimeStructuralContinuationKind.Complete, selection, null, null);
    }

    public static RuntimeStructuralContinuation Faulted(ActivityFault fault) =>
        new(RuntimeStructuralContinuationKind.Fault, null, fault ?? throw new ArgumentNullException(nameof(fault)), null);

    public static RuntimeStructuralContinuation Cancel(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        return new RuntimeStructuralContinuation(RuntimeStructuralContinuationKind.Cancel, null, null, reason);
    }

    /// <summary>
    /// Returns a new continuation carrying one complete structural private-state document. This does not
    /// mutate execution state; the runtime applies it only when it commits the continuation.
    /// </summary>
    public RuntimeStructuralContinuation WithState(ValueEnvelope value, int stateVersion = 1)
    {
        return new RuntimeStructuralContinuation(
            Kind,
            Selection,
            Fault,
            CancellationReason,
            new RuntimeStructuralStateUpdate(stateVersion, value),
            Control);
    }

    /// <summary>Returns a new decision with an indivisible control emission; this does not grant dispatch authority.</summary>
    public RuntimeStructuralContinuation WithControl(RuntimeChildControlBatch control)
    {
        ArgumentNullException.ThrowIfNull(control);
        if (control.IsTerminal == IsDeferred)
            throw new ArgumentException("Control terminal status must match the structural continuation.", nameof(control));
        if (control.AuthoredFailureCode is not null && Kind != RuntimeStructuralContinuationKind.Fault)
            throw new ArgumentException("Authored failure classification requires a fault continuation.", nameof(control));
        return new RuntimeStructuralContinuation(Kind, Selection, Fault, CancellationReason, StateUpdate, control);
    }
}

/// <summary>Typed, versioned state produced by one structural evaluation.</summary>
public sealed record RuntimeStructuralStateUpdate
{
    public RuntimeStructuralStateUpdate(int stateVersion, ValueEnvelope value)
    {
        if (stateVersion <= 0)
            throw new ArgumentOutOfRangeException(nameof(stateVersion), stateVersion, "Structural state version must be positive.");
        ArgumentNullException.ThrowIfNull(value);
        if (value.Presence != ValuePresence.Present || value.InlineValue is null)
            throw new ArgumentException("Structural state must be a present inline document.", nameof(value));
        if (value.Policy.Lifecycle == DurableValueLifecycle.None || value.Policy.Storage != DurableValueStorage.Inline)
            throw new ArgumentException("Structural state must use a persistable inline policy.", nameof(value));

        StateVersion = stateVersion;
        Value = value;
    }

    public int StateVersion { get; }
    public ValueEnvelope Value { get; }
}

public enum RuntimeStructuralContinuationKind
{
    Complete,
    Defer,
    Fault,
    Cancel
}
