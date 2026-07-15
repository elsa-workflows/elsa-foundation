using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Runtime seam for an ordinary activity boundary that contributes durable values to the existing
/// entry and completion checkpoints. Implementations prepare state only; the scheduler owns commits.
/// </summary>
public interface IRuntimeActivityCheckpointParticipant
{
    ValueTask<IReadOnlyCollection<RuntimeStateChange<DurableValueState>>> PrepareEntryCheckpointAsync(
        IRuntimeActivityExecutionContext context,
        IReadOnlyDictionary<string, object?> effectiveInputs,
        DateTimeOffset capturedAt,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyCollection<RuntimeStateChange<DurableValueState>>> PrepareCompletionCheckpointAsync(
        IRuntimeActivityExecutionContext context,
        IReadOnlyCollection<DurableValueState> persistedValues,
        DateTimeOffset capturedAt,
        CancellationToken cancellationToken = default);
}
