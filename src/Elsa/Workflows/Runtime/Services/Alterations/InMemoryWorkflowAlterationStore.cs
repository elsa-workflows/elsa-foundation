using System.Security.Cryptography;
using System.Text;
using Elsa.Workflows.Runtime.Core.Contracts.Alterations;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Models.Alterations;
using Elsa.Workflows.Runtime.Core.Services;

namespace Elsa.Workflows.Runtime.Services.Alterations;

/// <summary>Shared in-memory durability state. A production provider replaces this as one durable storage unit.</summary>
public sealed class InMemoryWorkflowAlterationStoreState : IInMemoryCheckpointTransactionParticipant
{
    public InMemoryCheckpointParticipantGate TransactionGate { get; } = new();
    public bool IsAffected(InMemoryCheckpointMutationPlan plan) => plan.AlterationJobId is not null;
    internal object SyncRoot { get; } = new();
    internal Dictionary<string, WorkflowAlterationPlanState> Plans { get; } = new(StringComparer.Ordinal);
    internal Dictionary<string, string> ActivePlanOrderKeys { get; } = new(StringComparer.Ordinal);
    internal Dictionary<string, WorkflowAlterationJobState> Jobs { get; } = new(StringComparer.Ordinal);
    internal Dictionary<string, WorkflowAlterationJobCounts> JobCounts { get; } = new(StringComparer.Ordinal);
    internal Dictionary<string, SortedDictionary<long, string>> JobIdsByPlan { get; } = new(StringComparer.Ordinal);
    internal Dictionary<string, SortedSet<(DateTimeOffset ClaimableAt, string JobId)>> ClaimableJobsByPlan { get; } = new(StringComparer.Ordinal);
    internal Dictionary<string, SortedSet<(DateTimeOffset ClaimableAt, string JobId)>> RunningJobsByPlan { get; } = new(StringComparer.Ordinal);
    internal Dictionary<string, InMemoryUnsealedCaptureCleanup> UnsealedCaptureCleanups { get; } = new(StringComparer.Ordinal);
    internal Dictionary<string, string> PlansByTenantIdempotency { get; } = new(StringComparer.Ordinal);

    object IInMemoryCheckpointTransactionParticipant.CaptureCheckpointState(InMemoryCheckpointMutationPlan scope)
    {
        lock (SyncRoot)
        {
            if (scope.AlterationJobId is null || !Jobs.TryGetValue(scope.AlterationJobId, out var job))
                return CheckpointSnapshot.Empty;
            return new CheckpointSnapshot(
                job.JobId,
                job.PlanId,
                job,
                JobCounts[job.PlanId],
                new SortedSet<(DateTimeOffset, string)>(ClaimableJobsByPlan[job.PlanId], ClaimableJobsByPlan[job.PlanId].Comparer),
                new SortedSet<(DateTimeOffset, string)>(RunningJobsByPlan[job.PlanId], RunningJobsByPlan[job.PlanId].Comparer));
        }
    }

    void IInMemoryCheckpointTransactionParticipant.RestoreCheckpointState(object snapshot)
    {
        var checkpoint = (CheckpointSnapshot)snapshot;
        if (checkpoint.JobId is null)
            return;
        var planId = checkpoint.PlanId!;
        var claimable = checkpoint.Claimable!;
        var running = checkpoint.Running!;
        lock (SyncRoot)
        {
            Jobs[checkpoint.JobId] = checkpoint.Job!;
            JobCounts[planId] = checkpoint.Counts!;
            ClaimableJobsByPlan[planId] = new(claimable, claimable.Comparer);
            RunningJobsByPlan[planId] = new(running, running.Comparer);
        }
    }

    private sealed record CheckpointSnapshot(
        string? JobId,
        string? PlanId,
        WorkflowAlterationJobState? Job,
        WorkflowAlterationJobCounts? Counts,
        SortedSet<(DateTimeOffset ClaimableAt, string JobId)>? Claimable,
        SortedSet<(DateTimeOffset ClaimableAt, string JobId)>? Running)
    {
        public static CheckpointSnapshot Empty { get; } = new(null, null, null, null, null, null);
    }
}

internal sealed record InMemoryUnsealedCaptureCleanup(
    WorkflowAlterationPlanStatus TerminalStatus,
    WorkflowAlterationSafeFailure? SafeFailure,
    DateTimeOffset CompletedAt);

/// <summary>In-memory conformance implementation for plan admission, capture, leasing, cancellation, and reconciliation.</summary>
public sealed class InMemoryWorkflowAlterationStore(InMemoryWorkflowAlterationStoreState? state = null) : IWorkflowAlterationStore, IInMemoryCheckpointTransactionSource
{
    private const int UnsealedCleanupPageSize = 100;
    private readonly InMemoryWorkflowAlterationStoreState _state = state ?? new();

    IEnumerable<object?> IInMemoryCheckpointTransactionSource.GetCheckpointTransactionParticipants() => [_state];

    public ValueTask<WorkflowAlterationPlanAdmissionResult> AdmitAsync(WorkflowAlterationPlanState plan, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        cancellationToken.ThrowIfCancellationRequested();
        using var checkpointAccess = _state.TransactionGate.Enter();
        lock (_state.SyncRoot)
        {
            var key = IdempotencyKey(plan.AuthorityScope.TenantPartition, plan.IdempotencyKeyHash);
            if (_state.PlansByTenantIdempotency.TryGetValue(key, out var existingId))
            {
                var existing = _state.Plans[existingId];
                if (!StringComparer.Ordinal.Equals(existing.CanonicalRequestHash, plan.CanonicalRequestHash))
                    throw new WorkflowAlterationIdempotencyConflictException(existing.PlanId);
                return ValueTask.FromResult(new WorkflowAlterationPlanAdmissionResult(existing, true));
            }

            if (_state.Plans.ContainsKey(plan.PlanId))
                throw new InvalidOperationException($"Alteration plan '{plan.PlanId}' already exists.");
            _state.Plans.Add(plan.PlanId, plan);
            _state.ActivePlanOrderKeys.Add(plan.PlanId, CreateActivePlanOrderKey(plan.CreatedAt, plan.PlanId));
            _state.JobCounts.Add(plan.PlanId, EmptyJobCounts);
            _state.JobIdsByPlan.Add(plan.PlanId, new());
            _state.ClaimableJobsByPlan.Add(plan.PlanId, new(ClaimableJobComparer));
            _state.RunningJobsByPlan.Add(plan.PlanId, new(ClaimableJobComparer));
            _state.PlansByTenantIdempotency.Add(key, plan.PlanId);
            return ValueTask.FromResult(new WorkflowAlterationPlanAdmissionResult(plan, false));
        }
    }

    public ValueTask<WorkflowAlterationPlanState?> FindPlanAsync(string planId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(planId);
        cancellationToken.ThrowIfCancellationRequested();
        using var checkpointAccess = _state.TransactionGate.Enter();
        lock (_state.SyncRoot)
            return ValueTask.FromResult(_state.Plans.GetValueOrDefault(planId));
    }

    public ValueTask<WorkflowAlterationActivePlanPage> ListActivePlansAsync(int pageSize, string? cursor = null, CancellationToken cancellationToken = default)
    {
        if (pageSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(pageSize));
        if (cursor is not null && string.IsNullOrWhiteSpace(cursor))
            throw new ArgumentException("The active alteration-plan cursor cannot be blank.", nameof(cursor));
        cancellationToken.ThrowIfCancellationRequested();

        using var checkpointAccess = _state.TransactionGate.Enter();

        lock (_state.SyncRoot)
        {
            var items = _state.Plans.Values
                .Where(plan => !IsTerminal(plan.Status))
                .OrderBy(plan => _state.ActivePlanOrderKeys[plan.PlanId], StringComparer.Ordinal)
                .Where(plan => cursor is null || StringComparer.Ordinal.Compare(_state.ActivePlanOrderKeys[plan.PlanId], cursor) > 0)
                .Take(checked(pageSize + 1))
                .ToArray();
            var hasNext = items.Length > pageSize;
            if (hasNext)
                items = items[..pageSize];
            return ValueTask.FromResult(new WorkflowAlterationActivePlanPage(items, hasNext ? _state.ActivePlanOrderKeys[items[^1].PlanId] : null, hasNext));
        }
    }

    public ValueTask RescheduleActivePlanAsync(string planId, DateTimeOffset servicedAt, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(planId);
        cancellationToken.ThrowIfCancellationRequested();
        using var checkpointAccess = _state.TransactionGate.Enter();
        lock (_state.SyncRoot)
        {
            var plan = GetPlan(planId);
            if (!IsTerminal(plan.Status))
                _state.ActivePlanOrderKeys[planId] = NextActivePlanOrderKey(_state.ActivePlanOrderKeys[planId], servicedAt, planId);
        }
        return ValueTask.CompletedTask;
    }

    public ValueTask<WorkflowAlterationPlanState> CaptureAsync(string planId, long expectedRevision, IReadOnlyCollection<WorkflowAlterationCapturedTarget> targets, string? nextCursor, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(planId);
        ArgumentNullException.ThrowIfNull(targets);
        cancellationToken.ThrowIfCancellationRequested();
        using var checkpointAccess = _state.TransactionGate.Enter();
        lock (_state.SyncRoot)
        {
            var plan = GetPlan(planId);
            EnsureRevision(plan, expectedRevision);
            if (plan.Status != WorkflowAlterationPlanStatus.CapturingTargets)
                throw new InvalidOperationException("Only a capturing alteration plan can accept target pages.");

            var ordinal = plan.CapturedSoFar;
            foreach (var target in targets.OrderBy(target => target.WorkflowExecutionId, StringComparer.Ordinal))
            {
                if (!StringComparer.Ordinal.Equals(target.TenantPartition, plan.AuthorityScope.TenantPartition))
                    throw new InvalidOperationException("A captured target must belong to the plan tenant partition.");
                var jobId = WorkflowAlterationIdentity.CreateJobId(plan.PlanId, target.WorkflowExecutionId);
                if (_state.Jobs.ContainsKey(jobId))
                    continue;
                var missing = target.SafeFailure is not null;
                var job = new WorkflowAlterationJobState(
                    jobId, plan.PlanId, target.WorkflowExecutionId, target.TenantPartition, ordinal++,
                    missing ? WorkflowAlterationJobStatus.Failed : WorkflowAlterationJobStatus.Pending, null, 0, [], null, target.SafeFailure, plan.CreatedAt, null, missing ? plan.CreatedAt : null, 0,
                    target.CapturedConcurrency);
                _state.Jobs.Add(jobId, job);
                _state.JobIdsByPlan[planId].Add(job.CaptureOrdinal, job.JobId);
                AddClaimableJob(job);
                AdjustJobCounts(planId, null, job.Status);
            }

            var updated = CopyPlan(plan, changeCaptureCursor: true, captureCursor: nextCursor, capturedSoFar: ordinal, revision: plan.Revision + 1);
            _state.Plans[planId] = updated;
            return ValueTask.FromResult(updated);
        }
    }

    public ValueTask<WorkflowAlterationPlanState> SealAsync(string planId, long expectedRevision, DateTimeOffset sealedAt, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var checkpointAccess = _state.TransactionGate.Enter();
        lock (_state.SyncRoot)
        {
            var plan = GetPlan(planId);
            EnsureRevision(plan, expectedRevision);
            if (_state.UnsealedCaptureCleanups.ContainsKey(planId))
                return ValueTask.FromResult(plan);
            if (plan.Status == WorkflowAlterationPlanStatus.Cancelling)
            {
                var captured = _state.JobCounts[planId].Total;
                var sealedCancelling = CopyPlan(plan, targetCount: captured, sealedAt: sealedAt, revision: plan.Revision + 1);
                _state.Plans[planId] = sealedCancelling;
                return ValueTask.FromResult(sealedCancelling);
            }
            if (plan.Status != WorkflowAlterationPlanStatus.CapturingTargets)
                throw new InvalidOperationException("Only a capturing alteration plan can be sealed.");

            var targetCount = _state.JobCounts[planId].Total;
            var status = targetCount == 0 ? WorkflowAlterationPlanStatus.Completed : WorkflowAlterationPlanStatus.Queued;
            var sealedPlan = CopyPlan(plan, status: status, targetCount: targetCount, sealedAt: sealedAt, completedAt: targetCount == 0 ? sealedAt : null, revision: plan.Revision + 1);
            _state.Plans[planId] = sealedPlan;
            return ValueTask.FromResult(sealedPlan);
        }
    }

    public ValueTask<WorkflowAlterationPlanState> CancelUnsealedCaptureAsync(string planId, DateTimeOffset cancelledAt, CancellationToken cancellationToken = default) =>
        TerminalizeUnsealedCapture(planId, WorkflowAlterationPlanStatus.Cancelled, null, cancelledAt, cancellationToken);

    public ValueTask<WorkflowAlterationPlanState> FailUnsealedCaptureAsync(string planId, WorkflowAlterationSafeFailure safeFailure, DateTimeOffset failedAt, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(safeFailure);
        return TerminalizeUnsealedCapture(planId, WorkflowAlterationPlanStatus.Failed, safeFailure, failedAt, cancellationToken);
    }

    public ValueTask<WorkflowAlterationPlanState> RequestCancellationAsync(string planId, DateTimeOffset requestedAt, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var checkpointAccess = _state.TransactionGate.Enter();
        lock (_state.SyncRoot)
        {
            var plan = GetPlan(planId);
            if (IsTerminal(plan.Status))
                return ValueTask.FromResult(plan);
            var updated = CopyPlan(plan, status: WorkflowAlterationPlanStatus.Cancelling, cancellationRequestedAt: requestedAt, revision: plan.Revision + 1);
            _state.Plans[planId] = updated;
            return ValueTask.FromResult(updated);
        }
    }

    private ValueTask<WorkflowAlterationPlanState> TerminalizeUnsealedCapture(
        string planId,
        WorkflowAlterationPlanStatus terminalStatus,
        WorkflowAlterationSafeFailure? safeFailure,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var checkpointAccess = _state.TransactionGate.Enter();
        lock (_state.SyncRoot)
        {
            var plan = GetPlan(planId);
            if (plan.Status is not (WorkflowAlterationPlanStatus.CapturingTargets or WorkflowAlterationPlanStatus.Cancelling) || plan.SealedAt is not null)
                return ValueTask.FromResult(plan);

            if (!_state.UnsealedCaptureCleanups.TryGetValue(planId, out var cleanup))
            {
                cleanup = plan.Status == WorkflowAlterationPlanStatus.Cancelling && plan.CancellationRequestedAt is { } requestedAt
                    ? new(WorkflowAlterationPlanStatus.Cancelled, null, requestedAt)
                    : new(terminalStatus, safeFailure, completedAt);
                _state.UnsealedCaptureCleanups.Add(planId, cleanup);
            }

            foreach (var entry in _state.JobIdsByPlan[planId].Take(UnsealedCleanupPageSize).ToArray())
            {
                var jobId = entry.Value;
                var job = _state.Jobs[jobId];
                RemoveClaimableJob(job);
                _state.Jobs.Remove(jobId);
                _state.JobIdsByPlan[planId].Remove(entry.Key);
                AdjustJobCounts(planId, job.Status, null);
            }

            if (_state.JobIdsByPlan[planId].Count > 0)
            {
                var fenced = CopyPlan(
                    plan,
                    status: WorkflowAlterationPlanStatus.Cancelling,
                    cancellationRequestedAt: cleanup.TerminalStatus == WorkflowAlterationPlanStatus.Cancelled ? cleanup.CompletedAt : null,
                    revision: plan.Revision + 1);
                _state.Plans[planId] = fenced;
                return ValueTask.FromResult(fenced);
            }

            var terminal = CopyPlan(
                plan,
                status: cleanup.TerminalStatus,
                changeCaptureCursor: true,
                captureCursor: null,
                capturedSoFar: plan.CapturedSoFar,
                targetCount: 0,
                succeededJobCount: 0,
                failedJobCount: 0,
                cancelledJobCount: 0,
                completedAt: cleanup.CompletedAt,
                cancellationRequestedAt: cleanup.TerminalStatus == WorkflowAlterationPlanStatus.Cancelled ? cleanup.CompletedAt : null,
                safeFailure: cleanup.SafeFailure,
                revision: plan.Revision + 1);
            _state.Plans[planId] = terminal;
            _state.UnsealedCaptureCleanups.Remove(planId);
            return ValueTask.FromResult(terminal);
        }
    }

    public ValueTask CancelPendingJobsAsync(string planId, IReadOnlyCollection<WorkflowAlterationOutcome> skippedOutcomes, DateTimeOffset completedAt, int maximumCount, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(skippedOutcomes);
        if (maximumCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumCount));
        cancellationToken.ThrowIfCancellationRequested();
        using var checkpointAccess = _state.TransactionGate.Enter();
        lock (_state.SyncRoot)
        {
            var plan = GetPlan(planId);
            if (plan.Status != WorkflowAlterationPlanStatus.Cancelling)
                throw new InvalidOperationException("Only a cancelling alteration plan can cancel pending jobs.");
            foreach (var cancellable in _state.Jobs.Values.Where(job =>
                         StringComparer.Ordinal.Equals(job.PlanId, planId) &&
                         job.Status == WorkflowAlterationJobStatus.Pending)
                         .OrderBy(job => job.CaptureOrdinal)
                         .ThenBy(job => job.JobId, StringComparer.Ordinal)
                         .Take(maximumCount)
                         .ToArray())
            {
                var cancelled = CopyJob(
                    cancellable,
                    status: WorkflowAlterationJobStatus.Cancelled,
                    claim: null,
                    outcomes: skippedOutcomes.ToArray(),
                    completedAt: completedAt,
                    revision: cancellable.Revision + 1);
                _state.Jobs[cancellable.JobId] = cancelled;
                RemoveClaimableJob(cancellable);
                AdjustJobCounts(planId, cancellable.Status, cancelled.Status);
            }
        }
        return ValueTask.CompletedTask;
    }

    public ValueTask<WorkflowAlterationJobState?> FindJobAsync(string jobId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobId);
        cancellationToken.ThrowIfCancellationRequested();
        using var checkpointAccess = _state.TransactionGate.Enter();
        lock (_state.SyncRoot)
            return ValueTask.FromResult(_state.Jobs.GetValueOrDefault(jobId));
    }

    public ValueTask<WorkflowAlterationJobState?> FindJobByCheckpointCommitIdAsync(string checkpointCommitId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(checkpointCommitId);
        cancellationToken.ThrowIfCancellationRequested();
        using var checkpointAccess = _state.TransactionGate.Enter();
        lock (_state.SyncRoot)
            return ValueTask.FromResult(_state.Jobs.Values.FirstOrDefault(job =>
                StringComparer.Ordinal.Equals(job.CheckpointCommitId, checkpointCommitId)));
    }

    public ValueTask<WorkflowAlterationJobCounts> GetJobCountsAsync(string planId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(planId);
        cancellationToken.ThrowIfCancellationRequested();
        using var checkpointAccess = _state.TransactionGate.Enter();
        lock (_state.SyncRoot)
        {
            _ = GetPlan(planId);
            return ValueTask.FromResult(_state.JobCounts[planId]);
        }
    }

    public ValueTask<WorkflowAlterationJobPage> PageJobsAsync(string planId, int pageSize, string? cursor = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(planId);
        if (pageSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(pageSize));
        cancellationToken.ThrowIfCancellationRequested();
        using var checkpointAccess = _state.TransactionGate.Enter();
        lock (_state.SyncRoot)
        {
            _ = GetPlan(planId);
            (long Ordinal, string JobId)? after = cursor is null ? null : DecodeJobCursor(cursor, planId);
            var jobs = _state.Jobs.Values
                .Where(job => StringComparer.Ordinal.Equals(job.PlanId, planId))
                .OrderBy(job => job.CaptureOrdinal)
                .ThenBy(job => job.JobId, StringComparer.Ordinal)
                .Where(job => after is null || job.CaptureOrdinal > after.Value.Ordinal || (job.CaptureOrdinal == after.Value.Ordinal && StringComparer.Ordinal.Compare(job.JobId, after.Value.JobId) > 0))
                .Take(checked(pageSize + 1))
                .ToArray();
            var hasNext = jobs.Length > pageSize;
            if (hasNext)
                jobs = jobs[..pageSize];
            return ValueTask.FromResult(new WorkflowAlterationJobPage(jobs, hasNext ? EncodeJobCursor(planId, jobs[^1]) : null, hasNext));
        }
    }

    public async ValueTask<WorkflowAlterationJobState?> ClaimNextAsync(string planId, string ownerId, DateTimeOffset now, TimeSpan leaseDuration, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(planId);
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        if (leaseDuration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(leaseDuration));
        cancellationToken.ThrowIfCancellationRequested();
        await using var checkpointGate = await _state.TransactionGate.EnterAsync(cancellationToken);
        lock (_state.SyncRoot)
        {
            var plan = GetPlan(planId);
            WorkflowAlterationJobState? candidate = null;
            // Reconcile in-flight work first. A successful re-claim grants a fresh lease, allowing pending
            // jobs to progress while that actor remains non-due.
            var runningJobs = _state.RunningJobsByPlan[planId];
            if (runningJobs.Count > 0 && runningJobs.Min.ClaimableAt <= now)
                candidate = _state.Jobs[runningJobs.Min.JobId];
            if (candidate is null && plan.Status != WorkflowAlterationPlanStatus.Cancelling)
            {
                foreach (var pending in _state.ClaimableJobsByPlan[planId])
                {
                    if (pending.ClaimableAt > now)
                        break;
                    var job = _state.Jobs[pending.JobId];
                    if (job.Status == WorkflowAlterationJobStatus.Pending)
                    {
                        candidate = job;
                        break;
                    }
                }
            }
            if (candidate is null)
                return null;
            if (plan.Status is not (WorkflowAlterationPlanStatus.Queued or WorkflowAlterationPlanStatus.Running) &&
                (plan.Status != WorkflowAlterationPlanStatus.Cancelling ||
                 candidate.Status != WorkflowAlterationJobStatus.Running))
                return null;

            var claim = new WorkflowAlterationJobClaim(ownerId, Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16)), now + leaseDuration);
            var claimed = CopyJob(candidate, status: WorkflowAlterationJobStatus.Running, claim: claim, attemptCount: candidate.AttemptCount + 1, startedAt: candidate.StartedAt ?? now, revision: candidate.Revision + 1);
            _state.Jobs[candidate.JobId] = claimed;
            RemoveClaimableJob(candidate);
            AddClaimableJob(claimed);
            AdjustJobCounts(candidate.PlanId, candidate.Status, claimed.Status);
            if (plan.Status == WorkflowAlterationPlanStatus.Queued)
                _state.Plans[plan.PlanId] = CopyPlan(plan, status: WorkflowAlterationPlanStatus.Running, startedAt: plan.StartedAt ?? now, revision: plan.Revision + 1);
            return claimed;
        }
    }

    public ValueTask ValidateTerminalJobChangeAsync(WorkflowAlterationJobTerminalChange change, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(change);
        cancellationToken.ThrowIfCancellationRequested();
        using var checkpointAccess = _state.TransactionGate.Enter();
        lock (_state.SyncRoot)
            ValidateTerminalChange(change, GetJob(change.JobId));
        return ValueTask.CompletedTask;
    }

    public ValueTask ApplyTerminalJobChangeAsync(WorkflowAlterationJobTerminalChange change, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(change);
        cancellationToken.ThrowIfCancellationRequested();
        using var checkpointAccess = _state.TransactionGate.Enter();
        lock (_state.SyncRoot)
        {
            var job = GetJob(change.JobId);
            ValidateTerminalChange(change, job);
            // Retain the completed claim as immutable checkpoint evidence. It is not an active lease once the job is
            // terminal, but acknowledgement reconciliation must prove the exact claimant that wrote this commit.
            var terminal = CopyJob(job, status: change.Status, claim: job.Claim, outcomes: change.Outcomes.ToArray(), checkpointCommitId: change.CheckpointCommitId, safeFailure: change.SafeFailure, completedAt: change.CompletedAt, revision: job.Revision + 1);
            _state.Jobs[job.JobId] = terminal;
            RemoveClaimableJob(job);
            AdjustJobCounts(job.PlanId, job.Status, terminal.Status);
        }
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public async ValueTask CommitTerminalJobChangeAtomicallyAsync(
        WorkflowAlterationJobTerminalChange change,
        Func<CancellationToken, ValueTask> commitWorkflowCheckpointAsync,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(change);
        ArgumentNullException.ThrowIfNull(commitWorkflowCheckpointAsync);
        cancellationToken.ThrowIfCancellationRequested();

        using (_state.TransactionGate.Enter())
        {
            lock (_state.SyncRoot)
                ValidateTerminalChange(change, GetJob(change.JobId));
        }

        await commitWorkflowCheckpointAsync(cancellationToken);
    }

    public ValueTask<WorkflowAlterationPlanState> ReconcileAsync(string planId, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var checkpointAccess = _state.TransactionGate.Enter();
        lock (_state.SyncRoot)
        {
            var plan = GetPlan(planId);
            if (IsTerminal(plan.Status))
                return ValueTask.FromResult(plan);
            var counts = _state.JobCounts[planId];
            if (plan.SealedAt is null || counts.Pending > 0 || counts.Running > 0)
                return ValueTask.FromResult(plan);

            var status = plan.Status == WorkflowAlterationPlanStatus.Cancelling
                ? WorkflowAlterationPlanStatus.Cancelled
                : counts.Failed > 0 ? WorkflowAlterationPlanStatus.CompletedWithFailures : WorkflowAlterationPlanStatus.Completed;
            var completed = CopyPlan(plan, status: status, targetCount: counts.Total, succeededJobCount: counts.Succeeded, failedJobCount: counts.Failed, cancelledJobCount: counts.Cancelled, completedAt: now, revision: plan.Revision + 1);
            _state.Plans[planId] = completed;
            return ValueTask.FromResult(completed);
        }
    }

    private static readonly WorkflowAlterationJobCounts EmptyJobCounts = new(0, 0, 0, 0, 0);
    private static readonly IComparer<(DateTimeOffset ClaimableAt, string JobId)> ClaimableJobComparer =
        Comparer<(DateTimeOffset ClaimableAt, string JobId)>.Create((left, right) =>
        {
            var time = left.ClaimableAt.CompareTo(right.ClaimableAt);
            return time != 0 ? time : StringComparer.Ordinal.Compare(left.JobId, right.JobId);
        });

    private void AddClaimableJob(WorkflowAlterationJobState job)
    {
        var claimableAt = GetClaimableAt(job);
        if (claimableAt is not null)
        {
            _state.ClaimableJobsByPlan[job.PlanId].Add((claimableAt.Value, job.JobId));
            if (job.Status == WorkflowAlterationJobStatus.Running)
                _state.RunningJobsByPlan[job.PlanId].Add((claimableAt.Value, job.JobId));
        }
    }

    private void RemoveClaimableJob(WorkflowAlterationJobState job)
    {
        var claimableAt = GetClaimableAt(job);
        if (claimableAt is not null)
        {
            _state.ClaimableJobsByPlan[job.PlanId].Remove((claimableAt.Value, job.JobId));
            if (job.Status == WorkflowAlterationJobStatus.Running)
                _state.RunningJobsByPlan[job.PlanId].Remove((claimableAt.Value, job.JobId));
        }
    }

    private static DateTimeOffset? GetClaimableAt(WorkflowAlterationJobState job) => job.Status switch
    {
        WorkflowAlterationJobStatus.Pending => job.CreatedAt,
        WorkflowAlterationJobStatus.Running => job.Claim?.ExpiresAt,
        _ => null
    };

    private void AdjustJobCounts(string planId, WorkflowAlterationJobStatus? from, WorkflowAlterationJobStatus? to)
    {
        if (from == to)
            return;
        var counts = _state.JobCounts[planId];
        static long Delta(WorkflowAlterationJobStatus status, WorkflowAlterationJobStatus? from, WorkflowAlterationJobStatus? to) =>
            (to == status ? 1 : 0) - (from == status ? 1 : 0);
        _state.JobCounts[planId] = new(
            checked(counts.Pending + Delta(WorkflowAlterationJobStatus.Pending, from, to)),
            checked(counts.Running + Delta(WorkflowAlterationJobStatus.Running, from, to)),
            checked(counts.Succeeded + Delta(WorkflowAlterationJobStatus.Succeeded, from, to)),
            checked(counts.Failed + Delta(WorkflowAlterationJobStatus.Failed, from, to)),
            checked(counts.Cancelled + Delta(WorkflowAlterationJobStatus.Cancelled, from, to)));
    }

    private WorkflowAlterationPlanState GetPlan(string planId) => _state.Plans.GetValueOrDefault(planId) ?? throw new KeyNotFoundException($"Alteration plan '{planId}' was not found.");
    private WorkflowAlterationJobState GetJob(string jobId) => _state.Jobs.GetValueOrDefault(jobId) ?? throw new KeyNotFoundException($"Alteration job '{jobId}' was not found.");
    private static void EnsureRevision(WorkflowAlterationPlanState plan, long expectedRevision)
    {
        if (plan.Revision != expectedRevision)
            throw new WorkflowAlterationConcurrencyException(plan.PlanId);
    }

    private static void ValidateTerminalChange(WorkflowAlterationJobTerminalChange change, WorkflowAlterationJobState job)
    {
        if (job.Status is WorkflowAlterationJobStatus.Succeeded or WorkflowAlterationJobStatus.Failed or WorkflowAlterationJobStatus.Cancelled)
        {
            if (StringComparer.Ordinal.Equals(job.CheckpointCommitId, change.CheckpointCommitId) &&
                job.Status == change.Status &&
                job.CompletedAt == change.CompletedAt &&
                job.SafeFailure == change.SafeFailure &&
                job.Outcomes.SequenceEqual(change.Outcomes))
                return;
            throw new InvalidOperationException("A terminal alteration job cannot be terminalized with conflicting checkpoint evidence.");
        }
        if (job.Status != WorkflowAlterationJobStatus.Running || job.Claim is null || !StringComparer.Ordinal.Equals(job.Claim.Token, change.ClaimToken))
            throw new WorkflowAlterationClaimFenceException(change.JobId);
    }

    private static bool IsTerminal(WorkflowAlterationPlanStatus status) => status is WorkflowAlterationPlanStatus.Completed or WorkflowAlterationPlanStatus.CompletedWithFailures or WorkflowAlterationPlanStatus.Failed or WorkflowAlterationPlanStatus.Cancelled;
    private static string CreateActivePlanOrderKey(DateTimeOffset time, string planId) =>
        $"{time.UtcTicks:D19}:{planId}";
    private static string NextActivePlanOrderKey(string current, DateTimeOffset servicedAt, string planId)
    {
        var separator = current.IndexOf(':', StringComparison.Ordinal);
        var currentTicks = long.Parse(current.AsSpan(0, separator), System.Globalization.CultureInfo.InvariantCulture);
        return $"{Math.Max(servicedAt.UtcTicks, checked(currentTicks + 1)):D19}:{planId}";
    }
    private static string IdempotencyKey(string tenant, string hash) => tenant + "\u001f" + hash;

    private static string EncodeJobCursor(string planId, WorkflowAlterationJobState job) => Convert.ToBase64String(Encoding.UTF8.GetBytes($"v1|{Convert.ToBase64String(Encoding.UTF8.GetBytes(planId))}|{job.CaptureOrdinal}|{Convert.ToBase64String(Encoding.UTF8.GetBytes(job.JobId))}"));
    private static (long Ordinal, string JobId) DecodeJobCursor(string cursor, string planId)
    {
        try
        {
            var parts = Encoding.UTF8.GetString(Convert.FromBase64String(cursor)).Split('|');
            if (parts.Length != 4 || parts[0] != "v1" || !long.TryParse(parts[2], out var ordinal) || !StringComparer.Ordinal.Equals(Encoding.UTF8.GetString(Convert.FromBase64String(parts[1])), planId))
                throw new FormatException();
            return (ordinal, Encoding.UTF8.GetString(Convert.FromBase64String(parts[3])));
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException)
        {
            throw new ArgumentException("The alteration job page cursor is invalid or does not belong to this plan.", nameof(cursor), exception);
        }
    }

    private static WorkflowAlterationPlanState CopyPlan(WorkflowAlterationPlanState plan, WorkflowAlterationPlanStatus? status = null, bool changeCaptureCursor = false, string? captureCursor = null, long? capturedSoFar = null, long? targetCount = null, long? succeededJobCount = null, long? failedJobCount = null, long? cancelledJobCount = null, DateTimeOffset? sealedAt = null, DateTimeOffset? startedAt = null, DateTimeOffset? completedAt = null, DateTimeOffset? cancellationRequestedAt = null, WorkflowAlterationSafeFailure? safeFailure = null, long? revision = null) =>
        new(plan.PlanId, plan.AuthorityScope, plan.SubmittedBy, plan.IdempotencyKeyHash, plan.CanonicalRequestHash, plan.ProtectedPayload, plan.Target, status ?? plan.Status, plan.CreatedAt,
            changeCaptureCursor ? captureCursor : plan.CaptureCursor, capturedSoFar ?? plan.CapturedSoFar, targetCount ?? plan.TargetCount, succeededJobCount ?? plan.SucceededJobCount, failedJobCount ?? plan.FailedJobCount, cancelledJobCount ?? plan.CancelledJobCount,
            sealedAt ?? plan.SealedAt, startedAt ?? plan.StartedAt, completedAt ?? plan.CompletedAt, cancellationRequestedAt ?? plan.CancellationRequestedAt, safeFailure ?? plan.SafeFailure, revision ?? plan.Revision,
            plan.AlterationDescriptors);

    private static WorkflowAlterationJobState CopyJob(WorkflowAlterationJobState job, WorkflowAlterationJobStatus? status = null, WorkflowAlterationJobClaim? claim = null, int? attemptCount = null, IReadOnlyList<WorkflowAlterationOutcome>? outcomes = null, string? checkpointCommitId = null, WorkflowAlterationSafeFailure? safeFailure = null, DateTimeOffset? startedAt = null, DateTimeOffset? completedAt = null, long? revision = null) =>
        new(job.JobId, job.PlanId, job.WorkflowExecutionId, job.TenantPartition, job.CaptureOrdinal, status ?? job.Status, claim, attemptCount ?? job.AttemptCount, (outcomes ?? job.Outcomes).ToArray(), checkpointCommitId ?? job.CheckpointCommitId, safeFailure ?? job.SafeFailure, job.CreatedAt, startedAt ?? job.StartedAt, completedAt ?? job.CompletedAt, revision ?? job.Revision, job.CapturedConcurrency);
}

public sealed class WorkflowAlterationIdempotencyConflictException(string planId) : InvalidOperationException($"The idempotency key is already associated with alteration plan '{planId}' and a different request.");
public sealed class WorkflowAlterationConcurrencyException(string planId) : InvalidOperationException($"The alteration plan '{planId}' changed while the operation was in progress.");
public sealed class WorkflowAlterationClaimFenceException(string jobId) : InvalidOperationException($"The alteration job claim for '{jobId}' is no longer current.");
