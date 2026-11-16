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
        string? outcomeName,
        ActivityFault? fault,
        string? cancellationReason,
        RuntimeStructuralStateUpdate? stateUpdate = null,
        RuntimeChildControlBatch? control = null, IReadOnlyList<RuntimeHistorySourceFact>? historySourceFacts = null,
        Func<DateTimeOffset, IEnumerable<RuntimeHistorySourceFact>>? historySourceFactory = null)
    {
        Kind = kind;
        OutcomeName = outcomeName;
        Fault = fault;
        CancellationReason = cancellationReason;
        StateUpdate = stateUpdate;
        Control = control;
        HistorySourceFacts = historySourceFacts is null ? null : Array.AsReadOnly(historySourceFacts.ToArray());
        _historySourceFactory = historySourceFactory;
    }

    public RuntimeStructuralContinuationKind Kind { get; }
    public bool IsComplete => Kind == RuntimeStructuralContinuationKind.Complete;
    public bool IsDeferred => Kind == RuntimeStructuralContinuationKind.Defer;
    public string? OutcomeName { get; }
    public ActivityFault? Fault { get; }
    public string? CancellationReason { get; }
    /// <summary>One typed structural-state document staged for the continuation checkpoint.</summary>
    public RuntimeStructuralStateUpdate? StateUpdate { get; }

    /// <summary>Optional authored control staged separately from private semantic state and runtime fault diagnostics.</summary>
    public RuntimeChildControlBatch? Control { get; }
    public IReadOnlyList<RuntimeHistorySourceFact>? HistorySourceFacts { get; }
    private readonly Func<DateTimeOffset, IEnumerable<RuntimeHistorySourceFact>>? _historySourceFactory;

    public static RuntimeStructuralContinuation Defer { get; } = new(RuntimeStructuralContinuationKind.Defer, null, null, null);

    public static RuntimeStructuralContinuation Complete(string outcomeName = ActivityOutcomes.Done)
    {
        if (string.IsNullOrWhiteSpace(outcomeName))
            throw new ArgumentException("Structural completion outcome name cannot be blank.", nameof(outcomeName));

        return new RuntimeStructuralContinuation(RuntimeStructuralContinuationKind.Complete, outcomeName, null, null);
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
            OutcomeName,
            Fault,
            CancellationReason,
            new RuntimeStructuralStateUpdate(stateVersion, value),
            Control, HistorySourceFacts, _historySourceFactory);
    }

    public RuntimeStructuralContinuation WithHistorySourceFacts(IReadOnlyList<RuntimeHistorySourceFact> facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        return new(Kind, OutcomeName, Fault, CancellationReason, StateUpdate, Control, facts, _historySourceFactory);
    }

    /// <summary>Stages the owner decision before its continuation facts, without invoking optional capture on an uncaptured execution.</summary>
    public RuntimeStructuralContinuation WithHistorySourceFacts(Func<DateTimeOffset, IEnumerable<RuntimeHistorySourceFact>> facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        return new(Kind, OutcomeName, Fault, CancellationReason, StateUpdate, Control, HistorySourceFacts,
            at => facts(at).Concat(_historySourceFactory?.Invoke(at) ?? []));
    }

    /// <summary>Materializes the transient owner contribution only at the admitted raw checkpoint handoff.</summary>
    public IReadOnlyList<RuntimeHistorySourceFact> MaterializeHistorySourceFacts(DateTimeOffset observedAt) =>
        Array.AsReadOnly((HistorySourceFacts ?? []).Concat(_historySourceFactory?.Invoke(observedAt) ?? []).ToArray());

    /// <summary>Returns a new decision with an indivisible control emission; this does not grant dispatch authority.</summary>
    public RuntimeStructuralContinuation WithControl(RuntimeChildControlBatch control)
    {
        ArgumentNullException.ThrowIfNull(control);
        if (control.IsTerminal == IsDeferred)
            throw new ArgumentException("Control terminal status must match the structural continuation.", nameof(control));
        if (control.AuthoredFailureCode is not null && Kind != RuntimeStructuralContinuationKind.Fault)
            throw new ArgumentException("Authored failure classification requires a fault continuation.", nameof(control));
        return new RuntimeStructuralContinuation(Kind, OutcomeName, Fault, CancellationReason, StateUpdate, control, HistorySourceFacts, _historySourceFactory);
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
