using System.Text.Json.Serialization;
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

    /// <summary>When accepted by a successor, the immutable original offer that owns the timer identity.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public RuntimeKernelScheduleOccurrenceOrigin? Origin { get; init; }

    public string Id => RuntimeDeliveryFingerprint.Compute("elsa.runtime.kernel-schedule-occurrence.v1",
        new { Scope = Origin is { } origin ? Scope with { TenantId = origin.TenantId } : Scope, ScheduleReference, ArmGeneration, TimerDeliveryId });
}

/// <summary>A hash-only link to the original offer, retained even after physical retirement.</summary>
public sealed record RuntimeKernelScheduleOccurrenceOrigin(string? TenantId, string Fingerprint);

/// <summary>Commits a schedule occurrence and its retained executable under the same publication transaction boundary.</summary>
public interface IRuntimeKernelScheduleOccurrenceStore
{
    /// <param name="occurrence">The authenticated offer.</param>
    /// <param name="readScheduleClock">The bound Actor Foundation clock that issued the due time and deadline.</param>
    /// <param name="cancellationToken">Cancellation for the current attempt.</param>
    /// <returns>True when the exact occurrence was accepted or is an immutable replay. False when authority has moved or expired.</returns>
    ValueTask<bool> TryAcceptAsync(RuntimeKernelScheduleOccurrence occurrence,
        Func<CancellationToken, ValueTask<DateTimeOffset>> readScheduleClock, CancellationToken cancellationToken = default);

    /// <summary>Decides the original occurrence within a separately fenced job deadline.</summary>
    /// <remarks>Replays retain the original occurrence bytes, including their first attempt's deadline.</remarks>
    async ValueTask<RuntimeKernelScheduleAttemptOutcome> TryAcceptAttemptAsync(RuntimeKernelScheduleOccurrence occurrence,
        DateTimeOffset attemptDeadline, Func<CancellationToken, ValueTask<DateTimeOffset>> readScheduleClock, CancellationToken cancellationToken = default)
    {
        if (attemptDeadline != occurrence.AcceptBefore)
            throw new NotSupportedException("The Runtime provider does not support renewed schedule attempts.");
        // The predecessor contract cannot distinguish an expired attempt from displaced authority.
        return await TryAcceptAsync(occurrence, readScheduleClock, cancellationToken)
            ? RuntimeKernelScheduleAttemptOutcome.Accepted
            : RuntimeKernelScheduleAttemptOutcome.Retry;
    }

    /// <summary>Decides the same timer under a currently authorized successor publication.</summary>
    ValueTask<RuntimeKernelScheduleAttemptOutcome> TryHandoffAttemptAsync(RuntimeKernelScheduleOccurrence origin,
        RuntimeKernelScheduleOccurrence successor, string continuity, DateTimeOffset attemptDeadline,
        Func<CancellationToken, ValueTask<DateTimeOffset>> readScheduleClock, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The Runtime provider does not support schedule publication handoff.");
}

/// <summary>Whether a managed schedule attempt resolved its immutable occurrence.</summary>
public enum RuntimeKernelScheduleAttemptOutcome
{
    /// <summary>The window expired or the provider could not settle the original occurrence.</summary>
    Retry,
    /// <summary>The original occurrence committed or replayed its exact receipt.</summary>
    Accepted,
    /// <summary>The original occurrence no longer owns its selected publication.</summary>
    Refused
}

/// <summary>A seek position within one pass over the pending occurrences of a bound tenant.</summary>
public sealed record RuntimeKernelScheduleOccurrenceCursor(long AcceptedAtUtcTicks, string Id);

/// <summary>A persisted admission and its position in delivery order.</summary>
public sealed record RuntimeKernelScheduleOccurrenceDelivery(RuntimeKernelScheduleOccurrence Occurrence, RuntimeKernelScheduleOccurrenceCursor Cursor);

/// <summary>Relays immutable accepted occurrences to the Actor Foundation.</summary>
public interface IRuntimeKernelScheduleOccurrenceDeliveryStore
{
    /// <summary>Reads a page of pending admissions. Begin every new scan with a null cursor.</summary>
    /// <remarks>Commit order can differ from admission time, so rows published behind an in-flight cursor appear on the next scan.</remarks>
    ValueTask<IReadOnlyList<RuntimeKernelScheduleOccurrenceDelivery>> ListPendingAsync(TriggerScope scope,
        RuntimeKernelScheduleOccurrenceCursor? after, int limit, CancellationToken cancellationToken = default);

    /// <summary>Reads the inclusive end of a delivery pass, or null when there are no pending admissions.</summary>
    ValueTask<RuntimeKernelScheduleOccurrenceCursor?> ReadPendingTailAsync(TriggerScope scope,
        CancellationToken cancellationToken = default) => throw new NotSupportedException();

    /// <summary>Reads a page of pending admissions through the end captured at the start of this pass.</summary>
    ValueTask<IReadOnlyList<RuntimeKernelScheduleOccurrenceDelivery>> ListPendingAsync(TriggerScope scope,
        RuntimeKernelScheduleOccurrenceCursor? after, RuntimeKernelScheduleOccurrenceCursor through, int limit,
        CancellationToken cancellationToken = default) => throw new NotSupportedException();

    ValueTask MarkMaterializedAsync(RuntimeKernelScheduleOccurrence occurrence, string actorReference, CancellationToken cancellationToken = default);
}

/// <summary>Exact executable roots held by accepted occurrences until their resulting actor retires.</summary>
public interface IRuntimeKernelScheduleRetentionRoots
{
    ValueTask<IReadOnlyCollection<string>> ListPinnedExecutableArtifactIdsAsync(CancellationToken cancellationToken = default);
}
