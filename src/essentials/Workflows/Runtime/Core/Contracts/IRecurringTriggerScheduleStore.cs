using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Durable store for <see cref="RecurringTriggerSchedule"/> documents — the recurring-start counterpart to
/// <see cref="IDurableTimerStore"/>. The default in-memory implementation keeps schedules only for the
/// process lifetime; a durable persistence provider (the Runtime EF Core module) swaps in a restart-surviving
/// implementation so a Timer/Cron start trigger keeps firing across process restarts.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="SaveAsync"/> is an idempotent upsert keyed by <see cref="RecurringTriggerSchedule.ScheduleId"/>.
/// Republishing an artifact replaces its schedules through <see cref="DeleteByArtifactAsync"/> then re-save,
/// mirroring how the trigger index replaces a republished artifact's bindings.
/// </para>
/// <para>
/// <b>At-least-once occurrences (#2198).</b> The pump fires through the claim transitions: <see cref="ClaimDueAsync"/>
/// records each due occurrence as in flight under a fenced, time-limited claim <i>before</i> it is routed, and the cursor
/// moves past the occurrence only through <see cref="SettleClaimAsync"/> (or, for an exhausted Cron, by deleting the
/// schedule once its last occurrence was routed). A claimant that dies, or whose fire fails, leaves the occurrence in the
/// cursor, so it is fired again: by a peer once the claim's visibility lapses, or after the backoff a
/// <see cref="ReleaseClaimAsync"/> set. Every claim transition is a compare-and-set on the claim's owner, fencing token and
/// the row's revision, and on the stored schedule still being the one claimed. Fencing tokens increase with every claim of
/// a schedule, and a schedule deleted and saved again later does not reissue the tokens it issued before; whatever the
/// tokens, a claim on the earlier schedule cannot act on a recreated one that differs from it. The transitions have no
/// default: a store that does not implement them does not compile, rather than falling back to at-most-once.
/// </para>
/// <para>
/// <b>Republish (#2198).</b> <see cref="ActivateAsync"/> replaces the replaced activation's schedules atomically, and each
/// activated schedule takes over the cursor of the replaced schedule of the same trigger when that names an occurrence
/// which fell due before the activated schedule was materialized (<see cref="RecurringTriggerSchedule.TakeOverFrom"/>).
/// The replaced schedules change in the same write, so a claim in flight on one of them is stale from then on.
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

    /// <summary>
    /// Activates one activation and deactivates only the explicitly replaced activation. Each activated schedule takes over
    /// a due, unsettled occurrence from the replaced schedule of the same trigger (<see cref="RecurringTriggerSchedule.TakeOverFrom"/>).
    /// </summary>
    ValueTask ActivateAsync(
        string activationId,
        string? replacedActivationId,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromException(new NotSupportedException("This recurring-schedule store does not support activation-scoped activation."));

    /// <summary>Deletes schedules owned by one activation without affecting another activation of the artifact.</summary>
    ValueTask DeleteByActivationAsync(string activationId, CancellationToken cancellationToken = default) =>
        ValueTask.FromException(new NotSupportedException("This recurring-schedule store does not support activation-scoped deletion."));

    /// <summary>Finds a single schedule by its id, or <c>null</c> if it does not exist.</summary>
    ValueTask<RecurringTriggerSchedule?> FindAsync(string scheduleId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically claims at most <see cref="RecurringTriggerOccurrenceClaimRequest.Limit"/> active schedules whose
    /// occurrence is due and whose earlier claim, if any, is no longer visible, ordered by next occurrence then id. The
    /// claim is the occurrence's durable in-flight marker; the cursor is left on the occurrence. Competing claimants
    /// cannot both receive a claim on one occurrence, and a schedule deleted and saved again later does not reissue its
    /// earlier fencing tokens.
    /// </summary>
    ValueTask<IReadOnlyCollection<RecurringTriggerOccurrenceClaim>> ClaimDueAsync(
        RecurringTriggerOccurrenceClaimRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Extends a claim's visibility to <paramref name="now"/> plus <paramref name="visibilityTimeout"/> while its owner,
    /// fencing token and revision are current. Returns the renewed claim, or <see langword="null"/> when the claim no
    /// longer holds the schedule.
    /// </summary>
    ValueTask<RecurringTriggerOccurrenceClaim?> RenewClaimAsync(
        RecurringTriggerOccurrenceClaim claim,
        DateTimeOffset now,
        TimeSpan visibilityTimeout,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Settles the claimed occurrence: moves the cursor to <paramref name="nextOccurrence"/> and clears the claim, only
    /// while the claim is current. This is the only transition that lets the pump drop an occurrence. Returns
    /// <c>false</c> when the claim is stale, in which case nothing changed.
    /// </summary>
    ValueTask<bool> SettleClaimAsync(
        RecurringTriggerOccurrenceClaim claim,
        DateTimeOffset nextOccurrence,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Releases a current claim after a failed fire without moving the cursor, counting the failure. The occurrence stays
    /// due and becomes claimable again at <paramref name="visibleAt"/>. Returns <c>false</c> when the claim is stale.
    /// </summary>
    ValueTask<bool> ReleaseClaimAsync(
        RecurringTriggerOccurrenceClaim claim,
        DateTimeOffset visibleAt,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes every schedule owned by an artifact. Deleting for an unknown artifact is a no-op.</summary>
    ValueTask DeleteByArtifactAsync(string artifactId, CancellationToken cancellationToken = default);

    /// <summary>Deletes a single schedule by its id. Deleting a missing schedule is a no-op.</summary>
    ValueTask DeleteAsync(string scheduleId, CancellationToken cancellationToken = default);
}
