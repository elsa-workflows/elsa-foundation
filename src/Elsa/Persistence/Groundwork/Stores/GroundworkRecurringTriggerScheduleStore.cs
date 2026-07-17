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
    : GroundworkDocumentStore(store, serializer, ElsaRuntimeStorageManifest.RecurringTriggerScheduleDocumentKind, boundedStore), IRecurringTriggerScheduleStore
{
    private const string ProjectionKind = "recurringSchedules";

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

    public async ValueTask PreparePublicationAsync(
        string publicationId,
        IReadOnlyCollection<RecurringTriggerSchedule> schedules,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publicationId);
        ArgumentNullException.ThrowIfNull(schedules);
        ValidatePublicationSchedules(publicationId, schedules);

        var existing = await ListByPublicationAsync(publicationId, cancellationToken);
        await CommitAtomicallyAsync(
            existing.Select(schedule => schedule.ScheduleId),
            schedules.Select(schedule => schedule with { IsActive = false }),
            new PublicationProjectionState(ProjectionKind, publicationId, IsActive: false),
            deleteProjectionStateId: null,
            cancellationToken);
    }

    public async ValueTask<IReadOnlyCollection<RecurringTriggerSchedule>> ListByPublicationAsync(
        string publicationId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publicationId);
        return await QueryDocumentsAsync<RecurringTriggerScheduleEnvelope, RecurringTriggerSchedule>(
            ElsaRuntimeStorageManifest.ListRecurringTriggerSchedulesByPublicationQuery,
            ElsaRuntimeStorageManifest.RecurringTriggerSchedulePublicationIdField,
            publicationId,
            envelope => envelope.Schedule,
            cancellationToken);
    }

    public async ValueTask ActivatePublicationAsync(
        string publicationId,
        string? replacedPublicationId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publicationId);
        if (replacedPublicationId is not null)
            ArgumentException.ThrowIfNullOrWhiteSpace(replacedPublicationId);

        var candidateStateEnvelope = await LoadProjectionStateEnvelopeAsync(publicationId, cancellationToken);
        if (candidateStateEnvelope is null)
            throw new InvalidOperationException($"Publication '{publicationId}' has no prepared recurring-schedule projection.");

        var candidateState = Serializer.Deserialize<PublicationProjectionState>(candidateStateEnvelope);
        var candidate = await ListByPublicationAsync(publicationId, cancellationToken);
        var replaced = replacedPublicationId is null || StringComparer.Ordinal.Equals(publicationId, replacedPublicationId)
            ? []
            : await ListByPublicationAsync(replacedPublicationId, cancellationToken);
        var updates = candidate.Select(schedule => schedule with { IsActive = true })
            .Concat(replaced.Select(schedule => schedule with { IsActive = false }))
            .ToArray();
        var replacedStateEnvelope = replacedPublicationId is null || StringComparer.Ordinal.Equals(publicationId, replacedPublicationId)
            ? null
            : await LoadProjectionStateEnvelopeAsync(replacedPublicationId, cancellationToken);
        var replacedState = replacedStateEnvelope is null
            ? null
            : Serializer.Deserialize<PublicationProjectionState>(replacedStateEnvelope);

        await CommitAtomicallyAsync(
            [],
            updates,
            candidateState with { IsActive = true },
            deleteProjectionStateId: null,
            cancellationToken,
            secondaryProjectionState: replacedState is null ? null : replacedState with { IsActive = false },
            projectionStateExpectedVersion: candidateStateEnvelope.Version,
            secondaryProjectionStateExpectedVersion: replacedStateEnvelope?.Version);
    }

    public async ValueTask DeleteByPublicationAsync(string publicationId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publicationId);
        var existing = await ListByPublicationAsync(publicationId, cancellationToken);
        await CommitAtomicallyAsync(
            existing.Select(schedule => schedule.ScheduleId),
            [],
            projectionState: null,
            ProjectionStateId(publicationId),
            cancellationToken);
    }

    public async ValueTask<IReadOnlyCollection<RecurringTriggerSchedule>> ListDueAsync(DateTimeOffset asOf, int limit, CancellationToken cancellationToken = default)
    {
        if (limit <= 0)
            throw new ArgumentOutOfRangeException(nameof(limit), "Due-schedule listing limit must be greater than zero.");
        cancellationToken.ThrowIfCancellationRequested();

        var result = await BoundedStore.QueryAsync(
            new DocumentQuery(
                DocumentKind,
                ElsaRuntimeStorageManifest.ListDueRecurringTriggerSchedulesQuery,
                [DocumentQueryClause.Of(DocumentQueryComparison.LessThanOrEqual(
                    ElsaRuntimeStorageManifest.RecurringTriggerScheduleNextOccurrenceField,
                    asOf.ToString("O", CultureInfo.InvariantCulture)))],
                [new DocumentQueryOrder(ElsaRuntimeStorageManifest.RecurringTriggerScheduleNextOccurrenceField)]),
            cancellationToken);
        var schedules = result.Documents
            .Select(envelope => Serializer.Deserialize<RecurringTriggerScheduleEnvelope>(envelope).Schedule);

        return schedules
            .Where(schedule => schedule.IsActive && schedule.NextOccurrence <= asOf)
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

        var owned = await QueryDocumentsAsync<RecurringTriggerScheduleEnvelope, RecurringTriggerSchedule>(
            ElsaRuntimeStorageManifest.ListByArtifactQuery,
            ElsaRuntimeStorageManifest.ArtifactIdField,
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

    private async ValueTask<DocumentEnvelope?> LoadProjectionStateEnvelopeAsync(
        string publicationId,
        CancellationToken cancellationToken) =>
        await Store.LoadAsync(
            ElsaRuntimeStorageManifest.PublicationProjectionStateDocumentKind,
            ProjectionStateId(publicationId),
            cancellationToken);

    private async ValueTask CommitAtomicallyAsync(
        IEnumerable<string> deleteIds,
        IEnumerable<RecurringTriggerSchedule> upserts,
        PublicationProjectionState? projectionState,
        string? deleteProjectionStateId,
        CancellationToken cancellationToken,
        PublicationProjectionState? secondaryProjectionState = null,
        long? projectionStateExpectedVersion = null,
        long? secondaryProjectionStateExpectedVersion = null)
    {
        await using var unitOfWork = await Store.BeginAsync(
            DocumentCommitScope.Of(DocumentKind, ElsaRuntimeStorageManifest.PublicationProjectionStateDocumentKind),
            cancellationToken);
        foreach (var id in deleteIds)
            await unitOfWork.DeleteAsync(new DeleteDocumentRequest(DocumentKind, id), cancellationToken);
        if (deleteProjectionStateId is not null)
            await unitOfWork.DeleteAsync(
                new DeleteDocumentRequest(ElsaRuntimeStorageManifest.PublicationProjectionStateDocumentKind, deleteProjectionStateId),
                cancellationToken);
        foreach (var schedule in upserts)
        {
            var (schemaVersion, content) = Serializer.Serialize(DocumentKind, ToEnvelope(schedule));
            await unitOfWork.SaveAsync(
                new SaveDocumentRequest(DocumentKind, schedule.ScheduleId, schemaVersion, content),
                cancellationToken);
        }
        if (projectionState is not null)
            await SaveProjectionStateAsync(unitOfWork, projectionState, projectionStateExpectedVersion, cancellationToken);
        if (secondaryProjectionState is not null)
            await SaveProjectionStateAsync(unitOfWork, secondaryProjectionState, secondaryProjectionStateExpectedVersion, cancellationToken);

        await unitOfWork.CommitAsync(cancellationToken);
    }

    private async ValueTask SaveProjectionStateAsync(
        IDocumentUnitOfWork unitOfWork,
        PublicationProjectionState state,
        long? expectedVersion,
        CancellationToken cancellationToken)
    {
        var (schemaVersion, content) = Serializer.Serialize(
            ElsaRuntimeStorageManifest.PublicationProjectionStateDocumentKind,
            state);
        var result = await unitOfWork.SaveAsync(
            new SaveDocumentRequest(
                ElsaRuntimeStorageManifest.PublicationProjectionStateDocumentKind,
                ProjectionStateId(state.PublicationId),
                schemaVersion,
                content,
                ExpectedVersion: expectedVersion),
            cancellationToken);
        if (result.Status != DocumentStoreWriteStatus.Saved)
            throw new InvalidOperationException(
                $"Recurring-schedule publication projection '{state.PublicationId}' could not be saved because the stored projection version changed.");
    }

    private static string ProjectionStateId(string publicationId) =>
        $"{ProjectionKind}:{publicationId.Length}:{publicationId}";

    private sealed record PublicationProjectionState(string ProjectionKind, string PublicationId, bool IsActive);
}
