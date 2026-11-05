using System.Text.Json;
using System.Transactions;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Executions;
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
        if (context.ChangeTracker.HasChanges() || context.Database.CurrentTransaction is not null ||
            context.Database.GetEnlistedTransaction() is not null || Transaction.Current is not null)
            throw new InvalidOperationException("New timer history admission requires a clean timer owner boundary.");
        var scope = RequireScope(accessContextAccessor);
        try
        {
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
            await LockHistorySourceOwnershipAsync(scope, timer.WorkflowExecutionId, cancellationToken);
            await EfWorkflowExecutionStateStore.LockWorkflowExecutionForWriteAsync(context, scope, timer.WorkflowExecutionId, cancellationToken);
            // A winner may have landed while this creation waited for its owner's lock.
            firstWinner = await context.DurableTimers.AsNoTracking().SingleOrDefaultAsync(x => x.Id == existingId, cancellationToken);
            if (firstWinner is not null) return ReadChecked(firstWinner, scope, timer.WorkflowExecutionId, timer.TimerId);
            await EfExecutionLivenessStateStore.EnsureWorkflowExecutionNotDeletedAsync(context, scope, timer.WorkflowExecutionId, cancellationToken);
            var runId = EfWorkflowExecutionStateStore.CreateId(scope, timer.WorkflowExecutionId);
            var native = await context.WorkflowExecutionStates.AsNoTracking().SingleOrDefaultAsync(x => x.Id == runId, cancellationToken);
            if (native is not null)
            {
                var workflow = EfWorkflowExecutionStateStore.ReadChecked(native, scope, timer.WorkflowExecutionId);
                accessContextAccessor.Current.EnsureTenantScope(workflow.TenantId);
                if (!timer.HistoryResponsibility.Policy.IsEquivalentTo(workflow.HistoryCapturePolicy))
                    throw new InvalidOperationException("Timer history admission disagrees with its execution's pinned policy.");
                if (workflow.Status is WorkflowExecutionStatus.Completed or WorkflowExecutionStatus.Faulted or WorkflowExecutionStatus.Cancelled)
                    throw SourceUnavailable("The timer's recorded execution is terminal.");
            }
            else
            {
                var operationalId = RuntimeExecutionOwnershipStateId.For(timer.WorkflowExecutionId);
                var ownerId = CompositeId(scope, timer.WorkflowExecutionId, operationalId);
                var owner = await context.ExecutionLivenessStates.AsNoTracking().SingleOrDefaultAsync(x => x.Id == ownerId, cancellationToken);
                var ownership = owner is null ? null : EfExecutionLivenessStateStore.Read(owner, scope, timer.WorkflowExecutionId, operationalId);
                if (ownership?.ExecutionLease is not { } lease || lease.IsExpired(_timeProvider.GetUtcNow()))
                    throw SourceUnavailable("A new unrecorded timer requires its actual live execution owner.");
                RuntimeExecutionFenceValidator.EnsureCurrent(timer.WorkflowExecutionId, lease.ToFence(), ownership, _timeProvider.GetUtcNow());
                var headId = EfRuntimeHistorySupport.Id("head", scope, timer.WorkflowExecutionId);
                if (await context.RuntimeHistoryHeads.AsNoTracking().AnyAsync(x => x.Id == headId, cancellationToken))
                    throw SourceUnavailable("The timer's history owner lacks its recorded native execution.");
                if (await EfRuntimeHistoryCheckpoint.HasUnrecordedSourceGapAsync(context, scope, timer.WorkflowExecutionId, timer.HistoryResponsibility.Policy, cancellationToken))
                    throw SourceUnavailable("An earlier unrecorded completion already retired this source owner.");
            }
            if (timer.HistoryResponsibility is { Retired: false } responsibility &&
                !responsibility.PendingEvents.Any(e => e.Name == "TimerScheduled"))
            {
                var headId = EfRuntimeHistorySupport.Id("head", scope, timer.WorkflowExecutionId);
                if (native is null || !await context.RuntimeHistoryHeads.AsNoTracking().AnyAsync(x => x.Id == headId, cancellationToken))
                    throw new InvalidOperationException("A new captured timer requires its original schedule responsibility before native history exists.");
            }
            var result = await SaveCoreAsync(timer, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch (Exception exception) when (exception is IOException or TimeoutException || EfRelationalExceptionClassifier.IsProviderFailure(exception))
        { throw new RuntimeSchedulerWorkUnavailableException("runtime.history.capture-unavailable", "The timer history owner could not confirm source admission.", exception); }
        finally { context.ChangeTracker.Clear(); }
    }

    // Current-write lock first: a prior consistent SELECT under InnoDB repeatable-read could otherwise hide
    // a source admitted while this transaction waited. The actual lease validation follows this owner boundary.
    private ValueTask<int> LockHistorySourceOwnershipAsync(string scope, string run, CancellationToken cancellationToken)
    {
        var operational = RuntimeExecutionOwnershipStateId.For(run); var id = CompositeId(scope, run, operational);
        var sh = Hash(scope); var sk = Encode(scope); var rh = Hash(run); var rk = Encode(run); var oh = Hash(operational); var ok = Encode(operational);
        return new(context.ExecutionLivenessStates.Where(x => x.Id == id && x.ScopeKeyHash == sh && x.ScopeKey == sk &&
            x.WorkflowExecutionIdHash == rh && x.WorkflowExecutionId == rk && x.OperationalStateIdHash == oh && x.OperationalStateId == ok)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.Revision, x => x.Revision), cancellationToken));
    }
    private static RuntimeSchedulerWorkUnavailableException SourceUnavailable(string message) => new("runtime.history.capture-unavailable", message);

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
            var headId = EfRuntimeHistorySupport.Id("head", scope, workflowExecutionId);
            var acknowledged = await context.RuntimeHistoryHeads.AsNoTracking().SingleOrDefaultAsync(x => x.Id == headId, cancellationToken);
            if (acknowledged is null) return false;
            var head = EfRuntimeHistorySupport.Read(acknowledged, scope, workflowExecutionId);
            var nativeId = EfWorkflowExecutionStateStore.CreateId(scope, workflowExecutionId);
            var native = await context.WorkflowExecutionStates.AsNoTracking().SingleOrDefaultAsync(x => x.Id == nativeId, cancellationToken);
            if (native is null) return false;
            var workflow = EfWorkflowExecutionStateStore.ReadChecked(native, scope, workflowExecutionId);
            accessContextAccessor.Current.EnsureTenantScope(workflow.TenantId);
            if (!head.Policy.IsEquivalentTo(history.Policy) || !history.Policy.IsEquivalentTo(workflow.HistoryCapturePolicy))
                throw new InvalidOperationException("Timer source acknowledgement differs from its admitted history owner.");
            var receiptId = EfRuntimeHistorySupport.Id("event", scope, workflowExecutionId, historyEventId);
            var receipt = await context.RuntimeHistoryEvents.AsNoTracking().SingleOrDefaultAsync(x => x.Id == receiptId, cancellationToken);
            if (receipt is null) return false;
            if (EfRuntimeHistorySupport.ReadReceipt(receipt, scope, workflowExecutionId, head) != historyEventId ||
                receipt.CandidateFingerprint != RuntimeHistoryIdentity.Compute(history.PendingEvents.Single(e => e.HistoryEventId == historyEventId)))
                throw new InvalidOperationException("Timer source acknowledgement has a conflicting semantic receipt.");
            {
                var historyRevision = row.HistoryRevision;
                var next = timer with { HistoryResponsibility = new(history.Policy, remaining, history.Retired),
                    Input = history.Retired && remaining.Length == 0 ? null : timer.Input,
                    Metadata = history.Retired && remaining.Length == 0 ? null : timer.Metadata,
                    PayloadType = history.Retired && remaining.Length == 0 ? null : timer.PayloadType,
                    ProviderId = history.Retired && remaining.Length == 0 ? null : timer.ProviderId };
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
