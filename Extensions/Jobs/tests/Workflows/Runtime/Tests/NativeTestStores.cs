using Elsa.Jobs.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Jobs.Workflows.Runtime.Tests;

internal sealed class JobTransport : IJobCommands, IJobQueries
{
    internal JobSnapshot? Snapshot { get; set; }
    internal JobContinuationPermission? Permission { get; set; }
    internal JobOriginSnapshot? Origin { get; set; }
    internal JobDeliveryStatusSnapshot? Delivery { get; set; }
    internal Exception? AcknowledgementFailureAfterCommit { get; set; }
    internal Func<JobScope, string, CancellationToken, ValueTask<JobSnapshot?>>? Find { get; set; }
    internal Func<JobScope, JobPageRequest, CancellationToken, ValueTask<JobPage<JobDeliverySnapshot>>>? PendingDeliveries { get; set; }
    internal TaskCompletionSource<JobAcknowledgeDeliveryRequest> Acknowledged { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal Exception? ReadFailure { get; set; }
    internal int ExactPermissionReads { get; private set; }
    internal List<JobAdmission> Admissions { get; } = [];
    internal List<JobCancelOriginRequest> Cancellations { get; } = [];
    internal List<JobAcknowledgeDeliveryRequest> Acknowledgements { get; } = [];
    public ValueTask<JobSnapshot?> FindAsync(JobScope scope, string jobId, CancellationToken cancellationToken = default)
    {
        if (Find is { } find) return find(scope, jobId, cancellationToken);
        if (ReadFailure is { } error) throw error;
        var snapshot = Snapshot;
        return ValueTask.FromResult(snapshot is not null && snapshot.Scope == scope && snapshot.JobId == jobId ? snapshot : null);
    }
    public ValueTask<JobContinuationPermission?> FindContinuationAsync(JobScope scope, string jobId, string continuationId, CancellationToken cancellationToken = default)
    {
        ExactPermissionReads++;
        if (ReadFailure is { } error) throw error;
        var snapshot = Snapshot; var permission = Permission;
        return ValueTask.FromResult(snapshot is not null && snapshot.Scope == scope && permission is not null && permission.JobId == jobId &&
            permission.ContinuationId == continuationId ? permission : null);
    }
    public ValueTask<JobAdmissionResult> AdmitAsync(JobAdmission admission, CancellationToken cancellationToken = default)
    {
        Admissions.Add(admission);
        var acceptance = new JobAcceptance(admission.ProposedJobId, NativeFixture.Now, NativeFixture.Now.AddDays(30), 1);
        if (admission.Origin is { } origin) Origin = new(origin, admission.RequestSha256, false, false, acceptance);
        return ValueTask.FromResult(new JobAdmissionResult(JobCommandStatus.Applied, acceptance));
    }
    public ValueTask<JobTransition> CancelOriginAsync(JobCancelOriginRequest request, CancellationToken cancellationToken = default)
    {
        Cancellations.Add(request);
        Origin = new(request.Origin, request.RequestSha256, true, false, Origin?.Acceptance);
        return ValueTask.FromResult(new JobTransition(JobCommandStatus.Applied, 1, null, null));
    }
    public ValueTask<JobOriginSnapshot?> FindOriginAsync(JobScope scope, JobOrigin origin, CancellationToken cancellationToken = default)
    {
        if (ReadFailure is { } error) throw error;
        var retained = Origin;
        return ValueTask.FromResult(retained is not null && scope == NativeFixture.Scope && retained.Origin == origin ? retained : null);
    }
    public ValueTask<JobDeliveryStatusSnapshot?> FindDeliveryAsync(JobScope scope, string jobId, CancellationToken cancellationToken = default)
    {
        if (ReadFailure is { } error) throw error;
        var delivery = Delivery;
        return ValueTask.FromResult(delivery is not null && scope == NativeFixture.Scope && delivery.JobId == jobId ? delivery : null);
    }
    public ValueTask<JobOperationAuditSnapshot?> FindOperationAuditAsync(JobScope scope, string operationId, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Unexpected test authority call: FindOperationAuditAsync.");
    public ValueTask<JobPage<JobOperationAuditSnapshot>> ListOperationAuditsAsync(JobScope scope, JobPageRequest page, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Unexpected test authority call: ListOperationAuditsAsync.");
    public ValueTask<JobPage<JobSnapshot>> ListJobsAsync(JobScope scope, JobListQuery query, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Unexpected test authority call: ListJobsAsync.");
    public ValueTask<JobPage<JobAttemptSnapshot>> ListAttemptsAsync(JobScope scope, string jobId, JobPageRequest page, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Unexpected test authority call: ListAttemptsAsync.");
    public ValueTask<JobPage<JobPhysicalReceipt>> ListPhysicalReceiptsAsync(JobScope scope, string jobId, JobPageRequest page, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Unexpected test authority call: ListPhysicalReceiptsAsync.");
    public ValueTask<JobPage<JobHold>> ListHoldsAsync(JobScope scope, JobGateKey? gate, JobPageRequest page, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Unexpected test authority call: ListHoldsAsync.");
    public ValueTask<JobPage<JobCapacity>> ListCapacityAsync(JobScope scope, JobPageRequest page, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Unexpected test authority call: ListCapacityAsync.");
    public ValueTask<JobScheduleSnapshot?> FindScheduleAsync(JobScope scope, string scheduleId, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Unexpected test authority call: FindScheduleAsync.");
    public ValueTask<JobPage<JobScheduleSnapshot>> ListSchedulesAsync(JobScope scope, JobPageRequest page, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Unexpected test authority call: ListSchedulesAsync.");
    public ValueTask<JobPage<JobDeliverySnapshot>> ListPendingDeliveriesAsync(JobScope scope, JobPageRequest page, CancellationToken cancellationToken = default) =>
        PendingDeliveries is { } pending ? pending(scope, page, cancellationToken) : throw new InvalidOperationException("Unexpected test authority call: ListPendingDeliveriesAsync.");
    public ValueTask<JobPage<JobQueueSnapshot>> ListQueuesAsync(JobScope scope, JobPageRequest page, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Unexpected test authority call: ListQueuesAsync.");
    public ValueTask<JobClaimResult> ClaimAsync(JobClaimRequest request, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Unexpected test authority call: ClaimAsync.");
    public ValueTask<JobRenewal> RenewAsync(JobAttemptLease lease, TimeSpan leaseDuration, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Unexpected test authority call: RenewAsync.");
    public ValueTask<JobTransition> RecordPhysicalStartAsync(JobAttemptLease lease, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Unexpected test authority call: RecordPhysicalStartAsync.");
    public ValueTask<JobTransition> CompleteAsync(JobAttemptLease lease, JobPhysicalReceipt receipt, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Unexpected test authority call: CompleteAsync.");
    public ValueTask<JobTransition> RecordPhysicalReceiptAsync(JobScope scope, string jobId, JobPhysicalReceipt receipt, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Unexpected test authority call: RecordPhysicalReceiptAsync.");
    public ValueTask<JobTransition> RecordProgressAsync(JobAttemptLease lease, JobProgress progress, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Unexpected test authority call: RecordProgressAsync.");
    public ValueTask<JobTransition> CancelAsync(JobCancelRequest request, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Unexpected test authority call: CancelAsync.");
    public ValueTask<JobAdmissionResult> RetryAsNewRunAsync(JobRetryAsNewRunRequest request, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Unexpected test authority call: RetryAsNewRunAsync.");
    public ValueTask<JobTransition> PauseAsync(JobPauseRequest request, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Unexpected test authority call: PauseAsync.");
    public ValueTask<JobTransition> ReleaseHoldAsync(JobReleaseHoldRequest request, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Unexpected test authority call: ReleaseHoldAsync.");
    public ValueTask<JobTransition> SetCapacityAsync(JobSetCapacityRequest request, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Unexpected test authority call: SetCapacityAsync.");
    public ValueTask<JobTransition> SaveScheduleAsync(JobSaveScheduleRequest request, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Unexpected test authority call: SaveScheduleAsync.");
    public ValueTask<JobTransition> SetScheduleEnabledAsync(JobSetScheduleEnabledRequest request, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Unexpected test authority call: SetScheduleEnabledAsync.");
    public ValueTask<JobMaterializationResult> MaterializeDueAsync(JobMaterializationRequest request, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Unexpected test authority call: MaterializeDueAsync.");
    public ValueTask<JobContinuationResult> GrantContinuationAsync(JobContinuationRequest request, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Unexpected test authority call: GrantContinuationAsync.");
    public ValueTask<JobTransition> AcknowledgeDeliveryAsync(JobAcknowledgeDeliveryRequest request, CancellationToken cancellationToken = default)
    {
        Acknowledgements.Add(request);
        Delivery = (Delivery ?? throw new InvalidOperationException("Test delivery is missing.")) with
            { Acknowledgement = new(request.ContinuationId, request.Proof) };
        Acknowledged.TrySetResult(request);
        if (AcknowledgementFailureAfterCommit is { } error) throw error;
        return ValueTask.FromResult(new JobTransition(JobCommandStatus.Applied, 4, request.JobId, Delivery.ResultId));
    }
}

internal sealed class ActivityStore : IActivityExecutionStateStore
{
    internal Dictionary<string, ActivityExecutionState> States { get; } = new(StringComparer.Ordinal);
    public ValueTask<ActivityExecutionState> SaveAsync(ActivityExecutionState state, CancellationToken cancellationToken = default)
    { States[state.InvocationId] = state; return ValueTask.FromResult(state); }
    public ValueTask<ActivityExecutionState?> FindAsync(string workflowExecutionId, string activityExecutionId, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(States.TryGetValue(activityExecutionId, out var state) && state.Execution.WorkflowExecutionId == workflowExecutionId ? state : null);
    public ValueTask<RuntimeStorePage<ActivityExecutionState>> ListPageAsync(ActivityExecutionStatePageQuery query, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(new RuntimeStorePage<ActivityExecutionState>(query, States.Values.Where(state => state.Execution.WorkflowExecutionId == query.WorkflowExecutionId).ToArray()));
    public ValueTask<RuntimeStorePage<ActivityExecutionState>> ListByParentPageAsync(ActivityExecutionStateParentPageQuery query, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(new RuntimeStorePage<ActivityExecutionState>(query, States.Values.Where(state => state.Execution.WorkflowExecutionId == query.WorkflowExecutionId && state.ParentActivityExecutionId == query.ParentActivityExecutionId).ToArray()));
}

internal sealed class WorkflowStore(WorkflowExecutionState state) : IWorkflowExecutionStateStore
{
    internal WorkflowExecutionState State { get; set; } = state;
    internal bool Missing { get; set; }
    public ValueTask<WorkflowExecutionState?> FindAsync(string workflowExecutionId, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<WorkflowExecutionState?>(!Missing && workflowExecutionId == State.WorkflowExecutionId ? State : null);
    public ValueTask<WorkflowExecutionState> SaveAsync(WorkflowExecutionState value, CancellationToken cancellationToken = default)
    { State = value; return ValueTask.FromResult(value); }
    public ValueTask<IReadOnlyCollection<string>> ListPinnedExecutableArtifactIdsAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult<IReadOnlyCollection<string>>([State.PinnedExecutable.ArtifactId]);
    public ValueTask<IReadOnlyCollection<WorkflowExecutionState>> ListAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult<IReadOnlyCollection<WorkflowExecutionState>>([State]);
    public ValueTask<WorkflowExecutionStatePage> QueryPageAsync(WorkflowExecutionStatePageQuery query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public ValueTask<bool> DeleteAsync(string workflowExecutionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}

internal sealed class BookmarkStore(BookmarkState bookmark) : IBookmarkStateStore
{
    public ValueTask<BookmarkState?> FindAsync(string workflowExecutionId, string bookmarkId, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<BookmarkState?>(bookmark.WorkflowExecutionId == workflowExecutionId && bookmark.BookmarkId == bookmarkId ? bookmark : null);
    public ValueTask<BookmarkState> SaveAsync(BookmarkState state, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public ValueTask<bool> DeleteAsync(string workflowExecutionId, string bookmarkId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public ValueTask<RuntimeStorePage<BookmarkState>> ListPageAsync(BookmarkStatePageQuery query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}

internal sealed class Access : IPersistenceAccessContextAccessor
{
    public PersistenceAccessContext Current => PersistenceAccessContext.Scoped(new PersistenceScope(NativeFixture.Scope.PersistencePartition));
}
