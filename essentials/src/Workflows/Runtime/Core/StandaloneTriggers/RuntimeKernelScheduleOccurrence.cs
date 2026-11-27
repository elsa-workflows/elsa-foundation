using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.StandaloneTriggers;

/// <summary>An occurrence offered by the owned schedule actor against an exact published workflow.</summary>
public sealed record RuntimeKernelScheduleOccurrence(
    TriggerScope Scope,
    string ScheduleKey,
    string ScheduleReference,
    string SlotName,
    string ActivationId,
    long PublicationRevision,
    long ArmGeneration,
    string TimerDeliveryId,
    DateTimeOffset Due,
    DateTimeOffset AcceptBefore,
    WorkflowExecutableIdentity Executable,
    string TriggerNode,
    string PinnedStart)
{
    public const string ReservationType = "kernel-schedule-publication-v1";

    public static string ReservationKey(string scheduleKey) => WorkflowTriggerBinding.BuildId(ReservationType, scheduleKey, "owner");

    public string Id => RuntimeDeliveryFingerprint.Compute("elsa.runtime.kernel-schedule-occurrence.v1",
        new { Scope, ScheduleReference, ArmGeneration, TimerDeliveryId });
}

/// <summary>Commits a schedule occurrence and its retained executable under the same publication transaction boundary.</summary>
public interface IRuntimeKernelScheduleOccurrenceStore
{
    /// <returns>True when the exact occurrence was accepted or is an immutable replay. False when authority has moved or expired.</returns>
    ValueTask<bool> TryAcceptAsync(RuntimeKernelScheduleOccurrence occurrence, CancellationToken cancellationToken = default);
}

/// <summary>The stable seek position of an accepted occurrence within its tenant.</summary>
public sealed record RuntimeKernelScheduleOccurrenceCursor(long AcceptedAtUtcTicks, string Id);

/// <summary>A persisted admission and its position in delivery order.</summary>
public sealed record RuntimeKernelScheduleOccurrenceDelivery(RuntimeKernelScheduleOccurrence Occurrence, RuntimeKernelScheduleOccurrenceCursor Cursor);

/// <summary>Relays immutable accepted occurrences to the Actor Foundation.</summary>
public interface IRuntimeKernelScheduleOccurrenceDeliveryStore
{
    ValueTask<IReadOnlyList<RuntimeKernelScheduleOccurrenceDelivery>> ListPendingAsync(TriggerScope scope,
        RuntimeKernelScheduleOccurrenceCursor? after, int limit, CancellationToken cancellationToken = default);

    ValueTask MarkMaterializedAsync(RuntimeKernelScheduleOccurrence occurrence, string actorReference, CancellationToken cancellationToken = default);
}

/// <summary>Exact executable roots held by accepted occurrences until their resulting actor retires.</summary>
public interface IRuntimeKernelScheduleRetentionRoots
{
    ValueTask<IReadOnlyCollection<string>> ListPinnedExecutableArtifactIdsAsync(CancellationToken cancellationToken = default);
}
