using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

/// <summary>Concrete opt-in EF adapter for recurring Timer/Cron start schedules.</summary>
/// <remarks>
/// <para>
/// The JSON envelope remains authoritative. Relational columns are bounded lookup and ordering projections,
/// and every mutation advances the row revision so due-occurrence claims use provider CAS.
/// </para>
/// <para>
/// A switch reads each activation projection as its state row and its schedule rows, in separate statements. Every write
/// that changes whether a schedule serves, or the projection's immutable content, moves the state in the transaction that
/// writes the rows: preparing, switching and deleting a projection create, move or delete its state. So rows read while
/// the state stood still belong to it, and a state that moved while they were read is read again, never taken for a
/// corrupt projection (#2265). Claiming, renewing, settling and releasing an occurrence, and deleting one schedule, write a
/// row and move no state. That is safe: they change only what no projection check compares (the cursor, which the
/// immutable fingerprints leave out, the claim columns, and a row's presence, which an active projection does not require,
/// while a prepared one refuses the delete), and the row's own revision moves, so a switch that read the row loses its
/// write and reads again. <see cref="SaveAsync"/> refuses to change a schedule a projection manages.
/// </para>
/// </remarks>
public sealed class EfRecurringTriggerScheduleStore(
    RuntimeDbContext context,
    IPersistenceAccessContextAccessor accessContextAccessor,
    IRuntimeRecoveryContinuationCodec continuationCodec,
    TimeProvider? timeProvider = null) : IRecurringTriggerScheduleStore
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private const string ProjectionKind = "recurringSchedules";
    private const string ActivationCursorPurpose = "ef-runtime-recurring-schedule-activation-v1";
    private const string ArtifactCursorPurpose = "ef-runtime-recurring-schedule-artifact-v1";
    private const int MaterializationBatchSize = 256;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async ValueTask<RecurringTriggerSchedule> SaveAsync(RecurringTriggerSchedule schedule, CancellationToken cancellationToken = default)
    {
        Validate(schedule);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = RequireScope();
        context.ChangeTracker.Clear();
        var id = Id(scope, schedule.ScheduleId);
        var existing = await context.RecurringTriggerSchedules.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (existing is not null)
        {
            var current = Read(existing, scope, schedule.ScheduleId);
            if (await ManagedByActivationAsync(current, schedule, scope, cancellationToken))
            {
                if (SchedulesEqual(current, schedule))
                    return schedule;
                throw new InvalidOperationException($"Recurring-trigger schedule '{schedule.ScheduleId}' is managed by an activation projection and cannot be changed through direct save.");
            }

            if (SchedulesEqual(current, schedule))
                return schedule;
            var updated = ToEntity(current, scope, id, checked(existing.Revision + 1));
            Copy(updated, schedule, scope, updated.Revision);
            context.RecurringTriggerSchedules.Attach(updated);
            context.Entry(updated).Property(x => x.Revision).OriginalValue = existing.Revision;
            context.Entry(updated).State = EntityState.Modified;
        }
        else
        {
            if (schedule.ActivationId is not null && await ActivationState(scope, schedule.ActivationId, cancellationToken) is not null)
                throw new InvalidDataException($"Recurring-trigger schedule '{schedule.ScheduleId}' does not match its activation-managed state.");
            context.RecurringTriggerSchedules.Add(ToEntity(schedule, scope, id, 1));
        }

        try
        {
            await context.SaveChangesAsync(cancellationToken);
            context.ChangeTracker.Clear();
            return schedule;
        }
        catch (DbUpdateConcurrencyException exception)
        {
            context.ChangeTracker.Clear();
            throw new InvalidOperationException($"Recurring-trigger schedule '{schedule.ScheduleId}' changed concurrently; retry the operation.", exception);
        }
        catch (Exception exception) when (EfRelationalExceptionClassifier.IsSaveConflict(exception, EfWriteConflict.UniqueKey))
        {
            context.ChangeTracker.Clear();
            var winner = await context.RecurringTriggerSchedules.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
            if (winner is not null && SchedulesEqual(Read(winner, scope, schedule.ScheduleId), schedule))
                return schedule;
            throw new InvalidOperationException($"Recurring-trigger schedule '{schedule.ScheduleId}' changed concurrently and was not overwritten.", exception);
        }
    }

    public async ValueTask PrepareActivationAsync(string activationId, IReadOnlyCollection<RecurringTriggerSchedule> schedules, CancellationToken cancellationToken = default)
    {
        ValidateActivation(activationId, schedules);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = RequireScope();
        context.ChangeTracker.Clear();
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var existingState = await ActivationState(scope, activationId, cancellationToken);
        var existingRows = await RowsForActivation(scope, activationId, cancellationToken);
        var prepared = schedules.Select(x => x with { IsActive = false }).ToArray();
        if (existingState is not null)
        {
            ActivationProjectionStateLifecycle.EnsurePreparable(existingState.IsActive, existingState.Revision, "recurring-schedule", activationId);
            if (ProjectionMatches(existingState, existingRows, scope, activationId) && ProjectionsEqual(existingRows, prepared, scope))
            {
                await context.CommitAndClearAsync(transaction, cancellationToken, noChanges: true);
                return;
            }
            throw new InvalidOperationException($"Recurring-schedule activation projection '{activationId}' is already prepared with different state.");
        }

        var existingById = existingRows.ToDictionary(x => Decode(x.ScheduleId), StringComparer.Ordinal);
        var desiredById = prepared.ToDictionary(x => x.ScheduleId, StringComparer.Ordinal);
        foreach (var row in existingRows)
        {
            var id = Decode(row.ScheduleId);
            if (desiredById.TryGetValue(id, out var desired))
                Copy(row, desired, scope, checked(row.Revision + 1));
            else
                context.RecurringTriggerSchedules.Remove(row);
        }
        foreach (var schedule in prepared.Where(x => !existingById.ContainsKey(x.ScheduleId)))
            context.RecurringTriggerSchedules.Add(ToEntity(schedule, scope, Id(scope, schedule.ScheduleId), 1));
        context.RecurringTriggerScheduleProjectionStates.Add(ToStateEntity(CreateProjectionState(scope, activationId, prepared, false), scope, ActivationProjectionStateLifecycle.CreationRevision));

        try
        {
            await context.CommitAndClearAsync(transaction, cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            await context.RollbackAndClearAsync(transaction);
            throw new InvalidOperationException($"Recurring-schedule activation projection '{activationId}' changed concurrently; retry the operation.", exception);
        }
        catch (Exception exception) when (EfRelationalExceptionClassifier.IsSaveConflict(exception, EfWriteConflict.UniqueKey))
        {
            await context.RollbackAndClearAsync(transaction);
            var winner = await ActivationState(scope, activationId, cancellationToken);
            var winnerRows = await RowsForActivation(scope, activationId, cancellationToken);
            if (winner is not null &&
                ActivationProjectionStateLifecycle.Read(winner.IsActive, winner.Revision) == WorkflowActivationProjectionState.Prepared &&
                ProjectionMatches(winner, winnerRows, scope, activationId) && ProjectionsEqual(winnerRows, prepared, scope))
                return;
            throw new InvalidOperationException($"Recurring-schedule activation projection '{activationId}' changed concurrently with different state.", exception);
        }
        catch (Exception exception) when (EfRelationalExceptionClassifier.IsProviderFailure(exception))
        {
            await context.RollbackAndClearAsync(transaction);
            throw new InvalidOperationException($"Recurring-schedule activation projection '{activationId}' could not be committed.", exception);
        }
    }

    public async ValueTask<RuntimeStorePage<RecurringTriggerSchedule>> ListByActivationPageAsync(RecurringTriggerScheduleActivationPageQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ValidateIdentity(query.ActivationId, nameof(query.ActivationId));
        cancellationToken.ThrowIfCancellationRequested();
        var scope = RequireScope();
        var scopeHash = Hash(scope);
        var scopeKey = Encode(scope);
        var activationHash = Hash(query.ActivationId);
        var activationKey = Encode(query.ActivationId);
        var source = context.RecurringTriggerSchedules.AsNoTracking().Where(x => x.ScopeKeyHash == scopeHash && x.ScopeKey == scopeKey && x.ActivationIdHash == activationHash && x.ActivationId == activationKey);
        var last = DecodeCursor(query.ContinuationToken, ActivationCursorPurpose, scope, query.ActivationId, nameof(query.ContinuationToken));
        if (last is not null)
            source = source.Where(x => x.ScheduleIdOrderKey.CompareTo(last) > 0);
        var rows = await source.OrderBy(x => x.ScheduleIdOrderKey).Take(checked(query.Limit + 1)).ToArrayAsync(cancellationToken);
        var more = rows.Length > query.Limit;
        if (more) rows = rows[..query.Limit];
        var items = rows.Select(x => Read(x, scope)).ToArray();
        var next = more ? EncodeCursor(ActivationCursorPurpose, scope, query.ActivationId, items[^1].ScheduleId) : null;
        return new RuntimeStorePage<RecurringTriggerSchedule>(query, items, next);
    }

    public async ValueTask<RuntimeStorePage<RecurringTriggerSchedule>> ListByArtifactPageAsync(RecurringTriggerScheduleArtifactPageQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ValidateIdentity(query.ArtifactId, nameof(query.ArtifactId));
        cancellationToken.ThrowIfCancellationRequested();
        var scope = RequireScope();
        var scopeHash = Hash(scope);
        var scopeKey = Encode(scope);
        var artifactHash = Hash(query.ArtifactId);
        var artifactKey = Encode(query.ArtifactId);
        var source = context.RecurringTriggerSchedules.AsNoTracking().Where(x => x.ScopeKeyHash == scopeHash && x.ScopeKey == scopeKey && x.ArtifactIdHash == artifactHash && x.ArtifactId == artifactKey);
        var last = DecodeCursor(query.ContinuationToken, ArtifactCursorPurpose, scope, query.ArtifactId, nameof(query.ContinuationToken));
        if (last is not null)
            source = source.Where(x => x.ScheduleIdOrderKey.CompareTo(last) > 0);
        var rows = await source.OrderBy(x => x.ScheduleIdOrderKey).Take(checked(query.Limit + 1)).ToArrayAsync(cancellationToken);
        var more = rows.Length > query.Limit;
        if (more) rows = rows[..query.Limit];
        var items = rows.Select(x => Read(x, scope)).ToArray();
        var next = more ? EncodeCursor(ArtifactCursorPurpose, scope, query.ArtifactId, items[^1].ScheduleId) : null;
        return new RuntimeStorePage<RecurringTriggerSchedule>(query, items, next);
    }

    public async ValueTask ActivateAsync(string activationId, string? replacedActivationId, CancellationToken cancellationToken = default)
    {
        ValidateIdentity(activationId, nameof(activationId));
        if (replacedActivationId is not null) ValidateIdentity(replacedActivationId, nameof(replacedActivationId));
        cancellationToken.ThrowIfCancellationRequested();
        var scope = RequireScope();
        var operation = $"Recurring-schedule activation projection '{activationId}'";
        await EfRuntimeOperationalStoreSupport.ProjectionSwitches.RunUntilSettledAsync<bool>(
            context,
            () => TrySwitchAsync(scope, activationId, replacedActivationId, operation, cancellationToken),
            conflict => throw EfRuntimeOperationalStoreSupport.CommitFailure(operation, conflict),
            cancellationToken);
    }

    /// <summary>
    /// One attempt at <see cref="ActivateAsync"/>. It reads both projections first and decides only once neither state moved
    /// while they were read, so a switch, or a deletion and a new preparation, another call committed in between is read
    /// again, never taken for corruption or for a replaced activation that no longer serves. A switch committed after the
    /// reads, or a claim on a row it rewrites, loses this attempt's write to the revisions it read, and the next attempt reads
    /// them again; so does a deadlock that chose this attempt's write as its victim (#2265).
    /// </summary>
    private async ValueTask<EfWriteAttempt<bool>> TrySwitchAsync(string scope, string activationId, string? replacedActivationId, string operation, CancellationToken cancellationToken)
    {
        context.ChangeTracker.Clear();
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var candidate = await ActivationState(scope, activationId, cancellationToken);
        var candidateRows = candidate is null ? [] : await RowsForActivation(scope, activationId, cancellationToken);
        var distinct = replacedActivationId is not null && !StringComparer.Ordinal.Equals(activationId, replacedActivationId);
        var replaced = distinct ? await ActivationState(scope, replacedActivationId!, cancellationToken) : null;
        var replacedRows = replaced is null ? [] : await RowsForActivation(scope, replacedActivationId!, cancellationToken);
        if (await StateMovedAsync(scope, activationId, candidate, cancellationToken) ||
            distinct && await StateMovedAsync(scope, replacedActivationId!, replaced, cancellationToken))
        {
            await context.RollbackAndClearAsync(transaction);
            return EfWriteAttempt<bool>.Retry();
        }

        if (candidate is null)
            throw new InvalidOperationException($"Activation '{activationId}' has no prepared recurring-schedule projection.");
        // The candidate is checked first, so a switch that already happened is a no-op whoever made it (#2193).
        if (candidate.IsActive)
        {
            await EnsureActiveProjectionAsync(candidate, candidateRows, scope, activationId, cancellationToken);
            if (replaced is { IsActive: true })
                throw new InvalidOperationException($"Recurring-schedule activation '{activationId}' is active while replaced activation '{replacedActivationId}' is still active.");
            await context.CommitAndClearAsync(transaction, cancellationToken, noChanges: true);
            return true;
        }
        EnsurePreparedProjection(candidate, candidateRows, scope, activationId);
        if (distinct)
        {
            if (replaced is null || !replaced.IsActive)
                throw new InvalidOperationException($"Recurring-schedule activation '{activationId}' cannot replace a projection that is missing or no longer active.");
            await EnsureActiveProjectionAsync(replaced, replacedRows, scope, replacedActivationId!, cancellationToken);
        }
        // Each activated schedule takes over a due, unsettled occurrence from the replaced schedule of its trigger (#2198),
        // in this transaction. The replaced rows are rewritten below under a new revision, so a claim in flight on one of
        // them is stale from the same commit on, and cannot settle the occurrence the activated schedule now holds.
        var predecessors = replacedRows.Select(x => Read(x, scope)).ToArray();
        var activatedAt = _timeProvider.GetUtcNow();
        foreach (var row in candidateRows)
        {
            var schedule = Read(row, scope);
            var predecessor = predecessors.SingleOrDefault(schedule.IsSameTriggerAs);
            Copy(row, (predecessor is null ? schedule : schedule.TakeOverFrom(predecessor, activatedAt)) with { IsActive = true }, scope, checked(row.Revision + 1));
        }
        candidate.IsActive = true;
        candidate.Revision = checked(candidate.Revision + 1);
        UpdateStateContent(candidate, scope);
        if (replaced is not null)
        {
            foreach (var row in replacedRows)
                Copy(row, Read(row, scope) with { IsActive = false }, scope, checked(row.Revision + 1));
            replaced.IsActive = false;
            replaced.Revision = checked(replaced.Revision + 1);
            UpdateStateContent(replaced, scope);
        }
        return await context.TryCommitAndClearAsync(transaction, EfRuntimeOperationalStoreSupport.ProjectionSwitches, operation, cancellationToken) is { } conflict
            ? EfWriteAttempt<bool>.Retry(conflict)
            : true;
    }

    public async ValueTask<WorkflowActivationProjectionState> FindActivationStateAsync(string activationId, CancellationToken cancellationToken = default)
    {
        ValidateIdentity(activationId, nameof(activationId));
        cancellationToken.ThrowIfCancellationRequested();
        var scope = RequireScope();
        context.ChangeTracker.Clear();
        var state = await ActivationState(scope, activationId, cancellationToken);
        context.ChangeTracker.Clear();
        return state is null
            ? WorkflowActivationProjectionState.Missing
            : ActivationProjectionStateLifecycle.Read(state.IsActive, state.Revision);
    }

    /// <summary>
    /// No index covers the slot, so this reads the scope's active schedules once; only deactivation calls it. The rows'
    /// activation ids are deduplicated here rather than with a database <c>DISTINCT</c> over a text column.
    /// </summary>
    public async ValueTask<IReadOnlyCollection<string>> ListServingActivationIdsAsync(string slotId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slotId);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = RequireScope();
        var scopeHash = Hash(scope);
        var scopeKey = Encode(scope);
        var slotKey = Encode(slotId);
        var activations = await context.RecurringTriggerSchedules.AsNoTracking()
            .Where(x => x.ScopeKeyHash == scopeHash && x.ScopeKey == scopeKey && x.IsActive && x.SlotId == slotKey && x.ActivationId != null)
            .Select(x => x.ActivationId!)
            .ToArrayAsync(cancellationToken);
        return activations.Select(Decode).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    }

    public async ValueTask DeleteByActivationAsync(string activationId, CancellationToken cancellationToken = default)
    {
        ValidateIdentity(activationId, nameof(activationId));
        cancellationToken.ThrowIfCancellationRequested();
        var scope = RequireScope();
        context.ChangeTracker.Clear();
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        // Rows, then state, without ActivateAsync's re-read: a switch committed between them fails this loudly as corrupt (#2265).
        var rows = await RowsForActivation(scope, activationId, cancellationToken);
        foreach (var row in rows)
        {
            var schedule = Read(row, scope, Decode(row.ScheduleId));
            if (schedule.ActivationId != activationId)
                throw new InvalidDataException($"Recurring-schedule activation '{activationId}' contains a row with a different activation identity.");
        }
        var state = await ActivationState(scope, activationId, cancellationToken);
        if (state is not null)
        {
            if (state.IsActive) await EnsureActiveProjectionAsync(state, rows, scope, activationId, cancellationToken);
            else EnsurePreparedProjection(state, rows, scope, activationId);
            context.RecurringTriggerScheduleProjectionStates.Remove(state.Entity);
        }
        context.RecurringTriggerSchedules.RemoveRange(rows);
        await context.CommitMutationAndClearAsync(transaction, $"Recurring-schedule activation projection '{activationId}' deletion", cancellationToken);
    }

    public async ValueTask<RecurringTriggerSchedule?> FindAsync(string scheduleId, CancellationToken cancellationToken = default)
    {
        ValidateScheduleId(scheduleId, nameof(scheduleId));
        cancellationToken.ThrowIfCancellationRequested();
        var scope = RequireScope();
        var row = await context.RecurringTriggerSchedules.AsNoTracking().SingleOrDefaultAsync(x => x.Id == Id(scope, scheduleId), cancellationToken);
        return row is null ? null : Read(row, scope, scheduleId);
    }

    public async ValueTask<IReadOnlyCollection<RecurringTriggerOccurrenceClaim>> ClaimDueAsync(RecurringTriggerOccurrenceClaimRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateIdentity(request.OwnerId, nameof(request.OwnerId));
        cancellationToken.ThrowIfCancellationRequested();
        var scope = RequireScope();
        var scopeHash = Hash(scope);
        var scopeKey = Encode(scope);
        var now = request.Now.UtcTicks;
        context.ChangeTracker.Clear();
        // Only the (scope, active, NextOccurrence) range is served by an index; the visibility filter and the order key's
        // tie-break are evaluated on the rows in that range. That is acceptable because recurring schedules are few: one per
        // Timer/Cron start trigger of an active publication.
        var rows = await context.RecurringTriggerSchedules.AsNoTracking()
            .Where(x => x.ScopeKeyHash == scopeHash && x.ScopeKey == scopeKey && x.IsActive && x.NextOccurrenceUtcTicks <= now &&
                        (x.VisibleAfterUtcTicks == null || x.VisibleAfterUtcTicks <= now))
            .OrderBy(x => x.NextOccurrenceUtcTicks)
            .ThenBy(x => x.ScheduleIdOrderKey)
            .Take(request.Limit)
            .ToArrayAsync(cancellationToken);

        var claims = new List<RecurringTriggerOccurrenceClaim>(rows.Length);
        foreach (var row in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var schedule = Read(row, scope);
            if (!schedule.IsActive || schedule.NextOccurrence > request.Now || row.VisibleAfterUtcTicks is { } visible && visible > now)
                throw new InvalidDataException("Recurring-trigger claim query returned a row outside its active, due and visible predicate.");
            var originalRevision = row.Revision;
            row.ClaimOwnerId = Encode(request.OwnerId);
            row.ClaimToken = NextFencingToken(row);
            (row.ClaimedAtUtcTicks, row.ClaimedAtOffsetMinutes) = EfRuntimeOperationalStoreSupport.TimestampColumns(request.Now);
            (row.VisibleAfterUtcTicks, row.VisibleAfterOffsetMinutes) = EfRuntimeOperationalStoreSupport.TimestampColumns(request.Now.Add(request.VisibilityTimeout));
            row.Revision = checked(row.Revision + 1);
            if (await TryWriteAsync(row, originalRevision, cancellationToken))
                claims.Add(ToClaim(row, schedule));
        }

        return claims;
    }

    public async ValueTask<RecurringTriggerOccurrenceClaim?> RenewClaimAsync(RecurringTriggerOccurrenceClaim claim, DateTimeOffset now, TimeSpan visibilityTimeout, CancellationToken cancellationToken = default)
    {
        if (visibilityTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(visibilityTimeout), "A recurring-trigger claim visibility timeout must be greater than zero.");
        if (await LoadHeldAsync(claim, cancellationToken) is not { } held)
            return null;
        var row = held.Row;
        var originalRevision = row.Revision;
        (row.VisibleAfterUtcTicks, row.VisibleAfterOffsetMinutes) = EfRuntimeOperationalStoreSupport.TimestampColumns(now.Add(visibilityTimeout));
        row.Revision = checked(row.Revision + 1);
        return await TryWriteAsync(row, originalRevision, cancellationToken) ? ToClaim(row, held.Schedule) : null;
    }

    public async ValueTask<bool> SettleClaimAsync(RecurringTriggerOccurrenceClaim claim, DateTimeOffset nextOccurrence, CancellationToken cancellationToken = default)
    {
        if (await LoadHeldAsync(claim, cancellationToken) is not { } held)
            return false;
        var row = held.Row;
        var originalRevision = row.Revision;
        Copy(row, held.Schedule with { NextOccurrence = nextOccurrence }, held.Scope, checked(row.Revision + 1));
        row.ClaimOwnerId = null;
        (row.ClaimedAtUtcTicks, row.ClaimedAtOffsetMinutes) = EfRuntimeOperationalStoreSupport.TimestampColumns(null);
        (row.VisibleAfterUtcTicks, row.VisibleAfterOffsetMinutes) = EfRuntimeOperationalStoreSupport.TimestampColumns(null);
        row.FailureCount = 0;
        return await TryWriteAsync(row, originalRevision, cancellationToken);
    }

    public async ValueTask<bool> ReleaseClaimAsync(RecurringTriggerOccurrenceClaim claim, DateTimeOffset visibleAt, CancellationToken cancellationToken = default)
    {
        if (await LoadHeldAsync(claim, cancellationToken) is not { } held)
            return false;
        var row = held.Row;
        var originalRevision = row.Revision;
        row.ClaimOwnerId = null;
        (row.ClaimedAtUtcTicks, row.ClaimedAtOffsetMinutes) = EfRuntimeOperationalStoreSupport.TimestampColumns(null);
        (row.VisibleAfterUtcTicks, row.VisibleAfterOffsetMinutes) = EfRuntimeOperationalStoreSupport.TimestampColumns(visibleAt);
        row.FailureCount = checked(row.FailureCount + 1);
        row.Revision = checked(row.Revision + 1);
        return await TryWriteAsync(row, originalRevision, cancellationToken);
    }

    // The schedule row a claim transition may act on: present, intact, and still held by exactly this claim — its owner,
    // fencing token and revision — on the very schedule it claimed. Anything else (a peer's re-claim, a deactivation, a
    // republish, a delete) is stale. The schedule comparison also fences a claim out of a schedule deleted and saved again
    // under the same id, whose revision restarts, even if its tokens happened to coincide.
    private async Task<HeldClaim?> LoadHeldAsync(RecurringTriggerOccurrenceClaim claim, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(claim);
        ValidateScheduleId(claim.Schedule.ScheduleId, nameof(claim));
        ValidateIdentity(claim.OwnerId, nameof(claim));
        cancellationToken.ThrowIfCancellationRequested();
        var scope = RequireScope();
        context.ChangeTracker.Clear();
        var row = await context.RecurringTriggerSchedules.AsNoTracking().SingleOrDefaultAsync(x => x.Id == Id(scope, claim.Schedule.ScheduleId), cancellationToken);
        if (row is null)
            return null;
        var schedule = Read(row, scope, claim.Schedule.ScheduleId);
        return row.Revision == claim.Revision && row.ClaimToken == claim.FencingToken && row.ClaimOwnerId == Encode(claim.OwnerId) &&
               SchedulesEqual(schedule, claim.Schedule)
            ? new HeldClaim(row, schedule, scope)
            : null;
    }

    private sealed record HeldClaim(RecurringTriggerScheduleEntity Row, RecurringTriggerSchedule Schedule, string Scope);

    // Writes a detached row under its revision as the concurrency token. A concurrent writer or a transient conflict the
    // provider reports loses the write rather than failing it.
    private async Task<bool> TryWriteAsync(RecurringTriggerScheduleEntity row, long originalRevision, CancellationToken cancellationToken)
    {
        context.RecurringTriggerSchedules.Attach(row);
        context.Entry(row).Property(x => x.Revision).OriginalValue = originalRevision;
        context.Entry(row).State = EntityState.Modified;
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            return false;
        }
        catch (Exception exception) when (EfRelationalExceptionClassifier.IsSaveConflict(exception, EfWriteConflict.Transient))
        {
            return false;
        }
        finally
        {
            context.ChangeTracker.Clear();
        }
    }

    public async ValueTask DeleteByArtifactAsync(string artifactId, CancellationToken cancellationToken = default)
    {
        ValidateIdentity(artifactId, nameof(artifactId));
        cancellationToken.ThrowIfCancellationRequested();
        var scope = RequireScope();
        context.ChangeTracker.Clear();
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var rows = await RowsForArtifact(scope, artifactId, cancellationToken);
        foreach (var row in rows) _ = Read(row, scope);
        var activationRows = rows.Where(x => x.ActivationId is not null).Select(x => Decode(x.ActivationId!)).Distinct(StringComparer.Ordinal).ToArray();
        var states = await StatesForArtifact(scope, artifactId, cancellationToken);
        foreach (var activationId in activationRows.Concat(states.Select(x => x.ActivationId)).Distinct(StringComparer.Ordinal))
        {
            // Rows, then state, without ActivateAsync's re-read: a switch committed between them fails this loudly as corrupt (#2265).
            var all = await RowsForActivation(scope, activationId, cancellationToken);
            if (all.Any(x => !StringComparer.Ordinal.Equals(x.ArtifactId, Encode(artifactId))))
                throw new InvalidDataException($"Recurring-schedule activation '{activationId}' does not match artifact ownership '{artifactId}'.");
            var state = states.SingleOrDefault(x => x.ActivationId == activationId) ?? await ActivationState(scope, activationId, cancellationToken);
            if (state is not null)
            {
                if (state.ArtifactId is not null && state.ArtifactId != Encode(artifactId))
                    throw new InvalidDataException($"Recurring-schedule activation '{activationId}' has invalid artifact ownership.");
                if (state.IsActive) await EnsureActiveProjectionAsync(state, all, scope, activationId, cancellationToken);
                else EnsurePreparedProjection(state, all, scope, activationId);
                context.RecurringTriggerScheduleProjectionStates.Remove(state.Entity);
            }
        }
        context.RecurringTriggerSchedules.RemoveRange(rows);
        await context.CommitMutationAndClearAsync(transaction, $"Recurring-schedule artifact '{artifactId}' deletion", cancellationToken);
    }

    public async ValueTask DeleteAsync(string scheduleId, CancellationToken cancellationToken = default)
    {
        ValidateScheduleId(scheduleId, nameof(scheduleId));
        cancellationToken.ThrowIfCancellationRequested();
        var scope = RequireScope();
        context.ChangeTracker.Clear();
        var row = await context.RecurringTriggerSchedules.AsNoTracking().SingleOrDefaultAsync(x => x.Id == Id(scope, scheduleId), cancellationToken);
        if (row is null) return;
        var schedule = Read(row, scope, scheduleId);
        if (schedule.ActivationId is not null)
        {
            var state = await ActivationState(scope, schedule.ActivationId, cancellationToken);
            if (state is { IsActive: false })
                throw new InvalidOperationException($"Cannot delete recurring-trigger schedule '{scheduleId}' from inactive prepared activation '{schedule.ActivationId}'.");
            if (state is { IsActive: true }) EnsureActiveSchedule(state, schedule);
        }
        context.RecurringTriggerSchedules.Attach(row);
        context.Entry(row).State = EntityState.Deleted;
        context.Entry(row).Property(x => x.Revision).OriginalValue = row.Revision;
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            context.ChangeTracker.Clear();
        }
        catch (DbUpdateConcurrencyException exception)
        {
            context.ChangeTracker.Clear();
            throw new InvalidOperationException($"Recurring-trigger schedule '{scheduleId}' changed concurrently; retry the operation.", exception);
        }
    }

    private async Task<ProjectionStateSnapshot?> ActivationState(string scope, string activationId, CancellationToken ct)
    {
        var row = await context.RecurringTriggerScheduleProjectionStates.SingleOrDefaultAsync(x => x.Id == ProjectionId(scope, activationId), ct);
        return row is null ? null : ReadState(row, scope, activationId);
    }

    private async Task<RecurringTriggerScheduleEntity[]> RowsForActivation(string scope, string activationId, CancellationToken ct)
    {
        var rows = new List<RecurringTriggerScheduleEntity>();
        string? after = null;
        var scopeHash = Hash(scope);
        var scopeKey = Encode(scope);
        var activationHash = Hash(activationId);
        var activationKey = Encode(activationId);
        while (true)
        {
            var page = await context.RecurringTriggerSchedules.Where(x => x.ScopeKeyHash == scopeHash && x.ScopeKey == scopeKey && x.ActivationIdHash == activationHash && x.ActivationId == activationKey && (after == null || x.ScheduleIdOrderKey.CompareTo(after) > 0)).OrderBy(x => x.ScheduleIdOrderKey).Take(MaterializationBatchSize).ToArrayAsync(ct);
            rows.AddRange(page);
            if (page.Length < MaterializationBatchSize) return rows.ToArray();
            after = page[^1].ScheduleIdOrderKey;
        }
    }

    private async Task<RecurringTriggerScheduleEntity[]> RowsForArtifact(string scope, string artifactId, CancellationToken ct)
    {
        var rows = new List<RecurringTriggerScheduleEntity>();
        string? after = null;
        var scopeHash = Hash(scope);
        var scopeKey = Encode(scope);
        var artifactHash = Hash(artifactId);
        var artifactKey = Encode(artifactId);
        while (true)
        {
            var page = await context.RecurringTriggerSchedules.Where(x => x.ScopeKeyHash == scopeHash && x.ScopeKey == scopeKey && x.ArtifactIdHash == artifactHash && x.ArtifactId == artifactKey && (after == null || x.ScheduleIdOrderKey.CompareTo(after) > 0)).OrderBy(x => x.ScheduleIdOrderKey).Take(MaterializationBatchSize).ToArrayAsync(ct);
            rows.AddRange(page);
            if (page.Length < MaterializationBatchSize) return rows.ToArray();
            after = page[^1].ScheduleIdOrderKey;
        }
    }

    private async Task<ProjectionStateSnapshot[]> StatesForArtifact(string scope, string artifactId, CancellationToken ct)
    {
        var scopeHash = Hash(scope);
        var scopeKey = Encode(scope);
        var artifactHash = Hash(artifactId);
        var artifactKey = Encode(artifactId);
        var states = new List<ProjectionStateSnapshot>();
        string? afterOrder = null;
        string? afterId = null;
        while (true)
        {
            var page = await context.RecurringTriggerScheduleProjectionStates.AsNoTracking()
                .Where(x => x.ScopeKeyHash == scopeHash && x.ScopeKey == scopeKey && x.ArtifactIdHash == artifactHash && x.ArtifactId == artifactKey &&
                            (afterOrder == null || x.ActivationIdOrderKey.CompareTo(afterOrder) > 0 ||
                             x.ActivationIdOrderKey == afterOrder && x.Id.CompareTo(afterId!) > 0))
                .OrderBy(x => x.ActivationIdOrderKey).ThenBy(x => x.Id)
                .Take(MaterializationBatchSize).ToArrayAsync(ct);
            states.AddRange(page.Select(x => ReadState(x, scope)));
            if (page.Length < MaterializationBatchSize) return states.ToArray();
            var last = page[^1];
            if (afterOrder is not null &&
                (StringComparer.Ordinal.Compare(last.ActivationIdOrderKey, afterOrder) < 0 ||
                 last.ActivationIdOrderKey == afterOrder && StringComparer.Ordinal.Compare(last.Id, afterId) <= 0))
                throw new InvalidDataException("Recurring-schedule projection cleanup did not advance its activation cursor.");
            afterOrder = last.ActivationIdOrderKey;
            afterId = last.Id;
        }
    }

    private Task<bool> StateMovedAsync(string scope, string activationId, ProjectionStateSnapshot? read, CancellationToken ct) =>
        EfRuntimeOperationalStoreSupport.StateMovedAsync(context.RecurringTriggerScheduleProjectionStates.Where(x => x.Id == ProjectionId(scope, activationId)), read?.Entity, ct);

    private async Task<bool> ManagedByActivationAsync(RecurringTriggerSchedule current, RecurringTriggerSchedule proposed, string scope, CancellationToken cancellationToken)
    {
        foreach (var activationId in new[] { current.ActivationId, proposed.ActivationId }.Where(x => x is not null).Distinct(StringComparer.Ordinal))
        {
            var state = await ActivationState(scope, activationId!, cancellationToken);
            if (state is null) continue;
            if (current.ActivationId != activationId || current.IsActive != state.IsActive || !state.ScheduleFingerprints.TryGetValue(current.ScheduleId, out var fp) || fp != ImmutableFingerprint(current))
                throw new InvalidDataException($"Recurring-trigger schedule '{proposed.ScheduleId}' does not match its activation-managed state.");
            return true;
        }
        return false;
    }

    private string RequireScope() => EfRuntimeOperationalStoreSupport.RequireScope(accessContextAccessor);
    private static string Encode(string value) => EfRuntimeOperationalStoreSupport.Encode(value);
    private static string Decode(string value) => EfRuntimeOperationalStoreSupport.Decode(value);
    private static string Hash(string value) => EfRuntimeOperationalStoreSupport.Hash(value);
    private static string Order(string value) => EfRuntimeOperationalStoreSupport.Order(value);
    private static string Id(string scope, string scheduleId) => EfRuntimeOperationalStoreSupport.CompositeId(scope, scheduleId);
    private static string ProjectionId(string scope, string activationId) => EfRuntimeOperationalStoreSupport.CompositeId(scope, ProjectionKind, activationId);

    private static RecurringTriggerScheduleEntity ToEntity(RecurringTriggerSchedule schedule, string scope, string id, long revision) { var row = new RecurringTriggerScheduleEntity { Id = id }; Copy(row, schedule, scope, revision); return row; }
    private static void Copy(RecurringTriggerScheduleEntity row, RecurringTriggerSchedule schedule, string scope, long revision)
    {
        Validate(schedule);
        row.ScopeKey = Encode(scope); row.ScopeKeyHash = Hash(scope); row.ScheduleId = Encode(schedule.ScheduleId); row.ScheduleIdHash = Hash(schedule.ScheduleId); row.ScheduleIdOrderKey = ScheduleOrder(schedule.ScheduleId); row.ArtifactId = Encode(schedule.ArtifactId); row.ArtifactIdHash = Hash(schedule.ArtifactId); row.ArtifactIdOrderKey = Order(schedule.ArtifactId); row.ExecutableNodeId = Encode(schedule.ExecutableNodeId); row.StimulusType = Encode(schedule.StimulusType); row.StimulusHash = Encode(schedule.StimulusHash); row.Kind = (int)schedule.Kind; row.Expression = schedule.Expression; row.NextOccurrenceUtcTicks = schedule.NextOccurrence.UtcTicks; row.NextOccurrenceOffsetMinutes = (int)schedule.NextOccurrence.Offset.TotalMinutes; row.CreatedAtUtcTicks = schedule.CreatedAt.UtcTicks; row.CreatedAtOffsetMinutes = (int)schedule.CreatedAt.Offset.TotalMinutes; row.ActivationId = schedule.ActivationId is null ? null : Encode(schedule.ActivationId); row.ActivationIdHash = schedule.ActivationId is null ? null : Hash(schedule.ActivationId); row.ActivationIdOrderKey = schedule.ActivationId is null ? null : Order(schedule.ActivationId); row.SlotId = schedule.SlotId is null ? null : Encode(schedule.SlotId); row.IsActive = schedule.IsActive; row.ContentJson = RuntimeArtifactJson.Serialize(schedule); row.SchemaVersion = RuntimeOperationalStateEfModule.SchemaVersion; row.Revision = revision;
    }

    private static RecurringTriggerSchedule Read(RecurringTriggerScheduleEntity row, string scope, string? expectedId = null)
    {
        if (EfSchemaVersion.NotReadable(RuntimeOperationalStateEfModule.Chain, row.SchemaVersion) || row.Id != Id(scope, Decode(row.ScheduleId)) || row.ScopeKey != Encode(scope) || row.ScopeKeyHash != Hash(scope) || expectedId is not null && row.ScheduleId != Encode(expectedId) || row.Revision <= 0)
            throw new InvalidDataException("The persisted EF recurring-trigger schedule row does not match its identity envelope.");
        RecurringTriggerSchedule schedule;
        var scheduleContent = RuntimeOperationalStateEfModule.Chain.Upcast<RecurringTriggerScheduleEntity>(row.SchemaVersion, (nameof(row.ContentJson), row.ContentJson))[nameof(row.ContentJson)]!;
        try { schedule = RuntimeArtifactJson.Deserialize<RecurringTriggerSchedule>(scheduleContent); }
        catch (Exception exception) when (exception is JsonException or NotSupportedException or ArgumentException or InvalidOperationException or FormatException)
        { throw new InvalidDataException("The persisted EF recurring-trigger schedule content is not valid current JSON.", exception); }
        Validate(schedule);
        if (schedule.ScheduleId != Decode(row.ScheduleId) || schedule.ArtifactId != Decode(row.ArtifactId) || schedule.ExecutableNodeId != Decode(row.ExecutableNodeId) || schedule.StimulusType != Decode(row.StimulusType) || schedule.StimulusHash != Decode(row.StimulusHash) || schedule.Kind != (RecurringScheduleKind)row.Kind || schedule.Expression != row.Expression || schedule.NextOccurrence.UtcTicks != row.NextOccurrenceUtcTicks || (int)schedule.NextOccurrence.Offset.TotalMinutes != row.NextOccurrenceOffsetMinutes || schedule.CreatedAt.UtcTicks != row.CreatedAtUtcTicks || (int)schedule.CreatedAt.Offset.TotalMinutes != row.CreatedAtOffsetMinutes || schedule.ActivationId != Optional(row.ActivationId) || schedule.SlotId != Optional(row.SlotId) || schedule.IsActive != row.IsActive || row.ScheduleIdHash != Hash(schedule.ScheduleId) || row.ScheduleIdOrderKey != ScheduleOrder(schedule.ScheduleId) || row.ArtifactIdHash != Hash(schedule.ArtifactId) || row.ArtifactIdOrderKey != Order(schedule.ArtifactId) || schedule.ActivationId is not null && (row.ActivationIdHash != Hash(schedule.ActivationId) || row.ActivationIdOrderKey != Order(schedule.ActivationId)) || schedule.ActivationId is null && (row.ActivationIdHash is not null || row.ActivationIdOrderKey is not null))
            throw new InvalidDataException("The persisted EF recurring-trigger schedule content does not match its authoritative projections.");
        ValidateClaimProjection(row);
        return schedule;
    }

    // Never claimed (token 0, nothing set); claimed (owner, claim time and a later visibility deadline); released after a
    // failure (no owner, a visibility deadline and at least one failure); or settled (no owner, no deadline, no failures).
    private static void ValidateClaimProjection(RecurringTriggerScheduleEntity row)
    {
        var claimedAtPaired = (row.ClaimedAtUtcTicks is null) == (row.ClaimedAtOffsetMinutes is null);
        var visibleAfterPaired = (row.VisibleAfterUtcTicks is null) == (row.VisibleAfterOffsetMinutes is null);
        var consistent = row.ClaimToken >= 0 && row.FailureCount >= 0 && claimedAtPaired && visibleAfterPaired && (row.ClaimToken, row.ClaimOwnerId) switch
        {
            (0, _) => row.ClaimOwnerId is null && row.ClaimedAtUtcTicks is null && row.VisibleAfterUtcTicks is null && row.FailureCount == 0,
            (_, not null) => row.ClaimedAtUtcTicks is { } claimed && row.VisibleAfterUtcTicks is { } visible && visible > claimed,
            _ => row.ClaimedAtUtcTicks is null && (row.VisibleAfterUtcTicks is null ? row.FailureCount == 0 : row.FailureCount > 0)
        };
        if (!consistent)
            throw new InvalidDataException("The persisted EF recurring-trigger schedule claim projection is inconsistent.");
    }

    // A fencing token is the row's previous token plus one, and never at or below the schedule's creation instant in ticks,
    // so a schedule deleted and saved again does not count from zero again but on from its own creation instant (#2198).
    // Successive claims of one row fall at strictly later instants (a claim needs the previous one settled, released or
    // lapsed), so once a schedule is due after its creation its tokens never run more than one tick past its claim times,
    // and a schedule recreated after its predecessor's last claim does not reissue any of the predecessor's tokens. The
    // token is not the only fence: LoadHeldAsync also compares the claimed schedule, so a claim on a predecessor cannot act
    // on a recreated schedule that differs from it, whatever the tokens.
    private static long NextFencingToken(RecurringTriggerScheduleEntity row) => checked(Math.Max(row.ClaimToken, row.CreatedAtUtcTicks) + 1);

    private static RecurringTriggerOccurrenceClaim ToClaim(RecurringTriggerScheduleEntity row, RecurringTriggerSchedule schedule) =>
        row is { ClaimOwnerId: { } owner, ClaimedAtUtcTicks: { } claimedAt, ClaimedAtOffsetMinutes: { } claimedAtOffset, VisibleAfterUtcTicks: { } visibleAfter, VisibleAfterOffsetMinutes: { } visibleAfterOffset }
            ? new(schedule, Decode(owner), row.ClaimToken, row.Revision,
                EfRuntimeOperationalStoreSupport.FromUtcTicks(claimedAt, claimedAtOffset),
                EfRuntimeOperationalStoreSupport.FromUtcTicks(visibleAfter, visibleAfterOffset), row.FailureCount)
            : throw new InvalidDataException("The recurring-trigger claim projection is incomplete.");

    private static string? Optional(string? value) => value is null ? null : Decode(value);

    private static void Validate(RecurringTriggerSchedule schedule)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        ValidateScheduleId(schedule.ScheduleId, nameof(schedule.ScheduleId)); ValidateIdentity(schedule.ArtifactId, nameof(schedule.ArtifactId)); ValidateIdentity(schedule.ExecutableNodeId, nameof(schedule.ExecutableNodeId));
        ArgumentException.ThrowIfNullOrWhiteSpace(schedule.StimulusType); ArgumentException.ThrowIfNullOrWhiteSpace(schedule.StimulusHash); ArgumentException.ThrowIfNullOrWhiteSpace(schedule.Expression);
        if (schedule.StimulusHash.Length > RuntimeOperationalStateEfModule.IdentityMaximumLength) throw new ArgumentException($"Runtime identity cannot exceed {RuntimeOperationalStateEfModule.IdentityMaximumLength} UTF-16 code units.", nameof(schedule.StimulusHash));
        if (!Enum.IsDefined(schedule.Kind)) throw new ArgumentOutOfRangeException(nameof(schedule.Kind));
        if (schedule.ActivationId is not null) ValidateIdentity(schedule.ActivationId, nameof(schedule.ActivationId));
        if (schedule.ActivationId is null && schedule.SlotId is not null) throw new ArgumentException("A recurring-trigger schedule slot requires an activation.", nameof(schedule));
        if (schedule.SlotId is not null) ArgumentException.ThrowIfNullOrWhiteSpace(schedule.SlotId);
        var expected = schedule.ActivationId is null ? RecurringTriggerSchedule.BuildId(schedule.ArtifactId, schedule.ExecutableNodeId) : RecurringTriggerSchedule.BuildId(schedule.ActivationId, schedule.ArtifactId, schedule.ExecutableNodeId);
        var fanOut = schedule.ActivationId is null ? RecurringTriggerSchedule.BuildFanOutId(schedule.ArtifactId, schedule.ExecutableNodeId, schedule.StimulusHash) : RecurringTriggerSchedule.BuildFanOutId(schedule.ActivationId, schedule.ArtifactId, schedule.ExecutableNodeId, schedule.StimulusHash);
        if (schedule.ScheduleId != expected && schedule.ScheduleId != fanOut) throw new ArgumentException("The recurring-trigger schedule id does not match its deterministic identity.", nameof(schedule));
    }

    private static void ValidateIdentity(string value, string parameterName)
    { ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName); if (value.Length > RuntimeOperationalStateEfModule.IdentityMaximumLength) throw new ArgumentException($"Runtime identity cannot exceed {RuntimeOperationalStateEfModule.IdentityMaximumLength} UTF-16 code units.", parameterName); }
    private static void ValidateScheduleId(string value, string parameterName)
    { ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName); if (value.Length > RuntimeOperationalStateEfModule.RecurringScheduleIdMaximumLength) throw new ArgumentException($"A recurring-trigger schedule id cannot exceed {RuntimeOperationalStateEfModule.RecurringScheduleIdMaximumLength} UTF-16 code units.", parameterName); }
    private static string ScheduleOrder(string value) => Convert.ToHexString(EfRelationalIdentity.CreateOrderKey(value, RuntimeOperationalStateEfModule.RecurringScheduleIdMaximumLength));
    private static void ValidateActivation(string id, IReadOnlyCollection<RecurringTriggerSchedule> schedules)
    {
        ValidateIdentity(id, nameof(id)); ArgumentNullException.ThrowIfNull(schedules); var ids = new HashSet<string>(StringComparer.Ordinal); var artifacts = new HashSet<string>(StringComparer.Ordinal);
        foreach (var schedule in schedules) { Validate(schedule); if (schedule.ActivationId != id || string.IsNullOrWhiteSpace(schedule.SlotId) || !ids.Add(schedule.ScheduleId)) throw new ArgumentException("Activation schedules must have matching activation, slot, and unique schedule ids.", nameof(schedules)); artifacts.Add(schedule.ArtifactId); }
        if (artifacts.Count > 1) throw new ArgumentException("An activation may contain schedules from only one artifact.", nameof(schedules));
    }

    private sealed class ProjectionStateSnapshot
    {
        public required RecurringTriggerScheduleProjectionStateEntity Entity { get; init; }
        public required string Scope { get; init; }
        public required string ActivationId { get; init; }
        public string? ArtifactId { get; init; }
        public bool IsActive { get; set; }
        public int ScheduleCount { get; init; }
        public required string ProjectionFingerprint { get; init; }
        public required IReadOnlyList<string> ScheduleIds { get; init; }
        public required IReadOnlyDictionary<string, string> ScheduleFingerprints { get; init; }
        public long Revision { get => Entity.Revision; set => Entity.Revision = value; }
    }

    private static ProjectionStateSnapshot CreateProjectionState(string scope, string activationId, IReadOnlyCollection<RecurringTriggerSchedule> schedules, bool active, string? retainedArtifact = null)
    {
        var ordered = schedules.OrderBy(x => x.ScheduleId, StringComparer.Ordinal).ToArray(); var artifacts = ordered.Select(x => x.ArtifactId).Distinct(StringComparer.Ordinal).ToArray();
        if (artifacts.Length > 1 || retainedArtifact is not null && artifacts.Length > 0 && artifacts[0] != retainedArtifact) throw new InvalidDataException($"Recurring-schedule activation '{activationId}' contains multiple artifacts.");
        var artifact = artifacts.SingleOrDefault() ?? retainedArtifact; var ids = ordered.Select(x => x.ScheduleId).ToArray(); var fps = ordered.ToDictionary(x => x.ScheduleId, ImmutableFingerprint, StringComparer.Ordinal);
        var entity = new RecurringTriggerScheduleProjectionStateEntity { Id = ProjectionId(scope, activationId), ScopeKey = Encode(scope), ScopeKeyHash = Hash(scope), ActivationId = Encode(activationId), ActivationIdHash = Hash(activationId), ActivationIdOrderKey = Order(activationId), ArtifactId = artifact is null ? null : Encode(artifact), ArtifactIdHash = artifact is null ? null : Hash(artifact), ArtifactIdOrderKey = artifact is null ? null : Order(artifact), IsActive = active, ScheduleCount = ids.Length, ProjectionFingerprint = ProjectionFingerprint(ordered), ScheduleIdsJson = JsonSerializer.Serialize(ids, JsonOptions), ScheduleFingerprintsJson = JsonSerializer.Serialize(fps, JsonOptions), SchemaVersion = RuntimeOperationalStateEfModule.SchemaVersion, Revision = ActivationProjectionStateLifecycle.CreationRevision };
        entity.ContentJson = JsonSerializer.Serialize(new ProjectionStateContent(ProjectionKind, activationId, artifact, active, ids.Length, entity.ProjectionFingerprint, ids, fps), JsonOptions);
        return new ProjectionStateSnapshot { Entity = entity, Scope = scope, ActivationId = activationId, ArtifactId = entity.ArtifactId, IsActive = active, ScheduleCount = ids.Length, ProjectionFingerprint = entity.ProjectionFingerprint, ScheduleIds = ids, ScheduleFingerprints = fps };
    }

    private static RecurringTriggerScheduleProjectionStateEntity ToStateEntity(ProjectionStateSnapshot state, string scope, long revision) { state.Entity.Revision = revision; return state.Entity; }
    private static void UpdateStateContent(ProjectionStateSnapshot state, string scope)
    {
        state.Entity.IsActive = state.IsActive;
        state.Entity.ContentJson = JsonSerializer.Serialize(new ProjectionStateContent(ProjectionKind, state.ActivationId, state.ArtifactId is null ? null : Decode(state.ArtifactId), state.IsActive, state.ScheduleCount, state.ProjectionFingerprint, state.ScheduleIds, state.ScheduleFingerprints), JsonOptions);
        // Every declared content column is rewritten from the upcast snapshot together, so no column is left at the
        // row's old stamp once SchemaVersion moves to the current version (spec 180, FR-014; #2144).
        state.Entity.ScheduleIdsJson = JsonSerializer.Serialize(state.ScheduleIds, JsonOptions);
        state.Entity.ScheduleFingerprintsJson = JsonSerializer.Serialize(state.ScheduleFingerprints, JsonOptions);
        state.Entity.SchemaVersion = RuntimeOperationalStateEfModule.SchemaVersion;
    }

    private static ProjectionStateSnapshot ReadState(RecurringTriggerScheduleProjectionStateEntity entity, string scope, string? expectedActivation = null)
    {
        if (EfSchemaVersion.NotReadable(RuntimeOperationalStateEfModule.Chain, entity.SchemaVersion) || entity.Id != ProjectionId(scope, Decode(entity.ActivationId)) || entity.ScopeKey != Encode(scope) || entity.ScopeKeyHash != Hash(scope) || expectedActivation is not null && entity.ActivationId != Encode(expectedActivation) || entity.ActivationIdHash != Hash(Decode(entity.ActivationId)) || entity.ActivationIdOrderKey != Order(Decode(entity.ActivationId)) || entity.ArtifactId is null && (entity.ArtifactIdHash is not null || entity.ArtifactIdOrderKey is not null) || entity.ArtifactId is not null && (entity.ArtifactIdHash != Hash(Decode(entity.ArtifactId)) || entity.ArtifactIdOrderKey != Order(Decode(entity.ArtifactId))) || entity.Revision <= 0)
            throw new InvalidDataException("The persisted EF recurring-schedule projection state does not match its identity envelope.");
        ProjectionStateContent content;
        string[] ids;
        Dictionary<string, string> fps;
        try
        {
            // The id and fingerprint sets restate the content's and are compared with it and returned, so they are content
            // too, upcast from the row's stamp together with it (spec 180, FR-009; #2140, #2144).
            var upcast = RuntimeOperationalStateEfModule.Chain.Upcast<RecurringTriggerScheduleProjectionStateEntity>(
                entity.SchemaVersion,
                (nameof(entity.ContentJson), entity.ContentJson),
                (nameof(entity.ScheduleIdsJson), entity.ScheduleIdsJson),
                (nameof(entity.ScheduleFingerprintsJson), entity.ScheduleFingerprintsJson));
            content = JsonSerializer.Deserialize<ProjectionStateContent>(upcast[nameof(entity.ContentJson)]!, JsonOptions) ?? throw new JsonException("Projection content is empty.");
            ids = JsonSerializer.Deserialize<string[]>(upcast[nameof(entity.ScheduleIdsJson)]!, JsonOptions) ?? throw new JsonException("Schedule identities are empty.");
            fps = JsonSerializer.Deserialize<Dictionary<string, string>>(upcast[nameof(entity.ScheduleFingerprintsJson)]!, JsonOptions) ?? throw new JsonException("Schedule fingerprints are empty.");
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException or ArgumentException)
        {
            throw new InvalidDataException("The persisted EF recurring-schedule projection content is not valid current JSON.", exception);
        }
        if (content.ProjectionKind != ProjectionKind || content.ActivationId != Decode(entity.ActivationId) || content.ArtifactId != Optional(entity.ArtifactId) || content.IsActive != entity.IsActive || content.ScheduleCount != entity.ScheduleCount || entity.ScheduleCount < 0 || content.ProjectionFingerprint != entity.ProjectionFingerprint || ids.Length != entity.ScheduleCount || !ids.SequenceEqual(content.ScheduleIds ?? []) || fps.Count != ids.Length || ids.Distinct(StringComparer.Ordinal).Count() != ids.Length || ids.Any(x => string.IsNullOrWhiteSpace(x) || !fps.TryGetValue(x, out var fp) || string.IsNullOrWhiteSpace(fp)) || content.ScheduleFingerprints is null || fps.Any(pair => !content.ScheduleFingerprints.TryGetValue(pair.Key, out var contentFp) || pair.Value != contentFp))
            throw new InvalidDataException("The recurring-schedule projection state content does not match its authoritative projections.");
        return new ProjectionStateSnapshot { Entity = entity, Scope = scope, ActivationId = Decode(entity.ActivationId), ArtifactId = entity.ArtifactId, IsActive = entity.IsActive, ScheduleCount = entity.ScheduleCount, ProjectionFingerprint = entity.ProjectionFingerprint, ScheduleIds = ids, ScheduleFingerprints = fps };
    }

    // The prepare-time ProjectionFingerprint is not compared: it covers each cursor, which moves for as long as the schedule
    // lives (a settled occurrence, a take-over at activation), so it would refuse an activation that was restored or
    // compensated after its first one. The per-schedule immutable fingerprints cover what must not change.
    private static bool ProjectionMatches(ProjectionStateSnapshot state, IEnumerable<RecurringTriggerScheduleEntity> rows, string scope, string activation)
    {
        var schedules = rows.Select(x => Read(x, scope)).ToArray();
        return state.ActivationId == activation && state.ScheduleCount == schedules.Length &&
               state.ScheduleIds.SequenceEqual(schedules.Select(x => x.ScheduleId).OrderBy(x => x, StringComparer.Ordinal)) &&
               schedules.All(x => x.ActivationId == activation && x.ArtifactId == Optional(state.ArtifactId) &&
                                  state.ScheduleFingerprints.TryGetValue(x.ScheduleId, out var fp) && fp == ImmutableFingerprint(x));
    }
    private static void EnsurePreparedProjection(ProjectionStateSnapshot state, IEnumerable<RecurringTriggerScheduleEntity> rows, string scope, string activation)
    { if (state.IsActive || !ProjectionMatches(state, rows, scope, activation) || rows.Any(x => Read(x, scope).IsActive) || state.ScheduleIds.Any(id => !rows.Any(x => Decode(x.ScheduleId) == id)) || state.ScheduleFingerprints.Any(pair => !rows.Any(x => Decode(x.ScheduleId) == pair.Key && ImmutableFingerprint(Read(x, scope)) == pair.Value))) throw new InvalidDataException($"Recurring-schedule activation projection '{activation}' does not match its prepared rows."); }
    private async Task EnsureActiveProjectionAsync(ProjectionStateSnapshot state, IEnumerable<RecurringTriggerScheduleEntity> rows, string scope, string activation, CancellationToken cancellationToken)
    {
        var schedules = rows.Select(x => Read(x, scope)).ToArray();
        if (!state.IsActive || state.ActivationId != activation ||
            state.ScheduleCount != state.ScheduleIds.Count || state.ScheduleFingerprints.Count != state.ScheduleCount ||
            state.ScheduleCount > 0 && state.ArtifactId is null ||
            schedules.Select(x => x.ScheduleId).Distinct(StringComparer.Ordinal).Count() != schedules.Length ||
            schedules.Any(schedule => !schedule.IsActive || schedule.ActivationId != activation ||
                                      state.ArtifactId != Encode(schedule.ArtifactId) ||
                                      !state.ScheduleFingerprints.TryGetValue(schedule.ScheduleId, out var fp) || fp != ImmutableFingerprint(schedule)))
            throw new InvalidDataException($"Recurring-schedule active projection '{activation}' does not match its active rows.");

        // Pump exhaustion may delete a live schedule. Per the provider-neutral contract, a missing
        // row is allowed, but a present row under an expected identity must retain its fence.
        var observed = schedules.Select(x => x.ScheduleId).ToHashSet(StringComparer.Ordinal);
        foreach (var scheduleId in state.ScheduleIds.Where(id => !observed.Contains(id)))
        {
            var row = await context.RecurringTriggerSchedules.AsNoTracking().SingleOrDefaultAsync(x => x.Id == Id(scope, scheduleId), cancellationToken);
            if (row is not null)
                EnsureActiveSchedule(state, Read(row, scope, scheduleId));
        }
    }
    private static void EnsureActiveSchedule(ProjectionStateSnapshot state, RecurringTriggerSchedule schedule)
    { if (!schedule.IsActive || schedule.ActivationId != state.ActivationId || state.ArtifactId != Encode(schedule.ArtifactId) || !state.ScheduleFingerprints.TryGetValue(schedule.ScheduleId, out var fp) || fp != ImmutableFingerprint(schedule)) throw new InvalidDataException("Recurring-schedule active projection contains a row with invalid immutable state."); }
    private static bool ProjectionsEqual(IEnumerable<RecurringTriggerScheduleEntity> rows, IEnumerable<RecurringTriggerSchedule> schedules, string scope) => ProjectionFingerprint(rows.Select(x => Read(x, scope))) == ProjectionFingerprint(schedules);
    private static string ProjectionFingerprint(IEnumerable<RecurringTriggerSchedule> schedules) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(RuntimeArtifactJson.Serialize(schedules.Select(x => x with { IsActive = false }).OrderBy(x => x.ScheduleId, StringComparer.Ordinal).ToArray()))));
    private static string ImmutableFingerprint(RecurringTriggerSchedule schedule) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(RuntimeArtifactJson.Serialize(schedule with { IsActive = false, NextOccurrence = DateTimeOffset.MinValue }))));
    private static bool SchedulesEqual(RecurringTriggerSchedule left, RecurringTriggerSchedule right) => RuntimeArtifactJson.Serialize(left) == RuntimeArtifactJson.Serialize(right);

    private string EncodeCursor(string purpose, string scope, string filter, string scheduleId) => continuationCodec.Encode(purpose, Encoding.UTF8.GetBytes(string.Join('\0', Encode(scope), Encode(filter), Encode(scheduleId))));
    private string? DecodeCursor(string? token, string purpose, string scope, string filter, string parameterName)
    {
        if (token is null) return null;
        try { var parts = Encoding.UTF8.GetString(continuationCodec.Decode(purpose, token)).Split('\0'); if (parts.Length != 3 || parts[0] != Encode(scope) || parts[1] != Encode(filter) || string.IsNullOrWhiteSpace(parts[2])) throw new FormatException(); return Order(Decode(parts[2])); }
        catch (Exception exception) when (exception is ArgumentException or FormatException or InvalidOperationException) { throw new ArgumentException("The recurring-trigger schedule continuation token is invalid or belongs to another query.", parameterName, exception); }
    }

    private sealed record ProjectionStateContent(string ProjectionKind, string ActivationId, string? ArtifactId, bool IsActive, int ScheduleCount, string ProjectionFingerprint, IReadOnlyList<string> ScheduleIds, IReadOnlyDictionary<string, string> ScheduleFingerprints);
}
