using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Services.Executions;
using System.Transactions;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.EntityFrameworkCore;
using static Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores.EfRuntimeOperationalStoreSupport;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

public sealed partial class EfDurableTimerStore
{
    public bool CanDischargeUnrecordedCompletion(IRuntimeCheckpointCommitStore writer) => SupportsHistoryResponsibility &&
        writer is EfRuntimeCheckpointCommitStore native && native.OwnsTimerHistory(context, accessContextAccessor, _timeProvider);

    private async ValueTask<bool> DischargeUnrecordedCompletionCoreAsync(RuntimeCheckpointCommit completion, int limit = 256,
        CancellationToken cancellationToken = default)
    {
        var workflow = RuntimeHistoryNoRecordCompletion.Validate(completion);
        RuntimeStorePageRequest.ValidateLimit(limit, nameof(limit)); cancellationToken.ThrowIfCancellationRequested();
        var captured = accessContextAccessor.Current; var scope = captured.RequireScope().Value;
        EfTimerHistoryOwner.EnsureNativeScope(captured, workflow);
        if (workflow.HistoryCaptureEnabledAtAdmission == false || completion.ExpectedFence!.FencingToken <= 0 ||
            !DurableTimerHistoryFacts.Kinds.All(kind => workflow.HistoryCapturePolicy!.SupportedKinds.Contains(kind, StringComparer.Ordinal)))
            throw new InvalidOperationException("Unrecorded timer completion requires the original enabled complete choice and positive fence.");
        var receipt = new DurableTimerNoRecordCompletion(completion.Checkpoint.CheckpointId, workflow.CompletedAt!.Value);
        if (!SupportsHistoryResponsibility || context.ChangeTracker.HasChanges() || context.Database.CurrentTransaction is not null ||
            context.Database.GetEnlistedTransaction() is not null || Transaction.Current is not null)
            throw new NotSupportedException("Unrecorded timer completion requires its clean qualified physical owner.");
        void Check()
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (accessContextAccessor.Current != captured) throw new InvalidOperationException("Timer completion changed persistence access.");
            EfTimerHistoryOwner.EnsureWriteTracking(context);
        }
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await EfTimerHistoryOwner.LockAsync(context, scope, workflow.WorkflowExecutionId, cancellationToken); Check();
            await EfRuntimeCheckpointParticipantStaging.StageExecutionFenceAsync(context, scope, workflow.WorkflowExecutionId,
                completion.ExpectedFence!, _timeProvider, cancellationToken); Check();
            await context.SaveChangesAsync(cancellationToken); Check();
            await EfExecutionLivenessStateStore.EnsureWorkflowExecutionNotDeletedAsync(context, scope, workflow.WorkflowExecutionId, cancellationToken); Check();
            if (await ReadNativeAsync(scope, workflow.WorkflowExecutionId, cancellationToken) is not null)
                throw new InvalidOperationException("Recorded native execution cannot discharge unrecorded source responsibility.");
            var headId = EfRuntimeHistoryProjectionMapping.Id("head", scope, workflow.WorkflowExecutionId);
            if (await context.Set<RuntimeHistoryHeadEntity>().AsNoTracking().AnyAsync(row => row.Id == headId, cancellationToken))
                throw new InvalidOperationException("Retained native history cannot discharge unrecorded source responsibility.");
            var row = await EfTimerHistoryOwner.ReadAsync(context, scope, workflow.WorkflowExecutionId, true, cancellationToken); Check();
            var owner = EfTimerHistoryOwner.Read(row, scope, workflow.WorkflowExecutionId);
            EfTimerHistoryOwner.EnsurePolicy(owner, scope, workflow.WorkflowExecutionId, workflow.HistoryCapturePolicy);
            var sh = Hash(scope); var sk = Encode(scope); var rh = Hash(workflow.WorkflowExecutionId); var rk = Encode(workflow.WorkflowExecutionId);
            var sources = context.DurableTimers.AsNoTracking().Where(item => item.ScopeKeyHash == sh && item.ScopeKey == sk &&
                item.WorkflowExecutionIdHash == rh && item.WorkflowExecutionId == rk);
            // Check the whole indexed active range before clearing any retired prefix, even if schedule ACK is corrupt.
            if (await sources.AnyAsync(item => !item.HistoryRetired && item.HistoryRevision > 0, cancellationToken))
                throw SourceUnavailable("An active captured timer outlives the unrecorded completion.");
            var pending = sources.Where(item => item.HistoryPending);
            var rows = await pending.Where(item => item.HistoryRetired).OrderBy(item => item.TimerIdHash).Take(limit).ToArrayAsync(cancellationToken); Check();
            foreach (var source in rows)
            {
                var timer = ReadChecked(source, scope, workflow.WorkflowExecutionId);
                if (timer.HistoryResponsibility is not { Retired: true } history || !history.Policy.IsEquivalentTo(workflow.HistoryCapturePolicy))
                    throw new InvalidOperationException("Retired timer responsibility differs from the actual completion's original policy.");
            }
            var nextOwner = new EfTimerHistoryOwner.Document(scope, workflow.WorkflowExecutionId, workflow.HistoryCapturePolicy!,
                owner?.Completion ?? receipt, owner?.LostFacts == true || rows.Length > 0);
            EfTimerHistoryOwner.Stage(context, row!, nextOwner); // Bounded closure validation precedes source mutation, including empty completion.
            foreach (var source in rows)
            {
                var timer = ReadChecked(source, scope, workflow.WorkflowExecutionId); var history = timer.HistoryResponsibility!;
                ApplyHistory(source, timer with { Input = null, Metadata = null, PayloadType = null, ProviderId = null,
                    HistoryResponsibility = new(history.Policy, [], true) { NoRecordCompletion = history.NoRecordCompletion ?? receipt } });
                var historyRevision = source.HistoryRevision;
                source.HistoryRevision = checked(historyRevision + 1); AttachForUpdate(source, source.Revision);
                context.Entry(source).Property(item => item.HistoryRevision).OriginalValue = historyRevision;
            }
            Check(); await context.SaveChangesAsync(cancellationToken); Check();
            var remains = await pending.AnyAsync(cancellationToken); Check();
            await transaction.CommitAsync(cancellationToken); Check(); return !remains;
        }
        catch (DbUpdateConcurrencyException error)
        { throw new RuntimeSchedulerWorkUnavailableException("runtime.history.capture-unavailable", "Timer completion ownership changed; persistence remains owed.", error); }
        finally { context.ChangeTracker.Clear(); }
    }
}
