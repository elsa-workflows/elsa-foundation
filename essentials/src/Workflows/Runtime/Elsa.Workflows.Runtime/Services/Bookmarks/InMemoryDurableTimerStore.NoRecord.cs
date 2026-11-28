using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Elsa.Workflows.Runtime.Services.Executions;

namespace Elsa.Workflows.Runtime.Services.Bookmarks;

public sealed partial class InMemoryDurableTimerStore
{
    public bool CanDischargeUnrecordedCompletion(IRuntimeCheckpointCommitStore checkpointStore) => SupportsHistoryResponsibility &&
        checkpointStore is InMemoryRuntimeCheckpointCommitStore memory &&
        memory.OwnsUnrecordedTimerState(_checkpoints!, _executions!, _liveness!, _access!, _clock);

    public async ValueTask<bool> DischargeUnrecordedCompletionAsync(RuntimeCheckpointCommit completion, int limit = 256,
        CancellationToken cancellationToken = default)
    {
        var workflow = RuntimeHistoryNoRecordCompletion.Validate(completion);
        RuntimeStorePageRequest.ValidateLimit(limit, nameof(limit)); cancellationToken.ThrowIfCancellationRequested();
        var context = RequireHistory(); EnsureNativeScope(context, workflow);
        if (workflow.HistoryCaptureEnabledAtAdmission == false || completion.ExpectedFence!.FencingToken <= 0 ||
            !DurableTimerHistoryFacts.Kinds.All(kind => workflow.HistoryCapturePolicy!.SupportedKinds.Contains(kind, StringComparer.Ordinal)))
            throw new InvalidOperationException("Timer discharge requires the original complete enabled capture choice and positive fence.");
        var scope = context.RequireScope().Value;
        // Construct all bounded receipt input before publishing any owner/index mutation.
        var completionReceipt = new DurableTimerNoRecordCompletion(completion.Checkpoint.CheckpointId, workflow.CompletedAt!.Value);
        return await _checkpoints!.ExecuteWriteGateAsync(token => _liveness!.ExecuteOwnershipAtomicAsync(async ownedToken =>
        {
            EnsureAccess(context);
            var ownership = await _liveness.FindAsync(workflow.WorkflowExecutionId, RuntimeExecutionOwnershipStateId.For(workflow.WorkflowExecutionId), ownedToken);
            EnsureAccess(context);
            RuntimeExecutionFenceValidator.EnsureCurrent(workflow.WorkflowExecutionId, completion.ExpectedFence!, ownership, _clock.GetUtcNow());
            var native = await _executions!.FindAsync(workflow.WorkflowExecutionId, ownedToken);
            if (native is not null || await _executions.IsDeletionTombstonedAsync(workflow.WorkflowExecutionId, ownedToken))
                throw new InvalidOperationException("A recorded or deleted native run cannot discharge an unrecorded completion.");
            EnsureAccess(context);
            lock (_checkpoints.SyncRoot)
                if (_checkpoints.HistoryHeads.ContainsKey((scope, workflow.WorkflowExecutionId)) ||
                    _checkpoints.HistoryRunOwners.ContainsKey(workflow.WorkflowExecutionId) ||
                    _checkpoints.HistoryDeletedRuns.Contains((scope, workflow.WorkflowExecutionId)))
                    throw new InvalidOperationException("A retained history owner cannot discharge an unrecorded completion.");
            ownedToken.ThrowIfCancellationRequested(); EnsureAccess(context);
            lock (_syncRoot)
            {
                if (_shared.SourceOwners.TryGetValue(workflow.WorkflowExecutionId, out var owner) &&
                    (owner.Scope != scope || !workflow.HistoryCapturePolicy!.IsEquivalentTo(owner.Policy)))
                    throw new InvalidOperationException("Timer source scope or original policy differs from the actual completion.");
                var bucket = Bucket;
                // The direct active count covers the entire run before any bounded prefix is changed.
                if (bucket.ActiveCaptured.GetValueOrDefault(workflow.WorkflowExecutionId) != 0)
                    throw Unavailable("An active captured timer still outlives the proposed unrecorded completion.");
                if (!bucket.PendingByRun.TryGetValue(workflow.WorkflowExecutionId, out var pending))
                {
                    RememberCompletion(); return true;
                }
                var keys = pending.Values.Take(limit).ToArray();
                foreach (var key in keys)
                    if (_timers[key].Timer.HistoryResponsibility is not { Retired: true } history ||
                        !workflow.HistoryCapturePolicy!.IsEquivalentTo(history.Policy))
                        throw new InvalidOperationException("The bounded retired prefix differs from the actual completion's policy.");
                foreach (var key in keys)
                {
                    var state = _timers[key]; var history = state.Timer.HistoryResponsibility!;
                    state.Timer = state.Timer with
                    {
                        Input = null, Metadata = null, PayloadType = null, ProviderId = null,
                        HistoryResponsibility = new(history.Policy, [], true)
                        {
                            NoRecordCompletion = history.NoRecordCompletion ?? completionReceipt
                        }
                    };
                    UpdatePending(key);
                }
                if (keys.Length > 0) _shared.NoRecordCompletionRuns.Add(workflow.WorkflowExecutionId);
                RememberCompletion();
                return bucket.PendingRuns.GetValueOrDefault(workflow.WorkflowExecutionId) == 0;

                void RememberCompletion()
                {
                    _shared.SourceOwners.TryAdd(workflow.WorkflowExecutionId, (scope, workflow.HistoryCapturePolicy!));
                    _shared.NoRecordClosures.TryAdd(workflow.WorkflowExecutionId, completionReceipt);
                }
            }
        }, token), cancellationToken);
    }
}
