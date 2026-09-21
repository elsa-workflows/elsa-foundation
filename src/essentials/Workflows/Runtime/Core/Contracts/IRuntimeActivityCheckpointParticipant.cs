using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Runtime seam for an ordinary activity boundary that contributes durable values to the existing
/// entry and completion checkpoints. Implementations prepare state only; the scheduler owns commits.
/// </summary>
public interface IRuntimeActivityCheckpointParticipant
{
    /// <remarks>
    /// <paramref name="effectiveInputs"/> contains the portable values from the committed value-flow
    /// snapshot (typically JSON materializations), not CLR-hydrated activity property values.
    /// </remarks>
    ValueTask<IReadOnlyCollection<RuntimeStateChange<DurableValueState>>> PrepareEntryCheckpointAsync(
        IRuntimeActivityExecutionContext context,
        IReadOnlyDictionary<string, object?> effectiveInputs,
        DateTimeOffset capturedAt,
        CancellationToken cancellationToken = default);

    ValueTask<RuntimeActivityCompletionCheckpointPreparation> PrepareCompletionCheckpointAsync(
        IRuntimeActivityExecutionContext context,
        IReadOnlyCollection<DurableValueState> persistedValues,
        DateTimeOffset capturedAt,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Completion state and the typed completion decision that must be projected and committed atomically.
/// </summary>
public sealed record RuntimeActivityCompletionCheckpointPreparation
{
    public RuntimeActivityCompletionCheckpointPreparation(
        IReadOnlyCollection<RuntimeStateChange<DurableValueState>> durableValueChanges,
        ActivityTransition transition)
    {
        ArgumentNullException.ThrowIfNull(durableValueChanges);
        ArgumentNullException.ThrowIfNull(transition);
        if (transition is not IActivityCompletionTransition)
            throw new ArgumentException("A completion checkpoint participant must return a successful completion transition.", nameof(transition));

        DurableValueChanges = Array.AsReadOnly(durableValueChanges.ToArray());
        Transition = transition;
    }

    public IReadOnlyCollection<RuntimeStateChange<DurableValueState>> DurableValueChanges { get; }
    public ActivityTransition Transition { get; }
}
