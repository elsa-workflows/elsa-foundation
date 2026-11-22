using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Elsa.Workflows.Runtime.Services.Triggers;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

internal sealed partial class EfRuntimeIngressDeliveryStore
{
    public ValueTask<RuntimeCommittedIngressWait> ReadCommittedWaitAsync(RuntimeIngressWaitOrigin origin, CancellationToken cancellationToken = default) =>
        EfTriggerBoundary.RunAsync(() => ReadCommittedWaitAsyncCore(origin, cancellationToken), origin?.Scope.Value, origin?.SourceOutboxItemId);

    private void RequireWaitBoundary()
    {
        var scope = access.Current.RequireScope();
        if (waitEvidence is null || resumeResolver is null || checkpoints is null || evidence is null ||
            !ReferenceEquals(waitEvidence.PhysicalQueue, evidence.PhysicalQueue) ||
            waitEvidence.PhysicalExecutions is not EfWorkflowExecutionStateStore owner || !ReferenceEquals(owner.Context, context) || owner.AccessContext.RequireScope() != scope ||
            waitEvidence.PhysicalBookmarks is not EfBookmarkStateStore bookmarks || !ReferenceEquals(bookmarks.Context, context) || bookmarks.AccessContext.RequireScope() != scope ||
            waitEvidence.PhysicalActivities is not EfActivityExecutionStateStore activities || !ReferenceEquals(activities.Context, context) || activities.AccessContext.RequireScope() != scope ||
            !checkpoints.CanCommitExactBookmarkResume(bookmarks, waitEvidence.PhysicalExecutions, activities))
            throw new NotSupportedException("Ordinary waits require the actual shared EF bookmark, owner, activity, queue and exact-checkpoint boundary.");
    }

    private async ValueTask<RuntimeIngressWaitDelivery?> PrepareWaitsAsync(RuntimeIngressAdmissionRequest request, CancellationToken token)
    {
        if (request.WaitCandidate is null) return null;
        RequireWaitBoundary();
        return await RuntimeIngressWaitCapture.ReadAsync(request.Candidate.StandaloneIngress!, request.WaitCandidate,
            (IBookmarkStimulusIndex)waitEvidence!.PhysicalBookmarks, waitEvidence.PhysicalBookmarks.ObserveAsync,
            executions, waitEvidence.PhysicalActivities, executables, resumeResolver, time, token);
    }

    private async ValueTask<BookmarkStateObservation?> ObserveWaitAsync(string executionId, string bookmarkId, CancellationToken token)
    {
        var scope = access.Current.RequireScope();
        var id = EfBookmarkStateStore.CreateId(scope.Value, executionId, bookmarkId);
        var row = await context.Bookmarks.AsNoTracking().SingleOrDefaultAsync(row => row.Id == id, token);
        if (row is null) return null;
        var state = EfBookmarkStateStore.MapChecked(row, scope.Value, executionId, bookmarkId, id);
        if (context.Database.CurrentTransaction is not null && await context.Bookmarks.Where(current => current.Id == id &&
                current.Revision == row.Revision && current.ContentJson == row.ContentJson && current.SchemaVersion == row.SchemaVersion)
            .ExecuteUpdateAsync(update => update.SetProperty(current => current.Revision, current => current.Revision), token) != 1)
            throw new TriggerOperationException("conflict", "An ordinary wait changed before its physical source boundary.");
        return new(state, row.Revision, scope);
    }

    private async ValueTask FenceWaitsAsync(RuntimeIngressAdmissionRequest request, CancellationToken token)
    {
        if (request.Candidate.SubscriptionDelivery!.WaitDelivery is not { } expected) return;
        RequireWaitBoundary();
        // The indexed stimulus range read is part of this SERIALIZABLE transaction, including an empty range.
        var current = await RuntimeIngressWaitCapture.ReadAsync(request.Candidate.StandaloneIngress!, expected.Candidate,
            (IBookmarkStimulusIndex)waitEvidence!.PhysicalBookmarks, ObserveWaitAsync, executions, waitEvidence.PhysicalActivities,
            executables, null, time, token);
        RuntimeIngressWaitCapture.Compare(expected, current);
        foreach (var recipient in expected.Recipients)
        {
            var scope = request.Scope.Persistence.Value;
            var ownerId = EfWorkflowExecutionStateStore.CreateIdForAtomicParticipant(scope, recipient.Identity.WorkflowExecutionId);
            var owner = await context.WorkflowExecutionStates.AsNoTracking().SingleOrDefaultAsync(row => row.Id == ownerId, token) ??
                throw new TriggerOperationException("conflict", "An ordinary wait owner disappeared before source acceptance.");
            _ = EfWorkflowExecutionStateStore.ReadChecked(owner, scope, recipient.Identity.WorkflowExecutionId);
            if (await context.WorkflowExecutionStates.Where(row => row.Id == ownerId && row.Revision == owner.Revision &&
                    row.ContentJson == owner.ContentJson && row.SchemaVersion == owner.SchemaVersion)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.Revision, row => row.Revision), token) != 1)
                throw new TriggerOperationException("conflict", "An ordinary wait owner changed before source acceptance.");
            var activityId = ActivityExecutionEfSupport.CreateId("state", scope, recipient.Identity.WorkflowExecutionId, recipient.Expected.ActivityExecutionId);
            var activity = await context.ActivityExecutionStates.AsNoTracking().SingleOrDefaultAsync(row => row.Id == activityId, token) ??
                throw new TriggerOperationException("conflict", "An ordinary wait activity disappeared before source acceptance.");
            _ = EfActivityExecutionStateStore.ReadChecked(activity, scope, recipient.Identity.WorkflowExecutionId, recipient.Expected.ActivityExecutionId);
            if (await context.ActivityExecutionStates.Where(row => row.Id == activityId && row.Revision == activity.Revision &&
                    row.ContentJson == activity.ContentJson && row.SchemaVersion == activity.SchemaVersion)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.Revision, row => row.Revision), token) != 1)
                throw new TriggerOperationException("conflict", "An ordinary wait activity changed before source acceptance.");
            await ((EfRuntimePostCommitOutboxStore)outbox).FenceExecutionDependencyAsync(scope, recipient.Identity.WorkflowExecutionId, token);
            if (await executions.IsDeletionTombstonedAsync(recipient.Identity.WorkflowExecutionId, token))
                throw new InvalidDataException("An ordinary wait owner was retired before source acceptance.");
        }
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
            return item.WithSubscriptionDelivery(item.SubscriptionDelivery.WithWaitDelivery(waits.WithProgress(
                waits.Progress.Single(progress => progress.WaitId == seal.Identity.WaitId).CommitDispatch(seal))));
        }, cancellationToken);
    }

    private async ValueTask<RuntimeCommittedIngressWait> ReadCommittedWaitAsyncCore(RuntimeIngressWaitOrigin origin, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(origin);
        RequireScope(origin.Scope, cancellationToken); RequireOwnedBoundary();
        var row = await LoadAsync(origin.Scope, origin.SourceOutboxItemId, false, cancellationToken) ??
            throw new InvalidDataException("The physical accepted wait source is missing.");
        return RuntimeIngressAdmissionValidation.CommittedWait(Read(row, origin.Scope, origin.SourceOutboxItemId), origin);
    }

    public ValueTask<RuntimePostCommitOutboxItem> RecordWaitReceiptAsync(RuntimeDeliverySourceContext source, RuntimeIngressWaitReceipt receipt,
        CancellationToken cancellationToken = default)
    {
        RequireScope(source.Scope, cancellationToken); RequireOwnedBoundary(); RequireWaitBoundary();
        return ChangeObservedAsync(source.Scope, source.SourceOutboxItemId, async (current, token) =>
        {
            RuntimeIngressAdmissionValidation.CheckClaim(current, source, time.GetUtcNow());
            var target = RuntimeIngressAdmissionValidation.WaitReceiptTarget(current, receipt);
            if (current.SubscriptionDelivery!.WaitDelivery!.Progress.Single(progress => progress.WaitId == receipt.WaitId).Receipt is not null) return current;
            var observed = await waitEvidence!.ReconcileAsync(current, target.Recipient, target.Seal, token, ObserveWaitAsync) ??
                throw new TriggerOperationException("unavailable", "The ordinary wait has no observed physical resume or positive retirement; delivery remains owed.");
            return RuntimeIngressAdmissionValidation.AcceptObservedWait(current, observed);
        }, cancellationToken, source);
    }

    public ValueTask<RuntimePostCommitOutboxItem> RecordWaitFailureAsync(RuntimeDeliverySourceContext source, string waitId,
        RuntimeRecipientRecoveryState recoveryState, string code, DateTimeOffset? retryAt, CancellationToken cancellationToken = default) =>
        MutateAsync(source, item => RuntimeIngressAdmissionValidation.RecordWaitFailure(item, waitId, recoveryState, code, retryAt), cancellationToken);
}
