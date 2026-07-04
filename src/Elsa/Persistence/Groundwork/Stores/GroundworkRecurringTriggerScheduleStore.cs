using Elsa.Persistence.Groundwork.Serialization;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Documents.Store;

namespace Elsa.Persistence.Groundwork.Stores;

/// <summary>
/// Durable <see cref="IRecurringTriggerScheduleStore"/> for the Groundwork bridge, backed by the portable
/// <see cref="IDocumentStore"/> (W16). Each recurring schedule is one document, so a Timer/Cron start trigger
/// keeps firing across process restarts — the recurring-start counterpart to
/// <see cref="GroundworkDurableTimerStore"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Scan cost.</b> Groundwork's portable query contract is equality-only, so <see cref="ListDueAsync"/>
/// queries the whole schedule partition through the constant <c>by-collection</c> keyword index, then filters
/// <c>NextOccurrence &lt;= asOf</c>, orders, and caps in memory — mirroring the durable-timer bridge. The pump's
/// per-tick limit bounds fires, not this load.
/// </para>
/// <para>
/// <b>Compare-and-swap (W20 hook).</b> <see cref="TryAdvanceAsync"/> reads the current schedule, checks the
/// cursor still equals the caller's expected value, then writes the advanced schedule. On a single node this is
/// the at-most-once claim the pump relies on; a future clustered store (W20) keeps the same contract but makes
/// the read-check-write atomic through the provider's optimistic-concurrency token so concurrent nodes cannot
/// both claim the same occurrence.
/// </para>
/// </remarks>
public sealed class GroundworkRecurringTriggerScheduleStore(IDocumentStore store, IGroundworkRuntimeDocumentSerializer serializer)
    : GroundworkDocumentStore(store, serializer, ElsaRuntimeStorageManifest.RecurringTriggerScheduleDocumentKind), IRecurringTriggerScheduleStore
{
    public async ValueTask<RecurringTriggerSchedule> SaveAsync(RecurringTriggerSchedule schedule, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        ArgumentException.ThrowIfNullOrWhiteSpace(schedule.ScheduleId);
        cancellationToken.ThrowIfCancellationRequested();

        // Upsert: republish rewrites the schedule (including a re-anchored NextOccurrence). Unlike the
        // durable-timer store's existing-wins rule, a recurring schedule has no one-shot deadline to protect.
        await SaveDocumentAsync(schedule.ScheduleId, ToEnvelope(schedule), cancellationToken);
        return schedule;
    }

    public async ValueTask<IReadOnlyCollection<RecurringTriggerSchedule>> ListDueAsync(DateTimeOffset asOf, int limit, CancellationToken cancellationToken = default)
    {
        if (limit <= 0)
            throw new ArgumentOutOfRangeException(nameof(limit), "Due-schedule listing limit must be greater than zero.");
        cancellationToken.ThrowIfCancellationRequested();

        var schedules = await QueryDocumentsAsync<RecurringTriggerScheduleEnvelope, RecurringTriggerSchedule>(
            ElsaRuntimeStorageManifest.ByCollectionIndex,
            ElsaRuntimeStorageManifest.RecurringTriggerScheduleDocumentKind,
            envelope => envelope.Schedule,
            cancellationToken);

        return schedules
            .Where(schedule => schedule.NextOccurrence <= asOf)
            .OrderBy(schedule => schedule.NextOccurrence)
            .ThenBy(schedule => schedule.ScheduleId, StringComparer.Ordinal)
            .Take(limit)
            .ToArray();
    }

    public async ValueTask<RecurringTriggerSchedule?> FindAsync(string scheduleId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scheduleId);
        cancellationToken.ThrowIfCancellationRequested();

        return await LoadScheduleAsync(scheduleId, cancellationToken);
    }

    public async ValueTask<bool> TryAdvanceAsync(string scheduleId, DateTimeOffset expectedNextOccurrence, DateTimeOffset newNextOccurrence, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scheduleId);
        cancellationToken.ThrowIfCancellationRequested();

        // Read-check-write compare-and-swap. Single-node hosts get an at-most-once claim from this; the W20
        // clustered store makes the same check atomic via the provider's concurrency token.
        var current = await LoadScheduleAsync(scheduleId, cancellationToken);
        if (current is null || current.NextOccurrence != expectedNextOccurrence)
            return false;

        var advanced = current with { NextOccurrence = newNextOccurrence };
        await SaveDocumentAsync(scheduleId, ToEnvelope(advanced), cancellationToken);
        return true;
    }

    public async ValueTask DeleteByArtifactAsync(string artifactId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactId);
        cancellationToken.ThrowIfCancellationRequested();

        var owned = await QueryDocumentsAsync<RecurringTriggerScheduleEnvelope, RecurringTriggerSchedule>(
            ElsaRuntimeStorageManifest.ByArtifactIndex,
            artifactId,
            envelope => envelope.Schedule,
            cancellationToken);

        foreach (var schedule in owned)
            await DeleteDocumentAsync(schedule.ScheduleId, cancellationToken);
    }

    public async ValueTask DeleteAsync(string scheduleId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scheduleId);
        cancellationToken.ThrowIfCancellationRequested();

        await DeleteDocumentAsync(scheduleId, cancellationToken);
    }

    private ValueTask<RecurringTriggerSchedule?> LoadScheduleAsync(string scheduleId, CancellationToken cancellationToken) =>
        LoadDocumentAsync<RecurringTriggerScheduleEnvelope, RecurringTriggerSchedule>(
            scheduleId, envelope => envelope.Schedule, cancellationToken);

    private static RecurringTriggerScheduleEnvelope ToEnvelope(RecurringTriggerSchedule schedule) => new(
        ElsaRuntimeStorageManifest.RecurringTriggerScheduleDocumentKind,
        schedule.ArtifactId,
        schedule);

    // The constant collection partition lets the due-schedule sweep use a keyword equality index instead of a
    // provider-wide scan; ArtifactId is lifted to the top level so the by-artifact replace path can query it
    // without a nested index path, mirroring the other list-capable bridges.
    private sealed record RecurringTriggerScheduleEnvelope(string Collection, string ArtifactId, RecurringTriggerSchedule Schedule);
}
