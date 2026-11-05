using System.Text.Json;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using static Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores.EfRuntimeOperationalStoreSupport;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

public sealed partial class EfDurableTimerStore
{
    private const string PendingHistoryPurpose = "ef-runtime-timer-history-v1";

    public async ValueTask<DurableTimer> SaveAsync(DurableTimer timer, CancellationToken cancellationToken = default)
    {
        if (timer.HistoryResponsibility is null) return await SaveCoreAsync(timer, cancellationToken);
        ValidateTimer(timer);
        var admittedScope = RequireScope(accessContextAccessor);
        var existingId = CompositeId(admittedScope, timer.WorkflowExecutionId, timer.TimerId);
        var firstWinner = await context.DurableTimers.AsNoTracking().SingleOrDefaultAsync(x => x.Id == existingId, cancellationToken);
        if (firstWinner is not null) return ReadChecked(firstWinner, admittedScope, timer.WorkflowExecutionId, timer.TimerId);
        if (context.ChangeTracker.HasChanges()) throw new InvalidOperationException("Timer history admission cannot flush unrelated pending changes.");
        var scope = RequireScope(accessContextAccessor);
        var ownTransaction = context.Database.CurrentTransaction is null;
        await using var transaction = ownTransaction ? await context.Database.BeginTransactionAsync(cancellationToken) : null;
        try
        {
            await EfWorkflowExecutionStateStore.LockWorkflowExecutionForWriteAsync(context, scope, timer.WorkflowExecutionId, cancellationToken);
            await EfExecutionLivenessStateStore.EnsureWorkflowExecutionNotDeletedAsync(context, scope, timer.WorkflowExecutionId, cancellationToken);
            var runId = EfWorkflowExecutionStateStore.CreateId(scope, timer.WorkflowExecutionId);
            var native = await context.WorkflowExecutionStates.AsNoTracking().SingleOrDefaultAsync(x => x.Id == runId, cancellationToken);
            if (native is not null && !timer.HistoryResponsibility.Policy.IsEquivalentTo(EfWorkflowExecutionStateStore.ReadChecked(native, scope, timer.WorkflowExecutionId).HistoryCapturePolicy))
                throw new InvalidOperationException("Timer history admission disagrees with its execution's pinned policy.");
            var result = await SaveCoreAsync(timer, cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch { if (transaction is not null) await transaction.RollbackAsync(cancellationToken); throw; }
    }

    public async ValueTask<bool> HasPendingHistoryAsync(string workflowExecutionId, CancellationToken cancellationToken = default)
    {
        ValidateIdentity(workflowExecutionId, nameof(workflowExecutionId));
        var scope = RequireScope(accessContextAccessor); var sh = Hash(scope); var sk = Encode(scope);
        var rh = Hash(workflowExecutionId); var rk = Encode(workflowExecutionId);
        return await context.DurableTimers.AsNoTracking().AnyAsync(x => x.ScopeKeyHash == sh && x.ScopeKey == sk &&
            x.WorkflowExecutionIdHash == rh && x.WorkflowExecutionId == rk && x.HistoryPending, cancellationToken);
    }

    public async ValueTask<RuntimeStorePage<DurableTimer>> ListPendingHistoryAsync(DurableTimerHistoryQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var scope = RequireScope(accessContextAccessor); var sh = Hash(scope); var sk = Encode(scope);
        var binding = RuntimeHistoryIdentity.CreateBinding([scope, query.Limit.ToString(System.Globalization.CultureInfo.InvariantCulture)]);
        var source = context.DurableTimers.AsNoTracking().Where(x => x.ScopeKeyHash == sh && x.ScopeKey == sk && x.HistoryPending);
        if (query.ContinuationToken is { } token)
        {
            PendingHistoryCursor cursor;
            try
            {
                cursor = JsonSerializer.Deserialize<PendingHistoryCursor>(continuationCodec.Decode(PendingHistoryPurpose, token)) ?? throw new FormatException();
                if (cursor.Binding != binding || cursor.RunHash.Length != 64 || cursor.TimerHash.Length != 64) throw new FormatException();
            }
            catch (Exception exception) when (exception is JsonException or ArgumentException or FormatException or InvalidOperationException)
            { throw new ArgumentException("The timer-history continuation does not belong to this scope and query.", nameof(query), exception); }
            source = source.Where(x => string.Compare(x.WorkflowExecutionIdHash, cursor.RunHash) > 0 || x.WorkflowExecutionIdHash == cursor.RunHash && string.Compare(x.TimerIdHash, cursor.TimerHash) > 0);
        }
        var rows = await source.OrderBy(x => x.WorkflowExecutionIdHash).ThenBy(x => x.TimerIdHash).Take(query.Limit + 1).ToArrayAsync(cancellationToken);
        var hasMore = rows.Length > query.Limit;
        if (hasMore) rows = rows[..query.Limit];
        var items = rows.Select(row => ReadChecked(row, scope)).ToArray();
        var next = hasMore ? continuationCodec.Encode(PendingHistoryPurpose, JsonSerializer.SerializeToUtf8Bytes(new PendingHistoryCursor(binding, rows[^1].WorkflowExecutionIdHash, rows[^1].TimerIdHash))) : null;
        return new(query, items, next);
    }

    public async ValueTask<bool> AcknowledgeHistoryAsync(string workflowExecutionId, string timerId, string historyEventId, CancellationToken cancellationToken = default)
    {
        ValidateIdentity(workflowExecutionId, timerId); ArgumentException.ThrowIfNullOrWhiteSpace(historyEventId);
        var scope = RequireScope(accessContextAccessor); var id = CompositeId(scope, workflowExecutionId, timerId);
        return await Transitions.RunAsync(context, async () =>
        {
            var row = await context.DurableTimers.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
            if (row is null) return true;
            var timer = ReadChecked(row, scope, workflowExecutionId, timerId);
            if (timer.HistoryResponsibility is not { } history) return false;
            var remaining = history.PendingEvents.Where(e => e.HistoryEventId != historyEventId).ToArray();
            if (remaining.Length == history.PendingEvents.Count) return true;
            {
                var historyRevision = row.HistoryRevision;
                var next = timer with { HistoryResponsibility = new(history.Policy, remaining, history.Retired),
                    Input = history.Retired && remaining.Length == 0 ? null : timer.Input,
                    Metadata = history.Retired && remaining.Length == 0 ? null : timer.Metadata };
                ApplyHistory(row, next);
                row.HistoryRevision = checked(historyRevision + 1);
                AttachForUpdate(row, row.Revision);
                context.Entry(row).Property(x => x.HistoryRevision).OriginalValue = historyRevision;
            }
            try { await context.SaveChangesAsync(cancellationToken); return true; }
            finally { Detach(row); }
        }, _ => throw TransitionDidNotSettle("acknowledge history", workflowExecutionId, timerId), cancellationToken);
    }

    public async ValueTask<RuntimeDurableTimerClaimTransitionResult> CompleteClaimWithHistoryAsync(RuntimeDurableTimerClaim claim,
        RuntimeHistoryObservation observation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(claim); ValidateClaim(claim);
        var scope = RequireScope(accessContextAccessor);
        var row = await LoadClaimAsync(claim, scope, cancellationToken);
        if (row is null) return RuntimeDurableTimerClaimTransitionResult.AlreadyApplied;
        var timer = ReadChecked(row, scope, claim.Timer.WorkflowExecutionId, claim.Timer.TimerId);
        ValidateTerminalObservation(timer, observation);
        if (row.HistoryRetired) return RuntimeDurableTimerClaimTransitionResult.AlreadyApplied;
        if (!Matches(row, claim)) return RuntimeDurableTimerClaimTransitionResult.Stale;
        var original = row.Revision;
        StageRetirement(row, timer, observation);
        AttachForUpdate(row, original);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return RuntimeDurableTimerClaimTransitionResult.Applied();
        }
        catch (DbUpdateConcurrencyException) { return RuntimeDurableTimerClaimTransitionResult.Stale; }
        finally { Detach(row); }
    }

    public async ValueTask RetireWithHistoryAsync(string workflowExecutionId, string timerId, RuntimeHistoryObservation observation, CancellationToken cancellationToken = default)
    {
        ValidateIdentity(workflowExecutionId, timerId);
        var scope = RequireScope(accessContextAccessor); var id = CompositeId(scope, workflowExecutionId, timerId);
        await Transitions.RunAsync(context, async () =>
        {
            var row = await context.DurableTimers.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
            if (row is null) return;
            var timer = ReadChecked(row, scope, workflowExecutionId, timerId);
            ValidateTerminalObservation(timer, observation);
            if (row.HistoryRetired) return;
            var original = row.Revision;
            StageRetirement(row, timer, observation);
            AttachForUpdate(row, original);
            try { await context.SaveChangesAsync(cancellationToken); }
            finally { Detach(row); }
        }, _ => throw TransitionDidNotSettle("retire with history", workflowExecutionId, timerId), cancellationToken);
    }

    internal static void StageRetirementOrDeletion(RuntimeDbContext context, DurableTimerEntity row, DurableTimer timer, RuntimeHistoryObservation? observation)
    {
        if (timer.HistoryResponsibility is not { } history) { context.DurableTimers.Remove(row); return; }
        if (history.Retired) return;
        if (observation is not null) StageRetirement(row, timer, observation);
        else
        {
            var next = timer with { HistoryResponsibility = new(history.Policy, history.PendingEvents, true) };
            ApplyHistory(row, next); row.Revision = checked(row.Revision + 1);
        }
        // A retired source remains a first-wins receipt until owned run deletion; late scheduling cannot resurrect it.
    }

    internal static void StageRetirement(DurableTimerEntity row, DurableTimer timer, RuntimeHistoryObservation observation)
    {
        ValidateTerminalObservation(timer, observation);
        var history = timer.HistoryResponsibility!;
        if (history.Retired) return;
        var pending = history.PendingEvents.ToList();
        var existing = pending.Find(e => e.HistoryEventId == observation.Event.HistoryEventId);
        if (existing is not null && RuntimeHistoryIdentity.Compute(existing) != RuntimeHistoryIdentity.Compute(observation.Event))
            throw new InvalidOperationException("A timer history identity has conflicting content.");
        if (existing is null) pending.Add(observation.Event);
        var next = timer with { HistoryResponsibility = new(history.Policy, pending, true) };
        ApplyHistory(row, next);
        row.Revision = checked(row.Revision + 1);
    }
    private static void ValidateTerminalObservation(DurableTimer timer, RuntimeHistoryObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        if (timer.HistoryResponsibility is not { } history || !history.Policy.IsEquivalentTo(observation.Policy) ||
            observation.Event.WorkflowExecutionId != timer.WorkflowExecutionId || observation.Event.ActivityExecutionId != timer.ActivityExecutionId ||
            !observation.Event.Metadata.TryGetValue("history.timerId", out var id) || id != timer.TimerId ||
            observation.Event.Name is not ("TimerDispatched" or "TimerRetired"))
            throw new InvalidOperationException("A timer terminal observation must match its admitted owning source.");
        _ = new DurableTimerHistoryResponsibility(history.Policy, [observation.Event], true);
    }
    private static void ApplyHistory(DurableTimerEntity row, DurableTimer timer)
    {
        row.ContentJson = RuntimeArtifactJson.Serialize(timer);
        row.HistoryPending = timer.HistoryResponsibility?.PendingEvents.Count > 0;
        row.HistoryRetired = timer.HistoryResponsibility?.Retired == true;
    }
    private sealed record PendingHistoryCursor(string Binding, string RunHash, string TimerHash);
}
