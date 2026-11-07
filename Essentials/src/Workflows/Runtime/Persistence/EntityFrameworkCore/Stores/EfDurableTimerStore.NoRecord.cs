using System.Transactions;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.EntityFrameworkCore;
using static Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores.EfRuntimeOperationalStoreSupport;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

public sealed partial class EfDurableTimerStore
{
    public async ValueTask<bool> DischargeUnrecordedCompletionAsync(RuntimeCheckpointCommit completion, int limit = 256,
        CancellationToken cancellationToken = default)
    {
        var workflow = RuntimeHistoryNoRecordCompletion.Validate(completion);
        RuntimeStorePageRequest.ValidateLimit(limit, nameof(limit));
        var scope = RequireScope(accessContextAccessor); accessContextAccessor.Current.EnsureTenantScope(workflow.TenantId);
        if (context.ChangeTracker.HasChanges() || context.Database.CurrentTransaction is not null ||
            context.Database.GetEnlistedTransaction() is not null || Transaction.Current is not null)
            throw new InvalidOperationException("No-record source discharge requires a clean timer owner boundary.");
        context.ChangeTracker.Clear();
        try
        {
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
            await LockHistorySourceOwnershipAsync(scope, workflow.WorkflowExecutionId, cancellationToken);
            // The existing liveness row is the owner boundary. Acquire its real revision CAS before observing
            // absent native/head state; a competing native checkpoint must conflict or follow this transaction.
            await EfRuntimeCheckpointParticipantStaging.StageExecutionFenceAsync(context, scope, workflow.WorkflowExecutionId,
                completion.ExpectedFence!, _timeProvider, cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            await EfExecutionLivenessStateStore.EnsureWorkflowExecutionNotDeletedAsync(context, scope, workflow.WorkflowExecutionId, cancellationToken);
            var nativeId = EfWorkflowExecutionStateStore.CreateId(scope, workflow.WorkflowExecutionId);
            var headId = EfRuntimeHistorySupport.Id("head", scope, workflow.WorkflowExecutionId);
            if (await context.WorkflowExecutionStates.AsNoTracking().AnyAsync(x => x.Id == nativeId, cancellationToken) ||
                await context.RuntimeHistoryHeads.AsNoTracking().AnyAsync(x => x.Id == headId, cancellationToken))
                throw new InvalidOperationException("A recorded workflow cannot discharge source history as an unrecorded completion.");
            var sh = Hash(scope); var sk = Encode(scope); var rh = Hash(workflow.WorkflowExecutionId); var rk = Encode(workflow.WorkflowExecutionId);
            var pending = context.DurableTimers.AsNoTracking().Where(x => x.ScopeKeyHash == sh && x.ScopeKey == sk &&
                x.WorkflowExecutionIdHash == rh && x.WorkflowExecutionId == rk && x.HistoryPending);
            // The extended exact-run index proves active refusal before clearing ANY retired slice. With no
            // native/head, a qualified schedule cannot have been acknowledged: active captured implies pending.
            if (await pending.AnyAsync(x => !x.HistoryRetired, cancellationToken))
                throw new RuntimeSchedulerWorkUnavailableException("runtime.history.capture-unavailable",
                    "An active timer still outlives the proposed unrecorded completion.");
            // Clearing pending removes the visited slice. Timer hash supplies indexed deterministic ordering.
            var rows = await pending.Where(x => x.HistoryRetired).OrderBy(x => x.TimerIdHash).Take(limit).ToArrayAsync(cancellationToken);
            foreach (var row in rows)
            {
                var timer = ReadChecked(row, scope, workflow.WorkflowExecutionId);
                if (timer.HistoryResponsibility is not { Retired: true } history || !workflow.HistoryCapturePolicy!.IsEquivalentTo(history.Policy))
                    throw new InvalidOperationException("Retired timer responsibility differs from the actual completion's admitted policy.");
                var historyRevision = row.HistoryRevision;
                var receipt = history.NoRecordCompletion ?? new DurableTimerNoRecordCompletion(completion.Checkpoint.CheckpointId, workflow.CompletedAt!.Value);
                var compact = timer with { Input = null, Metadata = null, PayloadType = null, ProviderId = null,
                    HistoryResponsibility = new(history.Policy, [], true) { NoRecordCompletion = receipt } };
                ApplyHistory(row, compact); row.HistoryRevision = checked(historyRevision + 1);
                AttachForUpdate(row, row.Revision);
                context.Entry(row).Property(x => x.HistoryRevision).OriginalValue = historyRevision;
            }
            await context.SaveChangesAsync(cancellationToken);
            var remains = await pending.AnyAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return !remains;
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new RuntimeSchedulerWorkUnavailableException("runtime.history.capture-unavailable",
                "The timer source owner changed before unrecorded completion; persistence remains owed.", exception);
        }
        catch (Exception exception) when (exception is IOException or TimeoutException || EfRelationalExceptionClassifier.IsProviderFailure(exception))
        {
            throw new RuntimeSchedulerWorkUnavailableException("runtime.history.capture-unavailable",
                "The timer source owner could not confirm unrecorded completion; persistence remains owed.", exception);
        }
        finally { context.ChangeTracker.Clear(); }
    }
}
