using System.Globalization;
using System.Security.Cryptography;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Workflows.Runtime.Core.Contracts.Alterations;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Models.Alterations;
using Groundwork.Kernel;
using Groundwork.Query.Model;
using Groundwork.Store;

namespace Elsa.Persistence.Groundwork.Runtime;

/// <summary>Current-only Groundwork v2 plan and target/job ledger for runtime alterations.</summary>
/// <remarks>
/// The implementation deliberately uses only the public v2 session, row-write, and query surfaces. Capture,
/// unsealed cleanup, and lease transitions use exact two-unit transactions; every mutable row transition is fenced
/// by the provider's optimistic revision. There is no v1 document-store bridge or migration path.
/// </remarks>
public sealed class GroundworkV2WorkflowAlterationStore : IWorkflowAlterationStore
{
    private const int TransitionAttempts = 16;
    private const int UnsealedCleanupPageSize = 100;
    private const int MaximumQueryPage = 2_000;

    private readonly IGroundworkStorageSessionSource sessions;
    private readonly IPersistenceAccessContextAccessor accessContextAccessor;
    private readonly string? targetName;
    private readonly StorageUnit planUnit;
    private readonly StorageUnit jobUnit;

    public GroundworkV2WorkflowAlterationStore(
        IGroundworkStorageSessionSource sessions,
        IPersistenceAccessContextAccessor accessContextAccessor,
        string? targetName = null)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(accessContextAccessor);
        this.sessions = sessions;
        this.accessContextAccessor = accessContextAccessor;
        this.targetName = targetName;
        planUnit = sessions.Unit(ElsaRuntimeV2StorageManifest.WorkflowAlterationPlanDocumentKind, targetName);
        jobUnit = sessions.Unit(ElsaRuntimeV2StorageManifest.WorkflowAlterationJobDocumentKind, targetName);
    }

    public ValueTask<WorkflowAlterationPlanAdmissionResult> AdmitAsync(
        WorkflowAlterationPlanState plan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        cancellationToken.ThrowIfCancellationRequested();
        EnsureTenant(plan.AuthorityScope.TenantPartition);
        var session = OpenScoped(planUnit);
        var byId = session.Read(GroundworkRuntimeRowStore.Key(
            GroundworkV2WorkflowAlterationStorageConventions.PhysicalPlanId(plan.PlanId)));
        if (byId is not null)
        {
            var existing = ReadPlan(byId, plan.PlanId).Plan;
            EnsureTenant(existing.AuthorityScope.TenantPartition);
            EnsureSameAdmission(existing, plan);
            return ValueTask.FromResult(new WorkflowAlterationPlanAdmissionResult(existing, true));
        }

        var existingByKey = QueryPlansByIdempotency(session, plan.AuthorityScope.TenantPartition, plan.IdempotencyKeyHash, 2);
        EnsureSingleIdempotencyMatch(existingByKey, plan.IdempotencyKeyHash);
        if (existingByKey.Count == 1)
        {
            var existing = existingByKey[0].Plan;
            EnsureTenant(existing.AuthorityScope.TenantPartition);
            EnsureSameAdmission(existing, plan);
            return ValueTask.FromResult(new WorkflowAlterationPlanAdmissionResult(existing, true));
        }

        var inserted = session.Insert(
            GroundworkV2WorkflowAlterationStorageConventions.Values(
                GroundworkV2WorkflowAlterationStorageConventions.CreatePlanDocument(plan)),
            WriteOptions.CreateOnly);
        if (IsSaved(inserted.Status))
            return ValueTask.FromResult(new WorkflowAlterationPlanAdmissionResult(plan, false));
        if (inserted.Status is WriteOutcomeStatus.ConcurrencyConflict or WriteOutcomeStatus.UniqueViolation)
        {
            var winner = QueryPlansByIdempotency(session, plan.AuthorityScope.TenantPartition, plan.IdempotencyKeyHash, 2);
            EnsureSingleIdempotencyMatch(winner, plan.IdempotencyKeyHash);
            if (winner.Count == 1)
            {
                EnsureTenant(winner[0].Plan.AuthorityScope.TenantPartition);
                EnsureSameAdmission(winner[0].Plan, plan);
                return ValueTask.FromResult(new WorkflowAlterationPlanAdmissionResult(winner[0].Plan, true));
            }
        }

        throw new InvalidOperationException($"Groundwork rejected alteration-plan admission for '{plan.PlanId}' with status '{inserted.Status}'.");
    }

    public ValueTask<WorkflowAlterationPlanState?> FindPlanAsync(
        string planId,
        CancellationToken cancellationToken = default)
    {
        ValidateId(planId, nameof(planId));
        cancellationToken.ThrowIfCancellationRequested();
        var entry = OpenScoped(planUnit).Read(GroundworkRuntimeRowStore.Key(
            GroundworkV2WorkflowAlterationStorageConventions.PhysicalPlanId(planId)));
        if (entry is null)
            return ValueTask.FromResult<WorkflowAlterationPlanState?>(null);
        var document = ReadPlan(entry, planId);
        EnsureTenant(document.Plan.AuthorityScope.TenantPartition);
        return ValueTask.FromResult<WorkflowAlterationPlanState?>(document.Plan);
    }

    public ValueTask<WorkflowAlterationActivePlanPage> ListActivePlansAsync(
        int pageSize,
        string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        if (pageSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(pageSize));
        if (pageSize > MaximumQueryPage)
            throw new ArgumentOutOfRangeException(nameof(pageSize), $"Alteration plan pages cannot exceed {MaximumQueryPage} rows.");
        if (cursor is not null && string.IsNullOrWhiteSpace(cursor))
            throw new ArgumentException("The active alteration-plan cursor cannot be blank.", nameof(cursor));
        cancellationToken.ThrowIfCancellationRequested();

        var context = AccessContext;
        if (context.Scope is null)
            throw new InvalidOperationException(
                "Active alteration-plan discovery requires one tenant-scoped persistence context; cross-scope alteration coordination is not available for scoped v2 units.");

        var table = new TableId(planUnit.Name);
        var predicates = new List<Predicate>
        {
            new Predicate.Or([
                Equal(Column(table, ElsaRuntimeV2StorageManifest.WorkflowAlterationPlanStatusField), WorkflowAlterationPlanStatus.CapturingTargets.ToString()),
                Equal(Column(table, ElsaRuntimeV2StorageManifest.WorkflowAlterationPlanStatusField), WorkflowAlterationPlanStatus.Queued.ToString()),
                Equal(Column(table, ElsaRuntimeV2StorageManifest.WorkflowAlterationPlanStatusField), WorkflowAlterationPlanStatus.Running.ToString()),
                Equal(Column(table, ElsaRuntimeV2StorageManifest.WorkflowAlterationPlanStatusField), WorkflowAlterationPlanStatus.Cancelling.ToString())])
        };
        if (context.Scope is not null)
            predicates.Add(Equal(Column(table, ElsaRuntimeV2StorageManifest.WorkflowAlterationPlanTenantPartitionField), context.Scope.Value));
        var order = Column(table, ElsaRuntimeV2StorageManifest.WorkflowAlterationPlanActiveOrderKeyField);
        var result = QueryWithBoundCursor(
            OpenScoped(planUnit),
            new QueryRequest(
                table,
                Combine(predicates),
                [new OrderTerm(order, OrderDirection.Ascending, NullOrder.Last)],
                Projection.All,
                cursor is null ? Paging.Keyset(pageSize) : Paging.Continuation(cursor, pageSize)),
            cursor,
            "active alteration-plan");
        var documents = result.Rows
            .Select(GroundworkV2WorkflowAlterationStorageConventions.DeserializePlan)
            .ToArray();
        foreach (var document in documents)
            EnsureTenant(document.Plan.AuthorityScope.TenantPartition);

        return ValueTask.FromResult(new WorkflowAlterationActivePlanPage(
            documents.Select(document => document.Plan).ToArray(),
            result.NextContinuationToken,
            result.NextContinuationToken is not null));
    }

    public async ValueTask RescheduleActivePlanAsync(
        string planId,
        DateTimeOffset servicedAt,
        CancellationToken cancellationToken = default)
    {
        ValidateId(planId, nameof(planId));
        cancellationToken.ThrowIfCancellationRequested();
        await UpdatePlanAsync(planId, (plan, document) => IsTerminal(plan.Status)
            ? (plan, document.ActiveOrderKey)
            : (plan, NextActivePlanOrderKey(document.ActiveOrderKey, servicedAt, planId)), cancellationToken);
    }

    public async ValueTask<WorkflowAlterationPlanState> CaptureAsync(
        string planId,
        long expectedRevision,
        IReadOnlyCollection<WorkflowAlterationCapturedTarget> targets,
        string? nextCursor,
        CancellationToken cancellationToken = default)
    {
        ValidateId(planId, nameof(planId));
        ArgumentNullException.ThrowIfNull(targets);
        cancellationToken.ThrowIfCancellationRequested();
        RequireAtomicCommit();

        using var unitOfWork = BeginAtomicUnitOfWork();
        var planSession = unitOfWork.OpenSession(planUnit);
        var jobSession = unitOfWork.OpenSession(jobUnit);
        var planEntry = planSession.Read(GroundworkRuntimeRowStore.Key(
            GroundworkV2WorkflowAlterationStorageConventions.PhysicalPlanId(planId)))
            ?? throw new KeyNotFoundException($"Alteration plan '{planId}' was not found.");
        var document = ReadPlan(planEntry, planId);
        var plan = document.Plan;
        EnsureTenant(plan.AuthorityScope.TenantPartition);
        if (plan.Revision != expectedRevision)
            throw Concurrency(planId);
        if (plan.Status != WorkflowAlterationPlanStatus.CapturingTargets)
            throw new InvalidOperationException("Only a capturing alteration plan can accept target pages.");

        var ordinal = plan.CapturedSoFar;
        foreach (var target in NormalizeTargets(targets))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!StringComparer.Ordinal.Equals(target.TenantPartition, plan.AuthorityScope.TenantPartition))
                throw new InvalidOperationException("A captured target must belong to the plan tenant partition.");
            var jobId = GroundworkV2WorkflowAlterationStorageConventions.CreateJobId(plan.PlanId, target.WorkflowExecutionId);
            var existing = jobSession.Read(GroundworkRuntimeRowStore.Key(
                GroundworkV2WorkflowAlterationStorageConventions.PhysicalJobId(jobId)));
            if (existing is not null)
            {
                var existingJob = ReadJob(existing, jobId).Job;
                if (!StringComparer.Ordinal.Equals(existingJob.PlanId, plan.PlanId) ||
                    !StringComparer.Ordinal.Equals(existingJob.WorkflowExecutionId, target.WorkflowExecutionId) ||
                    !StringComparer.Ordinal.Equals(existingJob.TenantPartition, target.TenantPartition))
                {
                    throw new InvalidDataException($"Groundwork alteration-job identity collision detected for target '{target.WorkflowExecutionId}'.");
                }
                continue;
            }

            var failed = target.SafeFailure is not null;
            var job = new WorkflowAlterationJobState(
                jobId,
                plan.PlanId,
                target.WorkflowExecutionId,
                target.TenantPartition,
                ordinal++,
                failed ? WorkflowAlterationJobStatus.Failed : WorkflowAlterationJobStatus.Pending,
                null,
                0,
                [],
                null,
                target.SafeFailure,
                plan.CreatedAt,
                null,
                failed ? plan.CreatedAt : null,
                0,
                target.CapturedConcurrency);
            unitOfWork.Stage(RowWrite.Insert(
                jobUnit,
                GroundworkV2WorkflowAlterationStorageConventions.Values(
                    GroundworkV2WorkflowAlterationStorageConventions.CreateJobDocument(job)),
                WriteOptions.CreateOnly));
        }

        var updated = CopyPlan(plan, captureCursor: nextCursor, setCaptureCursor: true, capturedSoFar: ordinal, revision: checked(plan.Revision + 1));
        unitOfWork.Stage(RowWrite.ConditionalUpsert(
            planUnit,
            GroundworkV2WorkflowAlterationStorageConventions.Values(
                GroundworkV2WorkflowAlterationStorageConventions.CreatePlanDocument(updated, document.ActiveOrderKey, document.UnsealedCaptureCleanup)),
            WriteOptions.IfVersion(RequiredVersion(planEntry))));
        var report = await CommitAsync(unitOfWork, cancellationToken);
        if (!report.IsSuccessful)
            throw Concurrency(planId);
        return updated;
    }

    public async ValueTask<WorkflowAlterationPlanState> SealAsync(
        string planId,
        long expectedRevision,
        DateTimeOffset sealedAt,
        CancellationToken cancellationToken = default)
    {
        ValidateId(planId, nameof(planId));
        cancellationToken.ThrowIfCancellationRequested();
        return await UpdatePlanAsync(planId, (plan, document) =>
        {
            if (plan.Revision != expectedRevision)
                throw Concurrency(planId);
            if (document.UnsealedCaptureCleanup is not null)
                return (plan, document.ActiveOrderKey);
            if (plan.Status == WorkflowAlterationPlanStatus.Cancelling)
                return (CopyPlan(plan, sealedAt: sealedAt, targetCount: plan.CapturedSoFar, revision: checked(plan.Revision + 1)), document.ActiveOrderKey);
            if (plan.Status != WorkflowAlterationPlanStatus.CapturingTargets)
                throw new InvalidOperationException("Only a capturing alteration plan can be sealed.");
            var status = plan.CapturedSoFar == 0 ? WorkflowAlterationPlanStatus.Completed : WorkflowAlterationPlanStatus.Queued;
            return (CopyPlan(
                plan,
                status: status,
                sealedAt: sealedAt,
                targetCount: plan.CapturedSoFar,
                completedAt: plan.CapturedSoFar == 0 ? sealedAt : null,
                revision: checked(plan.Revision + 1)), document.ActiveOrderKey);
        }, cancellationToken, requireExpectedRevision: true);
    }

    public ValueTask<WorkflowAlterationPlanState> RequestCancellationAsync(
        string planId,
        DateTimeOffset requestedAt,
        CancellationToken cancellationToken = default) =>
        UpdatePlanAsync(planId, (plan, document) =>
        {
            if (IsTerminal(plan.Status))
                return (plan, document.ActiveOrderKey);
            return (CopyPlan(plan, status: WorkflowAlterationPlanStatus.Cancelling, cancellationRequestedAt: requestedAt, revision: checked(plan.Revision + 1)), document.ActiveOrderKey);
        }, cancellationToken);

    public ValueTask<WorkflowAlterationPlanState> CancelUnsealedCaptureAsync(
        string planId,
        DateTimeOffset cancelledAt,
        CancellationToken cancellationToken = default) =>
        TerminalizeUnsealedCaptureAsync(planId, WorkflowAlterationPlanStatus.Cancelled, null, cancelledAt, cancellationToken);

    public ValueTask<WorkflowAlterationPlanState> FailUnsealedCaptureAsync(
        string planId,
        WorkflowAlterationSafeFailure safeFailure,
        DateTimeOffset failedAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(safeFailure);
        return TerminalizeUnsealedCaptureAsync(planId, WorkflowAlterationPlanStatus.Failed, safeFailure, failedAt, cancellationToken);
    }

    public async ValueTask CancelPendingJobsAsync(
        string planId,
        IReadOnlyCollection<WorkflowAlterationOutcome> skippedOutcomes,
        DateTimeOffset completedAt,
        int maximumCount,
        CancellationToken cancellationToken = default)
    {
        ValidateId(planId, nameof(planId));
        ArgumentNullException.ThrowIfNull(skippedOutcomes);
        if (maximumCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumCount));
        cancellationToken.ThrowIfCancellationRequested();
        RequireAtomicCommit();

        var plan = await RequirePlanAsync(planId, cancellationToken);
        if (plan.Status != WorkflowAlterationPlanStatus.Cancelling)
            throw new InvalidOperationException("Only a cancelling alteration plan can cancel pending jobs.");

        using var unitOfWork = BeginAtomicUnitOfWork();
        var session = unitOfWork.OpenSession(jobUnit);
        var candidates = QueryJobEntries(
            session,
            planId,
            [WorkflowAlterationJobStatus.Pending],
            maximumCount,
            claimableBefore: null,
            orderByCapture: true);
        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var jobId = RequiredProjectionString(candidate, ElsaRuntimeV2StorageManifest.WorkflowAlterationJobIdField);
            var currentEntry = session.Read(GroundworkRuntimeRowStore.Key(
                GroundworkV2WorkflowAlterationStorageConventions.PhysicalJobId(jobId)));
            if (currentEntry is null)
                continue;
            var current = ReadJob(currentEntry, jobId);
            if (current.Job.Status != WorkflowAlterationJobStatus.Pending)
                continue;
            var cancelled = CopyJob(current.Job, status: WorkflowAlterationJobStatus.Cancelled, claim: null, outcomes: skippedOutcomes, completedAt: completedAt, revision: checked(current.Job.Revision + 1));
            unitOfWork.Stage(RowWrite.ConditionalUpsert(
                jobUnit,
                GroundworkV2WorkflowAlterationStorageConventions.Values(
                    GroundworkV2WorkflowAlterationStorageConventions.CreateJobDocument(cancelled)),
                WriteOptions.IfVersion(RequiredVersion(currentEntry))));
        }
        if (candidates.Count == 0)
            return;
        var report = await CommitAsync(unitOfWork, cancellationToken);
        if (!report.IsSuccessful)
            throw Concurrency(planId);
    }

    public ValueTask<WorkflowAlterationJobState?> FindJobAsync(
        string jobId,
        CancellationToken cancellationToken = default)
    {
        ValidateId(jobId, nameof(jobId));
        cancellationToken.ThrowIfCancellationRequested();
        var entry = OpenScoped(jobUnit).Read(GroundworkRuntimeRowStore.Key(
            GroundworkV2WorkflowAlterationStorageConventions.PhysicalJobId(jobId)));
        if (entry is null)
            return ValueTask.FromResult<WorkflowAlterationJobState?>(null);
        var document = ReadJob(entry, jobId);
        EnsureTenant(document.Job.TenantPartition);
        return ValueTask.FromResult<WorkflowAlterationJobState?>(document.Job);
    }

    public async ValueTask<WorkflowAlterationJobCounts> GetJobCountsAsync(
        string planId,
        CancellationToken cancellationToken = default)
    {
        ValidateId(planId, nameof(planId));
        cancellationToken.ThrowIfCancellationRequested();
        _ = await RequirePlanAsync(planId, cancellationToken);
        var session = OpenScoped(jobUnit);
        var counts = new long[5];
        foreach (var status in Enum.GetValues<WorkflowAlterationJobStatus>())
            counts[(int)status] = CountJobs(session, planId, status);
        return new WorkflowAlterationJobCounts(counts[0], counts[1], counts[2], counts[3], counts[4]);
    }

    public ValueTask<WorkflowAlterationJobState?> FindJobByCheckpointCommitIdAsync(
        string checkpointCommitId,
        CancellationToken cancellationToken = default)
    {
        ValidateId(checkpointCommitId, nameof(checkpointCommitId));
        cancellationToken.ThrowIfCancellationRequested();
        var table = new TableId(jobUnit.Name);
        var checkpoint = Column(table, ElsaRuntimeV2StorageManifest.WorkflowAlterationJobCheckpointCommitIdField);
        var jobId = Column(table, ElsaRuntimeV2StorageManifest.WorkflowAlterationJobIdField);
        var result = OpenScoped(jobUnit).Query(new QueryRequest(
            table,
            Equal(checkpoint, checkpointCommitId),
            [new OrderTerm(jobId, OrderDirection.Ascending, NullOrder.Last)],
            Projection.All,
            Paging.Keyset(2)));
        var jobs = result.Rows.Select(GroundworkV2WorkflowAlterationStorageConventions.DeserializeJob).ToArray();
        if (jobs.Length > 1)
            throw new InvalidOperationException("Groundwork found multiple alteration jobs for one checkpoint commit.");
        if (jobs.Length == 1)
            EnsureTenant(jobs[0].Job.TenantPartition);
        return ValueTask.FromResult(jobs.SingleOrDefault()?.Job);
    }

    public async ValueTask<WorkflowAlterationJobPage> PageJobsAsync(
        string planId,
        int pageSize,
        string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        ValidateId(planId, nameof(planId));
        if (pageSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(pageSize));
        if (pageSize > MaximumQueryPage)
            throw new ArgumentOutOfRangeException(nameof(pageSize), $"Alteration job pages cannot exceed {MaximumQueryPage} rows.");
        cancellationToken.ThrowIfCancellationRequested();
        _ = await RequirePlanAsync(planId, cancellationToken);
        var result = QueryJobs(
            OpenScoped(jobUnit),
            planId,
            null,
            pageSize,
            null,
            true,
            continuationToken: cursor);
        var jobs = result.Rows.Select(ReadJob).Select(document => document.Job).ToArray();
        foreach (var job in jobs)
            EnsureTenant(job.TenantPartition);
        return new WorkflowAlterationJobPage(
            jobs,
            result.NextContinuationToken,
            result.NextContinuationToken is not null);
    }

    public async ValueTask<WorkflowAlterationJobState?> ClaimNextAsync(
        string planId,
        string ownerId,
        DateTimeOffset now,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default)
    {
        ValidateId(planId, nameof(planId));
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        if (leaseDuration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(leaseDuration));
        cancellationToken.ThrowIfCancellationRequested();
        RequireAtomicCommit();

        for (var attempt = 0; attempt < TransitionAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var plan = await RequirePlanAsync(planId, cancellationToken);
            var candidate = FindClaimableCandidate(planId, now, runningOnly: true);
            candidate ??= plan.Status == WorkflowAlterationPlanStatus.Cancelling
                ? null
                : FindClaimableCandidate(planId, now, runningOnly: false);
            if (candidate is null)
                return null;

            using var unitOfWork = BeginAtomicUnitOfWork();
            var planSession = unitOfWork.OpenSession(planUnit);
            var jobSession = unitOfWork.OpenSession(jobUnit);
            var currentPlanEntry = planSession.Read(GroundworkRuntimeRowStore.Key(
                GroundworkV2WorkflowAlterationStorageConventions.PhysicalPlanId(planId)));
            var currentJobEntry = jobSession.Read(GroundworkRuntimeRowStore.Key(
                GroundworkV2WorkflowAlterationStorageConventions.PhysicalJobId(candidate.Job.JobId)));
            if (currentPlanEntry is null || currentJobEntry is null)
                continue;
            var currentPlanDocument = ReadPlan(currentPlanEntry, planId);
            var currentJobDocument = ReadJob(currentJobEntry, candidate.Job.JobId);
            EnsureTenant(currentPlanDocument.Plan.AuthorityScope.TenantPartition);
            EnsureTenant(currentJobDocument.Job.TenantPartition);
            if (!IsClaimable(currentPlanDocument.Plan, currentJobDocument.Job, now))
                continue;

            var claim = new WorkflowAlterationJobClaim(
                ownerId,
                Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16)),
                now + leaseDuration);
            var claimed = CopyJob(
                currentJobDocument.Job,
                status: WorkflowAlterationJobStatus.Running,
                claim: claim,
                attemptCount: checked(currentJobDocument.Job.AttemptCount + 1),
                startedAt: currentJobDocument.Job.StartedAt ?? now,
                revision: checked(currentJobDocument.Job.Revision + 1));
            unitOfWork.Stage(RowWrite.ConditionalUpsert(
                jobUnit,
                GroundworkV2WorkflowAlterationStorageConventions.Values(
                    GroundworkV2WorkflowAlterationStorageConventions.CreateJobDocument(claimed)),
                WriteOptions.IfVersion(RequiredVersion(currentJobEntry))));

            if (currentPlanDocument.Plan.Status == WorkflowAlterationPlanStatus.Queued)
            {
                var runningPlan = CopyPlan(
                    currentPlanDocument.Plan,
                    status: WorkflowAlterationPlanStatus.Running,
                    startedAt: currentPlanDocument.Plan.StartedAt ?? now,
                    revision: checked(currentPlanDocument.Plan.Revision + 1));
                unitOfWork.Stage(RowWrite.ConditionalUpsert(
                    planUnit,
                    GroundworkV2WorkflowAlterationStorageConventions.Values(
                        GroundworkV2WorkflowAlterationStorageConventions.CreatePlanDocument(runningPlan, currentPlanDocument.ActiveOrderKey, currentPlanDocument.UnsealedCaptureCleanup)),
                    WriteOptions.IfVersion(RequiredVersion(currentPlanEntry))));
            }

            var report = await CommitAsync(unitOfWork, cancellationToken);
            if (report.IsSuccessful)
                return claimed;
        }

        throw Concurrency(planId);
    }

    public async ValueTask ValidateTerminalJobChangeAsync(
        WorkflowAlterationJobTerminalChange change,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(change);
        cancellationToken.ThrowIfCancellationRequested();
        var job = await RequireJobAsync(change.JobId, cancellationToken);
        ValidateTerminalChange(change, job);
    }

    public async ValueTask ApplyTerminalJobChangeAsync(
        WorkflowAlterationJobTerminalChange change,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(change);
        cancellationToken.ThrowIfCancellationRequested();
        for (var attempt = 0; attempt < TransitionAttempts; attempt++)
        {
            var entry = OpenScoped(jobUnit).Read(GroundworkRuntimeRowStore.Key(
                GroundworkV2WorkflowAlterationStorageConventions.PhysicalJobId(change.JobId)));
            if (entry is null)
                throw new KeyNotFoundException($"Alteration job '{change.JobId}' was not found.");
            var document = ReadJob(entry, change.JobId);
            EnsureTenant(document.Job.TenantPartition);
            ValidateTerminalChange(change, document.Job);
            if (IsTerminal(document.Job.Status))
                return;

            var terminal = CopyJob(
                document.Job,
                status: change.Status,
                claim: document.Job.Claim,
                outcomes: change.Outcomes,
                checkpointCommitId: change.CheckpointCommitId,
                safeFailure: change.SafeFailure,
                completedAt: change.CompletedAt,
                revision: checked(document.Job.Revision + 1));
            var result = ConditionalUpsert(
                OpenScoped(jobUnit),
                GroundworkV2WorkflowAlterationStorageConventions.Values(
                    GroundworkV2WorkflowAlterationStorageConventions.CreateJobDocument(terminal)),
                RequiredVersion(entry));
            if (IsSaved(result.Status))
                return;
            if (result.Status != WriteOutcomeStatus.ConcurrencyConflict)
                throw new InvalidOperationException($"Groundwork rejected terminalization of alteration job '{change.JobId}' with status '{result.Status}'.");
        }

        throw ClaimFence(change.JobId);
    }

    public async ValueTask<WorkflowAlterationPlanState> ReconcileAsync(
        string planId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ValidateId(planId, nameof(planId));
        cancellationToken.ThrowIfCancellationRequested();
        for (var attempt = 0; attempt < TransitionAttempts; attempt++)
        {
            var loaded = await LoadPlanAsync(planId, cancellationToken)
                ?? throw new KeyNotFoundException($"Alteration plan '{planId}' was not found.");
            var plan = loaded.Document.Plan;
            if (IsTerminal(plan.Status))
                return plan;
            if (plan.SealedAt is null)
                return plan;
            var counts = await GetJobCountsAsync(planId, cancellationToken);
            if (counts.Pending > 0 || counts.Running > 0)
                return plan;
            var status = plan.Status == WorkflowAlterationPlanStatus.Cancelling
                ? WorkflowAlterationPlanStatus.Cancelled
                : counts.Failed > 0 ? WorkflowAlterationPlanStatus.CompletedWithFailures : WorkflowAlterationPlanStatus.Completed;
            var updated = CopyPlan(
                plan,
                status: status,
                targetCount: counts.Total,
                succeededJobCount: counts.Succeeded,
                failedJobCount: counts.Failed,
                cancelledJobCount: counts.Cancelled,
                completedAt: now,
                revision: checked(plan.Revision + 1));
            var result = ConditionalUpsert(
                OpenScoped(planUnit),
                GroundworkV2WorkflowAlterationStorageConventions.Values(
                    GroundworkV2WorkflowAlterationStorageConventions.CreatePlanDocument(updated, loaded.Document.ActiveOrderKey, loaded.Document.UnsealedCaptureCleanup)),
                loaded.Version);
            if (IsSaved(result.Status))
                return updated;
            if (result.Status != WriteOutcomeStatus.ConcurrencyConflict)
                throw new InvalidOperationException($"Groundwork rejected alteration-plan reconciliation for '{planId}' with status '{result.Status}'.");
        }
        throw Concurrency(planId);
    }

    private async ValueTask<WorkflowAlterationPlanState> TerminalizeUnsealedCaptureAsync(
        string planId,
        WorkflowAlterationPlanStatus terminalStatus,
        WorkflowAlterationSafeFailure? safeFailure,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken)
    {
        ValidateId(planId, nameof(planId));
        cancellationToken.ThrowIfCancellationRequested();
        RequireAtomicCommit();
        for (var attempt = 0; attempt < TransitionAttempts; attempt++)
        {
            var observed = await LoadPlanAsync(planId, cancellationToken)
                ?? throw new KeyNotFoundException($"Alteration plan '{planId}' was not found.");
            if (observed.Document.Plan.Status is not (WorkflowAlterationPlanStatus.CapturingTargets or WorkflowAlterationPlanStatus.Cancelling) || observed.Document.Plan.SealedAt is not null)
                return observed.Document.Plan;

            using var unitOfWork = BeginAtomicUnitOfWork();
            var planSession = unitOfWork.OpenSession(planUnit);
            var jobSession = unitOfWork.OpenSession(jobUnit);
            var planEntry = planSession.Read(GroundworkRuntimeRowStore.Key(
                GroundworkV2WorkflowAlterationStorageConventions.PhysicalPlanId(planId)));
            if (planEntry is null)
                throw new KeyNotFoundException($"Alteration plan '{planId}' was not found.");
            var current = ReadPlan(planEntry, planId);
            if (current.Plan.Revision != observed.Document.Plan.Revision || current.Plan.SealedAt is not null ||
                current.Plan.Status is not (WorkflowAlterationPlanStatus.CapturingTargets or WorkflowAlterationPlanStatus.Cancelling))
                continue;

            var cleanup = current.UnsealedCaptureCleanup ??
                (current.Plan.Status == WorkflowAlterationPlanStatus.Cancelling && current.Plan.CancellationRequestedAt is { } requestedAt
                    ? new WorkflowAlterationUnsealedCaptureCleanup(WorkflowAlterationPlanStatus.Cancelled, null, requestedAt)
                    : new WorkflowAlterationUnsealedCaptureCleanup(terminalStatus, safeFailure, completedAt));
            var candidates = QueryJobEntries(jobSession, planId, null, UnsealedCleanupPageSize, null, true);
            var deletedCount = 0L;
            foreach (var candidate in candidates)
            {
                var jobId = RequiredProjectionString(candidate, ElsaRuntimeV2StorageManifest.WorkflowAlterationJobIdField);
                var jobEntry = jobSession.Read(GroundworkRuntimeRowStore.Key(
                    GroundworkV2WorkflowAlterationStorageConventions.PhysicalJobId(jobId)));
                if (jobEntry is not null)
                {
                    unitOfWork.Stage(RowWrite.Delete(
                        jobUnit,
                        GroundworkRuntimeRowStore.Key(GroundworkV2WorkflowAlterationStorageConventions.PhysicalJobId(jobId)),
                        WriteOptions.IfVersion(RequiredVersion(jobEntry))));
                    deletedCount++;
                }
            }

            var deletedTotal = checked(cleanup.DeletedCount + deletedCount);
            var complete = deletedTotal >= current.Plan.CapturedSoFar;
            var updated = complete
                ? CopyPlan(
                    current.Plan,
                    status: cleanup.TerminalStatus,
                    captureCursor: null,
                    setCaptureCursor: true,
                    targetCount: 0,
                    succeededJobCount: 0,
                    failedJobCount: 0,
                    cancelledJobCount: 0,
                    completedAt: cleanup.CompletedAt,
                    cancellationRequestedAt: cleanup.TerminalStatus == WorkflowAlterationPlanStatus.Cancelled ? cleanup.CompletedAt : null,
                    safeFailure: cleanup.SafeFailure,
                    revision: checked(current.Plan.Revision + 1))
                : CopyPlan(
                    current.Plan,
                    status: WorkflowAlterationPlanStatus.Cancelling,
                    cancellationRequestedAt: cleanup.TerminalStatus == WorkflowAlterationPlanStatus.Cancelled ? cleanup.CompletedAt : null,
                    revision: checked(current.Plan.Revision + 1));
            unitOfWork.Stage(RowWrite.ConditionalUpsert(
                planUnit,
                GroundworkV2WorkflowAlterationStorageConventions.Values(
                    GroundworkV2WorkflowAlterationStorageConventions.CreatePlanDocument(
                        updated,
                        current.ActiveOrderKey,
                        complete ? null : cleanup with { DeletedCount = deletedTotal })),
                WriteOptions.IfVersion(RequiredVersion(planEntry))));
            var report = await CommitAsync(unitOfWork, cancellationToken);
            if (report.IsSuccessful)
                return updated;
        }
        throw Concurrency(planId);
    }

    private async ValueTask<WorkflowAlterationPlanState> UpdatePlanAsync(
        string planId,
        Func<WorkflowAlterationPlanState, WorkflowAlterationPlanDocument, (WorkflowAlterationPlanState Plan, string ActiveOrderKey)> transition,
        CancellationToken cancellationToken,
        bool requireExpectedRevision = false)
    {
        ValidateId(planId, nameof(planId));
        for (var attempt = 0; attempt < TransitionAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var loaded = await LoadPlanAsync(planId, cancellationToken)
                ?? throw new KeyNotFoundException($"Alteration plan '{planId}' was not found.");
            var transitioned = transition(loaded.Document.Plan, loaded.Document);
            if (ReferenceEquals(transitioned.Plan, loaded.Document.Plan) || transitioned.Plan == loaded.Document.Plan)
                return transitioned.Plan;
            var result = ConditionalUpsert(
                OpenScoped(planUnit),
                GroundworkV2WorkflowAlterationStorageConventions.Values(
                    GroundworkV2WorkflowAlterationStorageConventions.CreatePlanDocument(transitioned.Plan, transitioned.ActiveOrderKey, loaded.Document.UnsealedCaptureCleanup)),
                loaded.Version);
            if (IsSaved(result.Status))
                return transitioned.Plan;
            if (result.Status != WriteOutcomeStatus.ConcurrencyConflict)
                throw new InvalidOperationException($"Groundwork rejected alteration-plan update for '{planId}' with status '{result.Status}'.");
            if (requireExpectedRevision)
                throw Concurrency(planId);
        }
        throw Concurrency(planId);
    }

    private async ValueTask<WorkflowAlterationPlanState> RequirePlanAsync(
        string planId,
        CancellationToken cancellationToken) =>
        await FindPlanAsync(planId, cancellationToken) ??
        throw new KeyNotFoundException($"Alteration plan '{planId}' was not found.");

    private async ValueTask<WorkflowAlterationJobState> RequireJobAsync(string jobId, CancellationToken cancellationToken) =>
        await FindJobAsync(jobId, cancellationToken) ?? throw new KeyNotFoundException($"Alteration job '{jobId}' was not found.");

    private ValueTask<LoadedPlan?> LoadPlanAsync(string planId, CancellationToken cancellationToken)
    {
        ValidateId(planId, nameof(planId));
        cancellationToken.ThrowIfCancellationRequested();
        var entry = OpenScoped(planUnit).Read(GroundworkRuntimeRowStore.Key(
            GroundworkV2WorkflowAlterationStorageConventions.PhysicalPlanId(planId)));
        if (entry is null)
            return ValueTask.FromResult<LoadedPlan?>(null);
        var document = ReadPlan(entry, planId);
        EnsureTenant(document.Plan.AuthorityScope.TenantPartition);
        return ValueTask.FromResult<LoadedPlan?>(new LoadedPlan(document, RequiredVersion(entry)));
    }

    private WorkflowAlterationJobDocument? FindClaimableCandidate(string planId, DateTimeOffset now, bool runningOnly)
    {
        var statuses = runningOnly
            ? new[] { WorkflowAlterationJobStatus.Running }
            : new[] { WorkflowAlterationJobStatus.Pending };
        var entries = QueryJobEntries(OpenScoped(jobUnit), planId, statuses, 1, now, false);
        return entries.Count == 0 ? null : ReadJob(entries[0]);
    }

    private List<IReadOnlyDictionary<string, object?>> QueryJobEntries(
        IStorageSession session,
        string planId,
        IReadOnlyCollection<WorkflowAlterationJobStatus>? statuses,
        int take,
        DateTimeOffset? claimableBefore,
        bool orderByCapture)
    {
        return QueryJobs(session, planId, statuses, take, claimableBefore, orderByCapture).Rows.ToList();
    }

    private QueryMaterializedResult QueryJobs(
        IStorageSession session,
        string planId,
        IReadOnlyCollection<WorkflowAlterationJobStatus>? statuses,
        int take,
        DateTimeOffset? claimableBefore,
        bool orderByCapture,
        bool countOnly = false,
        string? continuationToken = null)
    {
        var table = new TableId(jobUnit.Name);
        var predicates = new List<Predicate>
        {
            Equal(Column(table, ElsaRuntimeV2StorageManifest.WorkflowAlterationJobPlanIdField), planId)
        };
        if (statuses is { Count: > 0 })
        {
            var statusColumn = Column(table, ElsaRuntimeV2StorageManifest.WorkflowAlterationJobStatusField);
            predicates.Add(statuses.Count == 1
                ? Equal(statusColumn, statuses.Single().ToString())
                : new Predicate.Or(statuses.Select(status => Equal(statusColumn, status.ToString())).ToArray()));
        }
        if (claimableBefore is not null)
        {
            var claimable = Column(table, ElsaRuntimeV2StorageManifest.WorkflowAlterationJobClaimableAtField);
            predicates.Add(new Predicate.Range(claimable, null, Bound.Inclusive(QueryConstant.Of(claimable, claimableBefore.Value))));
        }
        var ordinalColumn = Column(table, ElsaRuntimeV2StorageManifest.WorkflowAlterationJobCaptureOrdinalField);
        var idColumn = Column(table, ElsaRuntimeV2StorageManifest.WorkflowAlterationJobIdField);
        var claimableColumn = Column(table, ElsaRuntimeV2StorageManifest.WorkflowAlterationJobClaimableAtField);
        var order = orderByCapture
            ? System.Collections.Immutable.ImmutableArray.Create(
                new OrderTerm(ordinalColumn, OrderDirection.Ascending, NullOrder.Last),
                new OrderTerm(idColumn, OrderDirection.Ascending, NullOrder.Last))
            : System.Collections.Immutable.ImmutableArray.Create(
                new OrderTerm(claimableColumn, OrderDirection.Ascending, NullOrder.Last),
                new OrderTerm(idColumn, OrderDirection.Ascending, NullOrder.Last));
        var result = countOnly
            ? session.Query(new QueryRequest(
                table,
                Combine(predicates),
                order,
                Projection.ColumnsOnly(idColumn),
                Paging.Keyset(1),
                ResultShape.TotalCount.Instance))
            : QueryWithBoundCursor(
                session,
                new QueryRequest(
                    table,
                    Combine(predicates),
                    order,
                    Projection.All,
                    continuationToken is null
                        ? Paging.Keyset(Math.Min(Math.Max(1, take), MaximumQueryPage))
                        : Paging.Continuation(continuationToken, Math.Min(Math.Max(1, take), MaximumQueryPage))),
                continuationToken,
                "alteration-job");
        if (result.Rows.Count == 0 && result.NextContinuationToken is not null)
            throw new InvalidDataException("Groundwork alteration-job query returned a continuation after an empty page.");
        return result;
    }

    private List<WorkflowAlterationPlanDocument> QueryPlansByIdempotency(
        IStorageSession session,
        string tenantPartition,
        string idempotencyKeyHash,
        int take)
    {
        var table = new TableId(planUnit.Name);
        var tenant = Column(table, ElsaRuntimeV2StorageManifest.WorkflowAlterationPlanTenantPartitionField);
        var hash = Column(table, ElsaRuntimeV2StorageManifest.WorkflowAlterationPlanIdempotencyKeyHashField);
        var id = Column(table, ElsaRuntimeV2StorageManifest.WorkflowAlterationPlanIdField);
        var result = session.Query(new QueryRequest(
            table,
            new Predicate.And([Equal(tenant, tenantPartition), Equal(hash, idempotencyKeyHash)]),
            [new OrderTerm(id, OrderDirection.Ascending, NullOrder.Last)],
            Projection.All,
            Paging.Keyset(Math.Min(Math.Max(1, take), 2))));
        return result.Rows.Select(GroundworkV2WorkflowAlterationStorageConventions.DeserializePlan).ToList();
    }

    private long CountJobs(IStorageSession session, string planId, WorkflowAlterationJobStatus status)
    {
        return QueryJobs(session, planId, [status], 1, null, true, countOnly: true).TotalCount ??
               throw new InvalidDataException("Groundwork alteration-job count did not return its provider-side total.");
    }

    private bool IsClaimable(WorkflowAlterationPlanState plan, WorkflowAlterationJobState job, DateTimeOffset now) =>
        StringComparer.Ordinal.Equals(plan.PlanId, job.PlanId) &&
        plan.Status is WorkflowAlterationPlanStatus.Queued or WorkflowAlterationPlanStatus.Running or WorkflowAlterationPlanStatus.Cancelling &&
        ((job.Status == WorkflowAlterationJobStatus.Pending && plan.Status != WorkflowAlterationPlanStatus.Cancelling && job.CreatedAt <= now) ||
         (job.Status == WorkflowAlterationJobStatus.Running && job.Claim is not null && job.Claim.ExpiresAt <= now));

    private IStorageSession OpenScoped(StorageUnit unit) => sessions.Open(unit.Id.Value, ScopedAccess, targetName);

    private StorageAccess ScopedAccess
    {
        get
        {
            var context = AccessContext;
            if (context.Scope is null || context.AcrossScopes)
                throw new InvalidOperationException("Groundwork workflow alterations require one explicit persistence scope for this operation.");
            return StorageAccess.Scoped(new StorageScope(context.Scope.Value));
        }
    }

    private PersistenceAccessContext AccessContext => accessContextAccessor.Current ??
        throw new InvalidOperationException("Groundwork workflow-alteration persistence access context is missing.");

    private void EnsureTenant(string tenantPartition) => AccessContext.EnsureTenantScope(tenantPartition);

    private IUnitOfWork BeginAtomicUnitOfWork() => sessions.BeginUnitOfWork(
        ScopedAccess,
        BatchWriteOptions.Exact,
        [ElsaRuntimeV2StorageManifest.WorkflowAlterationPlanDocumentKind, ElsaRuntimeV2StorageManifest.WorkflowAlterationJobDocumentKind],
        targetName);

    private void RequireAtomicCommit()
    {
        if (sessions is not IGroundworkStorageCapabilitySource source ||
            !source.Capabilities(targetName).Any(capability => capability.Id.Equals(WellKnownCapabilities.AtomicCommit)))
        {
            throw new NotSupportedException("Groundwork workflow alterations require the provider's evidenced atomic-commit capability.");
        }
    }

    private async ValueTask<BatchWriteReport> CommitAsync(IUnitOfWork unitOfWork, CancellationToken cancellationToken)
    {
        try
        {
            var report = await unitOfWork.CommitWithOutcomesAsync(cancellationToken);
            if (!report.IsSuccessful)
            {
                try
                { unitOfWork.Rollback(); }
                catch { /* Preserve attributed outcomes. */ }
            }
            return report;
        }
        catch
        {
            try
            { unitOfWork.Rollback(); }
            catch { /* Preserve provider failure. */ }
            throw;
        }
    }

    private static WorkflowAlterationPlanDocument ReadPlan(StoredEntry entry, string requestedPlanId)
    {
        var document = GroundworkV2WorkflowAlterationStorageConventions.DeserializePlan(entry.Values.Values);
        if (!StringComparer.Ordinal.Equals(document.Plan.PlanId, requestedPlanId))
            throw new InvalidDataException($"Groundwork alteration-plan physical identity collision detected for '{requestedPlanId}'.");
        return document;
    }

    private static WorkflowAlterationJobDocument ReadJob(IReadOnlyDictionary<string, object?> values) =>
        GroundworkV2WorkflowAlterationStorageConventions.DeserializeJob(values);

    private static WorkflowAlterationJobDocument ReadJob(StoredEntry entry, string requestedJobId)
    {
        var document = GroundworkV2WorkflowAlterationStorageConventions.DeserializeJob(entry.Values.Values);
        if (!StringComparer.Ordinal.Equals(document.Job.JobId, requestedJobId))
            throw new InvalidDataException($"Groundwork alteration-job physical identity collision detected for '{requestedJobId}'.");
        return document;
    }

    private static QueryMaterializedResult QueryWithBoundCursor(
        IStorageSession session,
        QueryRequest request,
        string? cursor,
        string cursorKind)
    {
        try
        {
            return session.Query(request);
        }
        catch (Exception exception) when (
            cursor is not null &&
            (exception is QueryRenderException { Code: "GW-QUERY-013" } ||
             exception is FormatException ||
             exception.InnerException is FormatException))
        {
            throw new ArgumentException(
                $"The {cursorKind} cursor is invalid or does not belong to this query.",
                nameof(cursor),
                exception);
        }
    }

    private static IEnumerable<WorkflowAlterationCapturedTarget> NormalizeTargets(
        IReadOnlyCollection<WorkflowAlterationCapturedTarget> targets)
    {
        foreach (var group in targets
                     .GroupBy(target => target.WorkflowExecutionId, StringComparer.Ordinal)
                     .OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            var target = group.First();
            if (group.Any(candidate => candidate != target))
            {
                throw new InvalidOperationException(
                    $"Captured target '{group.Key}' was supplied with conflicting immutable evidence.");
            }

            yield return target;
        }
    }

    private static long RequiredVersion(StoredEntry entry) => entry.Version ?? throw new InvalidDataException("Groundwork alteration row did not expose an optimistic revision.");

    private static string RequiredProjectionString(IReadOnlyDictionary<string, object?> values, string field) =>
        values.TryGetValue(field, out var raw) switch
        {
            true when raw is string text && !string.IsNullOrWhiteSpace(text) => text,
            true when raw is System.Text.Json.JsonElement { ValueKind: System.Text.Json.JsonValueKind.String } element && !string.IsNullOrWhiteSpace(element.GetString()) => element.GetString()!,
            _ => throw new InvalidDataException($"Groundwork alteration query row is missing required projection '{field}'.")
        };

    private static WriteOutcome ConditionalUpsert(IStorageSession session, StorageValues values, long revision)
    {
        if (session is not IConcurrencyStorageSession concurrency)
            throw new NotSupportedException("The selected Groundwork provider does not advertise optimistic alteration concurrency.");
        return concurrency.ConditionalUpsert(values, WriteOptions.IfVersion(revision));
    }

    private ColumnRef Column(TableId table, string name)
    {
        var definition = (table.Value == planUnit.Name ? planUnit : jobUnit).Columns.SingleOrDefault(column => StringComparer.Ordinal.Equals(column.Name, name))
            ?? throw new InvalidOperationException($"Groundwork alteration unit '{table.Value}' does not declare query column '{name}'.");
        var type = definition.Type switch
        {
            PortableType.String => QueryType.String,
            PortableType.DateTimeOffset => QueryType.DateTimeOffset,
            PortableType.Int64 => QueryType.Int64,
            _ => throw new InvalidOperationException($"Groundwork alteration query column '{name}' has unsupported type '{definition.Type}'.")
        };
        return new ColumnRef(table, name, type, definition.IsNullable, definition.MaxLength);
    }

    private static Predicate Equal(ColumnRef column, string value) => new Predicate.Equal(column, QueryConstant.Of(column, value));
    private static Predicate Equal(ColumnRef column, long value) => new Predicate.Equal(column, QueryConstant.Of(column, value));
    private static Predicate Combine(IReadOnlyList<Predicate> predicates) => predicates.Count switch
    {
        0 => Predicate.AlwaysTrue.Instance,
        1 => predicates[0],
        _ => new Predicate.And(predicates)
    };

    private static bool IsSaved(WriteOutcomeStatus status) => status is WriteOutcomeStatus.Inserted or WriteOutcomeStatus.Updated or WriteOutcomeStatus.Upserted or WriteOutcomeStatus.Replayed;

    private static InvalidOperationException Concurrency(string planId) =>
        new($"The alteration plan '{planId}' changed while the operation was in progress.");

    private static InvalidOperationException ClaimFence(string jobId) =>
        new($"The alteration job claim for '{jobId}' is no longer current.");

    private static void ValidateId(string value, string parameterName) =>
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

    private static void EnsureSameAdmission(WorkflowAlterationPlanState existing, WorkflowAlterationPlanState candidate)
    {
        if (!StringComparer.Ordinal.Equals(existing.CanonicalRequestHash, candidate.CanonicalRequestHash))
            throw new InvalidOperationException($"The idempotency key is already associated with alteration plan '{existing.PlanId}' and a different request.");
    }

    private static void EnsureSingleIdempotencyMatch(
        IReadOnlyCollection<WorkflowAlterationPlanDocument> matches,
        string idempotencyKeyHash)
    {
        if (matches.Count > 1)
        {
            throw new InvalidDataException(
                $"Groundwork found multiple alteration plans for idempotency identity '{idempotencyKeyHash}'.");
        }
    }

    private static bool IsTerminal(WorkflowAlterationPlanStatus status) => status is WorkflowAlterationPlanStatus.Completed or WorkflowAlterationPlanStatus.CompletedWithFailures or WorkflowAlterationPlanStatus.Failed or WorkflowAlterationPlanStatus.Cancelled;
    private static bool IsTerminal(WorkflowAlterationJobStatus status) => status is WorkflowAlterationJobStatus.Succeeded or WorkflowAlterationJobStatus.Failed or WorkflowAlterationJobStatus.Cancelled;
    private static string NextActivePlanOrderKey(string current, DateTimeOffset servicedAt, string planId)
    {
        var separator = current.IndexOf(':', StringComparison.Ordinal);
        var currentTicks = long.Parse(current.AsSpan(0, separator), CultureInfo.InvariantCulture);
        return $"{Math.Max(servicedAt.UtcTicks, checked(currentTicks + 1)):D19}:{planId}";
    }

    private static void ValidateTerminalChange(WorkflowAlterationJobTerminalChange change, WorkflowAlterationJobState job)
    {
        if (IsTerminal(job.Status))
        {
            if (StringComparer.Ordinal.Equals(job.CheckpointCommitId, change.CheckpointCommitId) && job.Status == change.Status && job.CompletedAt == change.CompletedAt && job.SafeFailure == change.SafeFailure && OutcomesEqual(job.Outcomes, change.Outcomes))
                return;
            throw new InvalidOperationException("A terminal alteration job cannot be terminalized with conflicting checkpoint evidence.");
        }
        if (job.Status != WorkflowAlterationJobStatus.Running || job.Claim is null || !StringComparer.Ordinal.Equals(job.Claim.Token, change.ClaimToken))
            throw ClaimFence(change.JobId);
    }

    private static bool OutcomesEqual(IReadOnlyList<WorkflowAlterationOutcome> left, IReadOnlyCollection<WorkflowAlterationOutcome> right) =>
        left.Count == right.Count && left.Zip(right.OrderBy(outcome => outcome.Ordinal)).All(pair =>
            pair.First.Ordinal == pair.Second.Ordinal && pair.First.Kind == pair.Second.Kind && pair.First.SchemaVersion == pair.Second.SchemaVersion && pair.First.Status == pair.Second.Status && pair.First.Code == pair.Second.Code && pair.First.Message == pair.Second.Message && pair.First.RecordedAt == pair.Second.RecordedAt && pair.First.StructuralMetadata.OrderBy(entry => entry.Key).SequenceEqual(pair.Second.StructuralMetadata.OrderBy(entry => entry.Key)));

    private static WorkflowAlterationPlanState CopyPlan(
        WorkflowAlterationPlanState plan,
        WorkflowAlterationPlanStatus? status = null,
        bool setCaptureCursor = false,
        string? captureCursor = null,
        long? capturedSoFar = null,
        long? targetCount = null,
        long? succeededJobCount = null,
        long? failedJobCount = null,
        long? cancelledJobCount = null,
        DateTimeOffset? sealedAt = null,
        DateTimeOffset? startedAt = null,
        DateTimeOffset? completedAt = null,
        DateTimeOffset? cancellationRequestedAt = null,
        WorkflowAlterationSafeFailure? safeFailure = null,
        long? revision = null) =>
        new(
            plan.PlanId,
            plan.AuthorityScope,
            plan.SubmittedBy,
            plan.IdempotencyKeyHash,
            plan.CanonicalRequestHash,
            plan.ProtectedPayload,
            plan.Target,
            status ?? plan.Status,
            plan.CreatedAt,
            setCaptureCursor ? captureCursor : plan.CaptureCursor,
            capturedSoFar ?? plan.CapturedSoFar,
            targetCount ?? plan.TargetCount,
            succeededJobCount ?? plan.SucceededJobCount,
            failedJobCount ?? plan.FailedJobCount,
            cancelledJobCount ?? plan.CancelledJobCount,
            sealedAt ?? plan.SealedAt,
            startedAt ?? plan.StartedAt,
            completedAt ?? plan.CompletedAt,
            cancellationRequestedAt ?? plan.CancellationRequestedAt,
            safeFailure ?? plan.SafeFailure,
            revision ?? plan.Revision,
            plan.AlterationDescriptors);

    private static WorkflowAlterationJobState CopyJob(
        WorkflowAlterationJobState job,
        WorkflowAlterationJobStatus? status = null,
        WorkflowAlterationJobClaim? claim = null,
        int? attemptCount = null,
        IReadOnlyCollection<WorkflowAlterationOutcome>? outcomes = null,
        string? checkpointCommitId = null,
        WorkflowAlterationSafeFailure? safeFailure = null,
        DateTimeOffset? startedAt = null,
        DateTimeOffset? completedAt = null,
        long? revision = null) =>
        new(
            job.JobId,
            job.PlanId,
            job.WorkflowExecutionId,
            job.TenantPartition,
            job.CaptureOrdinal,
            status ?? job.Status,
            claim,
            attemptCount ?? job.AttemptCount,
            (outcomes ?? job.Outcomes).ToArray(),
            checkpointCommitId ?? job.CheckpointCommitId,
            safeFailure ?? job.SafeFailure,
            job.CreatedAt,
            startedAt ?? job.StartedAt,
            completedAt ?? job.CompletedAt,
            revision ?? job.Revision,
            job.CapturedConcurrency);

}

internal sealed record LoadedPlan(WorkflowAlterationPlanDocument Document, long Version)
{
    public WorkflowAlterationPlanState Plan => Document.Plan;
}
