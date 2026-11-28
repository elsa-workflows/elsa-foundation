using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Elsa.Workflows.Runtime.Services.Triggers;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

internal sealed class EfLegacyRuntimeIngress(
    RuntimeDbContext context,
    IStandaloneTriggerStore resources,
    IRuntimePostCommitOutboxStore outbox,
    IWorkflowExecutableStore executables,
    IWorkflowExecutionStateStore executions,
    IPersistenceAccessContextAccessor access,
    TimeProvider time,
    NativeIngressTargetEvidence evidence,
    IRuntimeModelExecutionReader? modelInputs = null,
    IEnumerable<IStimulusDeliveryIntentAdapter>? stimulusAdapters = null,
    NativeIngressWaitTargetEvidence? waitEvidence = null,
    IBookmarkResumeResolver? resumeResolver = null,
    IRuntimeCheckpointCommitStore? checkpoints = null) : IEfLegacyRuntimeIngress
{
    public IWorkflowExecutionStateStore Executions => executions;

    public bool OwnsStartEvidence(RuntimeDbContext expectedContext, IStandaloneTriggerStore expectedResources,
        IRuntimePostCommitOutboxStore expectedOutbox, IWorkflowExecutableStore expectedExecutables,
        IPersistenceAccessContextAccessor expectedAccess, TimeProvider expectedTime) =>
        ReferenceEquals(context, expectedContext) && ReferenceEquals(resources, expectedResources) &&
        ReferenceEquals(outbox, expectedOutbox) && ReferenceEquals(executables, expectedExecutables) &&
        ReferenceEquals(access, expectedAccess) && ReferenceEquals(time, expectedTime) &&
        evidence.PhysicalQueue is EfSchedulerWorkQueueStore queue && ReferenceEquals(queue.Context, context) &&
        evidence.PhysicalExecutions is EfWorkflowExecutionStateStore owner && ReferenceEquals(owner.Context, context);

    public bool OwnsModelInputReader =>
        modelInputs is EfRuntimeModelExecutionReader reader && reader.Uses(context, access);

    public void ValidateDispatch(RuntimeCommittedRecipient target, WorkflowExecutable? native)
    {
        if (target.Recipient.ModelProtocol is null)
        {
            NativeIngressTargetEvidence.ValidateEnvelope(target, NativeIngressTargetEvidence.ReadEnvelope(target.Seal));
            NativeIngressTargetEvidence.ValidateExecutable(target, native ?? throw new InvalidDataException("Accepted target material is missing."));
        }
        else
        {
            RequireModelInputReader();
            _ = RuntimeIngressModelTargetEvidence.ReadCommand(target);
        }
    }

    public ValueTask<RuntimeTargetAcceptanceReceipt?> ObserveDispatchAsync(RuntimeCommittedRecipient target, CancellationToken token)
    {
        if (target.Recipient.ModelProtocol is null)
            return evidence.ReconcileAsync(target.Item, target.Recipient, target.Seal, token);
        RequireModelInputReader();
        return RuntimeIngressModelTargetEvidence.ReconcileAsync(target, evidence.PhysicalQueue, modelInputs!, time, token);
    }

    private void RequireModelInputReader()
    {
        if (!OwnsModelInputReader)
            throw new NotSupportedException("Model ingress requires the actual selected EF model input receipt reader.");
    }

    public void RequireWaitBoundary()
    {
        var scope = access.Current.RequireScope();
        if (waitEvidence is null || resumeResolver is null || checkpoints is null ||
            !ReferenceEquals(waitEvidence.PhysicalQueue, evidence.PhysicalQueue) ||
            waitEvidence.PhysicalExecutions is not EfWorkflowExecutionStateStore owner || !ReferenceEquals(owner.Context, context) || owner.AccessContext.RequireScope() != scope ||
            waitEvidence.PhysicalBookmarks is not EfBookmarkStateStore bookmarks || !ReferenceEquals(bookmarks.Context, context) || bookmarks.AccessContext.RequireScope() != scope ||
            waitEvidence.PhysicalActivities is not EfActivityExecutionStateStore activities || !ReferenceEquals(activities.Context, context) || activities.AccessContext.RequireScope() != scope ||
            !checkpoints.CanCommitExactBookmarkResume(bookmarks, waitEvidence.PhysicalExecutions, activities))
            throw new NotSupportedException("Ordinary waits require the actual shared EF bookmark, owner, activity, queue and exact-checkpoint boundary.");
    }

    public ValueTask<RuntimeIngressWaitDelivery> CaptureWaitsAsync(RuntimeIngressAdmissionRequest request, CancellationToken token)
    {
        RequireWaitBoundary();
        return RuntimeIngressWaitCapture.ReadAsync(request.Candidate.StandaloneIngress!, request.WaitCandidate!,
            (IBookmarkStimulusIndex)waitEvidence!.PhysicalBookmarks, waitEvidence.PhysicalBookmarks.ObserveAsync,
            executions, waitEvidence.PhysicalActivities, executables, resumeResolver, time, token);
    }

    public async ValueTask FenceWaitsAsync(RuntimeIngressAdmissionRequest request, RuntimeIngressWaitDelivery expected, CancellationToken token)
    {
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

    public ValueTask<RuntimeIngressWaitReceipt?> ObserveWaitAsync(RuntimeCommittedIngressWait target, CancellationToken token) =>
        waitEvidence!.ReconcileAsync(target.Item, target.Recipient, target.Seal, token, ObserveWaitAsync);

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

    public bool SupportsWorkflowCohort(string intentKind) =>
        new StimulusDeliveryRequestProjector(stimulusAdapters ?? []).Owns(intentKind);

    public async ValueTask<PostCommitDeliveryContext> InstallWorkflowCohortAsync(PostCommitDeliveryContext source,
        StimulusDeliverySnapshot ordinary, RuntimeWorkflowSubscriptionCandidate subscriptions, CancellationToken token)
    {
        var projector = new StimulusDeliveryRequestProjector(stimulusAdapters ?? []);
        var request = projector.Read(source);
        var expected = request.Mode == StimulusRoutingMode.ResumeOnly ? null : projector.SubscriptionSource(source);
        RuntimeWorkflowSubscriptionValidation.CheckCandidate(source, ordinary, subscriptions, expected);
        if (subscriptions.Delivery.Recipients.Any(recipient => recipient.ModelProtocol is not null) &&
            !OwnsModelInputReader)
            throw new NotSupportedException("Independent model starts require the actual selected model receipt boundary.");
        _ = await ((EfRuntimePostCommitOutboxStore)outbox).InstallCombinedAsync(source, ordinary, subscriptions, resources, token);
        return await ((IStimulusDeliveryStore)outbox).ReadSourceAsync(source, token);
    }
}
