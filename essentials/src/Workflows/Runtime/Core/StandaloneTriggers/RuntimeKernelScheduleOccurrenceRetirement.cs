namespace Elsa.Workflows.Runtime.Core.StandaloneTriggers;

/// <summary>An accepted occurrence whose exact Actor Foundation birth has been acknowledged.</summary>
public sealed record RuntimeKernelScheduleMaterializedOccurrence(RuntimeKernelScheduleOccurrence Occurrence, string ActorReference,
    RuntimeKernelScheduleOccurrenceCursor Cursor);

/// <summary>Releases executable pins after a trusted physical retirement proof.</summary>
public interface IRuntimeKernelScheduleOccurrenceRetirementStore
{
    /// <summary>Reads one bounded page of materialized, still pinned occurrences. Restart each pass with a null cursor.</summary>
    ValueTask<IReadOnlyList<RuntimeKernelScheduleMaterializedOccurrence>> ListMaterializedAsync(TriggerScope scope,
        RuntimeKernelScheduleOccurrenceCursor? after, int limit, CancellationToken cancellationToken = default);

    /// <summary>Removes the pin and scrubs the accepted start when its exact physical retirement is confirmed.</summary>
    /// <param name="occurrence">The immutable accepted occurrence.</param>
    /// <param name="actorReference">The exact materialized actor reference.</param>
    /// <param name="confirmRetired">The bound provider's proof of irreversible retirement and release of dependent work.</param>
    /// <param name="cancellationToken">Cancellation for the current attempt.</param>
    ValueTask<bool> TryRetireAsync(RuntimeKernelScheduleOccurrence occurrence, string actorReference,
        Func<RuntimeKernelScheduleOccurrence, string, CancellationToken, ValueTask<bool>> confirmRetired,
        CancellationToken cancellationToken = default);
}
