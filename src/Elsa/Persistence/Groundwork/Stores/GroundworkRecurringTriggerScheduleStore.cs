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
/// <b>Scan cost.</b> Groundwork's portable query contract is equality-only, so <see cref="ListDueAsync"/>
/// queries the whole schedule partition through the constant <c>by-collection</c> keyword index, then filters
/// <c>NextOccurrence &lt;= asOf</c>, orders, and caps in memory — mirroring the durable-timer bridge. The pump's
/// per-tick limit bounds fires, not this load.
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

        // Upsert: republish rewrites the schedule (including a re-anchored NextOccurrence). Unlike the
        // durable-timer store's existing-wins rule, a recurring schedule has no one-shot deadline to protect.
        await SaveDocumentAsync(schedule.ScheduleId, ToEnvelope(schedule), cancellationToken);
        return schedule;
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

        var candidateState = await LoadProjectionStateAsync(publicationId, cancellationToken);
        if (candidateState is null)
            throw new InvalidOperationException($"Publication '{publicationId}' has no prepared recurring-schedule projection.");

        var candidate = await ListByPublicationAsync(publicationId, cancellationToken);
        var replaced = replacedPublicationId is null || StringComparer.Ordinal.Equals(publicationId, replacedPublicationId)
            ? []
            : await ListByPublicationAsync(replacedPublicationId, cancellationToken);
        var updates = candidate.Select(schedule => schedule with { IsActive = true })
            .Concat(replaced.Select(schedule => schedule with { IsActive = false }))
            .ToArray();
        var replacedState = replacedPublicationId is null || StringComparer.Ordinal.Equals(publicationId, replacedPublicationId)
            ? null
            : await LoadProjectionStateAsync(replacedPublicationId, cancellationToken);

        await CommitAtomicallyAsync(
            [],
            updates,
            candidateState with { IsActive = true },
            deleteProjectionStateId: null,
            cancellationToken,
            replacedState is null ? null : replacedState with { IsActive = false });
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

        var schedules = await QueryDocumentsAsync<RecurringTriggerScheduleEnvelope, RecurringTriggerSchedule>(
            ElsaRuntimeStorageManifest.ListAllQuery,
            ElsaRuntimeStorageManifest.CollectionField,
            ElsaRuntimeStorageManifest.RecurringTriggerScheduleDocumentKind,
            envelope => envelope.Schedule,
            cancellationToken);

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

    private async ValueTask<PublicationProjectionState?> LoadProjectionStateAsync(
        string publicationId,
        CancellationToken cancellationToken)
    {
        var envelope = await Store.LoadAsync(
            ElsaRuntimeStorageManifest.PublicationProjectionStateDocumentKind,
            ProjectionStateId(publicationId),
            cancellationToken);
        return envelope is null ? null : Serializer.Deserialize<PublicationProjectionState>(envelope);
    }

    private async ValueTask CommitAtomicallyAsync(
        IEnumerable<string> deleteIds,
        IEnumerable<RecurringTriggerSchedule> upserts,
        PublicationProjectionState? projectionState,
        string? deleteProjectionStateId,
        CancellationToken cancellationToken,
        PublicationProjectionState? secondaryProjectionState = null)
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
            await SaveProjectionStateAsync(unitOfWork, projectionState, cancellationToken);
        if (secondaryProjectionState is not null)
            await SaveProjectionStateAsync(unitOfWork, secondaryProjectionState, cancellationToken);

        await unitOfWork.CommitAsync(cancellationToken);
    }

    private async ValueTask SaveProjectionStateAsync(
        IDocumentUnitOfWork unitOfWork,
        PublicationProjectionState state,
        CancellationToken cancellationToken)
    {
        var (schemaVersion, content) = Serializer.Serialize(
            ElsaRuntimeStorageManifest.PublicationProjectionStateDocumentKind,
            state);
        await unitOfWork.SaveAsync(
            new SaveDocumentRequest(
                ElsaRuntimeStorageManifest.PublicationProjectionStateDocumentKind,
                ProjectionStateId(state.PublicationId),
                schemaVersion,
                content),
            cancellationToken);
    }

    private static string ProjectionStateId(string publicationId) =>
        $"{ProjectionKind}:{publicationId.Length}:{publicationId}";

    private sealed record PublicationProjectionState(string ProjectionKind, string PublicationId, bool IsActive);
}
