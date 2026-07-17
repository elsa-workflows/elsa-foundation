using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using System.Collections.ObjectModel;

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
        IReadOnlyDictionary<string, string>? privateMetadata = null)
    {
        Kind = kind;
        OutcomeName = outcomeName;
        Fault = fault;
        CancellationReason = cancellationReason;
        PrivateMetadata = privateMetadata is null
            ? ReadOnlyDictionary<string, string>.Empty
            : new ReadOnlyDictionary<string, string>(privateMetadata.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal));
    }

    public RuntimeStructuralContinuationKind Kind { get; }
    public bool IsComplete => Kind == RuntimeStructuralContinuationKind.Complete;
    public bool IsDeferred => Kind == RuntimeStructuralContinuationKind.Defer;
    public string? OutcomeName { get; }
    public ActivityFault? Fault { get; }
    public string? CancellationReason { get; }
    /// <summary>
    /// Runtime-owned metadata staged by the structural engine. The runtime folds this patch into the
    /// activity execution state in the same checkpoint as the returned decision and child schedule intents.
    /// </summary>
    public IReadOnlyDictionary<string, string> PrivateMetadata { get; }

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
    /// Returns a new continuation carrying structural private-state metadata. This does not mutate the
    /// activity execution state; the runtime applies the patch only when it commits the continuation.
    /// </summary>
    public RuntimeStructuralContinuation WithPrivateMetadata(IReadOnlyDictionary<string, string> metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        var merged = PrivateMetadata.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        foreach (var (key, value) in metadata)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(key);
            ArgumentNullException.ThrowIfNull(value);
            merged[key] = value;
        }

        return new RuntimeStructuralContinuation(Kind, OutcomeName, Fault, CancellationReason, merged);
    }
}

public enum RuntimeStructuralContinuationKind
{
    Complete,
    Defer,
    Fault,
    Cancel
}
