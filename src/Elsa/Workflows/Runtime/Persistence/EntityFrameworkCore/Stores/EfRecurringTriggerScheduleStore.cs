using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

/// <summary>Concrete opt-in EF adapter for recurring Timer/Cron start schedules.</summary>
/// <remarks>
/// The JSON envelope remains authoritative. Relational columns are bounded lookup and ordering projections,
/// and every mutation advances the row revision so due-occurrence claims use provider CAS.
/// </remarks>
public sealed class EfRecurringTriggerScheduleStore(
    BookmarkStateDbContext context,
    IPersistenceAccessContextAccessor accessContextAccessor,
    IRuntimeRecoveryContinuationCodec continuationCodec) : IRecurringTriggerScheduleStore
{
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
        catch (DbUpdateException exception) when (EfRelationalExceptionClassifier.IsUniqueConstraintViolation(exception))
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
            if (!existingState.IsActive && ProjectionMatches(existingState, existingRows, scope, activationId) && ProjectionsEqual(existingRows, prepared, scope))
            {
                await CommitAndClearAsync(transaction, cancellationToken, noChanges: true);
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
        context.RecurringTriggerScheduleProjectionStates.Add(ToStateEntity(CreateProjectionState(scope, activationId, prepared, false), scope, 1));

        try
        {
            await CommitAndClearAsync(transaction, cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            await RollbackAndClearAsync(transaction);
            throw new InvalidOperationException($"Recurring-schedule activation projection '{activationId}' changed concurrently; retry the operation.", exception);
        }
        catch (DbUpdateException exception) when (EfRelationalExceptionClassifier.IsUniqueConstraintViolation(exception))
        {
            await RollbackAndClearAsync(transaction);
            var winner = await ActivationState(scope, activationId, cancellationToken);
            var winnerRows = await RowsForActivation(scope, activationId, cancellationToken);
            if (winner is { IsActive: false } && ProjectionMatches(winner, winnerRows, scope, activationId) && ProjectionsEqual(winnerRows, prepared, scope))
                return;
            throw new InvalidOperationException($"Recurring-schedule activation projection '{activationId}' changed concurrently with different state.", exception);
        }
        catch (DbUpdateException exception)
        {
            await RollbackAndClearAsync(transaction);
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
        context.ChangeTracker.Clear();
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var candidate = await ActivationState(scope, activationId, cancellationToken) ?? throw new InvalidOperationException($"Activation '{activationId}' has no prepared recurring-schedule projection.");
        var candidateRows = await RowsForActivation(scope, activationId, cancellationToken);
        var distinct = replacedActivationId is not null && !StringComparer.Ordinal.Equals(activationId, replacedActivationId);
        if (candidate.IsActive)
        {
            await EnsureActiveProjectionAsync(candidate, candidateRows, scope, activationId, cancellationToken);
            if (!distinct)
            {
                await CommitAndClearAsync(transaction, cancellationToken, noChanges: true);
                return;
            }
            var activeReplacement = await ActivationState(scope, replacedActivationId!, cancellationToken);
            if (activeReplacement is { IsActive: true })
                throw new InvalidOperationException($"Recurring-schedule activation '{activationId}' is active while replaced activation '{replacedActivationId}' is still active.");
            await CommitAndClearAsync(transaction, cancellationToken, noChanges: true);
            return;
        }
        EnsurePreparedProjection(candidate, candidateRows, scope, activationId);
        ProjectionStateSnapshot? replaced = null;
        RecurringTriggerScheduleEntity[] replacedRows = [];
        if (distinct)
        {
            replaced = await ActivationState(scope, replacedActivationId!, cancellationToken);
            if (replaced is null || !replaced.IsActive)
                throw new InvalidOperationException($"Recurring-schedule activation '{activationId}' cannot replace a projection that is missing or no longer active.");
            replacedRows = await RowsForActivation(scope, replacedActivationId!, cancellationToken);
            await EnsureActiveProjectionAsync(replaced, replacedRows, scope, replacedActivationId!, cancellationToken);
        }
        foreach (var row in candidateRows)
            Copy(row, Read(row, scope) with { IsActive = true }, scope, checked(row.Revision + 1));
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
        await CommitMutationAndClearAsync(transaction, cancellationToken, $"Recurring-schedule activation projection '{activationId}'");
    }

    public async ValueTask DeleteByActivationAsync(string activationId, CancellationToken cancellationToken = default)
    {
        ValidateIdentity(activationId, nameof(activationId));
        cancellationToken.ThrowIfCancellationRequested();
        var scope = RequireScope();
        context.ChangeTracker.Clear();
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
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
        await CommitMutationAndClearAsync(transaction, cancellationToken, $"Recurring-schedule activation projection '{activationId}' deletion");
    }

    public async ValueTask<IReadOnlyCollection<RecurringTriggerSchedule>> ListDueAsync(DateTimeOffset asOf, int limit, CancellationToken cancellationToken = default)
    {
        RuntimeStorePageRequest.ValidateLimit(limit, nameof(limit));
        cancellationToken.ThrowIfCancellationRequested();
        var scope = RequireScope();
        var scopeHash = Hash(scope);
        var scopeKey = Encode(scope);
        var rows = await context.RecurringTriggerSchedules.AsNoTracking()
            .Where(x => x.ScopeKeyHash == scopeHash && x.ScopeKey == scopeKey && x.IsActive && x.NextOccurrenceUtcTicks <= asOf.UtcTicks)
            .OrderBy(x => x.NextOccurrenceUtcTicks)
            .ThenBy(x => x.ScheduleIdOrderKey)
            .Take(limit)
            .ToArrayAsync(cancellationToken);
        var schedules = rows.Select(x => Read(x, scope)).ToArray();
        if (schedules.Any(x => !x.IsActive || x.NextOccurrence > asOf))
            throw new InvalidDataException("Recurring-trigger due query returned a row outside its active and due predicate.");
        return schedules;
    }

    public async ValueTask<RecurringTriggerSchedule?> FindAsync(string scheduleId, CancellationToken cancellationToken = default)
    {
        ValidateScheduleId(scheduleId, nameof(scheduleId));
        cancellationToken.ThrowIfCancellationRequested();
        var scope = RequireScope();
        var row = await context.RecurringTriggerSchedules.AsNoTracking().SingleOrDefaultAsync(x => x.Id == Id(scope, scheduleId), cancellationToken);
        return row is null ? null : Read(row, scope, scheduleId);
    }

    public async ValueTask<bool> TryAdvanceAsync(string scheduleId, DateTimeOffset expectedNextOccurrence, DateTimeOffset newNextOccurrence, CancellationToken cancellationToken = default)
    {
        ValidateScheduleId(scheduleId, nameof(scheduleId));
        cancellationToken.ThrowIfCancellationRequested();
        var scope = RequireScope();
        context.ChangeTracker.Clear();
        var row = await context.RecurringTriggerSchedules.AsNoTracking().SingleOrDefaultAsync(x => x.Id == Id(scope, scheduleId), cancellationToken);
        if (row is null) return false;
        var current = Read(row, scope, scheduleId);
        if (!current.IsActive || current.NextOccurrence != expectedNextOccurrence) return false;
        var updated = current with { NextOccurrence = newNextOccurrence };
        Copy(row, updated, scope, checked(row.Revision + 1));
        context.RecurringTriggerSchedules.Attach(row);
        context.Entry(row).Property(x => x.Revision).OriginalValue = checked(row.Revision - 1);
        context.Entry(row).State = EntityState.Modified;
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            context.ChangeTracker.Clear();
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            context.ChangeTracker.Clear();
            return false;
        }
        catch (DbUpdateException exception) when (EfRelationalExceptionClassifier.IsTransientWriteConflict(exception))
        {
            context.ChangeTracker.Clear();
            return false;
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
        await CommitMutationAndClearAsync(transaction, cancellationToken, $"Recurring-schedule artifact '{artifactId}' deletion");
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

    private async Task CommitAndClearAsync(IDbContextTransaction transaction, CancellationToken ct, bool noChanges = false)
    { if (!noChanges) await context.SaveChangesAsync(ct); await transaction.CommitAsync(ct); context.ChangeTracker.Clear(); }

    private async Task CommitMutationAndClearAsync(IDbContextTransaction transaction, CancellationToken ct, string operation)
    {
        try { await CommitAndClearAsync(transaction, ct); }
        catch (DbUpdateConcurrencyException exception) { await RollbackAndClearAsync(transaction); throw new InvalidOperationException($"{operation} changed concurrently; retry the operation.", exception); }
        catch (DbUpdateException exception) when (EfRelationalExceptionClassifier.IsTransientWriteConflict(exception)) { await RollbackAndClearAsync(transaction); throw new InvalidOperationException($"{operation} encountered a transient write conflict; retry the operation.", exception); }
        catch (DbUpdateException exception) { await RollbackAndClearAsync(transaction); throw new InvalidOperationException($"{operation} could not be committed.", exception); }
    }

    private async Task RollbackAndClearAsync(IDbContextTransaction transaction)
    { try { await transaction.RollbackAsync(); } catch { } context.ChangeTracker.Clear(); }

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
        if (row.Id != Id(scope, Decode(row.ScheduleId)) || row.ScopeKey != Encode(scope) || row.ScopeKeyHash != Hash(scope) || expectedId is not null && row.ScheduleId != Encode(expectedId) || row.SchemaVersion != RuntimeOperationalStateEfModule.SchemaVersion || row.Revision <= 0)
            throw new InvalidDataException("The persisted EF recurring-trigger schedule row does not match its identity envelope.");
        RecurringTriggerSchedule schedule;
        try { schedule = RuntimeArtifactJson.Deserialize<RecurringTriggerSchedule>(row.ContentJson); }
        catch (Exception exception) when (exception is JsonException or NotSupportedException or ArgumentException or InvalidOperationException or FormatException)
        { throw new InvalidDataException("The persisted EF recurring-trigger schedule content is not valid current JSON.", exception); }
        Validate(schedule);
        if (schedule.ScheduleId != Decode(row.ScheduleId) || schedule.ArtifactId != Decode(row.ArtifactId) || schedule.ExecutableNodeId != Decode(row.ExecutableNodeId) || schedule.StimulusType != Decode(row.StimulusType) || schedule.StimulusHash != Decode(row.StimulusHash) || schedule.Kind != (RecurringScheduleKind)row.Kind || schedule.Expression != row.Expression || schedule.NextOccurrence.UtcTicks != row.NextOccurrenceUtcTicks || (int)schedule.NextOccurrence.Offset.TotalMinutes != row.NextOccurrenceOffsetMinutes || schedule.CreatedAt.UtcTicks != row.CreatedAtUtcTicks || (int)schedule.CreatedAt.Offset.TotalMinutes != row.CreatedAtOffsetMinutes || schedule.ActivationId != Optional(row.ActivationId) || schedule.SlotId != Optional(row.SlotId) || schedule.IsActive != row.IsActive || row.ScheduleIdHash != Hash(schedule.ScheduleId) || row.ScheduleIdOrderKey != ScheduleOrder(schedule.ScheduleId) || row.ArtifactIdHash != Hash(schedule.ArtifactId) || row.ArtifactIdOrderKey != Order(schedule.ArtifactId) || schedule.ActivationId is not null && (row.ActivationIdHash != Hash(schedule.ActivationId) || row.ActivationIdOrderKey != Order(schedule.ActivationId)) || schedule.ActivationId is null && (row.ActivationIdHash is not null || row.ActivationIdOrderKey is not null))
            throw new InvalidDataException("The persisted EF recurring-trigger schedule content does not match its authoritative projections.");
        return schedule;
    }

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
        var entity = new RecurringTriggerScheduleProjectionStateEntity { Id = ProjectionId(scope, activationId), ScopeKey = Encode(scope), ScopeKeyHash = Hash(scope), ActivationId = Encode(activationId), ActivationIdHash = Hash(activationId), ActivationIdOrderKey = Order(activationId), ArtifactId = artifact is null ? null : Encode(artifact), ArtifactIdHash = artifact is null ? null : Hash(artifact), ArtifactIdOrderKey = artifact is null ? null : Order(artifact), IsActive = active, ScheduleCount = ids.Length, ProjectionFingerprint = ProjectionFingerprint(ordered), ScheduleIdsJson = JsonSerializer.Serialize(ids, JsonOptions), ScheduleFingerprintsJson = JsonSerializer.Serialize(fps, JsonOptions), SchemaVersion = RuntimeOperationalStateEfModule.SchemaVersion, Revision = 1 };
        entity.ContentJson = JsonSerializer.Serialize(new ProjectionStateContent(ProjectionKind, activationId, artifact, active, ids.Length, entity.ProjectionFingerprint, ids, fps), JsonOptions);
        return new ProjectionStateSnapshot { Entity = entity, Scope = scope, ActivationId = activationId, ArtifactId = entity.ArtifactId, IsActive = active, ScheduleCount = ids.Length, ProjectionFingerprint = entity.ProjectionFingerprint, ScheduleIds = ids, ScheduleFingerprints = fps };
    }

    private static RecurringTriggerScheduleProjectionStateEntity ToStateEntity(ProjectionStateSnapshot state, string scope, long revision) { state.Entity.Revision = revision; return state.Entity; }
    private static void UpdateStateContent(ProjectionStateSnapshot state, string scope)
    {
        state.Entity.IsActive = state.IsActive;
        state.Entity.ContentJson = JsonSerializer.Serialize(new ProjectionStateContent(ProjectionKind, state.ActivationId, state.ArtifactId is null ? null : Decode(state.ArtifactId), state.IsActive, state.ScheduleCount, state.ProjectionFingerprint, state.ScheduleIds, state.ScheduleFingerprints), JsonOptions);
    }

    private static ProjectionStateSnapshot ReadState(RecurringTriggerScheduleProjectionStateEntity entity, string scope, string? expectedActivation = null)
    {
        if (entity.Id != ProjectionId(scope, Decode(entity.ActivationId)) || entity.ScopeKey != Encode(scope) || entity.ScopeKeyHash != Hash(scope) || expectedActivation is not null && entity.ActivationId != Encode(expectedActivation) || entity.ActivationIdHash != Hash(Decode(entity.ActivationId)) || entity.ActivationIdOrderKey != Order(Decode(entity.ActivationId)) || entity.ArtifactId is null && (entity.ArtifactIdHash is not null || entity.ArtifactIdOrderKey is not null) || entity.ArtifactId is not null && (entity.ArtifactIdHash != Hash(Decode(entity.ArtifactId)) || entity.ArtifactIdOrderKey != Order(Decode(entity.ArtifactId))) || entity.SchemaVersion != RuntimeOperationalStateEfModule.SchemaVersion || entity.Revision <= 0)
            throw new InvalidDataException("The persisted EF recurring-schedule projection state does not match its identity envelope.");
        ProjectionStateContent content;
        string[] ids;
        Dictionary<string, string> fps;
        try
        {
            content = JsonSerializer.Deserialize<ProjectionStateContent>(entity.ContentJson, JsonOptions) ?? throw new JsonException("Projection content is empty.");
            ids = JsonSerializer.Deserialize<string[]>(entity.ScheduleIdsJson, JsonOptions) ?? throw new JsonException("Schedule identities are empty.");
            fps = JsonSerializer.Deserialize<Dictionary<string, string>>(entity.ScheduleFingerprintsJson, JsonOptions) ?? throw new JsonException("Schedule fingerprints are empty.");
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException or ArgumentException)
        {
            throw new InvalidDataException("The persisted EF recurring-schedule projection content is not valid current JSON.", exception);
        }
        if (content.ProjectionKind != ProjectionKind || content.ActivationId != Decode(entity.ActivationId) || content.ArtifactId != Optional(entity.ArtifactId) || content.IsActive != entity.IsActive || content.ScheduleCount != entity.ScheduleCount || entity.ScheduleCount < 0 || content.ProjectionFingerprint != entity.ProjectionFingerprint || ids.Length != entity.ScheduleCount || !ids.SequenceEqual(content.ScheduleIds ?? []) || fps.Count != ids.Length || ids.Distinct(StringComparer.Ordinal).Count() != ids.Length || ids.Any(x => string.IsNullOrWhiteSpace(x) || !fps.TryGetValue(x, out var fp) || string.IsNullOrWhiteSpace(fp)) || content.ScheduleFingerprints is null || fps.Any(pair => !content.ScheduleFingerprints.TryGetValue(pair.Key, out var contentFp) || pair.Value != contentFp))
            throw new InvalidDataException("The recurring-schedule projection state content does not match its authoritative projections.");
        return new ProjectionStateSnapshot { Entity = entity, Scope = scope, ActivationId = Decode(entity.ActivationId), ArtifactId = entity.ArtifactId, IsActive = entity.IsActive, ScheduleCount = entity.ScheduleCount, ProjectionFingerprint = entity.ProjectionFingerprint, ScheduleIds = ids, ScheduleFingerprints = fps };
    }

    private static bool ProjectionMatches(ProjectionStateSnapshot state, IEnumerable<RecurringTriggerScheduleEntity> rows, string scope, string activation)
    {
        var schedules = rows.Select(x => Read(x, scope)).ToArray();
        return state.ActivationId == activation && state.ScheduleCount == schedules.Length &&
               state.ProjectionFingerprint == ProjectionFingerprint(schedules) &&
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

        // Pump exhaustion may delete a live schedule. As in the Groundwork contract, a missing
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
