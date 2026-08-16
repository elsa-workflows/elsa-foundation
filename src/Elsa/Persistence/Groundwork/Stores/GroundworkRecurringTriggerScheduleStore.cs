using System.Globalization;
using Elsa.Persistence.Groundwork.Serialization;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Core.Transactions;
using Groundwork.Documents.Store;
using Groundwork.Documents.UnitOfWork;

namespace Elsa.Persistence.Groundwork.Stores;

/// <summary>
/// Durable <see cref="IRecurringTriggerScheduleStore"/> for the Groundwork bridge, backed by the portable
/// <see cref="IDocumentStore"/> (W16). Each recurring schedule is one document, so a Timer/Cron start trigger
/// keeps firing across process restarts — the recurring-start counterpart to
/// <see cref="GroundworkDurableTimerStore"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Due selection.</b> <see cref="ListDueAsync"/> uses the declared <c>list-due</c> date route to bound the
/// result set to schedules whose persisted <c>NextOccurrence</c> is at or before the requested instant, then
/// preserves the contract's active-only filtering, deterministic ordering, and cap in process.
/// </para>
/// <para>
/// <b>Compare-and-swap.</b> <see cref="TryAdvanceAsync"/> reads the current schedule envelope, checks the cursor
/// still equals the caller's expected value, then writes the advanced schedule with the loaded Groundwork document
/// version as the expected version. Concurrent nodes therefore cannot both claim the same occurrence.
/// </para>
/// </remarks>
public sealed class GroundworkRecurringTriggerScheduleStore(
    IDocumentStore store,
    IGroundworkRuntimeDocumentSerializer serializer,
    IBoundedDocumentStore? boundedStore = null)
    : GroundworkPublicationProjectionStore<RecurringTriggerSchedule>(store, serializer, ElsaRuntimeStorageManifest.RecurringTriggerScheduleDocumentKind, boundedStore), IRecurringTriggerScheduleStore
{
    protected override string ProjectionKind => "recurringSchedules";
    protected override string ProjectionNoun => "recurring-schedule";

    protected override string ItemId(RecurringTriggerSchedule item) => item.ScheduleId;

    protected override RecurringTriggerSchedule WithActive(RecurringTriggerSchedule item, bool isActive) =>
        item with { IsActive = isActive };

    protected override object StoragePayload(RecurringTriggerSchedule item) => ToEnvelope(item);

    protected override async ValueTask<IReadOnlyCollection<RecurringTriggerSchedule>> ListAllByPublicationCoreAsync(
        string publicationId,
        CancellationToken cancellationToken) =>
        await RuntimeOperationalStorePagingExtensions.ListAllByPublicationAsync(this, publicationId, cancellationToken);

    public async ValueTask<RecurringTriggerSchedule> SaveAsync(RecurringTriggerSchedule schedule, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        ArgumentException.ThrowIfNullOrWhiteSpace(schedule.ScheduleId);
        cancellationToken.ThrowIfCancellationRequested();

        // Republish may rewrite the schedule, but it must not silently overwrite a concurrent publication or
        // occurrence advance. Creation is expected-version zero; replacement uses the version actually read.
        var existing = await Store.LoadAsync(DocumentKind, schedule.ScheduleId, cancellationToken);
        var result = await SaveDocumentAsync(
            schedule.ScheduleId,
            ToEnvelope(schedule),
            cancellationToken,
            existing?.Version ?? 0);
        if (result.Status == DocumentStoreWriteStatus.Saved)
            return schedule;
        if (result.Status != DocumentStoreWriteStatus.ConcurrencyConflict)
        {
            throw new InvalidOperationException(
                $"Groundwork rejected recurring trigger schedule '{schedule.ScheduleId}' with status '{result.Status}'.");
        }

        var winnerEnvelope = await Store.LoadAsync(DocumentKind, schedule.ScheduleId, cancellationToken);
        if (winnerEnvelope is not null &&
            Serializer.Deserialize<RecurringTriggerScheduleEnvelope>(winnerEnvelope).Schedule == schedule)
        {
            return schedule;
        }

        throw new InvalidOperationException(
            $"Recurring trigger schedule '{schedule.ScheduleId}' changed concurrently and was not overwritten.");
    }

    public async ValueTask PrepareActivationAsync(
        string publicationId,
        IReadOnlyCollection<RecurringTriggerSchedule> schedules,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publicationId);
        ArgumentNullException.ThrowIfNull(schedules);
        ValidatePublicationSchedules(publicationId, schedules);

        await PreparePublicationCoreAsync(publicationId, schedules, cancellationToken);
    }

    public async ValueTask<RuntimeStorePage<RecurringTriggerSchedule>> ListByPublicationPageAsync(
        RecurringTriggerSchedulePublicationPageQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var result = await BoundedStore.QueryAsync(
            new DocumentQuery(
                DocumentKind,
                ElsaRuntimeStorageManifest.PageRecurringTriggerSchedulesByPublicationQuery,
                [Equal(ElsaRuntimeStorageManifest.RecurringTriggerSchedulePublicationIdField, query.PublicationId)],
                [new DocumentQueryOrder(ElsaRuntimeStorageManifest.RecurringTriggerScheduleIdField)],
                take: query.Limit,
                continuation: query.ContinuationToken),
            cancellationToken);

        return new RuntimeStorePage<RecurringTriggerSchedule>(
            query,
            result.Documents
                .Select(envelope => Serializer.Deserialize<RecurringTriggerScheduleEnvelope>(envelope).Schedule)
                .ToArray(),
            result.NextContinuation);
    }

    public async ValueTask<RuntimeStorePage<RecurringTriggerSchedule>> ListByArtifactPageAsync(
        RecurringTriggerScheduleArtifactPageQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var result = await BoundedStore.QueryAsync(
            new DocumentQuery(
                DocumentKind,
                ElsaRuntimeStorageManifest.PageRecurringTriggerSchedulesByArtifactQuery,
                [Equal(ElsaRuntimeStorageManifest.ArtifactIdField, query.ArtifactId)],
                [new DocumentQueryOrder(ElsaRuntimeStorageManifest.RecurringTriggerScheduleIdField)],
                take: query.Limit,
                continuation: query.ContinuationToken),
            cancellationToken);

        return new RuntimeStorePage<RecurringTriggerSchedule>(
            query,
            result.Documents
                .Select(envelope => Serializer.Deserialize<RecurringTriggerScheduleEnvelope>(envelope).Schedule)
                .ToArray(),
            result.NextContinuation);
    }

    public async ValueTask<IReadOnlyCollection<RecurringTriggerSchedule>> ListByActivationAsync(
        string publicationId,
        CancellationToken cancellationToken = default) =>
        await RuntimeOperationalStorePagingExtensions.ListAllByPublicationAsync(this, publicationId, cancellationToken);

    public async ValueTask ActivateAsync(
        string publicationId,
        string? replacedPublicationId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publicationId);
        if (replacedPublicationId is not null)
            ArgumentException.ThrowIfNullOrWhiteSpace(replacedPublicationId);

        await ActivatePublicationCoreAsync(publicationId, replacedPublicationId, cancellationToken);
    }

    public async ValueTask DeleteByActivationAsync(string publicationId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publicationId);
        await DeleteByPublicationCoreAsync(publicationId, cancellationToken);
    }

    public async ValueTask<IReadOnlyCollection<RecurringTriggerSchedule>> ListDueAsync(DateTimeOffset asOf, int limit, CancellationToken cancellationToken = default)
    {
        if (limit is <= 0 or > RuntimeStorePageRequest.MaximumLimit)
            throw new ArgumentOutOfRangeException(nameof(limit), $"Due-schedule listing limit must be between 1 and {RuntimeStorePageRequest.MaximumLimit}.");
        cancellationToken.ThrowIfCancellationRequested();

        var result = await BoundedStore.QueryAsync(
            new DocumentQuery(
                DocumentKind,
                ElsaRuntimeStorageManifest.ListDueRecurringTriggerSchedulesQuery,
                [
                    DocumentQueryClause.Of(DocumentQueryComparison.Equal(
                        ElsaRuntimeStorageManifest.RecurringTriggerScheduleIsActiveField,
                        bool.TrueString.ToLowerInvariant())),
                    DocumentQueryClause.Of(DocumentQueryComparison.LessThanOrEqual(
                        ElsaRuntimeStorageManifest.RecurringTriggerScheduleNextOccurrenceField,
                        asOf.ToString("O", CultureInfo.InvariantCulture)))
                ],
                [
                    new DocumentQueryOrder(ElsaRuntimeStorageManifest.RecurringTriggerScheduleIsActiveField),
                    new DocumentQueryOrder(ElsaRuntimeStorageManifest.RecurringTriggerScheduleNextOccurrenceField),
                    new DocumentQueryOrder(ElsaRuntimeStorageManifest.RecurringTriggerScheduleIdField)
                ],
                take: limit),
            cancellationToken);
        return result.Documents
            .Select(envelope => Serializer.Deserialize<RecurringTriggerScheduleEnvelope>(envelope).Schedule)
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

        var envelope = await Store.LoadAsync(DocumentKind, scheduleId, cancellationToken);
        if (envelope is null)
            return false;

        var current = Serializer.Deserialize<RecurringTriggerScheduleEnvelope>(envelope).Schedule;
        if (current is null || !current.IsActive || current.NextOccurrence != expectedNextOccurrence)
            return false;

        var advanced = current with { NextOccurrence = newNextOccurrence };
        var result = await SaveDocumentAsync(scheduleId, ToEnvelope(advanced), cancellationToken, envelope.Version);
        return result.Status == DocumentStoreWriteStatus.Saved;
    }

    public async ValueTask DeleteByArtifactAsync(string artifactId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactId);
        cancellationToken.ThrowIfCancellationRequested();

        var owned = await RuntimeOperationalStorePagingExtensions.ListAllByArtifactAsync(this, artifactId, cancellationToken);

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

    private static DocumentQueryClause Equal(string path, string value) =>
        DocumentQueryClause.Of(DocumentQueryComparison.Equal(path, value));

    // The constant collection partition lets the due-schedule sweep use a keyword equality index instead of a
    // provider-wide scan; ArtifactId is lifted to the top level so the by-artifact replace path can query it
    // without a nested index path, mirroring the other list-capable bridges.
    private sealed record RecurringTriggerScheduleEnvelope(string Collection, string ArtifactId, RecurringTriggerSchedule Schedule);

    private static void ValidatePublicationSchedules(
        string publicationId,
        IReadOnlyCollection<RecurringTriggerSchedule> schedules)
    {
        foreach (var schedule in schedules)
        {
            ArgumentNullException.ThrowIfNull(schedule);
            if (!StringComparer.Ordinal.Equals(schedule.PublicationId, publicationId))
                throw new ArgumentException($"Schedule '{schedule.ScheduleId}' does not belong to publication '{publicationId}'.", nameof(schedules));
            ArgumentException.ThrowIfNullOrWhiteSpace(schedule.SlotId);
        }
    }

}
