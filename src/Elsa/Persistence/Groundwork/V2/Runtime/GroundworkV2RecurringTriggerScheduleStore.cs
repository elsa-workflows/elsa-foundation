using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Kernel;
using Groundwork.Query.Model;
using Groundwork.Store;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Elsa.Persistence.Groundwork.Runtime;

/// <summary>Current-only Groundwork v2 recurring-trigger schedule store.</summary>
/// <remarks>
/// Schedule rows use the escaped logical schedule identity as their stable row key and retain the complete
/// current schedule in the JSON envelope. Activation preparation and activation use the schedule and
/// activation-projection units in one evidenced atomic unit of work. Serving queries are bounded provider
/// queries over the declared projections; this adapter has no v1 document-store or migration path.
/// </remarks>
public sealed class GroundworkV2RecurringTriggerScheduleStore : IRecurringTriggerScheduleStore
{
    private const string ProjectionKind = "recurringSchedules";

    private readonly IGroundworkStorageSessionSource sessions;
    private readonly IPersistenceAccessContextAccessor accessContextAccessor;
    private readonly string? targetName;
    private readonly StorageUnit scheduleUnit;
    private readonly StorageUnit projectionStateUnit;

    public GroundworkV2RecurringTriggerScheduleStore(
        IGroundworkStorageSessionSource sessions,
        IPersistenceAccessContextAccessor accessContextAccessor,
        string? targetName = null)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(accessContextAccessor);
        this.sessions = sessions;
        this.accessContextAccessor = accessContextAccessor;
        this.targetName = targetName;
        scheduleUnit = sessions.Unit(ElsaRuntimeV2StorageManifest.RecurringTriggerScheduleDocumentKind, targetName);
        projectionStateUnit = sessions.Unit(ElsaRuntimeV2StorageManifest.PublicationProjectionStateDocumentKind, targetName);
    }

    public ValueTask<RecurringTriggerSchedule> SaveAsync(
        RecurringTriggerSchedule schedule,
        CancellationToken cancellationToken = default)
    {
        GroundworkV2RecurringTriggerScheduleStorageConventions.Validate(schedule);
        cancellationToken.ThrowIfCancellationRequested();

        var session = OpenScheduleSession();
        var key = GroundworkRuntimeRowStore.Key(
            GroundworkV2RecurringTriggerScheduleStorageConventions.PhysicalId(schedule.ScheduleId));
        var values = GroundworkV2RecurringTriggerScheduleStorageConventions.Values(schedule);
        var existing = session.Read(key);
        var previous = existing is null
            ? null
            : GroundworkV2RecurringTriggerScheduleStorageConventions.Deserialize(existing.Values.Values);
        if (previous is not null)
            EnsureIdentity(previous, schedule.ScheduleId);
        if (DirectSaveTargetsManagedActivation(schedule, previous))
        {
            if (previous is not null && SchedulesEqual(previous, schedule))
                return ValueTask.FromResult(previous);

            throw new InvalidOperationException(
                $"Recurring-trigger schedule '{schedule.ScheduleId}' is managed by an activation projection and cannot be changed through direct save.");
        }

        var result = existing is null
            ? session.Insert(values, WriteOptions.CreateOnly)
            : UpdateExisting(session, values, existing, schedule);

        if (IsSaved(result.Status))
            return ValueTask.FromResult(schedule);
        if (result.Status != WriteOutcomeStatus.ConcurrencyConflict)
        {
            throw new InvalidOperationException(
                $"Groundwork rejected recurring-trigger schedule '{schedule.ScheduleId}' with status '{result.Status}'.");
        }

        // An equal concurrent publish is idempotent; a different concurrent publish must never be overwritten.
        var winner = session.Read(key);
        if (winner is not null)
        {
            var winnerSchedule = GroundworkV2RecurringTriggerScheduleStorageConventions.Deserialize(winner.Values.Values);
            if (StringComparer.Ordinal.Equals(
                    GroundworkV2RuntimeJson.Serialize(winnerSchedule),
                    GroundworkV2RuntimeJson.Serialize(schedule)))
            {
                return ValueTask.FromResult(schedule);
            }
        }

        throw new InvalidOperationException(
            $"Recurring-trigger schedule '{schedule.ScheduleId}' changed concurrently and was not overwritten.");
    }

    private bool DirectSaveTargetsManagedActivation(
        RecurringTriggerSchedule proposed,
        RecurringTriggerSchedule? previous)
    {
        var activationIds = new HashSet<string>(StringComparer.Ordinal);
        if (proposed.ActivationId is not null)
            activationIds.Add(proposed.ActivationId);
        if (previous?.ActivationId is not null)
            activationIds.Add(previous.ActivationId);

        foreach (var activationId in activationIds)
        {
            var state = ReadProjectionState(OpenProjectionStateSession(), activationId);
            if (state is null)
                continue;

            if (previous is null ||
                !StringComparer.Ordinal.Equals(previous.ActivationId, activationId) ||
                previous.IsActive != state.Value.State.IsActive ||
                !state.Value.State.ScheduleFingerprints.TryGetValue(previous.ScheduleId, out var expectedFingerprint) ||
                !StringComparer.Ordinal.Equals(expectedFingerprint, ImmutableFingerprint(previous)))
            {
                throw new InvalidDataException(
                    $"Recurring-trigger schedule '{proposed.ScheduleId}' does not match its activation-managed state.");
            }

            return true;
        }

        return false;
    }

    public async ValueTask PrepareActivationAsync(
        string activationId,
        IReadOnlyCollection<RecurringTriggerSchedule> schedules,
        CancellationToken cancellationToken = default)
    {
        ValidateActivation(activationId, schedules);
        cancellationToken.ThrowIfCancellationRequested();
        _ = Access;
        RequireAtomicCommit();

        using var unitOfWork = BeginUnitOfWork();
        var scheduleSession = unitOfWork.OpenSession(scheduleUnit);
        var stateSession = unitOfWork.OpenSession(projectionStateUnit);
        var existingState = ReadProjectionState(stateSession, activationId);
        var existingRows = ListAllByActivation(scheduleSession, activationId, cancellationToken);
        var prepared = schedules.Select(schedule => schedule with { IsActive = false }).ToArray();

        if (existingState is not null)
        {
            if (!existingState.Value.State.IsActive &&
                ProjectionMatches(existingState.Value.State, existingRows) &&
                ProjectionsEqual(existingRows, prepared))
            {
                return;
            }

            throw new InvalidOperationException(
                $"Recurring-schedule activation projection '{activationId}' is already prepared with different state.");
        }

        var existingById = existingRows.ToDictionary(row => row.Schedule.ScheduleId, StringComparer.Ordinal);
        var desiredById = prepared.ToDictionary(schedule => schedule.ScheduleId, StringComparer.Ordinal);
        foreach (var existing in existingRows)
        {
            if (desiredById.TryGetValue(existing.Schedule.ScheduleId, out var desired))
                StageUpsert(unitOfWork, desired, existing.Version);
            else
                StageDelete(unitOfWork, scheduleUnit, existing.Schedule.ScheduleId, existing.Version);
        }

        foreach (var schedule in prepared.Where(schedule => !existingById.ContainsKey(schedule.ScheduleId)))
            StageInsert(unitOfWork, schedule);

        StageProjectionState(
            unitOfWork,
            ProjectionState(activationId, prepared, isActive: false),
            expectedVersion: null,
            createOnly: true);
        try
        {
            await CommitAsync(unitOfWork, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // A concurrent prepare may have committed the same exact inactive projection before this
            // acknowledgement arrived. Reconcile only exact convergence; never hide a different winner.
            try
            {
                var stateAfter = ReadProjectionState(OpenProjectionStateSession(), activationId);
                var rowsAfter = ListAllByActivation(OpenScheduleSession(), activationId, cancellationToken);
                if (stateAfter is { State.IsActive: false } &&
                    ProjectionMatches(stateAfter.Value.State, rowsAfter) &&
                    ProjectionsEqual(rowsAfter, prepared))
                {
                    return;
                }
            }
            catch
            {
                // Preserve the original provider failure when reconciliation cannot establish convergence.
            }

            throw;
        }
    }

    public ValueTask<RuntimeStorePage<RecurringTriggerSchedule>> ListByActivationPageAsync(
        RecurringTriggerScheduleActivationPageQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ValidateActivationId(query.ActivationId);
        cancellationToken.ThrowIfCancellationRequested();
        var table = new TableId(scheduleUnit.Name);
        var activation = Column(table, ElsaRuntimeV2StorageManifest.RecurringTriggerScheduleActivationIdField);
        var scheduleId = Column(table, ElsaRuntimeV2StorageManifest.RecurringTriggerScheduleIdField);
        var result = QueryWithBoundCursor(new QueryRequest(
            table,
            Equal(activation, query.ActivationId),
            [new OrderTerm(scheduleId, OrderDirection.Ascending, NullOrder.Last)],
            Projection.All,
            PagingFor(query.Limit, query.ContinuationToken)), query.ContinuationToken);
        return ValueTask.FromResult(Page(query, result));
    }

    public ValueTask<RuntimeStorePage<RecurringTriggerSchedule>> ListByArtifactPageAsync(
        RecurringTriggerScheduleArtifactPageQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();
        var table = new TableId(scheduleUnit.Name);
        var artifact = Column(table, ElsaRuntimeV2StorageManifest.ArtifactIdField);
        var scheduleId = Column(table, ElsaRuntimeV2StorageManifest.RecurringTriggerScheduleIdField);
        var result = QueryWithBoundCursor(new QueryRequest(
            table,
            Equal(artifact, query.ArtifactId),
            [new OrderTerm(scheduleId, OrderDirection.Ascending, NullOrder.Last)],
            Projection.All,
            PagingFor(query.Limit, query.ContinuationToken)), query.ContinuationToken);
        return ValueTask.FromResult(Page(query, result));
    }

    public ValueTask<IReadOnlyCollection<RecurringTriggerSchedule>> ListByActivationAsync(
        string activationId,
        CancellationToken cancellationToken = default)
    {
        ValidateActivationId(activationId);
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<IReadOnlyCollection<RecurringTriggerSchedule>>(
            ListAllByActivation(OpenScheduleSession(), activationId, cancellationToken)
                .Select(row => row.Schedule)
                .ToArray());
    }

    public async ValueTask ActivateAsync(
        string activationId,
        string? replacedActivationId,
        CancellationToken cancellationToken = default)
    {
        ValidateActivationId(activationId);
        if (replacedActivationId is not null)
            ValidateActivationId(replacedActivationId);
        cancellationToken.ThrowIfCancellationRequested();
        _ = Access;
        RequireAtomicCommit();

        using var unitOfWork = BeginUnitOfWork();
        var scheduleSession = unitOfWork.OpenSession(scheduleUnit);
        var stateSession = unitOfWork.OpenSession(projectionStateUnit);
        var candidate = ReadProjectionState(stateSession, activationId)
            ?? throw new InvalidOperationException(
                $"Activation '{activationId}' has no prepared recurring-schedule projection.");
        var hasDistinctReplacement = replacedActivationId is not null &&
            !StringComparer.Ordinal.Equals(activationId, replacedActivationId);
        var replaced = hasDistinctReplacement
            ? ReadProjectionState(stateSession, replacedActivationId!)
            : null;
        var candidateRows = ListAllByActivation(scheduleSession, activationId, cancellationToken);
        if (candidate.State.IsActive)
            EnsureActiveProjection(candidate.State, candidateRows, scheduleSession);
        else
            EnsureProjectionMatches(candidate.State, candidateRows);

        if (candidate.State.IsActive)
        {
            if (!hasDistinctReplacement || replaced is null || !replaced.Value.State.IsActive)
            {
                if (candidateRows.Any(row => !row.Schedule.IsActive))
                {
                    throw new InvalidDataException(
                        $"Recurring-schedule activation '{activationId}' is active but has non-active rows.");
                }

                return;
            }

            throw new InvalidOperationException(
                $"Recurring-schedule activation '{activationId}' is active while replaced activation '{replacedActivationId}' is still active.");
        }

        if (hasDistinctReplacement && (replaced is null || !replaced.Value.State.IsActive))
        {
            throw new InvalidOperationException(
                $"Recurring-schedule activation '{activationId}' cannot replace a projection that is missing or no longer active.");
        }

        var replacedRows = hasDistinctReplacement
            ? ListAllByActivation(scheduleSession, replacedActivationId!, cancellationToken)
            : [];
        if (replaced is not null)
        {
            if (replaced.Value.State.IsActive)
                EnsureActiveProjection(replaced.Value.State, replacedRows, scheduleSession);
            else
                EnsureProjectionMatches(replaced.Value.State, replacedRows);
        }

        foreach (var row in candidateRows)
            StageUpsert(unitOfWork, row.Schedule with { IsActive = true }, row.Version);
        foreach (var row in replacedRows)
            StageUpsert(unitOfWork, row.Schedule with { IsActive = false }, row.Version);

        StageProjectionState(
            unitOfWork,
            candidate.State with { IsActive = true },
            candidate.Version,
            createOnly: false);
        if (replaced is not null)
        {
            StageProjectionState(
                unitOfWork,
                ProjectionState(
                    replaced.Value.State.ActivationId,
                    replacedRows.Select(row => row.Schedule).ToArray(),
                    isActive: false,
                    replaced.Value.State.ArtifactId),
                replaced.Value.Version,
                createOnly: false);
        }

        await CommitAsync(unitOfWork, cancellationToken);
    }

    public async ValueTask DeleteByActivationAsync(
        string activationId,
        CancellationToken cancellationToken = default)
    {
        ValidateActivationId(activationId);
        cancellationToken.ThrowIfCancellationRequested();
        _ = Access;
        RequireAtomicCommit();

        using var unitOfWork = BeginUnitOfWork();
        var scheduleSession = unitOfWork.OpenSession(scheduleUnit);
        var stateSession = unitOfWork.OpenSession(projectionStateUnit);
        var rows = ListAllByActivation(scheduleSession, activationId, cancellationToken);
        var state = ReadProjectionState(stateSession, activationId);
        foreach (var row in rows)
            StageDelete(unitOfWork, scheduleUnit, row.Schedule.ScheduleId, row.Version);
        if (state is not null)
        {
            if (state.Value.State.IsActive)
                EnsureActiveProjection(state.Value.State, rows, scheduleSession);
            else
                EnsureProjectionMatches(state.Value.State, rows);
            unitOfWork.Stage(RowWrite.Delete(
                projectionStateUnit,
                GroundworkRuntimeRowStore.Key(ProjectionStateId(activationId)),
                WriteOptions.IfVersion(state.Value.Version)));
        }

        await CommitAsync(unitOfWork, cancellationToken);
    }

    public ValueTask<IReadOnlyCollection<RecurringTriggerSchedule>> ListDueAsync(
        DateTimeOffset asOf,
        int limit,
        CancellationToken cancellationToken = default)
    {
        RuntimeStorePageRequest.ValidateLimit(limit, nameof(limit));
        cancellationToken.ThrowIfCancellationRequested();

        var table = new TableId(scheduleUnit.Name);
        var active = Column(table, ElsaRuntimeV2StorageManifest.RecurringTriggerScheduleIsActiveField);
        var nextOccurrence = Column(table, ElsaRuntimeV2StorageManifest.RecurringTriggerScheduleNextOccurrenceField);
        var scheduleId = Column(table, ElsaRuntimeV2StorageManifest.RecurringTriggerScheduleIdField);
        var result = OpenScheduleSession().Query(new QueryRequest(
            table,
            new Predicate.And([
                Equal(active, true),
                Due(nextOccurrence, asOf)
            ]),
            [
                new OrderTerm(nextOccurrence, OrderDirection.Ascending, NullOrder.Last),
                new OrderTerm(scheduleId, OrderDirection.Ascending, NullOrder.Last)
            ],
            Projection.All,
            Paging.Keyset(limit)));

        if (result.Rows.Count == 0 && result.NextContinuationToken is not null)
        {
            throw new InvalidDataException(
                "Groundwork recurring-trigger due query returned a continuation after an empty page.");
        }

        var schedules = result.Rows.Select(GroundworkV2RecurringTriggerScheduleStorageConventions.Deserialize).ToArray();
        if (schedules.Any(schedule => !schedule.IsActive || schedule.NextOccurrence > asOf))
        {
            throw new InvalidDataException(
                "Groundwork recurring-trigger due query returned a row outside its active and due predicate.");
        }

        return ValueTask.FromResult<IReadOnlyCollection<RecurringTriggerSchedule>>(schedules);
    }

    public ValueTask<RecurringTriggerSchedule?> FindAsync(
        string scheduleId,
        CancellationToken cancellationToken = default)
    {
        GroundworkV2RecurringTriggerScheduleStorageConventions.ValidateScheduleId(scheduleId);
        cancellationToken.ThrowIfCancellationRequested();
        var entry = OpenScheduleSession().Read(
            GroundworkRuntimeRowStore.Key(
                GroundworkV2RecurringTriggerScheduleStorageConventions.PhysicalId(scheduleId)));
        if (entry is null)
            return ValueTask.FromResult<RecurringTriggerSchedule?>(null);

        var schedule = GroundworkV2RecurringTriggerScheduleStorageConventions.Deserialize(entry.Values.Values);
        EnsureIdentity(schedule, scheduleId);
        return ValueTask.FromResult<RecurringTriggerSchedule?>(schedule);
    }

    public ValueTask<bool> TryAdvanceAsync(
        string scheduleId,
        DateTimeOffset expectedNextOccurrence,
        DateTimeOffset newNextOccurrence,
        CancellationToken cancellationToken = default)
    {
        GroundworkV2RecurringTriggerScheduleStorageConventions.ValidateScheduleId(scheduleId);
        cancellationToken.ThrowIfCancellationRequested();
        var session = OpenScheduleSession();
        var key = GroundworkRuntimeRowStore.Key(
            GroundworkV2RecurringTriggerScheduleStorageConventions.PhysicalId(scheduleId));
        var entry = session.Read(key);
        if (entry is null)
            return ValueTask.FromResult(false);

        var current = GroundworkV2RecurringTriggerScheduleStorageConventions.Deserialize(entry.Values.Values);
        EnsureIdentity(current, scheduleId);
        if (!current.IsActive || current.NextOccurrence != expectedNextOccurrence)
            return ValueTask.FromResult(false);

        var revision = entry.Version ?? throw new InvalidDataException(
            $"Groundwork recurring-trigger schedule '{scheduleId}' did not expose an optimistic revision.");
        var result = ConditionalUpsert(
            session,
            GroundworkV2RecurringTriggerScheduleStorageConventions.Values(
                current with { NextOccurrence = newNextOccurrence }),
            revision);
        return ValueTask.FromResult(result.Status switch
        {
            WriteOutcomeStatus.Inserted or WriteOutcomeStatus.Updated or WriteOutcomeStatus.Upserted or WriteOutcomeStatus.Replayed => true,
            WriteOutcomeStatus.ConcurrencyConflict or WriteOutcomeStatus.NotFound => false,
            _ => throw new InvalidOperationException(
                $"Groundwork recurring-trigger schedule '{scheduleId}' advance failed with status '{result.Status}'.")
        });
    }

    public async ValueTask DeleteByArtifactAsync(
        string artifactId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactId);
        cancellationToken.ThrowIfCancellationRequested();
        _ = Access;
        RequireAtomicCommit();

        using var unitOfWork = BeginUnitOfWork();
        var scheduleSession = unitOfWork.OpenSession(scheduleUnit);
        var stateSession = unitOfWork.OpenSession(projectionStateUnit);
        var rows = ListAll(scheduleSession, Equal(
            Column(new TableId(scheduleUnit.Name), ElsaRuntimeV2StorageManifest.ArtifactIdField),
            artifactId), cancellationToken);
        var activationRows = rows
            .Where(row => row.Schedule.ActivationId is not null)
            .GroupBy(row => row.Schedule.ActivationId!, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => ListAllByActivation(scheduleSession, group.Key, cancellationToken),
                StringComparer.Ordinal);
        var statesToDelete = ListProjectionStatesByArtifact(stateSession, artifactId, cancellationToken)
            .ToDictionary(item => item.State.ActivationId, StringComparer.Ordinal);
        foreach (var activationId in activationRows.Keys.Concat(statesToDelete.Keys).Distinct(StringComparer.Ordinal))
        {
            var allRows = activationRows.TryGetValue(activationId, out var discoveredRows)
                ? discoveredRows
                : ListAllByActivation(scheduleSession, activationId, cancellationToken);
            if (allRows.Any(row => !StringComparer.Ordinal.Equals(row.Schedule.ArtifactId, artifactId)))
            {
                throw new InvalidDataException(
                    $"Recurring-schedule activation '{activationId}' does not match artifact ownership '{artifactId}'.");
            }

            var state = statesToDelete.TryGetValue(activationId, out var discoveredState)
                ? discoveredState
                : ReadProjectionState(stateSession, activationId);
            if (state is not null)
            {
                if (!StringComparer.Ordinal.Equals(state.Value.State.ArtifactId, artifactId))
                {
                    throw new InvalidDataException(
                        $"Recurring-schedule activation '{activationId}' has invalid artifact ownership.");
                }

                if (state.Value.State.IsActive)
                    EnsureActiveProjection(state.Value.State, allRows, scheduleSession);
                else
                    EnsureProjectionMatches(state.Value.State, allRows);
                statesToDelete[activationId] = state.Value;
            }
        }

        foreach (var row in rows)
            StageDelete(unitOfWork, scheduleUnit, row.Schedule.ScheduleId, row.Version);
        foreach (var (activationId, state) in statesToDelete)
        {
            unitOfWork.Stage(RowWrite.Delete(
                projectionStateUnit,
                GroundworkRuntimeRowStore.Key(ProjectionStateId(activationId)),
                WriteOptions.IfVersion(state.Version)));
        }

        await CommitAsync(unitOfWork, cancellationToken);
    }

    public ValueTask DeleteAsync(
        string scheduleId,
        CancellationToken cancellationToken = default)
    {
        GroundworkV2RecurringTriggerScheduleStorageConventions.ValidateScheduleId(scheduleId);
        cancellationToken.ThrowIfCancellationRequested();
        var session = OpenScheduleSession();
        var key = GroundworkRuntimeRowStore.Key(
            GroundworkV2RecurringTriggerScheduleStorageConventions.PhysicalId(scheduleId));
        var entry = session.Read(key);
        if (entry is null)
            return ValueTask.CompletedTask;

        var schedule = GroundworkV2RecurringTriggerScheduleStorageConventions.Deserialize(entry.Values.Values);
        EnsureIdentity(schedule, scheduleId);
        if (schedule.ActivationId is not null)
        {
            var state = ReadProjectionState(OpenProjectionStateSession(), schedule.ActivationId);
            if (state is { State.IsActive: false })
            {
                throw new InvalidOperationException(
                    $"Cannot delete recurring-trigger schedule '{scheduleId}' from inactive prepared activation '{schedule.ActivationId}'.");
            }

            if (state is { State.IsActive: true })
                EnsureActiveSchedule(state.Value.State, schedule);
        }
        var revision = entry.Version ?? throw new InvalidDataException(
            $"Groundwork recurring-trigger schedule '{scheduleId}' did not expose an optimistic revision.");
        var result = session.Delete(key, WriteOptions.IfVersion(revision));
        if (result.Status is WriteOutcomeStatus.Deleted or WriteOutcomeStatus.NotFound)
            return ValueTask.CompletedTask;
        if (result.Status == WriteOutcomeStatus.ConcurrencyConflict)
        {
            throw new InvalidOperationException(
                $"Groundwork recurring-trigger schedule '{scheduleId}' changed concurrently and was not deleted.");
        }

        throw new InvalidOperationException(
            $"Groundwork recurring-trigger schedule '{scheduleId}' delete failed with status '{result.Status}'.");
    }

    private RuntimeStorePage<RecurringTriggerSchedule> Page(
        RuntimeStorePageRequest query,
        QueryMaterializedResult result)
    {
        if (result.Rows.Count == 0 && result.NextContinuationToken is not null)
        {
            throw new InvalidDataException(
                "Groundwork recurring-trigger schedule query returned a continuation after an empty page.");
        }

        return new RuntimeStorePage<RecurringTriggerSchedule>(
            query,
            result.Rows.Select(GroundworkV2RecurringTriggerScheduleStorageConventions.Deserialize).ToArray(),
            result.NextContinuationToken);
    }

    private QueryMaterializedResult QueryWithBoundCursor(QueryRequest request, string? cursor)
    {
        try
        {
            return OpenScheduleSession().Query(request);
        }
        catch (Exception exception) when (
            cursor is not null &&
            (exception is QueryRenderException { Code: "GW-QUERY-013" } ||
             exception is FormatException ||
             exception.InnerException is FormatException))
        {
            throw new ArgumentException(
                "The recurring-trigger schedule continuation token is invalid or does not belong to this query.",
                "continuationToken",
                exception);
        }
    }

    private List<StoredSchedule> ListAllByActivation(
        IStorageSession session,
        string activationId,
        CancellationToken cancellationToken) =>
        ListAll(
            session,
            Equal(
                Column(new TableId(scheduleUnit.Name), ElsaRuntimeV2StorageManifest.RecurringTriggerScheduleActivationIdField),
                activationId),
            cancellationToken);

    private List<StoredSchedule> ListAll(
        IStorageSession session,
        Predicate predicate,
        CancellationToken cancellationToken)
    {
        var table = new TableId(scheduleUnit.Name);
        var scheduleId = Column(table, ElsaRuntimeV2StorageManifest.RecurringTriggerScheduleIdField);
        var rows = new List<StoredSchedule>();
        var seenContinuations = new HashSet<string>(StringComparer.Ordinal);
        string? continuationToken = null;
        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = session.Query(new QueryRequest(
                table,
                predicate,
                [new OrderTerm(scheduleId, OrderDirection.Ascending, NullOrder.Last)],
                Projection.All,
                PagingFor(RuntimeStorePageRequest.MaximumLimit, continuationToken)));
            if (result.Rows.Count == 0 && result.NextContinuationToken is not null)
            {
                throw new InvalidDataException(
                    "Groundwork recurring-trigger schedule query returned a continuation after an empty page.");
            }

            foreach (var values in result.Rows)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var schedule = GroundworkV2RecurringTriggerScheduleStorageConventions.Deserialize(values);
                var entry = session.Read(GroundworkRuntimeRowStore.Key(
                    GroundworkV2RecurringTriggerScheduleStorageConventions.PhysicalId(schedule.ScheduleId)));
                if (entry is null)
                    continue;
                var current = GroundworkV2RecurringTriggerScheduleStorageConventions.Deserialize(entry.Values.Values);
                EnsureIdentity(current, schedule.ScheduleId);
                rows.Add(new StoredSchedule(
                    current,
                    entry.Version ?? throw new InvalidDataException(
                        $"Groundwork recurring-trigger schedule '{schedule.ScheduleId}' did not expose an optimistic revision.")));
            }

            continuationToken = result.NextContinuationToken;
            if (continuationToken is not null && !seenContinuations.Add(continuationToken))
            {
                throw new InvalidDataException(
                    "Groundwork recurring-trigger schedule query repeated or cycled a continuation token.");
            }
        } while (continuationToken is not null);

        return rows;
    }

    private List<(GroundworkV2RecurringTriggerScheduleProjectionState State, long Version)>
        ListProjectionStatesByArtifact(
            IStorageSession session,
            string artifactId,
            CancellationToken cancellationToken)
    {
        var table = new TableId(projectionStateUnit.Name);
        var projectionKind = ProjectionStateColumn(
            table,
            ElsaRuntimeV2StorageManifest.PublicationProjectionKindField);
        var artifact = ProjectionStateColumn(
            table,
            ElsaRuntimeV2StorageManifest.PublicationProjectionArtifactIdField);
        var id = ProjectionStateColumn(table, ElsaRuntimeV2StorageManifest.IdField);
        var states = new List<(GroundworkV2RecurringTriggerScheduleProjectionState State, long Version)>();
        var seenContinuations = new HashSet<string>(StringComparer.Ordinal);
        string? continuationToken = null;
        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = session.Query(new QueryRequest(
                table,
                new Predicate.And([
                    Equal(projectionKind, ProjectionKind),
                    Equal(artifact, artifactId)
                ]),
                [new OrderTerm(id, OrderDirection.Ascending, NullOrder.Last)],
                Projection.All,
                PagingFor(RuntimeStorePageRequest.MaximumLimit, continuationToken)));
            if (result.Rows.Count == 0 && result.NextContinuationToken is not null)
            {
                throw new InvalidDataException(
                    "Groundwork recurring-trigger projection-state query returned a continuation after an empty page.");
            }

            foreach (var values in result.Rows)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var queried = DeserializeProjectionState(values);
                if (!StringComparer.Ordinal.Equals(queried.ArtifactId, artifactId))
                {
                    throw new InvalidDataException(
                        "Groundwork recurring-trigger projection-state query returned a row outside its artifact predicate.");
                }

                var entry = session.Read(GroundworkRuntimeRowStore.Key(ProjectionStateId(queried.ActivationId)));
                if (entry is null)
                    continue;
                var current = DeserializeProjectionState(entry.Values.Values);
                if (!StringComparer.Ordinal.Equals(current.ActivationId, queried.ActivationId) ||
                    !StringComparer.Ordinal.Equals(current.ArtifactId, artifactId))
                {
                    throw new InvalidDataException(
                        "Groundwork recurring-trigger projection state changed outside the artifact cleanup predicate.");
                }

                states.Add((
                    current,
                    entry.Version ?? throw new InvalidDataException(
                        $"Groundwork recurring-trigger projection state '{current.ActivationId}' did not expose an optimistic revision.")));
            }

            continuationToken = result.NextContinuationToken;
            if (continuationToken is not null && !seenContinuations.Add(continuationToken))
            {
                throw new InvalidDataException(
                    "Groundwork recurring-trigger projection-state query repeated or cycled a continuation token.");
            }
        } while (continuationToken is not null);

        return states;
    }

    private static bool ProjectionsEqual(
        IReadOnlyCollection<StoredSchedule> existing,
        IReadOnlyCollection<RecurringTriggerSchedule> prepared)
    {
        if (existing.Count != prepared.Count)
            return false;

        var existingById = existing.ToDictionary(row => row.Schedule.ScheduleId, StringComparer.Ordinal);
        foreach (var expected in prepared)
        {
            if (!existingById.TryGetValue(expected.ScheduleId, out var actual) ||
                actual.Schedule.IsActive ||
                !StringComparer.Ordinal.Equals(
                    GroundworkV2RuntimeJson.Serialize(actual.Schedule with { IsActive = false }),
                    GroundworkV2RuntimeJson.Serialize(expected)))
            {
                return false;
            }
        }

        return true;
    }

    private static bool ProjectionMatches(
        GroundworkV2RecurringTriggerScheduleProjectionState state,
        IReadOnlyCollection<StoredSchedule> rows)
    {
        if (state.IsActive ||
            state.ProjectionKind != ProjectionKind ||
            state.ScheduleCount != rows.Count ||
            state.ScheduleIds.Count != rows.Count ||
            !StringComparer.Ordinal.Equals(
                state.ProjectionFingerprint,
                ProjectionFingerprint(rows.Select(row => row.Schedule))))
        {
            return false;
        }

        var rowsById = rows.ToDictionary(row => row.Schedule.ScheduleId, StringComparer.Ordinal);
        return rows.All(row =>
                   !row.Schedule.IsActive &&
                   StringComparer.Ordinal.Equals(row.Schedule.ActivationId, state.ActivationId) &&
                   StringComparer.Ordinal.Equals(row.Schedule.ArtifactId, state.ArtifactId)) &&
               state.ScheduleIds.All(rowsById.ContainsKey) &&
               state.ScheduleFingerprints.All(pair =>
                   rowsById.TryGetValue(pair.Key, out var row) &&
                   StringComparer.Ordinal.Equals(pair.Value, ImmutableFingerprint(row.Schedule)));
    }

    private static void EnsureProjectionMatches(
        GroundworkV2RecurringTriggerScheduleProjectionState state,
        IReadOnlyCollection<StoredSchedule> rows)
    {
        if (!ProjectionMatches(state, rows))
        {
            throw new InvalidDataException(
                $"Groundwork recurring-schedule activation projection '{state.ActivationId}' does not match its projection state.");
        }
    }

    private static void EnsureActiveProjection(
        GroundworkV2RecurringTriggerScheduleProjectionState state,
        IReadOnlyCollection<StoredSchedule> rows,
        IStorageSession session)
    {
        if (!state.IsActive ||
            state.ProjectionKind != ProjectionKind ||
            (state.ScheduleCount > 0 && string.IsNullOrWhiteSpace(state.ArtifactId)) ||
            !StringComparer.Ordinal.Equals(state.ActivationId, rows.FirstOrDefault()?.Schedule.ActivationId ?? state.ActivationId) ||
            state.ScheduleIds is null ||
            state.ScheduleFingerprints is null ||
            state.ScheduleCount != state.ScheduleIds.Count ||
            state.ScheduleIds.Distinct(StringComparer.Ordinal).Count() != state.ScheduleIds.Count ||
            state.ScheduleFingerprints.Count != state.ScheduleIds.Count ||
            state.ScheduleIds.Any(id => !state.ScheduleFingerprints.ContainsKey(id)))
        {
            throw new InvalidDataException(
                $"Groundwork recurring-schedule active projection '{state.ActivationId}' has invalid identity state.");
        }

        var currentIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            if (!currentIds.Add(row.Schedule.ScheduleId) ||
                !StringComparer.Ordinal.Equals(row.Schedule.ActivationId, state.ActivationId) ||
                !StringComparer.Ordinal.Equals(row.Schedule.ArtifactId, state.ArtifactId))
            {
                throw new InvalidDataException(
                    $"Groundwork recurring-schedule active projection '{state.ActivationId}' contains an unexpected row.");
            }

            EnsureActiveSchedule(state, row.Schedule);
        }

        // A missing row is an allowed operational outcome (the pump may have exhausted and deleted it),
        // but a row still present under an expected key must retain its immutable activation identity.
        foreach (var scheduleId in state.ScheduleIds)
        {
            var entry = session.Read(GroundworkRuntimeRowStore.Key(
                GroundworkV2RecurringTriggerScheduleStorageConventions.PhysicalId(scheduleId)));
            if (entry is null)
                continue;

            var schedule = GroundworkV2RecurringTriggerScheduleStorageConventions.Deserialize(entry.Values.Values);
            EnsureIdentity(schedule, scheduleId);
            EnsureActiveSchedule(state, schedule);
        }
    }

    private static void EnsureActiveSchedule(
        GroundworkV2RecurringTriggerScheduleProjectionState state,
        RecurringTriggerSchedule schedule)
    {
        if (!schedule.IsActive ||
            !StringComparer.Ordinal.Equals(schedule.ActivationId, state.ActivationId) ||
            !StringComparer.Ordinal.Equals(schedule.ArtifactId, state.ArtifactId) ||
            !state.ScheduleFingerprints.TryGetValue(schedule.ScheduleId, out var expectedFingerprint) ||
            !StringComparer.Ordinal.Equals(expectedFingerprint, ImmutableFingerprint(schedule)))
        {
            throw new InvalidDataException(
                $"Groundwork recurring-schedule active projection '{state.ActivationId}' contains a row with invalid immutable state.");
        }
    }

    private static GroundworkV2RecurringTriggerScheduleProjectionState ProjectionState(
        string activationId,
        IReadOnlyCollection<RecurringTriggerSchedule> schedules,
        bool isActive,
        string? retainedArtifactId = null)
    {
        var ordered = schedules
            .OrderBy(schedule => schedule.ScheduleId, StringComparer.Ordinal)
            .ToArray();
        var artifacts = ordered
            .Select(schedule => schedule.ArtifactId)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (artifacts.Length > 1)
        {
            throw new InvalidDataException(
                $"Recurring-schedule activation projection '{activationId}' contains multiple artifacts.");
        }

        var artifactId = artifacts.SingleOrDefault() ?? retainedArtifactId;
        if (artifacts.Length == 1 && retainedArtifactId is not null &&
            !StringComparer.Ordinal.Equals(artifacts[0], retainedArtifactId))
        {
            throw new InvalidDataException(
                $"Recurring-schedule activation projection '{activationId}' changed artifact ownership.");
        }

        return new(
            ProjectionKind,
            activationId,
            artifactId,
            isActive,
            ordered.Length,
            ProjectionFingerprint(ordered),
            ordered.Select(schedule => schedule.ScheduleId).ToArray(),
            ordered.ToDictionary(
                schedule => schedule.ScheduleId,
                ImmutableFingerprint,
                StringComparer.Ordinal));
    }

    private static string ProjectionFingerprint(IEnumerable<RecurringTriggerSchedule> schedules)
    {
        var canonical = GroundworkV2RuntimeJson.Serialize(
            schedules
                .Select(schedule => schedule with { IsActive = false })
                .OrderBy(schedule => schedule.ScheduleId, StringComparer.Ordinal)
                .ToArray());
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private static string ImmutableFingerprint(RecurringTriggerSchedule schedule)
    {
        var canonical = GroundworkV2RuntimeJson.Serialize(
            schedule with
            {
                IsActive = false,
                NextOccurrence = DateTimeOffset.MinValue
            });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private static bool SchedulesEqual(RecurringTriggerSchedule left, RecurringTriggerSchedule right) =>
        StringComparer.Ordinal.Equals(
            GroundworkV2RuntimeJson.Serialize(left),
            GroundworkV2RuntimeJson.Serialize(right));

    private void StageUpsert(
        IUnitOfWork unitOfWork,
        RecurringTriggerSchedule schedule,
        long expectedVersion) =>
        unitOfWork.Stage(RowWrite.Upsert(
            scheduleUnit,
            GroundworkV2RecurringTriggerScheduleStorageConventions.Values(schedule),
            WriteOptions.IfVersion(expectedVersion)));

    private void StageInsert(IUnitOfWork unitOfWork, RecurringTriggerSchedule schedule) =>
        unitOfWork.Stage(RowWrite.Insert(
            scheduleUnit,
            GroundworkV2RecurringTriggerScheduleStorageConventions.Values(schedule),
            WriteOptions.CreateOnly));

    private static void StageDelete(
        IUnitOfWork unitOfWork,
        StorageUnit unit,
        string scheduleId,
        long version) =>
        unitOfWork.Stage(RowWrite.Delete(
            unit,
            GroundworkRuntimeRowStore.Key(
                GroundworkV2RecurringTriggerScheduleStorageConventions.PhysicalId(scheduleId)),
            WriteOptions.IfVersion(version)));

    private void StageProjectionState(
        IUnitOfWork unitOfWork,
        GroundworkV2RecurringTriggerScheduleProjectionState state,
        long? expectedVersion,
        bool createOnly)
    {
        var options = createOnly
            ? WriteOptions.CreateOnly
            : WriteOptions.IfVersion(expectedVersion ?? throw new InvalidOperationException(
                $"Recurring-schedule projection state '{state.ActivationId}' did not expose an optimistic revision."));
        unitOfWork.Stage(RowWrite.Upsert(
            projectionStateUnit,
            GroundworkRuntimeRowStore.Values(
                ProjectionStateId(state.ActivationId),
                ElsaRuntimeV2StorageManifest.SchemaVersion,
                GroundworkV2RuntimeJson.Serialize(state),
                new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    [ElsaRuntimeV2StorageManifest.PublicationProjectionKindField] = state.ProjectionKind,
                    [ElsaRuntimeV2StorageManifest.PublicationProjectionArtifactIdField] = state.ArtifactId
                }),
            options));
    }

    private (GroundworkV2RecurringTriggerScheduleProjectionState State, long Version)? ReadProjectionState(
        IStorageSession session,
        string activationId)
    {
        var entry = session.Read(GroundworkRuntimeRowStore.Key(ProjectionStateId(activationId)));
        if (entry is null)
            return null;
        var state = DeserializeProjectionState(entry.Values.Values);
        if (!StringComparer.Ordinal.Equals(state.ActivationId, activationId))
        {
            throw new InvalidDataException(
                "Groundwork recurring-schedule projection state identity does not match its key.");
        }

        return (state, entry.Version ?? throw new InvalidDataException(
            $"Groundwork recurring-schedule projection state '{activationId}' did not expose an optimistic revision."));
    }

    private static GroundworkV2RecurringTriggerScheduleProjectionState DeserializeProjectionState(
        IReadOnlyDictionary<string, object?> values)
    {
        var schemaVersion = RequiredString(values, ElsaRuntimeV2StorageManifest.SchemaVersionField);
        if (!StringComparer.Ordinal.Equals(schemaVersion, ElsaRuntimeV2StorageManifest.SchemaVersion))
            throw new InvalidDataException("Groundwork recurring-schedule projection state returned an unsupported schema version.");

        var content = values.TryGetValue(ElsaRuntimeV2StorageManifest.ContentField, out var rawContent)
            ? rawContent switch
            {
                string text => text,
                System.Text.Json.JsonElement element => element.GetRawText(),
                System.Text.Json.JsonDocument document => document.RootElement.GetRawText(),
                _ => throw new InvalidDataException("Groundwork recurring-schedule projection state content is not JSON.")
            }
            : throw new InvalidDataException("Groundwork recurring-schedule projection state did not contain JSON content.");
        GroundworkV2RecurringTriggerScheduleProjectionState state;
        try
        {
            state = GroundworkV2RuntimeJson.Deserialize<GroundworkV2RecurringTriggerScheduleProjectionState>(content)
                    ?? throw new InvalidDataException("Groundwork recurring-schedule projection state content was empty.");
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonException
                                          or NotSupportedException
                                          or ArgumentException
                                          or InvalidOperationException
                                          or KeyNotFoundException
                                          or FormatException
                                          or OverflowException)
        {
            throw new InvalidDataException(
                "Groundwork recurring-schedule projection state content was not valid current JSON.",
                exception);
        }

        if (!StringComparer.Ordinal.Equals(state.ProjectionKind, ProjectionKind) ||
            string.IsNullOrWhiteSpace(state.ActivationId) ||
            (state.ArtifactId is not null &&
             (string.IsNullOrWhiteSpace(state.ArtifactId) ||
              state.ArtifactId.Length > ElsaRuntimeV2StorageManifest.RuntimeExecutionIdProjectionLength)) ||
            (state.ScheduleCount > 0 && state.ArtifactId is null) ||
            state.ScheduleCount < 0 ||
            string.IsNullOrWhiteSpace(state.ProjectionFingerprint) ||
            state.ScheduleIds is null ||
            state.ScheduleFingerprints is null ||
            state.ScheduleIds.Count != state.ScheduleCount ||
            state.ScheduleIds.Distinct(StringComparer.Ordinal).Count() != state.ScheduleIds.Count ||
            state.ScheduleFingerprints.Count != state.ScheduleIds.Count ||
            state.ScheduleIds.Any(id => string.IsNullOrWhiteSpace(id) || !state.ScheduleFingerprints.ContainsKey(id)) ||
            state.ScheduleFingerprints.Any(pair => string.IsNullOrWhiteSpace(pair.Key) || string.IsNullOrWhiteSpace(pair.Value)))
        {
            throw new InvalidDataException("Groundwork recurring-schedule projection state has an invalid identity.");
        }

        EnsureProjection(
            values,
            ElsaRuntimeV2StorageManifest.IdField,
            ProjectionStateId(state.ActivationId));
        EnsureProjection(
            values,
            ElsaRuntimeV2StorageManifest.PublicationProjectionKindField,
            state.ProjectionKind);
        EnsureOptionalProjection(
            values,
            ElsaRuntimeV2StorageManifest.PublicationProjectionArtifactIdField,
            state.ArtifactId);
        return state;
    }

    private IStorageSession OpenScheduleSession() => sessions.Open(
        scheduleUnit.Id.Value,
        Access,
        targetName);

    private IStorageSession OpenProjectionStateSession() => sessions.Open(
        projectionStateUnit.Id.Value,
        Access,
        targetName);

    private IUnitOfWork BeginUnitOfWork() => sessions.BeginUnitOfWork(
        Access,
        BatchWriteOptions.Exact,
        [
            ElsaRuntimeV2StorageManifest.RecurringTriggerScheduleDocumentKind,
            ElsaRuntimeV2StorageManifest.PublicationProjectionStateDocumentKind
        ],
        targetName);

    private StorageAccess Access
    {
        get
        {
            var context = accessContextAccessor.Current ??
                          throw new InvalidOperationException(
                              "Recurring-trigger schedule persistence access context is missing.");
            if (context.Scope is null || context.AcrossScopes)
            {
                throw new InvalidOperationException(
                    "Groundwork recurring-trigger schedules require one explicit persistence scope; " +
                    "global and across-scope access are refused.");
            }

            return StorageAccess.Scoped(new StorageScope(context.Scope.Value));
        }
    }

    private void RequireAtomicCommit()
    {
        if (sessions is not IGroundworkStorageCapabilitySource capabilitySource ||
            !capabilitySource.Capabilities(targetName).Any(capability =>
                capability.Id.Equals(WellKnownCapabilities.AtomicCommit)))
        {
            throw new NotSupportedException(
                "Groundwork recurring-schedule activation changes require the provider's evidenced atomic-commit capability.");
        }
    }

    private static WriteOutcome UpdateExisting(
        IStorageSession session,
        StorageValues values,
        StoredEntry existing,
        RecurringTriggerSchedule schedule)
    {
        var previous = GroundworkV2RecurringTriggerScheduleStorageConventions.Deserialize(existing.Values.Values);
        EnsureIdentity(previous, schedule.ScheduleId);
        var revision = existing.Version ?? throw new InvalidDataException(
            $"Groundwork recurring-trigger schedule '{schedule.ScheduleId}' did not expose an optimistic revision.");
        return ConditionalUpsert(session, values, revision);
    }

    private static WriteOutcome ConditionalUpsert(
        IStorageSession session,
        StorageValues values,
        long revision)
    {
        if (session is not IConcurrencyStorageSession concurrency)
        {
            throw new NotSupportedException(
                "The selected Groundwork provider does not advertise optimistic recurring-trigger schedule concurrency.");
        }

        return concurrency.ConditionalUpsert(values, WriteOptions.IfVersion(revision));
    }

    private static void EnsureIdentity(RecurringTriggerSchedule schedule, string scheduleId)
    {
        if (!StringComparer.Ordinal.Equals(schedule.ScheduleId, scheduleId))
        {
            throw new InvalidDataException(
                "Groundwork recurring-trigger schedule row identity does not match its requested key.");
        }
    }

    private static void ValidateActivation(
        string activationId,
        IReadOnlyCollection<RecurringTriggerSchedule> schedules)
    {
        ValidateActivationId(activationId);
        ArgumentNullException.ThrowIfNull(schedules);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        string? artifactId = null;
        foreach (var schedule in schedules)
        {
            GroundworkV2RecurringTriggerScheduleStorageConventions.Validate(schedule);
            if (!StringComparer.Ordinal.Equals(schedule.ActivationId, activationId))
            {
                throw new ArgumentException(
                    $"Schedule '{schedule.ScheduleId}' does not belong to activation '{activationId}'.",
                    nameof(schedules));
            }

            ArgumentException.ThrowIfNullOrWhiteSpace(schedule.SlotId);
            artifactId ??= schedule.ArtifactId;
            if (!StringComparer.Ordinal.Equals(artifactId, schedule.ArtifactId))
            {
                throw new ArgumentException(
                    $"Activation '{activationId}' cannot contain recurring-trigger schedules from multiple artifacts.",
                    nameof(schedules));
            }

            if (!ids.Add(schedule.ScheduleId))
            {
                throw new ArgumentException(
                    $"Activation '{activationId}' contains duplicate recurring-trigger schedule '{schedule.ScheduleId}'.",
                    nameof(schedules));
            }
        }
    }

    private static void ValidateActivationId(string activationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(activationId);
        if (activationId.Length > ElsaRuntimeV2StorageManifest.RuntimeExecutionIdProjectionLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(activationId),
                activationId,
                $"Groundwork recurring-schedule activation identity cannot exceed {ElsaRuntimeV2StorageManifest.RuntimeExecutionIdProjectionLength} characters.");
        }
    }

    private static void EnsureProjection(
        IReadOnlyDictionary<string, object?> values,
        string field,
        string expected)
    {
        if (!values.TryGetValue(field, out var actual) ||
            (actual is string text
                ? !StringComparer.Ordinal.Equals(text, expected)
                : actual is not System.Text.Json.JsonElement { ValueKind: System.Text.Json.JsonValueKind.String } element ||
                  !StringComparer.Ordinal.Equals(element.GetString(), expected)))
        {
            throw new InvalidDataException(
                $"Groundwork recurring-schedule projection state projection '{field}' does not match its current content.");
        }
    }

    private static void EnsureOptionalProjection(
        IReadOnlyDictionary<string, object?> values,
        string field,
        string? expected)
    {
        if (!values.TryGetValue(field, out var actual) ||
            (expected is null
                ? actual is not null && actual is not JsonElement { ValueKind: JsonValueKind.Null }
                : actual is string text
                    ? !StringComparer.Ordinal.Equals(text, expected)
                    : actual is not JsonElement { ValueKind: JsonValueKind.String } element ||
                      !StringComparer.Ordinal.Equals(element.GetString(), expected)))
        {
            throw new InvalidDataException(
                $"Groundwork recurring-schedule projection state projection '{field}' does not match its current content.");
        }
    }

    private static string RequiredString(IReadOnlyDictionary<string, object?> values, string field) =>
        values.TryGetValue(field, out var value) switch
        {
            true when value is string text && !string.IsNullOrWhiteSpace(text) => text,
            true when value is System.Text.Json.JsonElement { ValueKind: System.Text.Json.JsonValueKind.String } element &&
                       !string.IsNullOrWhiteSpace(element.GetString()) => element.GetString()!,
            _ => throw new InvalidDataException(
                $"Groundwork recurring-schedule projection state is missing required string field '{field}'.")
        };

    private ColumnRef Column(TableId table, string name) => Column(scheduleUnit, table, name);

    private ColumnRef ProjectionStateColumn(TableId table, string name) =>
        Column(projectionStateUnit, table, name);

    private static ColumnRef Column(StorageUnit unit, TableId table, string name)
    {
        var definition = unit.Columns.SingleOrDefault(column =>
            StringComparer.Ordinal.Equals(column.Name, name))
            ?? throw new InvalidOperationException(
                $"Groundwork unit '{unit.Id.Value}' does not declare recurring-trigger query column '{name}'.");
        var type = definition.Type switch
        {
            PortableType.String => QueryType.String,
            PortableType.DateTimeOffset => QueryType.DateTimeOffset,
            PortableType.Int32 => QueryType.Int32,
            PortableType.Int64 => QueryType.Int64,
            PortableType.Boolean => QueryType.Boolean,
            _ => throw new InvalidOperationException(
                $"Groundwork recurring-trigger schedule query column '{name}' has unsupported type '{definition.Type}'.")
        };
        return new ColumnRef(table, name, type, definition.IsNullable, definition.MaxLength);
    }

    private static Predicate Equal(ColumnRef column, object? value) =>
        new Predicate.Equal(column, QueryConstant.Of(column, value));

    private static Predicate Due(ColumnRef column, DateTimeOffset value) =>
        new Predicate.Range(column, null, Bound.Inclusive(QueryConstant.Of(column, value)));

    private static Paging PagingFor(int limit, string? continuationToken) =>
        continuationToken is null
            ? Paging.Keyset(limit)
            : Paging.Continuation(continuationToken, limit);

    private static string ProjectionStateId(string activationId) =>
        $"{ProjectionKind}:{activationId.Length}:{activationId}";

    private static bool IsSaved(WriteOutcomeStatus status) =>
        status is WriteOutcomeStatus.Inserted or
        WriteOutcomeStatus.Updated or
        WriteOutcomeStatus.Upserted or
        WriteOutcomeStatus.Replayed;

    private static async ValueTask CommitAsync(IUnitOfWork unitOfWork, CancellationToken cancellationToken)
    {
        BatchWriteReport report;
        try
        {
            report = await unitOfWork.CommitWithOutcomesAsync(cancellationToken);
        }
        catch
        {
            try
            {
                unitOfWork.Rollback();
            }
            catch
            {
                // Preserve the provider's original exception.
            }

            throw;
        }

        if (!report.IsSuccessful)
        {
            try
            {
                unitOfWork.Rollback();
            }
            catch
            {
                // Preserve the provider's attributed failure.
            }

            throw new InvalidOperationException(
                $"Groundwork rejected recurring-schedule activation change with {report.Failed} failed row outcomes.");
        }
    }

    private sealed record StoredSchedule(RecurringTriggerSchedule Schedule, long Version);
}
