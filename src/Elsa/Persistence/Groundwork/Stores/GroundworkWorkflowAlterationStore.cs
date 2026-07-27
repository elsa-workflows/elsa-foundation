using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork.Serialization;
using Elsa.Workflows.Runtime.Core.Contracts.Alterations;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Models.Alterations;
using Elsa.Workflows.Runtime.Services.Alterations;
using Groundwork.Core.Queries;
using Groundwork.Core.PhysicalStorage;
using Groundwork.Core.Transactions;
using Groundwork.Documents.Scoping;
using Groundwork.Documents.Store;
using Groundwork.Documents.UnitOfWork;

namespace Elsa.Persistence.Groundwork.Stores;

/// <summary>
/// Groundwork implementation of the durable alteration plan and target-job ledger. The plan and every job are
/// scope-bound documents; target capture writes the page and its plan cursor in one cross-unit transaction.
/// </summary>
public sealed class GroundworkWorkflowAlterationStore : IWorkflowAlterationStore
{
    private const int TransitionAttempts = 16;
    private const int UnsealedCleanupPageSize = 100;
    private readonly IDocumentStore _store;
    private readonly IGroundworkRuntimeDocumentSerializer _serializer;
    private readonly IPersistenceAccessContextAccessor _accessContextAccessor;
    private readonly IBoundedDocumentStore? _boundedStore;

    public GroundworkWorkflowAlterationStore(
        IDocumentStore store,
        IGroundworkRuntimeDocumentSerializer serializer,
        IPersistenceAccessContextAccessor accessContextAccessor,
        IBoundedDocumentStore? boundedStore = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        _accessContextAccessor = accessContextAccessor ?? throw new ArgumentNullException(nameof(accessContextAccessor));
        _boundedStore = boundedStore ?? store as IBoundedDocumentStore;
    }

    private IBoundedDocumentStore Queries => _boundedStore ?? throw new InvalidOperationException(
        "Workflow-alteration queries require an admitted bounded document-store runtime.");

    public async ValueTask<WorkflowAlterationPlanAdmissionResult> AdmitAsync(
        WorkflowAlterationPlanState plan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        _accessContextAccessor.Current.EnsureTenantScope(plan.AuthorityScope.TenantPartition);
        var existing = await FindPlanForAdmissionAsync(plan.AuthorityScope.TenantPartition, plan.IdempotencyKeyHash, cancellationToken);
        if (existing is not null)
        {
            EnsureSameAdmission(existing, plan);
            return new(existing, true);
        }

        var result = await SavePlanAsync(_store, plan, 0, cancellationToken);
        if (result.Status == DocumentStoreWriteStatus.Saved)
            return new(plan, false);
        if (result.Status != DocumentStoreWriteStatus.ConcurrencyConflict)
            throw Rejected("admit", plan.PlanId, result);

        existing = await FindPlanForAdmissionAsync(plan.AuthorityScope.TenantPartition, plan.IdempotencyKeyHash, cancellationToken)
            ?? throw new InvalidOperationException($"Alteration plan '{plan.PlanId}' conflicted during admission but could not be reloaded.");
        EnsureSameAdmission(existing, plan);
        return new(existing, true);
    }

    public async ValueTask<WorkflowAlterationPlanState?> FindPlanAsync(string planId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(planId);
        var loaded = await LoadPlanAsync(_store, planId, cancellationToken);
        if (loaded is not null)
            _accessContextAccessor.Current.EnsureTenantScope(loaded.Value.Document.Plan.AuthorityScope.TenantPartition);
        return loaded?.Document.Plan;
    }

    public async ValueTask<WorkflowAlterationActivePlanPage> ListActivePlansAsync(int pageSize, string? cursor = null, CancellationToken cancellationToken = default)
    {
        if (pageSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(pageSize));
        if (cursor is not null && string.IsNullOrWhiteSpace(cursor))
            throw new ArgumentException("The active alteration plan cursor cannot be blank.", nameof(cursor));

        // The coordinator normally executes once per persistence scope. An ordinary global session must never become
        // implicit cross-tenant coordination; only a named, privileged across-scope context may enumerate all plans.
        var access = _accessContextAccessor.Current;
        var tenantPartition = access.Scope?.Value;
        if (tenantPartition is null && !access.AcrossScopes)
            throw new InvalidOperationException(
                "Active alteration-plan discovery requires a tenant-scoped persistence context; cross-tenant coordination must be explicitly privileged.");

        var activeStatuses = new[]
        {
            WorkflowAlterationPlanStatus.CapturingTargets,
            WorkflowAlterationPlanStatus.Queued,
            WorkflowAlterationPlanStatus.Running,
            WorkflowAlterationPlanStatus.Cancelling
        };
        var pages = await Task.WhenAll(activeStatuses.Select(async status =>
        {
            DocumentQueryClause[] clauses = tenantPartition is null
                ? [DocumentQueryClause.Of(DocumentQueryComparison.Equal(ElsaRuntimeStorageManifest.WorkflowAlterationPlanStatusField, status.ToString()))]
                :
                [
                    DocumentQueryClause.Of(DocumentQueryComparison.Equal(ElsaRuntimeStorageManifest.WorkflowAlterationPlanTenantPartitionField, tenantPartition)),
                    DocumentQueryClause.Of(DocumentQueryComparison.Equal(ElsaRuntimeStorageManifest.WorkflowAlterationPlanStatusField, status.ToString()))
                ];
            if (cursor is not null)
            {
                clauses =
                [
                    .. clauses,
                    DocumentQueryClause.Of(DocumentQueryComparison.GreaterThan(
                        ElsaRuntimeStorageManifest.WorkflowAlterationPlanActiveOrderKeyField,
                        cursor))
                ];
            }
            return await Queries.QueryAsync(
                new DocumentQuery(
                    ElsaRuntimeStorageManifest.WorkflowAlterationPlanDocumentKind,
                    tenantPartition is null
                        ? ElsaRuntimeStorageManifest.PageActiveWorkflowAlterationPlansQuery
                        : ElsaRuntimeStorageManifest.PageActiveWorkflowAlterationPlansByTenantQuery,
                    clauses,
                    [new DocumentQueryOrder(ElsaRuntimeStorageManifest.WorkflowAlterationPlanActiveOrderKeyField, PhysicalSortDirection.Ascending)],
                    take: pageSize + 1), cancellationToken);
        }));
        var items = pages.SelectMany(page => page.Documents)
            .Select(_serializer.Deserialize<WorkflowAlterationPlanDocument>)
            .Where(document => cursor is null || StringComparer.Ordinal.Compare(document.ActiveOrderKey, cursor) > 0)
            .OrderBy(document => document.ActiveOrderKey, StringComparer.Ordinal)
            .Take(pageSize + 1)
            .ToArray();
        if (tenantPartition is not null)
            foreach (var document in items)
                _accessContextAccessor.Current.EnsureTenantScope(document.Plan.AuthorityScope.TenantPartition);
        var hasNext = items.Length > pageSize;
        var pageItems = hasNext ? items[..pageSize] : items;
        return new(pageItems.Select(document => document.Plan).ToArray(), hasNext ? pageItems[^1].ActiveOrderKey : null, hasNext);
    }

    public async ValueTask RescheduleActivePlanAsync(string planId, DateTimeOffset servicedAt, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(planId);
        for (var attempt = 0; attempt < TransitionAttempts; attempt++)
        {
            var loaded = await LoadPlanAsync(_store, planId, cancellationToken)
                ?? throw new KeyNotFoundException($"Alteration plan '{planId}' was not found.");
            _accessContextAccessor.Current.EnsureTenantScope(loaded.Document.Plan.AuthorityScope.TenantPartition);
            if (IsTerminal(loaded.Document.Plan.Status))
                return;
            var document = loaded.Document with
            {
                ActiveOrderKey = NextActivePlanOrderKey(loaded.Document.ActiveOrderKey, servicedAt, planId)
            };
            var saved = await SaveAsync(
                _store,
                ElsaRuntimeStorageManifest.WorkflowAlterationPlanDocumentKind,
                PhysicalId(planId),
                document,
                loaded.Envelope.Version,
                cancellationToken);
            if (saved.Status == DocumentStoreWriteStatus.Saved)
                return;
            if (saved.Status != DocumentStoreWriteStatus.ConcurrencyConflict)
                throw Rejected("reschedule", planId, saved);
        }
        throw new WorkflowAlterationConcurrencyException(planId);
    }

    public async ValueTask<WorkflowAlterationPlanState> CaptureAsync(
        string planId,
        long expectedRevision,
        IReadOnlyCollection<WorkflowAlterationCapturedTarget> targets,
        string? nextCursor,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(planId);
        ArgumentNullException.ThrowIfNull(targets);
        if (_store.TransactionBoundary != TransactionBoundary.CrossUnitAtomic)
            throw new InvalidOperationException("Groundwork target capture requires cross-unit atomic transactions.");

        await using var unit = await _store.BeginAsync(
            DocumentCommitScope.Of(
                ElsaRuntimeStorageManifest.WorkflowAlterationPlanDocumentKind,
                ElsaRuntimeStorageManifest.WorkflowAlterationJobDocumentKind),
            cancellationToken);
        var transactional = new GroundworkDocumentUnitOfWorkStore(_store, unit);
        var loaded = await LoadPlanAsync(transactional, planId, cancellationToken)
            ?? throw new KeyNotFoundException($"Alteration plan '{planId}' was not found.");
        var plan = loaded.Document.Plan;
        _accessContextAccessor.Current.EnsureTenantScope(plan.AuthorityScope.TenantPartition);
        EnsureRevision(plan, expectedRevision);
        if (plan.Status != WorkflowAlterationPlanStatus.CapturingTargets)
            throw new InvalidOperationException("Only a capturing alteration plan can accept target pages.");

        var ordinal = plan.CapturedSoFar;
        foreach (var target in targets.OrderBy(target => target.WorkflowExecutionId, StringComparer.Ordinal))
        {
            if (!StringComparer.Ordinal.Equals(target.TenantPartition, plan.AuthorityScope.TenantPartition))
                throw new InvalidOperationException("A captured target must belong to the plan tenant partition.");
            var jobId = CreateJobId(plan.PlanId, target.WorkflowExecutionId);
            if (await LoadJobAsync(transactional, jobId, cancellationToken) is not null)
                continue;
            var missing = target.SafeFailure is not null;
            var job = new WorkflowAlterationJobState(
                jobId,
                plan.PlanId,
                target.WorkflowExecutionId,
                target.TenantPartition,
                ordinal++,
                missing ? WorkflowAlterationJobStatus.Failed : WorkflowAlterationJobStatus.Pending,
                null,
                0,
                [],
                null,
                target.SafeFailure,
                plan.CreatedAt,
                null,
                missing ? plan.CreatedAt : null,
                0,
                target.CapturedConcurrency);
            var created = await SaveJobAsync(transactional, job, 0, cancellationToken);
            if (created.Status == DocumentStoreWriteStatus.ConcurrencyConflict)
                throw new WorkflowAlterationConcurrencyException(plan.PlanId);
            if (created.Status != DocumentStoreWriteStatus.Saved)
                throw Rejected("capture target", jobId, created);
        }

        var updated = CopyPlan(plan, changeCaptureCursor: true, captureCursor: nextCursor, capturedSoFar: ordinal, revision: plan.Revision + 1);
        var saved = await SavePlanAsync(transactional, updated, loaded.Envelope.Version, cancellationToken);
        if (saved.Status == DocumentStoreWriteStatus.ConcurrencyConflict)
            throw new WorkflowAlterationConcurrencyException(plan.PlanId);
        if (saved.Status != DocumentStoreWriteStatus.Saved)
            throw Rejected("capture", planId, saved);
        await unit.CommitAsync(cancellationToken);
        return updated;
    }

    public async ValueTask<WorkflowAlterationPlanState> SealAsync(string planId, long expectedRevision, DateTimeOffset sealedAt, CancellationToken cancellationToken = default)
    {
        for (var attempt = 0; attempt < TransitionAttempts; attempt++)
        {
            var loaded = await LoadPlanAsync(_store, planId, cancellationToken)
                ?? throw new KeyNotFoundException($"Alteration plan '{planId}' was not found.");
            var plan = loaded.Document.Plan;
            _accessContextAccessor.Current.EnsureTenantScope(plan.AuthorityScope.TenantPartition);
            EnsureRevision(plan, expectedRevision);
            if (loaded.Document.UnsealedCaptureCleanup is not null)
                return plan;
            var targetCount = plan.CapturedSoFar;
            WorkflowAlterationPlanState sealedPlan;
            if (plan.Status == WorkflowAlterationPlanStatus.Cancelling)
                // Preserve the cancellation barrier until the coordinator has written a safe skipped
                // outcome for every captured but unclaimed job. Reconciliation owns the terminal
                // transition and aggregate counts.
                sealedPlan = CopyPlan(plan, targetCount: targetCount, sealedAt: sealedAt, revision: plan.Revision + 1);
            else if (plan.Status != WorkflowAlterationPlanStatus.CapturingTargets)
                throw new InvalidOperationException("Only a capturing alteration plan can be sealed.");
            else
                sealedPlan = CopyPlan(
                    plan,
                    status: targetCount == 0 ? WorkflowAlterationPlanStatus.Completed : WorkflowAlterationPlanStatus.Queued,
                    targetCount: targetCount,
                    sealedAt: sealedAt,
                    completedAt: targetCount == 0 ? sealedAt : null,
                    revision: plan.Revision + 1);

            var saved = await SaveAsync(
                _store,
                ElsaRuntimeStorageManifest.WorkflowAlterationPlanDocumentKind,
                PhysicalId(planId),
                WorkflowAlterationPlanDocument.From(sealedPlan, loaded.Document.ActiveOrderKey),
                loaded.Envelope.Version,
                cancellationToken);
            if (saved.Status == DocumentStoreWriteStatus.Saved)
                return sealedPlan;
            if (saved.Status != DocumentStoreWriteStatus.ConcurrencyConflict)
                throw Rejected("seal", planId, saved);
        }
        throw new WorkflowAlterationConcurrencyException(planId);
    }

    public ValueTask<WorkflowAlterationPlanState> RequestCancellationAsync(string planId, DateTimeOffset requestedAt, CancellationToken cancellationToken = default) =>
        UpdatePlanAsync(planId, plan =>
        {
            if (IsTerminal(plan.Status))
                return plan;
            return CopyPlan(plan, status: WorkflowAlterationPlanStatus.Cancelling, cancellationRequestedAt: requestedAt, revision: plan.Revision + 1);
        }, cancellationToken);

    public ValueTask<WorkflowAlterationPlanState> CancelUnsealedCaptureAsync(string planId, DateTimeOffset cancelledAt, CancellationToken cancellationToken = default) =>
        TerminalizeUnsealedCaptureAsync(planId, WorkflowAlterationPlanStatus.Cancelled, null, cancelledAt, cancellationToken);

    public ValueTask<WorkflowAlterationPlanState> FailUnsealedCaptureAsync(string planId, WorkflowAlterationSafeFailure safeFailure, DateTimeOffset failedAt, CancellationToken cancellationToken = default)
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
        ArgumentNullException.ThrowIfNull(skippedOutcomes);
        if (maximumCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumCount));
        var plan = await RequirePlanAsync(planId, cancellationToken);
        if (plan.Status != WorkflowAlterationPlanStatus.Cancelling)
            throw new InvalidOperationException("Only a cancelling alteration plan can cancel pending jobs.");
        var candidates = await Queries.QueryAsync(
            new DocumentQuery(
                ElsaRuntimeStorageManifest.WorkflowAlterationJobDocumentKind,
                ElsaRuntimeStorageManifest.ListPendingWorkflowAlterationJobsByPlanQuery,
                [
                    DocumentQueryClause.Of(DocumentQueryComparison.Equal(
                        ElsaRuntimeStorageManifest.WorkflowAlterationJobPlanIdField,
                        planId)),
                    DocumentQueryClause.Of(DocumentQueryComparison.Equal(
                        ElsaRuntimeStorageManifest.WorkflowAlterationJobStatusField,
                        WorkflowAlterationJobStatus.Pending.ToString()))
                ],
                [new DocumentQueryOrder(ElsaRuntimeStorageManifest.WorkflowAlterationJobIdField, PhysicalSortDirection.Ascending)],
                take: maximumCount),
            cancellationToken);
        foreach (var envelope in candidates.Documents)
        {
            var document = _serializer.Deserialize<WorkflowAlterationJobDocument>(envelope);
            var current = document.Job;
            if (current.Status != WorkflowAlterationJobStatus.Pending)
                continue;
            var cancelled = CopyJob(
                current,
                status: WorkflowAlterationJobStatus.Cancelled,
                claim: null,
                outcomes: skippedOutcomes,
                completedAt: completedAt,
                revision: current.Revision + 1);
            var saved = await SaveJobAsync(_store, cancelled, envelope.Version, cancellationToken);
            if (saved.Status is not (DocumentStoreWriteStatus.Saved or DocumentStoreWriteStatus.ConcurrencyConflict))
                throw Rejected("cancel pending", current.JobId, saved);
        }
    }

    public async ValueTask<WorkflowAlterationJobState?> FindJobAsync(string jobId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobId);
        var loaded = await LoadJobAsync(_store, jobId, cancellationToken);
        if (loaded is not null)
            _accessContextAccessor.Current.EnsureTenantScope(loaded.Value.Document.Job.TenantPartition);
        return loaded?.Document.Job;
    }

    public async ValueTask<WorkflowAlterationJobCounts> GetJobCountsAsync(string planId, CancellationToken cancellationToken = default)
    {
        _ = await RequirePlanAsync(planId, cancellationToken);
        async Task<long> CountAsync(WorkflowAlterationJobStatus status)
        {
            var result = await Queries.QueryAsync(new DocumentQuery(
                ElsaRuntimeStorageManifest.WorkflowAlterationJobDocumentKind,
                ElsaRuntimeStorageManifest.CountWorkflowAlterationJobsByPlanAndStatusQuery,
                [
                    DocumentQueryClause.Of(DocumentQueryComparison.Equal(ElsaRuntimeStorageManifest.WorkflowAlterationJobPlanIdField, planId)),
                    DocumentQueryClause.Of(DocumentQueryComparison.Equal(ElsaRuntimeStorageManifest.WorkflowAlterationJobStatusField, status.ToString()))
                ],
                [new DocumentQueryOrder(ElsaRuntimeStorageManifest.WorkflowAlterationJobIdField, PhysicalSortDirection.Ascending)],
                take: 1), cancellationToken);
            return result.TotalCount;
        }
        var counts = await Task.WhenAll(Enum.GetValues<WorkflowAlterationJobStatus>().Select(CountAsync));
        return new(counts[0], counts[1], counts[2], counts[3], counts[4]);
    }

    public async ValueTask<WorkflowAlterationJobState?> FindJobByCheckpointCommitIdAsync(string checkpointCommitId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(checkpointCommitId);
        var result = await Queries.QueryAsync(new DocumentQuery(
            ElsaRuntimeStorageManifest.WorkflowAlterationJobDocumentKind,
            ElsaRuntimeStorageManifest.FindWorkflowAlterationJobByCheckpointCommitIdQuery,
            [DocumentQueryClause.Of(DocumentQueryComparison.Equal(ElsaRuntimeStorageManifest.WorkflowAlterationJobCheckpointCommitIdField, checkpointCommitId))],
            [new DocumentQueryOrder(ElsaRuntimeStorageManifest.WorkflowAlterationJobIdField, PhysicalSortDirection.Ascending)], take: 2), cancellationToken);
        var jobs = result.Documents.Select(_serializer.Deserialize<WorkflowAlterationJobDocument>).Select(document => document.Job).ToArray();
        if (jobs.Length > 1)
            throw new InvalidOperationException("Groundwork found multiple alteration jobs for one checkpoint commit.");
        if (jobs.Length == 1)
            _accessContextAccessor.Current.EnsureTenantScope(jobs[0].TenantPartition);
        return jobs.SingleOrDefault();
    }

    public async ValueTask<WorkflowAlterationJobPage> PageJobsAsync(
        string planId,
        int pageSize,
        string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(planId);
        if (pageSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(pageSize));
        _ = await RequirePlanAsync(planId, cancellationToken);
        try
        {
            var result = await Queries.QueryAsync(
                new DocumentQuery(
                    ElsaRuntimeStorageManifest.WorkflowAlterationJobDocumentKind,
                    ElsaRuntimeStorageManifest.PageWorkflowAlterationJobsByPlanQuery,
                    [DocumentQueryClause.Of(DocumentQueryComparison.Equal(ElsaRuntimeStorageManifest.WorkflowAlterationJobPlanIdField, planId))],
                    [
                        new DocumentQueryOrder(ElsaRuntimeStorageManifest.WorkflowAlterationJobCaptureOrdinalField, PhysicalSortDirection.Ascending),
                        new DocumentQueryOrder(ElsaRuntimeStorageManifest.WorkflowAlterationJobIdField, PhysicalSortDirection.Ascending)
                    ],
                    take: pageSize,
                    continuation: cursor),
                cancellationToken);
            return new(
                result.Documents.Select(_serializer.Deserialize<WorkflowAlterationJobDocument>).Select(document => document.Job).ToArray(),
                result.NextContinuation,
                result.NextContinuation is not null);
        }
        catch (InvalidDocumentQueryContinuationException exception)
        {
            throw new ArgumentException("The alteration job page cursor is invalid or does not belong to this plan.", nameof(cursor), exception);
        }
    }

    public async ValueTask<WorkflowAlterationJobState?> ClaimNextAsync(string planId, string ownerId, DateTimeOffset now, TimeSpan leaseDuration, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(planId);
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        if (leaseDuration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(leaseDuration));
        if (_store.TransactionBoundary != TransactionBoundary.CrossUnitAtomic)
            throw new InvalidOperationException("Groundwork alteration-job claims require cross-unit atomic transactions.");

        for (var attempt = 0; attempt < TransitionAttempts; attempt++)
        {
            var sawConcurrencyConflict = false;
            var observedPlan = await RequirePlanAsync(planId, cancellationToken);
            if (observedPlan.Status is not (WorkflowAlterationPlanStatus.Queued or WorkflowAlterationPlanStatus.Running or WorkflowAlterationPlanStatus.Cancelling))
                return null;
            var reclaimOnly = observedPlan.Status == WorkflowAlterationPlanStatus.Cancelling;
            // Reconcile in-flight work before admitting new work. A successful re-claim grants a fresh lease, so
            // pending jobs become eligible again while that actor remains non-due.
            var candidateRoutes = reclaimOnly
                ? new[] { (WorkflowAlterationJobStatus.Running, ElsaRuntimeStorageManifest.ListRunningClaimableWorkflowAlterationJobsByPlanQuery) }
                : new[]
                {
                    (WorkflowAlterationJobStatus.Running, ElsaRuntimeStorageManifest.ListRunningClaimableWorkflowAlterationJobsByPlanQuery),
                    (WorkflowAlterationJobStatus.Pending, ElsaRuntimeStorageManifest.ListPendingClaimableWorkflowAlterationJobsByPlanQuery)
                };
            foreach (var (expectedStatus, queryIdentity) in candidateRoutes)
            {
                var candidates = await Queries.QueryAsync(
                    new DocumentQuery(
                        ElsaRuntimeStorageManifest.WorkflowAlterationJobDocumentKind,
                        queryIdentity,
                        [
                            DocumentQueryClause.Of(DocumentQueryComparison.Equal(
                                ElsaRuntimeStorageManifest.WorkflowAlterationJobPlanIdField,
                                planId)),
                            DocumentQueryClause.Of(DocumentQueryComparison.Equal(
                                ElsaRuntimeStorageManifest.WorkflowAlterationJobStatusField,
                                expectedStatus.ToString())),
                            DocumentQueryClause.Of(DocumentQueryComparison.LessThanOrEqual(
                                ElsaRuntimeStorageManifest.WorkflowAlterationJobClaimableAtField,
                                now.UtcDateTime.ToString("O", CultureInfo.InvariantCulture)))
                        ],
                        [
                            new DocumentQueryOrder(ElsaRuntimeStorageManifest.WorkflowAlterationJobClaimableAtField, PhysicalSortDirection.Ascending),
                            new DocumentQueryOrder(ElsaRuntimeStorageManifest.WorkflowAlterationJobIdField, PhysicalSortDirection.Ascending)
                        ],
                        take: ElsaGroundworkQueryRoutes.MaximumResultCount),
                    cancellationToken);
                foreach (var envelope in candidates.Documents)
                {
                    var document = _serializer.Deserialize<WorkflowAlterationJobDocument>(envelope);
                    if (!StringComparer.Ordinal.Equals(document.Job.PlanId, planId))
                        throw new InvalidOperationException($"Groundwork claim query returned job '{document.Job.JobId}' for a different alteration plan.");

                    await using var unit = await _store.BeginAsync(
                        DocumentCommitScope.Of(
                            ElsaRuntimeStorageManifest.WorkflowAlterationPlanDocumentKind,
                            ElsaRuntimeStorageManifest.WorkflowAlterationJobDocumentKind),
                        cancellationToken);
                    var transactional = new GroundworkDocumentUnitOfWorkStore(_store, unit);
                    var loadedPlan = await LoadPlanAsync(transactional, planId, cancellationToken);
                    var loadedJob = await LoadJobAsync(transactional, document.Job.JobId, cancellationToken);
                    if (loadedPlan is null || loadedJob is null)
                        continue;

                    var plan = loadedPlan.Value.Document.Plan;
                    var job = loadedJob.Value.Document.Job;
                    _accessContextAccessor.Current.EnsureTenantScope(plan.AuthorityScope.TenantPartition);
                    _accessContextAccessor.Current.EnsureTenantScope(job.TenantPartition);
                    if (!StringComparer.Ordinal.Equals(job.PlanId, planId) ||
                        job.Status != expectedStatus ||
                        plan.Status is not (WorkflowAlterationPlanStatus.Queued or WorkflowAlterationPlanStatus.Running) &&
                        (plan.Status != WorkflowAlterationPlanStatus.Cancelling || job.Status != WorkflowAlterationJobStatus.Running) ||
                        job.Claim is { ExpiresAt: var expiresAt } && expiresAt > now)
                    {
                        continue;
                    }

                    if (plan.Status == WorkflowAlterationPlanStatus.Queued)
                    {
                        var runningPlan = CopyPlan(plan, status: WorkflowAlterationPlanStatus.Running, startedAt: plan.StartedAt ?? now, revision: plan.Revision + 1);
                        var planSaved = await SavePlanAsync(transactional, runningPlan, loadedPlan.Value.Envelope.Version, cancellationToken);
                        if (planSaved.Status == DocumentStoreWriteStatus.ConcurrencyConflict)
                        {
                            sawConcurrencyConflict = true;
                            break;
                        }
                        if (planSaved.Status != DocumentStoreWriteStatus.Saved)
                            throw Rejected("claim plan", plan.PlanId, planSaved);
                    }

                    var claim = new WorkflowAlterationJobClaim(ownerId, Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16)), now + leaseDuration);
                    var claimed = CopyJob(job, status: WorkflowAlterationJobStatus.Running, claim: claim, attemptCount: job.AttemptCount + 1, startedAt: job.StartedAt ?? now, revision: job.Revision + 1);
                    var jobSaved = await SaveJobAsync(transactional, claimed, loadedJob.Value.Envelope.Version, cancellationToken);
                    if (jobSaved.Status == DocumentStoreWriteStatus.ConcurrencyConflict)
                    {
                        sawConcurrencyConflict = true;
                        break;
                    }
                    if (jobSaved.Status != DocumentStoreWriteStatus.Saved)
                        throw Rejected("claim", job.JobId, jobSaved);
                    await unit.CommitAsync(cancellationToken);
                    return claimed;
                }
                if (sawConcurrencyConflict)
                    break;
            }
            if (!sawConcurrencyConflict)
                return null;
        }
        return null;
    }

    public async ValueTask ValidateTerminalJobChangeAsync(WorkflowAlterationJobTerminalChange change, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(change);
        var job = await RequireJobAsync(change.JobId, cancellationToken);
        ValidateTerminalChange(change, job);
    }

    public async ValueTask ApplyTerminalJobChangeAsync(WorkflowAlterationJobTerminalChange change, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(change);
        var loaded = await LoadJobAsync(_store, change.JobId, cancellationToken)
            ?? throw new KeyNotFoundException($"Alteration job '{change.JobId}' was not found.");
        var job = loaded.Document.Job;
        _accessContextAccessor.Current.EnsureTenantScope(job.TenantPartition);
        ValidateTerminalChange(change, job);
        if (IsTerminal(job.Status))
            return;
        // The claim is no longer leaseable once the job is terminal, but retaining it makes
        // acknowledgement reconciliation prove which claimant committed the checkpoint.
        var terminal = CopyJob(job, status: change.Status, claim: job.Claim, outcomes: change.Outcomes, checkpointCommitId: change.CheckpointCommitId, safeFailure: change.SafeFailure, completedAt: change.CompletedAt, revision: job.Revision + 1);
        var saved = await SaveJobAsync(_store, terminal, loaded.Envelope.Version, cancellationToken);
        if (saved.Status != DocumentStoreWriteStatus.Saved)
            throw Rejected("terminalize", job.JobId, saved);
    }

    public async ValueTask<WorkflowAlterationPlanState> ReconcileAsync(string planId, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var plan = await RequirePlanAsync(planId, cancellationToken);
        if (IsTerminal(plan.Status) || plan.SealedAt is null)
            return plan;
        var counts = await GetJobCountsAsync(planId, cancellationToken);
        if (counts.Pending > 0 || counts.Running > 0)
            return plan;
        return await UpdatePlanAsync(planId, current =>
            IsTerminal(current.Status)
                ? current
                : CopyPlan(
                    current,
                    status: current.Status == WorkflowAlterationPlanStatus.Cancelling
                        ? WorkflowAlterationPlanStatus.Cancelled
                        : counts.Failed > 0 ? WorkflowAlterationPlanStatus.CompletedWithFailures : WorkflowAlterationPlanStatus.Completed,
                    targetCount: counts.Total,
                    succeededJobCount: counts.Succeeded,
                    failedJobCount: counts.Failed,
                    cancelledJobCount: counts.Cancelled,
                    completedAt: now,
                    revision: current.Revision + 1), cancellationToken);
    }

    private async ValueTask<WorkflowAlterationPlanState> UpdatePlanAsync(string planId, Func<WorkflowAlterationPlanState, WorkflowAlterationPlanState> transition, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < TransitionAttempts; attempt++)
        {
            var loaded = await LoadPlanAsync(_store, planId, cancellationToken)
                ?? throw new KeyNotFoundException($"Alteration plan '{planId}' was not found.");
            _accessContextAccessor.Current.EnsureTenantScope(loaded.Document.Plan.AuthorityScope.TenantPartition);
            var updated = transition(loaded.Document.Plan);
            if (ReferenceEquals(updated, loaded.Document.Plan) || updated == loaded.Document.Plan)
                return updated;
            var saved = await SavePlanAsync(_store, updated, loaded.Envelope.Version, cancellationToken);
            if (saved.Status == DocumentStoreWriteStatus.Saved)
                return updated;
            if (saved.Status != DocumentStoreWriteStatus.ConcurrencyConflict)
                throw Rejected("update", planId, saved);
        }
        throw new InvalidOperationException($"Alteration plan '{planId}' changed while the operation was in progress.");
    }

    private async ValueTask<WorkflowAlterationPlanState> TerminalizeUnsealedCaptureAsync(
        string planId,
        WorkflowAlterationPlanStatus terminalStatus,
        WorkflowAlterationSafeFailure? safeFailure,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken)
    {
        if (_store.TransactionBoundary != TransactionBoundary.CrossUnitAtomic)
            throw new InvalidOperationException("Groundwork unsealed-capture terminalization requires cross-unit atomic transactions.");

        for (var attempt = 0; attempt < TransitionAttempts; attempt++)
        {
            var observed = await RequirePlanAsync(planId, cancellationToken);
            if (observed.Status is not (WorkflowAlterationPlanStatus.CapturingTargets or WorkflowAlterationPlanStatus.Cancelling) || observed.SealedAt is not null)
                return observed;

            var page = await PageJobsAsync(planId, UnsealedCleanupPageSize, cancellationToken: cancellationToken);

            await using var unit = await _store.BeginAsync(
                DocumentCommitScope.Of(
                    ElsaRuntimeStorageManifest.WorkflowAlterationPlanDocumentKind,
                    ElsaRuntimeStorageManifest.WorkflowAlterationJobDocumentKind),
                cancellationToken);
            var transactional = new GroundworkDocumentUnitOfWorkStore(_store, unit);
            var loadedPlan = await LoadPlanAsync(transactional, planId, cancellationToken)
                ?? throw new KeyNotFoundException($"Alteration plan '{planId}' was not found.");
            var current = loadedPlan.Document.Plan;
            _accessContextAccessor.Current.EnsureTenantScope(current.AuthorityScope.TenantPartition);
            if (current.Revision != observed.Revision || current.SealedAt is not null ||
                current.Status is not (WorkflowAlterationPlanStatus.CapturingTargets or WorkflowAlterationPlanStatus.Cancelling))
            {
                continue;
            }

            var cleanup = loadedPlan.Document.UnsealedCaptureCleanup ??
                          (current.Status == WorkflowAlterationPlanStatus.Cancelling && current.CancellationRequestedAt is { } requestedAt
                              ? new WorkflowAlterationUnsealedCaptureCleanup(WorkflowAlterationPlanStatus.Cancelled, null, requestedAt)
                              : new WorkflowAlterationUnsealedCaptureCleanup(terminalStatus, safeFailure, completedAt));
            var retry = false;
            var deletedCount = 0L;
            foreach (var candidate in page.Items)
            {
                var loadedJob = await LoadJobAsync(transactional, candidate.JobId, cancellationToken);
                if (loadedJob is null)
                    continue;
                var deleted = await transactional.DeleteAsync(
                    new DeleteDocumentRequest(ElsaRuntimeStorageManifest.WorkflowAlterationJobDocumentKind, PhysicalId(candidate.JobId), loadedJob.Value.Envelope.Version),
                    cancellationToken);
                if (deleted.Status != DocumentStoreWriteStatus.Deleted)
                {
                    retry = true;
                    break;
                }
                deletedCount++;
            }
            if (retry)
                continue;

            var deletedTotal = checked(cleanup.DeletedCount + deletedCount);
            var isComplete = deletedTotal >= current.CapturedSoFar;
            var updatedCleanup = cleanup with { DeletedCount = deletedTotal };
            var updated = isComplete
                ? CopyPlan(
                    current,
                    status: cleanup.TerminalStatus,
                    changeCaptureCursor: true,
                    captureCursor: null,
                    capturedSoFar: current.CapturedSoFar,
                    targetCount: 0,
                    succeededJobCount: 0,
                    failedJobCount: 0,
                    cancelledJobCount: 0,
                    completedAt: cleanup.CompletedAt,
                    cancellationRequestedAt: cleanup.TerminalStatus == WorkflowAlterationPlanStatus.Cancelled ? cleanup.CompletedAt : null,
                    safeFailure: cleanup.SafeFailure,
                    revision: current.Revision + 1)
                : CopyPlan(
                    current,
                    status: WorkflowAlterationPlanStatus.Cancelling,
                    cancellationRequestedAt: cleanup.TerminalStatus == WorkflowAlterationPlanStatus.Cancelled ? cleanup.CompletedAt : null,
                    revision: current.Revision + 1);
            var document = WorkflowAlterationPlanDocument.From(
                updated,
                loadedPlan.Document.ActiveOrderKey,
                isComplete ? null : updatedCleanup);
            var saved = await SaveAsync(
                transactional,
                ElsaRuntimeStorageManifest.WorkflowAlterationPlanDocumentKind,
                PhysicalId(planId),
                document,
                loadedPlan.Envelope.Version,
                cancellationToken);
            if (saved.Status != DocumentStoreWriteStatus.Saved)
                continue;
            await unit.CommitAsync(cancellationToken);
            return updated;
        }

        throw new WorkflowAlterationConcurrencyException(planId);
    }

    private async ValueTask<WorkflowAlterationPlanState> RequirePlanAsync(string planId, CancellationToken cancellationToken) =>
        await FindPlanAsync(planId, cancellationToken) ?? throw new KeyNotFoundException($"Alteration plan '{planId}' was not found.");

    private async ValueTask<WorkflowAlterationJobState> RequireJobAsync(string jobId, CancellationToken cancellationToken) =>
        await FindJobAsync(jobId, cancellationToken) ?? throw new KeyNotFoundException($"Alteration job '{jobId}' was not found.");

    private async ValueTask<WorkflowAlterationPlanState?> FindPlanForAdmissionAsync(string tenantPartition, string idempotencyKeyHash, CancellationToken cancellationToken)
    {
        var result = await Queries.QueryAsync(
            new DocumentQuery(
                ElsaRuntimeStorageManifest.WorkflowAlterationPlanDocumentKind,
                ElsaRuntimeStorageManifest.FindWorkflowAlterationPlanByTenantAndIdempotencyQuery,
                [
                    DocumentQueryClause.Of(DocumentQueryComparison.Equal(ElsaRuntimeStorageManifest.WorkflowAlterationPlanTenantPartitionField, tenantPartition)),
                    DocumentQueryClause.Of(DocumentQueryComparison.Equal(ElsaRuntimeStorageManifest.WorkflowAlterationPlanIdempotencyKeyHashField, idempotencyKeyHash))
                ],
                [new DocumentQueryOrder(ElsaRuntimeStorageManifest.WorkflowAlterationPlanIdField, PhysicalSortDirection.Ascending)],
                take: 2),
            cancellationToken);
        var matches = result.Documents
            .Select(_serializer.Deserialize<WorkflowAlterationPlanDocument>)
            .Select(document => document.Plan)
            .ToArray();
        return matches.Length switch
        {
            0 => null,
            1 => matches[0],
            _ => throw new InvalidOperationException("Groundwork found multiple alteration plans for one tenant idempotency key.")
        };
    }

    private async ValueTask<(DocumentEnvelope Envelope, WorkflowAlterationPlanDocument Document)?> LoadPlanAsync(IDocumentStore store, string planId, CancellationToken cancellationToken)
    {
        var envelope = await store.LoadAsync(ElsaRuntimeStorageManifest.WorkflowAlterationPlanDocumentKind, PhysicalId(planId), cancellationToken);
        if (envelope is null)
            return null;
        var document = _serializer.Deserialize<WorkflowAlterationPlanDocument>(envelope);
        if (!StringComparer.Ordinal.Equals(document.PlanId, planId))
            throw new InvalidOperationException($"Groundwork physical identity collision for alteration plan '{planId}'.");
        return (envelope, document);
    }

    private async ValueTask<(DocumentEnvelope Envelope, WorkflowAlterationJobDocument Document)?> LoadJobAsync(IDocumentStore store, string jobId, CancellationToken cancellationToken)
    {
        var envelope = await store.LoadAsync(ElsaRuntimeStorageManifest.WorkflowAlterationJobDocumentKind, PhysicalId(jobId), cancellationToken);
        if (envelope is null)
            return null;
        var document = _serializer.Deserialize<WorkflowAlterationJobDocument>(envelope);
        if (!StringComparer.Ordinal.Equals(document.JobId, jobId))
            throw new InvalidOperationException($"Groundwork physical identity collision for alteration job '{jobId}'.");
        return (envelope, document);
    }

    private async Task<DocumentStoreWriteResult> SavePlanAsync(IDocumentStore store, WorkflowAlterationPlanState plan, long expectedVersion, CancellationToken cancellationToken)
    {
        var existing = expectedVersion == 0
            ? null
            : await LoadPlanAsync(store, plan.PlanId, cancellationToken);
        return await SaveAsync(
            store,
            ElsaRuntimeStorageManifest.WorkflowAlterationPlanDocumentKind,
            PhysicalId(plan.PlanId),
            WorkflowAlterationPlanDocument.From(
                plan,
                existing?.Document.ActiveOrderKey,
                existing?.Document.UnsealedCaptureCleanup),
            expectedVersion,
            cancellationToken);
    }

    private Task<DocumentStoreWriteResult> SaveJobAsync(IDocumentStore store, WorkflowAlterationJobState job, long expectedVersion, CancellationToken cancellationToken) =>
        SaveAsync(store, ElsaRuntimeStorageManifest.WorkflowAlterationJobDocumentKind, PhysicalId(job.JobId), WorkflowAlterationJobDocument.From(job), expectedVersion, cancellationToken);

    private Task<DocumentStoreWriteResult> SaveAsync<T>(IDocumentStore store, string kind, string id, T document, long expectedVersion, CancellationToken cancellationToken)
    {
        var (schemaVersion, content) = _serializer.Serialize(kind, document);
        return store.SaveAsync(new SaveDocumentRequest(kind, id, schemaVersion, content, expectedVersion), cancellationToken);
    }

    private static void EnsureSameAdmission(WorkflowAlterationPlanState existing, WorkflowAlterationPlanState candidate)
    {
        if (!StringComparer.Ordinal.Equals(existing.CanonicalRequestHash, candidate.CanonicalRequestHash))
            throw new InvalidOperationException($"The idempotency key is already associated with alteration plan '{existing.PlanId}' and a different request.");
    }

    private static void EnsureRevision(WorkflowAlterationPlanState plan, long expectedRevision)
    {
        if (plan.Revision != expectedRevision)
            throw new WorkflowAlterationConcurrencyException(plan.PlanId);
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
            throw new InvalidOperationException($"The alteration job claim for '{change.JobId}' is no longer current.");
    }

    private static bool IsTerminal(WorkflowAlterationPlanStatus status) => status is WorkflowAlterationPlanStatus.Completed or WorkflowAlterationPlanStatus.CompletedWithFailures or WorkflowAlterationPlanStatus.Failed or WorkflowAlterationPlanStatus.Cancelled;
    private static bool IsTerminal(WorkflowAlterationJobStatus status) => status is WorkflowAlterationJobStatus.Succeeded or WorkflowAlterationJobStatus.Failed or WorkflowAlterationJobStatus.Cancelled;
    private static string NextActivePlanOrderKey(string current, DateTimeOffset servicedAt, string planId)
    {
        var separator = current.IndexOf(':', StringComparison.Ordinal);
        var currentTicks = long.Parse(current.AsSpan(0, separator), CultureInfo.InvariantCulture);
        return $"{Math.Max(servicedAt.UtcTicks, checked(currentTicks + 1)):D19}:{planId}";
    }
    private static bool OutcomesEqual(IReadOnlyList<WorkflowAlterationOutcome> left, IReadOnlyCollection<WorkflowAlterationOutcome> right) =>
        left.Count == right.Count && left.Zip(right.OrderBy(outcome => outcome.Ordinal)).All(pair =>
            pair.First.Ordinal == pair.Second.Ordinal &&
            pair.First.Kind == pair.Second.Kind &&
            pair.First.SchemaVersion == pair.Second.SchemaVersion &&
            pair.First.Status == pair.Second.Status &&
            pair.First.Code == pair.Second.Code &&
            pair.First.Message == pair.Second.Message &&
            pair.First.RecordedAt == pair.Second.RecordedAt &&
            pair.First.StructuralMetadata.OrderBy(entry => entry.Key).SequenceEqual(pair.Second.StructuralMetadata.OrderBy(entry => entry.Key)));
    private static string PhysicalId(string logicalId) => GroundworkPhysicalDocumentId.FromLogicalId(logicalId);
    private static Exception Rejected(string operation, string identity, DocumentStoreWriteResult result) => new InvalidOperationException($"Groundwork rejected alteration {operation} for '{identity}' with status '{result.Status}'.");

    private static string CreateJobId(string planId, string workflowExecutionId)
    {
        var bytes = Encoding.UTF8.GetBytes(string.Join("\u001f", "job", planId, workflowExecutionId));
        return $"alteration-job-{Convert.ToHexStringLower(SHA256.HashData(bytes))[..32]}";
    }

    private static WorkflowAlterationPlanState CopyPlan(WorkflowAlterationPlanState plan, WorkflowAlterationPlanStatus? status = null, bool changeCaptureCursor = false, string? captureCursor = null, long? capturedSoFar = null, long? targetCount = null, long? succeededJobCount = null, long? failedJobCount = null, long? cancelledJobCount = null, DateTimeOffset? sealedAt = null, DateTimeOffset? startedAt = null, DateTimeOffset? completedAt = null, DateTimeOffset? cancellationRequestedAt = null, WorkflowAlterationSafeFailure? safeFailure = null, long? revision = null) =>
        new(plan.PlanId, plan.AuthorityScope, plan.SubmittedBy, plan.IdempotencyKeyHash, plan.CanonicalRequestHash, plan.ProtectedPayload, plan.Target, status ?? plan.Status, plan.CreatedAt, changeCaptureCursor ? captureCursor : plan.CaptureCursor, capturedSoFar ?? plan.CapturedSoFar, targetCount ?? plan.TargetCount, succeededJobCount ?? plan.SucceededJobCount, failedJobCount ?? plan.FailedJobCount, cancelledJobCount ?? plan.CancelledJobCount, sealedAt ?? plan.SealedAt, startedAt ?? plan.StartedAt, completedAt ?? plan.CompletedAt, cancellationRequestedAt ?? plan.CancellationRequestedAt, safeFailure ?? plan.SafeFailure, revision ?? plan.Revision, plan.AlterationDescriptors);

    private static WorkflowAlterationJobState CopyJob(WorkflowAlterationJobState job, WorkflowAlterationJobStatus? status = null, WorkflowAlterationJobClaim? claim = null, int? attemptCount = null, IReadOnlyCollection<WorkflowAlterationOutcome>? outcomes = null, string? checkpointCommitId = null, WorkflowAlterationSafeFailure? safeFailure = null, DateTimeOffset? startedAt = null, DateTimeOffset? completedAt = null, long? revision = null) =>
        new(job.JobId, job.PlanId, job.WorkflowExecutionId, job.TenantPartition, job.CaptureOrdinal, status ?? job.Status, claim, attemptCount ?? job.AttemptCount, (outcomes ?? job.Outcomes).ToArray(), checkpointCommitId ?? job.CheckpointCommitId, safeFailure ?? job.SafeFailure, job.CreatedAt, startedAt ?? job.StartedAt, completedAt ?? job.CompletedAt, revision ?? job.Revision, job.CapturedConcurrency);
}

internal sealed record WorkflowAlterationPlanDocument(
    string Collection,
    string PlanId,
    string TenantPartition,
    string IdempotencyKeyHash,
    string TenantIdempotencyKey,
    string Status,
    string ActiveOrderKey,
    WorkflowAlterationPlanState Plan,
    WorkflowAlterationUnsealedCaptureCleanup? UnsealedCaptureCleanup = null)
{
    public static WorkflowAlterationPlanDocument From(
        WorkflowAlterationPlanState plan,
        string? activeOrderKey = null,
        WorkflowAlterationUnsealedCaptureCleanup? unsealedCaptureCleanup = null) => new(
        ElsaRuntimeStorageManifest.WorkflowAlterationPlanCollection,
        plan.PlanId,
        plan.AuthorityScope.TenantPartition,
        plan.IdempotencyKeyHash,
        plan.AuthorityScope.TenantPartition + "\u001f" + plan.IdempotencyKeyHash,
        plan.Status.ToString(),
        activeOrderKey ?? $"{plan.CreatedAt.UtcTicks:D19}:{plan.PlanId}",
        plan,
        unsealedCaptureCleanup);
}

internal sealed record WorkflowAlterationUnsealedCaptureCleanup(
    WorkflowAlterationPlanStatus TerminalStatus,
    WorkflowAlterationSafeFailure? SafeFailure,
    DateTimeOffset CompletedAt,
    long DeletedCount = 0);

internal sealed record WorkflowAlterationJobDocument(string JobId, string PlanId, long CaptureOrdinal, DateTimeOffset? ClaimableAt, string Status, string? CheckpointCommitId, WorkflowAlterationJobState Job)
{
    public static WorkflowAlterationJobDocument From(WorkflowAlterationJobState job) => new(job.JobId, job.PlanId, job.CaptureOrdinal, CalculateClaimableAt(job), job.Status.ToString(), job.CheckpointCommitId, job);

    private static DateTimeOffset? CalculateClaimableAt(WorkflowAlterationJobState job) => job.Status switch
    {
        WorkflowAlterationJobStatus.Pending => job.CreatedAt,
        WorkflowAlterationJobStatus.Running => job.Claim?.ExpiresAt,
        _ => null
    };
}
