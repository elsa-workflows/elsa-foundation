using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Elsa.Workflows.Runtime.Services.ActivityExecutions;
using Elsa.Workflows.Runtime.Services.Bookmarks;

namespace Elsa.Workflows.Runtime.Services.Triggers;

internal sealed partial class InMemoryRuntimeIngressDeliveryStore
{
    private void RequireWaitBoundary()
    {
        if (waitEvidence is null || resumeResolver is null || checkpoints is null || evidence is null ||
            !ReferenceEquals(waitEvidence.PhysicalQueue, evidence.PhysicalQueue) || !ReferenceEquals(waitEvidence.PhysicalExecutions, executions) ||
            waitEvidence.PhysicalBookmarks is not InMemoryBookmarkStateStore bookmarks || !bookmarks.Coordinates(State) ||
            waitEvidence.PhysicalActivities is not InMemoryActivityExecutionStateStore activities || !activities.Coordinates(State) ||
            !checkpoints.CanCommitExactBookmarkResume(bookmarks, executions, activities))
            throw new NotSupportedException("Ordinary waits require the actual shared native bookmark, owner, activity, queue and exact-checkpoint boundary.");
    }

    private ValueTask<RuntimeIngressWaitDelivery?> PrepareWaitsAsync(RuntimeIngressAdmissionRequest request, CancellationToken token)
    {
        if (request.WaitCandidate is null) return ValueTask.FromResult<RuntimeIngressWaitDelivery?>(null);
        RequireWaitBoundary();
        return PrepareAsync();
        async ValueTask<RuntimeIngressWaitDelivery?> PrepareAsync() => await RuntimeIngressWaitCapture.ReadAsync(request.Candidate.StandaloneIngress!,
            request.WaitCandidate, (IBookmarkStimulusIndex)waitEvidence!.PhysicalBookmarks, waitEvidence.PhysicalBookmarks.ObserveAsync,
            executions, waitEvidence.PhysicalActivities, executables, resumeResolver, time, token);
    }

    private async ValueTask FenceWaitsAsync(RuntimeIngressAdmissionRequest request, CancellationToken token)
    {
        if (request.Candidate.SubscriptionDelivery!.WaitDelivery is not { } expected) return;
        RequireWaitBoundary();
        var current = await RuntimeIngressWaitCapture.ReadAsync(request.Candidate.StandaloneIngress!, expected.Candidate,
            (IBookmarkStimulusIndex)waitEvidence!.PhysicalBookmarks, waitEvidence.PhysicalBookmarks.ObserveAsync,
            executions, waitEvidence.PhysicalActivities, executables, null, time, token);
        RuntimeIngressWaitCapture.Compare(expected, current);
        foreach (var recipient in expected.Recipients)
            if (await executions.IsDeletionTombstonedAsync(recipient.Identity.WorkflowExecutionId, token))
                throw new InvalidDataException("An ordinary wait owner was retired before source acceptance.");
    }

    public ValueTask<RuntimePostCommitOutboxItem> CommitWaitDispatchAsync(RuntimeDeliverySourceContext source, RuntimeIngressWaitDispatchSeal seal,
        CancellationToken cancellationToken = default)
    {
        RequireWaitBoundary();
        return MutateAsync(source, item =>
        {
            var target = RuntimeIngressAdmissionValidation.ProposedWait(item, seal);
            if (seal.CommittedAt > time.GetUtcNow()) throw new InvalidDataException("The wait commitment cannot be in the future.");
            RuntimeIngressWaitCommand.ValidateEnvelope(target, RuntimeIngressWaitCommand.ReadEnvelope(seal));
            var waits = item.SubscriptionDelivery!.WaitDelivery!;
            var progress = waits.Progress.Single(progress => progress.WaitId == seal.Identity.WaitId).CommitDispatch(seal);
            return item.WithSubscriptionDelivery(item.SubscriptionDelivery.WithWaitDelivery(waits.WithProgress(progress)));
        }, cancellationToken);
    }

    public ValueTask<RuntimeCommittedIngressWait> ReadCommittedWaitAsync(RuntimeIngressWaitOrigin origin, CancellationToken cancellationToken = default)
    {
        RequireScope(origin.Scope, cancellationToken);
        lock (State.SyncRoot) return ValueTask.FromResult(RuntimeIngressAdmissionValidation.CommittedWait(Find(origin.SourceOutboxItemId), origin));
    }

    public async ValueTask<RuntimePostCommitOutboxItem> RecordWaitReceiptAsync(RuntimeDeliverySourceContext source, RuntimeIngressWaitReceipt receipt,
        CancellationToken cancellationToken = default)
    {
        RequireScope(source.Scope, cancellationToken); RequireWaitBoundary();
        return await State.ExecuteWriteGateAsync(async token =>
        {
            RuntimeCommittedIngressWait target;
            lock (State.SyncRoot)
            {
                var current = RuntimeIngressAdmissionValidation.CheckClaim(Find(source.SourceOutboxItemId), source, time.GetUtcNow());
                target = RuntimeIngressAdmissionValidation.WaitReceiptTarget(current, receipt);
                if (current.SubscriptionDelivery!.WaitDelivery!.Progress.Single(progress => progress.WaitId == receipt.WaitId).Receipt is not null) return current;
            }
            var observed = await waitEvidence!.ReconcileAsync(target.Item, target.Recipient, target.Seal, token) ??
                throw new TriggerOperationException("unavailable", "The ordinary wait has no observed physical resume or positive retirement; delivery remains owed.");
            lock (State.SyncRoot)
            {
                var current = RuntimeIngressAdmissionValidation.CheckClaim(Find(source.SourceOutboxItemId), source, time.GetUtcNow());
                var replacement = RuntimeIngressAdmissionValidation.AcceptObservedWait(current, observed);
                State.OutboxItems[current.OutboxItemId] = replacement;
                return replacement;
            }
        }, cancellationToken);
    }

    public ValueTask<RuntimePostCommitOutboxItem> RecordWaitFailureAsync(RuntimeDeliverySourceContext source, string waitId,
        RuntimeRecipientRecoveryState recoveryState, string code, DateTimeOffset? retryAt, CancellationToken cancellationToken = default) =>
        MutateAsync(source, item => RuntimeIngressAdmissionValidation.RecordWaitFailure(item, waitId, recoveryState, code, retryAt), cancellationToken);
}
