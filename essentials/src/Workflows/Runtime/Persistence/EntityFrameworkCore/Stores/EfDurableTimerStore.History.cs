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
    public bool CanInspectHistorySources(Elsa.Workflows.Runtime.Core.Contracts.IRuntimeHistoryStore history) =>
        SupportsHistoryResponsibility && history is EfRuntimeHistoryStore selected && selected.Uses(context, accessContextAccessor, RuntimeHistoryEfModule.Chain);

    private const string PendingHistoryPurpose = "ef-runtime-timer-history-v1";

    private async ValueTask<DurableTimer> SaveCoreAsyncHistory(DurableTimer timer, CancellationToken cancellationToken = default)
    {
        ValidateTimer(timer); cancellationToken.ThrowIfCancellationRequested();
        var captured = accessContextAccessor.Current; var scope = captured.RequireScope().Value;
        void Check()
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (accessContextAccessor.Current != captured) throw new InvalidOperationException("Timer admission changed persistence access.");
            if (timer.HistoryResponsibility is not null) EfTimerHistoryOwner.EnsureWriteTracking(context);
        }
        if (timer.HistoryResponsibility is not null && (!SupportsHistoryResponsibility || context.ChangeTracker.HasChanges() ||
            context.Database.CurrentTransaction is not null || context.Database.GetEnlistedTransaction() is not null || Transaction.Current is not null))
            throw new NotSupportedException("Captured scheduling requires a clean ordinary timer owner and current history model.");
        var id = CompositeId(scope, timer.WorkflowExecutionId, timer.TimerId);
        var winner = await context.DurableTimers.AsNoTracking().SingleOrDefaultAsync(row => row.Id == id, cancellationToken); Check();
        if (winner is not null) return ReadChecked(winner, scope, timer.WorkflowExecutionId, timer.TimerId);
        if (timer.HistoryResponsibility is null)
        {
            // A retained uncaptured native admission is immutable and cannot acquire a timer owner later.
            // Preserve the legacy store's caller-owned tracker/transaction behavior for that proven case.
            var original = await ReadNativeAsync(scope, timer.WorkflowExecutionId, cancellationToken); Check();
            var owner = EfTimerHistoryOwner.Read(await EfTimerHistoryOwner.ReadAsync(context, scope, timer.WorkflowExecutionId, false, cancellationToken), scope, timer.WorkflowExecutionId); Check();
            if (owner is null && original is not null && !HasTimerChoice(original))
                return await SaveCoreAsync(timer, cancellationToken);
        }
        if (context.Database.CurrentTransaction is not null || Transaction.Current is not null)
        {
            // Lock the current ownership row even when the caller established an older consistent snapshot.
            // Conditional writes observe current committed authority under InnoDB repeatable read.
            await EfTimerHistoryOwner.LockAsync(context, scope, timer.WorkflowExecutionId, cancellationToken); Check();
            var ownerId = EfTimerHistoryOwner.Id(scope, timer.WorkflowExecutionId);
            if (await context.ExecutionLivenessStates.Where(row => row.Id == ownerId && row.TimerHistoryOwnerJson != null)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.Revision, row => row.Revision + 1), cancellationToken) != 0)
                throw SourceUnavailable("A current timer owner cannot use legacy transactional scheduling.");
            Check();
            var original = await ReadNativeAsync(scope, timer.WorkflowExecutionId, cancellationToken); Check();
            if (original is null)
            {
                var nativeId = EfWorkflowExecutionStateStore.CreateId(scope, timer.WorkflowExecutionId);
                if (await context.WorkflowExecutionStates.Where(row => row.Id == nativeId)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.Revision, row => row.Revision + 1), cancellationToken) != 0)
                    throw SourceUnavailable("A stale caller snapshot cannot prove the current native timer choice.");
                Check();
            }
            var owner = EfTimerHistoryOwner.Read(await EfTimerHistoryOwner.ReadAsync(context, scope, timer.WorkflowExecutionId, false, cancellationToken), scope, timer.WorkflowExecutionId); Check();
            if (owner is not null || HasTimerChoice(original)) throw SourceUnavailable("An original timer capture choice cannot use legacy transactional scheduling.");
            return await SaveCoreAsync(timer, cancellationToken);
        }
        if (context.ChangeTracker.HasChanges()) throw new InvalidOperationException("Timer source admission requires a clean native context.");
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await EfTimerHistoryOwner.LockAsync(context, scope, timer.WorkflowExecutionId, cancellationToken); Check();
            var row = await EfTimerHistoryOwner.ReadAsync(context, scope, timer.WorkflowExecutionId, true, cancellationToken); Check();
            var owner = EfTimerHistoryOwner.Read(row, scope, timer.WorkflowExecutionId);
            var native = await ReadNativeAsync(scope, timer.WorkflowExecutionId, cancellationToken); Check();
            winner = await context.DurableTimers.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id, cancellationToken); Check();
            if (winner is not null) return ReadChecked(winner, scope, timer.WorkflowExecutionId, timer.TimerId);
            if (timer.HistoryResponsibility is not { } history)
            {
                if (owner is not null || HasTimerChoice(native)) throw SourceUnavailable("The original timer capture choice cannot drop its scheduling responsibility.");
            }
            else
            {
                if (history.Retired || history.NoRecordCompletion is not null || history.PendingEvents.Count != 1 ||
                    !DurableTimerHistoryFacts.Kinds.All(kind => history.Policy.SupportedKinds.Contains(kind, StringComparer.Ordinal)))
                    throw new InvalidOperationException("A new timer owes its complete original scheduling responsibility.");
                ValidateSourceObservation(timer, new(history.Policy, history.PendingEvents[0]), false);
                await EfExecutionLivenessStateStore.EnsureWorkflowExecutionNotDeletedAsync(context, scope, timer.WorkflowExecutionId, cancellationToken); Check();
                var ownership = row is null ? null : EfExecutionLivenessStateStore.Read(row, scope, timer.WorkflowExecutionId, RuntimeExecutionOwnershipStateId.For(timer.WorkflowExecutionId));
                if (ownership?.ExecutionLease is not { FencingToken: > 0 } lease) throw SourceUnavailable("Captured scheduling requires its actual positive live lease.");
                RuntimeExecutionFenceValidator.EnsureCurrent(timer.WorkflowExecutionId, lease.ToFence(), ownership, _timeProvider.GetUtcNow());
                EfTimerHistoryOwner.EnsurePolicy(owner, scope, timer.WorkflowExecutionId, history.Policy);
                if (native is not null)
                {
                    EfTimerHistoryOwner.EnsureNativeScope(captured, native);
                    if (native.Status.IsTerminal() || native.HistoryCaptureEnabledAtAdmission == false || !history.Policy.IsEquivalentTo(native.HistoryCapturePolicy))
                        throw SourceUnavailable("The actual native run cannot admit this timer capture choice.");
                }
                else
                {
                    if (owner?.Completion is not null) throw SourceUnavailable("An unrecorded completion cannot reopen timers without native recovery.");
                    var head = EfRuntimeHistoryProjectionMapping.Id("head", scope, timer.WorkflowExecutionId);
                    if (await context.Set<RuntimeHistoryHeadEntity>().AsNoTracking().AnyAsync(item => item.Id == head, cancellationToken))
                        throw SourceUnavailable("A history head without its native run cannot admit a timer.");
                }
                Check(); EfTimerHistoryOwner.Stage(context, row!, owner ?? new(scope, timer.WorkflowExecutionId, history.Policy));
            }
            var result = await SaveCoreAsync(timer, cancellationToken); Check();
            await transaction.CommitAsync(cancellationToken); Check(); return result;
        }
        catch (DbUpdateConcurrencyException error) { throw new RuntimeSchedulerWorkUnavailableException("runtime.history.capture-unavailable", "Timer source ownership changed before admission.", error); }
        finally { context.ChangeTracker.Clear(); }
    }

    private async ValueTask<WorkflowExecutionState?> ReadNativeAsync(string scope, string run, CancellationToken token)
    {
        if (!EfTimerHistoryOwner.HasReadableNativeModel(context))
            throw SourceUnavailable("The timer's original native admission requires its canonical complete read mapping.");
        var id = EfWorkflowExecutionStateStore.CreateId(scope, run);
        var row = await context.WorkflowExecutionStates.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id, token);
        return row is null ? null : EfWorkflowExecutionStateStore.ReadChecked(row, scope, run);
    }
    private static bool HasTimerChoice(WorkflowExecutionState? native) => native?.HistoryCapturePolicy is { } policy &&
        policy.SupportedKinds.Any(kind => DurableTimerHistoryFacts.Kinds.Contains(kind, StringComparer.Ordinal));
    private static RuntimeSchedulerWorkUnavailableException SourceUnavailable(string message) => new("runtime.history.capture-unavailable", message);

    private async ValueTask<bool> HasPendingHistoryCoreAsync(string workflowExecutionId, CancellationToken cancellationToken = default)
    {
        ValidateIdentity(workflowExecutionId, nameof(workflowExecutionId));
        var captured = accessContextAccessor.Current; var scope = captured.RequireScope().Value; var sh = Hash(scope); var sk = Encode(scope);
        var rh = Hash(workflowExecutionId); var rk = Encode(workflowExecutionId);
        var pending = await context.DurableTimers.AsNoTracking().AnyAsync(x => x.ScopeKeyHash == sh && x.ScopeKey == sk &&
            x.WorkflowExecutionIdHash == rh && x.WorkflowExecutionId == rk && x.HistoryPending, cancellationToken);
        CheckHistoryAccess(captured, cancellationToken); return pending;
    }

    private async ValueTask<RuntimeStorePage<DurableTimer>> ListPendingHistoryCoreAsync(DurableTimerHistoryQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var captured = accessContextAccessor.Current; var scope = captured.RequireScope().Value; var sh = Hash(scope); var sk = Encode(scope);
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
        CheckHistoryAccess(captured, cancellationToken);
        var hasMore = rows.Length > query.Limit;
        if (hasMore) rows = rows[..query.Limit];
        var items = rows.Select(row => ReadChecked(row, scope)).ToArray();
        var next = hasMore ? continuationCodec.Encode(PendingHistoryPurpose, JsonSerializer.SerializeToUtf8Bytes(new PendingHistoryCursor(binding, rows[^1].WorkflowExecutionIdHash, rows[^1].TimerIdHash))) : null;
        CheckHistoryAccess(captured, cancellationToken); return new(query, items, next);
    }

    private void CheckHistoryAccess(PersistenceAccessContext captured, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (accessContextAccessor.Current != captured) throw new InvalidOperationException("Persistence access changed while timer history was inspected.");
        _ = captured.RequireScope();
    }

    private async ValueTask<bool> AcknowledgeHistoryCoreAsync(string workflowExecutionId, string timerId, string historyEventId, CancellationToken cancellationToken = default)
    {
        ValidateIdentity(workflowExecutionId, timerId); ArgumentException.ThrowIfNullOrWhiteSpace(historyEventId);
        var captured = accessContextAccessor.Current; var scope = captured.RequireScope().Value; var id = CompositeId(scope, workflowExecutionId, timerId);
        return await Transitions.RunAsync(context, async () =>
        {
            CheckHistoryAccess(captured, cancellationToken);
            var row = await context.DurableTimers.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
            CheckHistoryAccess(captured, cancellationToken);
            if (row is null) return true;
            var timer = ReadChecked(row, scope, workflowExecutionId, timerId);
            if (timer.HistoryResponsibility is not { } history) return false;
            var remaining = history.PendingEvents.Where(e => e.HistoryEventId != historyEventId).ToArray();
            if (remaining.Length == history.PendingEvents.Count) return true;
            var headId = EfRuntimeHistoryProjectionMapping.Id("head", scope, workflowExecutionId);
            var acknowledged = await context.Set<RuntimeHistoryHeadEntity>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == headId, cancellationToken);
            CheckHistoryAccess(captured, cancellationToken);
            if (acknowledged is null) return false;
            var mapping = new EfRuntimeHistoryProjectionMapping(RuntimeHistoryEfModule.Chain);
            var head = mapping.Read(acknowledged, scope, workflowExecutionId);
            var nativeId = EfWorkflowExecutionStateStore.CreateId(scope, workflowExecutionId);
            var native = await context.WorkflowExecutionStates.AsNoTracking().SingleOrDefaultAsync(x => x.Id == nativeId, cancellationToken);
            CheckHistoryAccess(captured, cancellationToken);
            if (native is null) return false;
            var workflow = EfWorkflowExecutionStateStore.ReadChecked(native, scope, workflowExecutionId);
            EfTimerHistoryOwner.EnsureNativeScope(captured, workflow);
            if (!head.Policy.IsEquivalentTo(history.Policy) || !history.Policy.IsEquivalentTo(workflow.HistoryCapturePolicy))
                throw new InvalidOperationException("Timer source acknowledgement differs from its admitted history owner.");
            var receiptId = EfRuntimeHistoryProjectionMapping.Id("event", scope, workflowExecutionId, historyEventId);
            var receipt = await context.Set<RuntimeHistoryEventEntity>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == receiptId, cancellationToken);
            CheckHistoryAccess(captured, cancellationToken);
            if (receipt is null) return false;
            var owed = history.PendingEvents.Single(e => e.HistoryEventId == historyEventId);
            var fingerprint = EfRuntimeHistoryObservation.Fingerprint(owed);
            var retained = mapping.ReadReceipt(receipt, scope, workflowExecutionId, head);
            if (retained.HistoryEventId != historyEventId || receipt.CandidateFingerprint != fingerprint)
                throw new RuntimeHistoryIntegrityException(workflowExecutionId, "timer source acknowledgement has a conflicting semantic receipt");
            if (!retained.IsPurged)
            {
                var recorded = mapping.Read(receipt, scope, workflowExecutionId, head);
                if (recorded.PhysicalCommitId != "owner-observation:" + historyEventId || EfRuntimeHistoryObservation.Fingerprint(recorded) != fingerprint)
                    throw new RuntimeHistoryIntegrityException(workflowExecutionId, "timer acknowledgement has conflicting retained content or physical ownership");
            }
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
            CheckHistoryAccess(captured, cancellationToken);
            try { await context.SaveChangesAsync(cancellationToken); CheckHistoryAccess(captured, cancellationToken); return true; }
            finally { Detach(row); }
        }, _ => throw TransitionDidNotSettle("acknowledge history", workflowExecutionId, timerId), cancellationToken);
    }

    private async ValueTask<RuntimeDurableTimerClaimTransitionResult> CompleteClaimWithHistoryCoreAsync(RuntimeDurableTimerClaim claim,
        RuntimeHistoryObservation observation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(claim); ValidateClaim(claim);
        var captured = accessContextAccessor.Current; var scope = captured.RequireScope().Value;
        CheckHistoryAccess(captured, cancellationToken);
        var row = await LoadClaimAsync(claim, scope, cancellationToken); CheckHistoryAccess(captured, cancellationToken);
        if (row is null) return RuntimeDurableTimerClaimTransitionResult.AlreadyApplied;
        var timer = ReadChecked(row, scope, claim.Timer.WorkflowExecutionId, claim.Timer.TimerId);
        ValidateTerminalObservation(timer, observation);
        if (row.HistoryRetired) return RuntimeDurableTimerClaimTransitionResult.AlreadyApplied;
        if (!Matches(row, claim)) return RuntimeDurableTimerClaimTransitionResult.Stale;
        CheckHistoryAccess(captured, cancellationToken);
        var original = row.Revision;
        StageRetirement(row, timer, observation);
        AttachForUpdate(row, original);
        try
        {
            CheckHistoryAccess(captured, cancellationToken); await context.SaveChangesAsync(cancellationToken); CheckHistoryAccess(captured, cancellationToken);
            return RuntimeDurableTimerClaimTransitionResult.Applied();
        }
        catch (DbUpdateConcurrencyException) { return RuntimeDurableTimerClaimTransitionResult.Stale; }
        finally { Detach(row); }
    }

    private async ValueTask RetireWithHistoryCoreAsync(string workflowExecutionId, string timerId, RuntimeHistoryObservation observation, CancellationToken cancellationToken = default)
    {
        ValidateIdentity(workflowExecutionId, timerId);
        var captured = accessContextAccessor.Current; var scope = captured.RequireScope().Value; var id = CompositeId(scope, workflowExecutionId, timerId);
        await Transitions.RunAsync(context, async () =>
        {
            CheckHistoryAccess(captured, cancellationToken);
            var row = await context.DurableTimers.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
            CheckHistoryAccess(captured, cancellationToken);
            if (row is null) return;
            var timer = ReadChecked(row, scope, workflowExecutionId, timerId);
            ValidateTerminalObservation(timer, observation);
            if (row.HistoryRetired) return;
            var original = row.Revision;
            StageRetirement(row, timer, observation);
            AttachForUpdate(row, original);
            CheckHistoryAccess(captured, cancellationToken);
            try { await context.SaveChangesAsync(cancellationToken); CheckHistoryAccess(captured, cancellationToken); }
            finally { Detach(row); }
        }, _ => throw TransitionDidNotSettle("retire with history", workflowExecutionId, timerId), cancellationToken);
    }

    private void RequireCleanHistoryMutation()
    {
        if (!SupportsHistoryResponsibility || context.ChangeTracker.HasChanges() || context.Database.CurrentTransaction is not null ||
            context.Database.GetEnlistedTransaction() is not null || Transaction.Current is not null)
            throw new NotSupportedException("Timer history mutations require the qualified clean physical owner.");
    }

    internal static void StageRetirementOrDeletion(RuntimeDbContext context, DurableTimerEntity row, DurableTimer timer, RuntimeHistoryObservation? observation)
    {
        if (timer.HistoryResponsibility is not { } history) { context.DurableTimers.Remove(row); return; }
        EfTimerHistoryOwner.EnsureWriteTracking(context);
        if (history.Retired) return;
        if (observation is not null) StageRetirement(row, timer, observation);
        else throw new NotSupportedException("Captured retirement requires its actual terminal owner fact.");
        var entry = context.Entry(row);
        entry.Property(item => item.ContentJson).IsModified = true;
        entry.Property(item => item.HistoryPending).IsModified = true;
        entry.Property(item => item.HistoryRetired).IsModified = true;
        entry.Property(item => item.Revision).IsModified = true;
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
        ValidateSourceObservation(timer, observation, true);
    }
    private static void ValidateSourceObservation(DurableTimer timer, RuntimeHistoryObservation observation, bool terminal)
    {
        RuntimeHistorySafety.ValidateObservation(observation);
        var e = observation.Event;
        if (timer.HistoryResponsibility is not { } history || !history.Policy.IsEquivalentTo(observation.Policy) ||
            (terminal ? e.Name is not ("TimerDispatched" or "TimerRetired") : e.Name != "TimerScheduled") ||
            e.WorkflowExecutionId != timer.WorkflowExecutionId || e.ActivityExecutionId != timer.ActivityExecutionId ||
            e.ExecutionScopeId != timer.ExecutionScopeId || e.Metadata["history.timerId"] != timer.TimerId ||
            e.Metadata["history.scheduledAt"] != timer.DueTime.ToString("O", System.Globalization.CultureInfo.InvariantCulture) ||
            !terminal && !e.OccurredAt.EqualsExact(timer.CreatedAt))
            throw new InvalidOperationException("The timer observation differs from its exact original source.");
    }
    private static void ApplyHistory(DurableTimerEntity row, DurableTimer timer)
    {
        row.ContentJson = RuntimeArtifactJson.Serialize(timer);
        row.HistoryPending = timer.HistoryResponsibility?.PendingEvents.Count > 0;
        row.HistoryRetired = timer.HistoryResponsibility?.Retired == true;
    }
    private sealed record PendingHistoryCursor(string Binding, string RunHash, string TimerHash);
}
