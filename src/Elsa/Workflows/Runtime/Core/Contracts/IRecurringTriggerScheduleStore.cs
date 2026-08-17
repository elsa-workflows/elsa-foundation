using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Durable store for <see cref="RecurringTriggerSchedule"/> documents — the recurring-start counterpart to
/// <see cref="IDurableTimerStore"/> (W16). The default in-memory implementation keeps schedules only for the
/// process lifetime; a durable persistence provider (the Groundwork bridge) swaps in a restart-surviving
/// implementation so a Timer/Cron start trigger keeps firing across process restarts.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="SaveAsync"/> is an idempotent upsert keyed by <see cref="RecurringTriggerSchedule.ScheduleId"/>.
/// Republishing an artifact replaces its schedules through <see cref="DeleteByArtifactAsync"/> then re-save,
/// mirroring how the trigger index replaces a republished artifact's bindings.
/// </para>
/// <para>
/// <see cref="ListDueAsync"/> returns schedules whose <see cref="RecurringTriggerSchedule.NextOccurrence"/> is
/// at or before the supplied instant, ordered by (NextOccurrence, ScheduleId) and capped by <paramref name="limit"/>.
/// </para>
/// <para>
/// <see cref="TryAdvanceAsync"/> is the compare-and-swap that claims an occurrence: it advances the cursor only
/// if the stored <see cref="RecurringTriggerSchedule.NextOccurrence"/> still equals the caller's expected value,
/// so at most one worker fires a given occurrence. This is the seam a future clustered store keeps to make the
/// pump cluster-safe (W20) without changing the pump.
/// </para>
/// </remarks>
public interface IRecurringTriggerScheduleStore
{
    /// <summary>Upserts a schedule (keyed by <see cref="RecurringTriggerSchedule.ScheduleId"/>) and returns the stored schedule.</summary>
    ValueTask<RecurringTriggerSchedule> SaveAsync(RecurringTriggerSchedule schedule, CancellationToken cancellationToken = default);

    /// <summary>Atomically replaces one activation's prepared schedules without exposing them to the pump.</summary>
    ValueTask PrepareActivationAsync(
        string activationId,
        IReadOnlyCollection<RecurringTriggerSchedule> schedules,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromException(new NotSupportedException("This recurring-schedule store does not support activation-scoped preparation."));

    /// <summary>Returns one finite, ordered page of prepared or active schedules owned by an activation.</summary>
    ValueTask<RuntimeStorePage<RecurringTriggerSchedule>> ListByActivationPageAsync(
        RecurringTriggerScheduleActivationPageQuery query,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromException<RuntimeStorePage<RecurringTriggerSchedule>>(
            new NotSupportedException("This recurring-schedule store does not support activation-scoped pages."));

    /// <summary>Returns one finite, ordered page of schedules owned by an artifact.</summary>
    ValueTask<RuntimeStorePage<RecurringTriggerSchedule>> ListByArtifactPageAsync(
        RecurringTriggerScheduleArtifactPageQuery query,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromException<RuntimeStorePage<RecurringTriggerSchedule>>(
            new NotSupportedException("This recurring-schedule store does not support artifact-scoped pages."));

    /// <summary>Legacy complete traversal for activation-oriented commands.</summary>
    async ValueTask<IReadOnlyCollection<RecurringTriggerSchedule>> ListByActivationAsync(
        string activationId,
        CancellationToken cancellationToken = default) =>
        await RuntimeOperationalStorePagingExtensions.ListAllByActivationAsync(this, activationId, cancellationToken);

    /// <summary>Activates one activation and deactivates only the explicitly replaced activation.</summary>
    ValueTask ActivateAsync(
        string activationId,
        string? replacedActivationId,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromException(new NotSupportedException("This recurring-schedule store does not support activation-scoped activation."));

    /// <summary>Deletes schedules owned by one activation without affecting another activation of the artifact.</summary>
    ValueTask DeleteByActivationAsync(string activationId, CancellationToken cancellationToken = default) =>
        ValueTask.FromException(new NotSupportedException("This recurring-schedule store does not support activation-scoped deletion."));

    /// <summary>Returns due schedules (NextOccurrence &lt;= <paramref name="asOf"/>), ordered by next occurrence then id, capped by <paramref name="limit"/>.</summary>
    ValueTask<IReadOnlyCollection<RecurringTriggerSchedule>> ListDueAsync(DateTimeOffset asOf, int limit, CancellationToken cancellationToken = default);

    /// <summary>Finds a single schedule by its id, or <c>null</c> if it does not exist.</summary>
    ValueTask<RecurringTriggerSchedule?> FindAsync(string scheduleId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Compare-and-swap advance of the fire cursor: sets <see cref="RecurringTriggerSchedule.NextOccurrence"/> to
    /// <paramref name="newNextOccurrence"/> only if the stored value still equals <paramref name="expectedNextOccurrence"/>.
    /// Returns <c>true</c> when this caller won the claim, <c>false</c> when the schedule is gone or another
    /// worker already advanced it.
    /// </summary>
    ValueTask<bool> TryAdvanceAsync(string scheduleId, DateTimeOffset expectedNextOccurrence, DateTimeOffset newNextOccurrence, CancellationToken cancellationToken = default);

    /// <summary>Deletes every schedule owned by an artifact. Deleting for an unknown artifact is a no-op.</summary>
    ValueTask DeleteByArtifactAsync(string artifactId, CancellationToken cancellationToken = default);

    /// <summary>Deletes a single schedule by its id. Deleting a missing schedule is a no-op.</summary>
    ValueTask DeleteAsync(string scheduleId, CancellationToken cancellationToken = default);
}
