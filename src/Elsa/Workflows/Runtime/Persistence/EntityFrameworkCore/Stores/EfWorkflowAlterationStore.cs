using System.Security.Cryptography;
using System.Text;
using System.Globalization;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts.Alterations;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Models.Alterations;
using Elsa.Workflows.Runtime.Services.Alterations;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

/// <summary>Provider-neutral EF implementation of the R11/R12 alteration plan and job ledger.</summary>
public sealed class EfWorkflowAlterationStore(
    BookmarkStateDbContext context,
    IPersistenceAccessContextAccessor accessContextAccessor,
    IRuntimeRecoveryContinuationCodec continuationCodec) : IWorkflowAlterationStore
{
    private const int TransitionAttempts = 16;
    private const string ActiveCursorPurpose = "ef-runtime-alteration-active-v1";
    private const string JobCursorPurpose = "ef-runtime-alteration-jobs-v1";
    private readonly BookmarkStateDbContext _context = context ?? throw new ArgumentNullException(nameof(context));
    private readonly IPersistenceAccessContextAccessor _access = accessContextAccessor ?? throw new ArgumentNullException(nameof(accessContextAccessor));
    private readonly IRuntimeRecoveryContinuationCodec _codec = continuationCodec ?? throw new ArgumentNullException(nameof(continuationCodec));
    private sealed record UnsealedCleanupIntent(WorkflowAlterationPlanStatus TerminalStatus, WorkflowAlterationSafeFailure? SafeFailure, DateTimeOffset CompletedAt, long DeletedCount);

    public async ValueTask<WorkflowAlterationPlanAdmissionResult> AdmitAsync(WorkflowAlterationPlanState plan, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan); cancellationToken.ThrowIfCancellationRequested();
        var scope = RequireScope(); _access.Current.EnsureTenantScope(plan.AuthorityScope.TenantPartition);
        var scopeKey = Key(scope); var planId = Id(scopeKey, plan.PlanId); var encodedScope = EfRelationalIdentity.Encode(scopeKey); var scopeHash = EfRelationalIdentity.Hash(scopeKey); var encodedPlanId = EfRelationalIdentity.Encode(plan.PlanId); var planHash = EfRelationalIdentity.Hash(plan.PlanId);
        var existingById = await _context.WorkflowAlterationPlans.AsNoTracking().SingleOrDefaultAsync(x => x.Id == planId && x.ScopeKey == encodedScope && x.ScopeKeyHash == scopeHash && x.PlanId == encodedPlanId && x.PlanIdHash == planHash, cancellationToken);
        if (existingById is not null)
        {
            var storedById = ReadPlan(existingById, scopeKey, plan.PlanId);
            _access.Current.EnsureTenantScope(storedById.AuthorityScope.TenantPartition);
            EnsureSameAdmission(storedById, plan);
            return new(storedById, true);
        }
        var tenantIdempotencyKey = plan.AuthorityScope.TenantPartition + "\u001f" + plan.IdempotencyKeyHash;
        var keyHash = EfRelationalIdentity.Hash(tenantIdempotencyKey);
        var existing = await _context.WorkflowAlterationPlans.AsNoTracking().SingleOrDefaultAsync(x => x.ScopeKey == encodedScope && x.ScopeKeyHash == EfRelationalIdentity.Hash(scopeKey) && x.TenantIdempotencyKey == EfRelationalIdentity.Encode(tenantIdempotencyKey) && x.TenantIdempotencyKeyHash == keyHash, cancellationToken);
        if (existing is not null)
        {
            var stored = ReadPlan(existing, scopeKey);
            EnsureSameAdmission(stored, plan);
            return new(stored, true);
        }
        var row = ToPlan(plan, scopeKey, planId);
        _context.WorkflowAlterationPlans.Add(row);
        try { await _context.SaveChangesAsync(cancellationToken); return new(plan, false); }
        catch (DbUpdateException)
        {
            _context.ChangeTracker.Clear();
            var winner = await _context.WorkflowAlterationPlans.AsNoTracking().SingleOrDefaultAsync(x => x.Id == planId && x.ScopeKey == encodedScope && x.ScopeKeyHash == scopeHash && x.PlanId == encodedPlanId && x.PlanIdHash == planHash, cancellationToken)
                         ?? await _context.WorkflowAlterationPlans.AsNoTracking().SingleOrDefaultAsync(x => x.ScopeKey == encodedScope && x.ScopeKeyHash == EfRelationalIdentity.Hash(scopeKey) && x.TenantIdempotencyKey == EfRelationalIdentity.Encode(tenantIdempotencyKey) && x.TenantIdempotencyKeyHash == keyHash, cancellationToken);
            if (winner is null)
                throw;
            var stored = ReadPlan(winner, scopeKey);
            EnsureSameAdmission(stored, plan);
            return new(stored, true);
        }
    }

    public async ValueTask<WorkflowAlterationPlanState?> FindPlanAsync(string planId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(planId); cancellationToken.ThrowIfCancellationRequested();
        var scope = RequireScope(); var scopeKey = Key(scope);
        var row = await _context.WorkflowAlterationPlans.AsNoTracking().SingleOrDefaultAsync(x => x.Id == Id(scopeKey, planId) && x.ScopeKey == EfRelationalIdentity.Encode(scopeKey) && x.ScopeKeyHash == EfRelationalIdentity.Hash(scopeKey) && x.PlanId == EfRelationalIdentity.Encode(planId) && x.PlanIdHash == EfRelationalIdentity.Hash(planId), cancellationToken);
        return row is null ? null : ReadPlan(row, scopeKey, planId);
    }

    public async ValueTask<WorkflowAlterationActivePlanPage> ListActivePlansAsync(int pageSize, string? cursor = null, CancellationToken cancellationToken = default)
    {
        if (pageSize <= 0 || pageSize > RuntimeWorkflowAlterationEfModule.MaximumPageSize) throw new ArgumentOutOfRangeException(nameof(pageSize));
        var scope = RequireScope(); var scopeKey = Key(scope); var scopeHash = EfRelationalIdentity.Hash(scopeKey);
        string? after = null;
        if (cursor is not null) { var parts = Encoding.UTF8.GetString(_codec.Decode(ActiveCursorPurpose, cursor)).Split('\u001f'); if (parts.Length != 2 || parts[0] != EfRelationalIdentity.Encode(scopeKey)) throw new ArgumentException("The alteration active-plan cursor belongs to another persistence scope.", nameof(cursor)); after = DecodeCursorIdentity(parts[1], nameof(cursor)); }
        var encodedScope = EfRelationalIdentity.Encode(scopeKey);
        var rows = await _context.WorkflowAlterationPlans.AsNoTracking().Where(x => x.ScopeKey == encodedScope && x.ScopeKeyHash == scopeHash && (x.Status == (int)WorkflowAlterationPlanStatus.CapturingTargets || x.Status == (int)WorkflowAlterationPlanStatus.Queued || x.Status == (int)WorkflowAlterationPlanStatus.Running || x.Status == (int)WorkflowAlterationPlanStatus.Cancelling) && (after == null || x.ActiveOrderKey.CompareTo(after) > 0)).OrderBy(x => x.ActiveOrderKey).Take(pageSize + 1).ToListAsync(cancellationToken);
        var hasNext = rows.Count > pageSize; if (hasNext) rows.RemoveAt(pageSize);
        return new(rows.Select(x => ReadPlan(x, scopeKey)).ToArray(), hasNext ? _codec.Encode(ActiveCursorPurpose, Encoding.UTF8.GetBytes($"{EfRelationalIdentity.Encode(scopeKey)}\u001f{EfRelationalIdentity.Encode(rows[^1].ActiveOrderKey)}")) : null, hasNext);
    }

    public async ValueTask RescheduleActivePlanAsync(string planId, DateTimeOffset servicedAt, CancellationToken cancellationToken = default)
    {
        var scopeKey = Key(RequireScope());
        var row = await RequirePlan(planId, cancellationToken); var plan = ReadPlan(row, scopeKey, planId);
        if (IsTerminal(plan.Status)) return;
        var next = CopyPlan(plan);
        CopyPlan(row, next, scopeKey, NextActiveKey(row.ActiveOrderKey, servicedAt, plan.PlanId));
        await SaveConcurrency(row, cancellationToken, planId);
    }

    public async ValueTask<WorkflowAlterationPlanState> CaptureAsync(string planId, long expectedRevision, IReadOnlyCollection<WorkflowAlterationCapturedTarget> targets, string? nextCursor, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(targets); cancellationToken.ThrowIfCancellationRequested();
        var scopeKey = Key(RequireScope()); var row = await RequirePlan(planId, cancellationToken); var plan = ReadPlan(row, scopeKey, planId); EnsureRevision(plan, expectedRevision);
        if (plan.Status != WorkflowAlterationPlanStatus.CapturingTargets) throw new InvalidOperationException("Only a capturing alteration plan can accept target pages.");
        var ordinal = plan.CapturedSoFar;
        foreach (var target in NormalizeTargets(targets))
        {
            if (!StringComparer.Ordinal.Equals(target.TenantPartition, plan.AuthorityScope.TenantPartition)) throw new InvalidOperationException("A captured target must belong to the plan tenant partition.");
            var jobId = WorkflowAlterationIdentity.CreateJobId(plan.PlanId, target.WorkflowExecutionId);
            var existing = await _context.WorkflowAlterationJobs.AsNoTracking().SingleOrDefaultAsync(x => x.Id == Id(scopeKey, jobId) && x.ScopeKey == EfRelationalIdentity.Encode(scopeKey) && x.ScopeKeyHash == EfRelationalIdentity.Hash(scopeKey) && x.JobId == EfRelationalIdentity.Encode(jobId) && x.JobIdHash == EfRelationalIdentity.Hash(jobId), cancellationToken);
            if (existing is not null)
            {
                var existingJob = ReadJob(existing, scopeKey, jobId);
                if (!StringComparer.Ordinal.Equals(existingJob.PlanId, plan.PlanId) ||
                    !StringComparer.Ordinal.Equals(existingJob.WorkflowExecutionId, target.WorkflowExecutionId) ||
                    !StringComparer.Ordinal.Equals(existingJob.TenantPartition, target.TenantPartition) ||
                    existingJob.CapturedConcurrency != target.CapturedConcurrency ||
                    existingJob.SafeFailure != target.SafeFailure)
                {
                    throw new InvalidDataException($"Alteration-job identity collision detected for target '{target.WorkflowExecutionId}'.");
                }

                continue;
            }
            var status = target.SafeFailure is null ? WorkflowAlterationJobStatus.Pending : WorkflowAlterationJobStatus.Failed;
            DateTimeOffset? completedAt = target.SafeFailure is null ? null : plan.CreatedAt;
            var job = new WorkflowAlterationJobState(jobId, plan.PlanId, target.WorkflowExecutionId, target.TenantPartition, ordinal++, status, null, 0, [], null, target.SafeFailure, plan.CreatedAt, null, completedAt, 0, target.CapturedConcurrency);
            _context.WorkflowAlterationJobs.Add(ToJob(job, scopeKey));
        }
        var updated = CopyPlan(plan, captureCursor: nextCursor, replaceCaptureCursor: true, capturedSoFar: ordinal, revision: checked(plan.Revision + 1));
        CopyPlan(row, updated, scopeKey); await SaveConcurrency(row, cancellationToken, planId); return updated;
    }

    public async ValueTask<WorkflowAlterationPlanState> SealAsync(string planId, long expectedRevision, DateTimeOffset sealedAt, CancellationToken cancellationToken = default)
    {
        var scopeKey = Key(RequireScope()); var row = await RequirePlan(planId, cancellationToken); var plan = ReadPlan(row, scopeKey, planId); EnsureRevision(plan, expectedRevision);
        if (plan.Status is not (WorkflowAlterationPlanStatus.CapturingTargets or WorkflowAlterationPlanStatus.Cancelling)) throw new InvalidOperationException("Only an unsealed alteration plan can be sealed.");
        var count = await _context.WorkflowAlterationJobs.CountAsync(x => x.ScopeKey == EfRelationalIdentity.Encode(scopeKey) && x.ScopeKeyHash == EfRelationalIdentity.Hash(scopeKey) && x.PlanId == EfRelationalIdentity.Encode(plan.PlanId) && x.PlanIdHash == EfRelationalIdentity.Hash(plan.PlanId), cancellationToken);
        var status = plan.Status == WorkflowAlterationPlanStatus.Cancelling ? WorkflowAlterationPlanStatus.Cancelling : count == 0 ? WorkflowAlterationPlanStatus.Completed : WorkflowAlterationPlanStatus.Queued;
        var updated = CopyPlan(plan, status: status, sealedAt: sealedAt, replaceSealedAt: true, targetCount: count, completedAt: count == 0 && status == WorkflowAlterationPlanStatus.Completed ? sealedAt : plan.CompletedAt, replaceCompletedAt: count == 0 && status == WorkflowAlterationPlanStatus.Completed, revision: checked(plan.Revision + 1));
        CopyPlan(row, updated, scopeKey); await SaveConcurrency(row, cancellationToken, planId); return updated;
    }

    public ValueTask<WorkflowAlterationPlanState> CancelUnsealedCaptureAsync(string planId, DateTimeOffset cancelledAt, CancellationToken cancellationToken = default) => CleanupUnsealed(planId, cancelledAt, null, cancellationToken);
    public ValueTask<WorkflowAlterationPlanState> FailUnsealedCaptureAsync(string planId, WorkflowAlterationSafeFailure safeFailure, DateTimeOffset failedAt, CancellationToken cancellationToken = default) => CleanupUnsealed(planId, failedAt, safeFailure, cancellationToken);

    private async ValueTask<WorkflowAlterationPlanState> CleanupUnsealed(string planId, DateTimeOffset at, WorkflowAlterationSafeFailure? failure, CancellationToken cancellationToken)
    {
        var scopeKey = Key(RequireScope()); var row = await RequirePlan(planId, cancellationToken); var plan = ReadPlan(row, scopeKey, planId);
        if (plan.Status != WorkflowAlterationPlanStatus.CapturingTargets && plan.Status != WorkflowAlterationPlanStatus.Cancelling) return plan;
        var cleanup = ReadCleanup(row) ?? (plan.Status == WorkflowAlterationPlanStatus.Cancelling && plan.CancellationRequestedAt is { } requestedAt
            ? new UnsealedCleanupIntent(WorkflowAlterationPlanStatus.Cancelled, null, requestedAt, 0)
            : new UnsealedCleanupIntent(failure is null ? WorkflowAlterationPlanStatus.Cancelled : WorkflowAlterationPlanStatus.Failed, failure, at, 0));
        var jobs = await _context.WorkflowAlterationJobs.Where(x => x.ScopeKey == EfRelationalIdentity.Encode(scopeKey) && x.ScopeKeyHash == EfRelationalIdentity.Hash(scopeKey) && x.PlanId == EfRelationalIdentity.Encode(plan.PlanId) && x.PlanIdHash == EfRelationalIdentity.Hash(plan.PlanId)).OrderBy(x => x.CaptureOrdinal).ThenBy(x => x.JobIdOrderKey).Take(RuntimeWorkflowAlterationEfModule.UnsealedCleanupPageSize).ToListAsync(cancellationToken);
        _context.WorkflowAlterationJobs.RemoveRange(jobs);
        var deletedTotal = checked(cleanup.DeletedCount + jobs.Count);
        var complete = deletedTotal >= plan.CapturedSoFar;
        var updated = complete
            ? CopyPlan(plan, status: cleanup.TerminalStatus, captureCursor: null, replaceCaptureCursor: true, targetCount: 0, succeededJobCount: 0, failedJobCount: 0, cancelledJobCount: 0, completedAt: cleanup.CompletedAt, replaceCompletedAt: true, cancellationRequestedAt: cleanup.TerminalStatus == WorkflowAlterationPlanStatus.Cancelled ? cleanup.CompletedAt : null, replaceCancellationRequestedAt: cleanup.TerminalStatus == WorkflowAlterationPlanStatus.Cancelled, safeFailure: cleanup.SafeFailure, revision: checked(plan.Revision + 1))
            : CopyPlan(plan, status: WorkflowAlterationPlanStatus.Cancelling, cancellationRequestedAt: cleanup.TerminalStatus == WorkflowAlterationPlanStatus.Cancelled ? cleanup.CompletedAt : null, replaceCancellationRequestedAt: cleanup.TerminalStatus == WorkflowAlterationPlanStatus.Cancelled, revision: checked(plan.Revision + 1));
        CopyPlan(row, updated, scopeKey, cleanup: complete ? null : cleanup with { DeletedCount = deletedTotal }, clearCleanup: complete);
        await SaveConcurrency(row, cancellationToken, planId); return updated;
    }

    public async ValueTask<WorkflowAlterationPlanState> RequestCancellationAsync(string planId, DateTimeOffset requestedAt, CancellationToken cancellationToken = default)
    {
        var scopeKey = Key(RequireScope()); var row = await RequirePlan(planId, cancellationToken); var plan = ReadPlan(row, scopeKey, planId);
        if (IsTerminal(plan.Status) || plan.Status == WorkflowAlterationPlanStatus.Cancelling) return plan;
        var updated = CopyPlan(plan, status: WorkflowAlterationPlanStatus.Cancelling, cancellationRequestedAt: requestedAt, revision: checked(plan.Revision + 1)); CopyPlan(row, updated, scopeKey); await SaveConcurrency(row, cancellationToken, planId); return updated;
    }

    public async ValueTask CancelPendingJobsAsync(string planId, IReadOnlyCollection<WorkflowAlterationOutcome> skippedOutcomes, DateTimeOffset completedAt, int maximumCount, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(skippedOutcomes); if (maximumCount <= 0) throw new ArgumentOutOfRangeException(nameof(maximumCount));
        var scopeKey = Key(RequireScope()); var plan = ReadPlan(await RequirePlan(planId, cancellationToken), scopeKey, planId); if (plan.Status != WorkflowAlterationPlanStatus.Cancelling) throw new InvalidOperationException("Only a cancelling alteration plan can cancel pending jobs.");
        var rows = await _context.WorkflowAlterationJobs.Where(x => x.ScopeKey == EfRelationalIdentity.Encode(scopeKey) && x.ScopeKeyHash == EfRelationalIdentity.Hash(scopeKey) && x.PlanId == EfRelationalIdentity.Encode(plan.PlanId) && x.PlanIdHash == EfRelationalIdentity.Hash(plan.PlanId) && x.Status == (int)WorkflowAlterationJobStatus.Pending).OrderBy(x => x.CaptureOrdinal).ThenBy(x => x.JobIdOrderKey).Take(maximumCount).ToListAsync(cancellationToken);
        foreach (var row in rows) { var job = ReadJob(row, scopeKey); var updated = new WorkflowAlterationJobState(job.JobId, job.PlanId, job.WorkflowExecutionId, job.TenantPartition, job.CaptureOrdinal, WorkflowAlterationJobStatus.Cancelled, job.Claim, job.AttemptCount, skippedOutcomes.ToArray(), job.CheckpointCommitId, job.SafeFailure, job.CreatedAt, job.StartedAt, completedAt, checked(job.Revision + 1), job.CapturedConcurrency); CopyJob(row, updated, scopeKey); }
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            _context.ChangeTracker.Clear();
            throw new WorkflowAlterationConcurrencyException(planId);
        }
    }

    public async ValueTask<WorkflowAlterationJobState?> FindJobAsync(string jobId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobId); var scopeKey = Key(RequireScope()); var row = await _context.WorkflowAlterationJobs.AsNoTracking().SingleOrDefaultAsync(x => x.Id == Id(scopeKey, jobId) && x.ScopeKey == EfRelationalIdentity.Encode(scopeKey) && x.ScopeKeyHash == EfRelationalIdentity.Hash(scopeKey) && x.JobId == EfRelationalIdentity.Encode(jobId) && x.JobIdHash == EfRelationalIdentity.Hash(jobId), cancellationToken); return row is null ? null : ReadJob(row, scopeKey, jobId);
    }
    public async ValueTask<WorkflowAlterationJobCounts> GetJobCountsAsync(string planId, CancellationToken cancellationToken = default)
    {
        var scopeKey = Key(RequireScope());
        _ = ReadPlan(await RequirePlan(planId, cancellationToken), scopeKey, planId);
        var grouped = await _context.WorkflowAlterationJobs
            .Where(x => x.ScopeKey == EfRelationalIdentity.Encode(scopeKey) &&
                        x.ScopeKeyHash == EfRelationalIdentity.Hash(scopeKey) &&
                        x.PlanId == EfRelationalIdentity.Encode(planId) &&
                        x.PlanIdHash == EfRelationalIdentity.Hash(planId))
            .GroupBy(x => x.Status)
            .Select(g => new { g.Key, Count = g.LongCount() })
            .ToListAsync(cancellationToken);
        long Get(WorkflowAlterationJobStatus status) => grouped.SingleOrDefault(x => x.Key == (int)status)?.Count ?? 0;
        return new(Get(WorkflowAlterationJobStatus.Pending), Get(WorkflowAlterationJobStatus.Running), Get(WorkflowAlterationJobStatus.Succeeded), Get(WorkflowAlterationJobStatus.Failed), Get(WorkflowAlterationJobStatus.Cancelled));
    }
    public async ValueTask<WorkflowAlterationJobState?> FindJobByCheckpointCommitIdAsync(string checkpointCommitId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(checkpointCommitId); var scopeKey = Key(RequireScope()); var hash = EfRelationalIdentity.Hash(checkpointCommitId); var row = await _context.WorkflowAlterationJobs.AsNoTracking().SingleOrDefaultAsync(x => x.ScopeKey == EfRelationalIdentity.Encode(scopeKey) && x.ScopeKeyHash == EfRelationalIdentity.Hash(scopeKey) && x.CheckpointCommitIdHash == hash && x.CheckpointCommitId == EfRelationalIdentity.Encode(checkpointCommitId), cancellationToken); return row is null ? null : ReadJob(row, scopeKey);
    }
    public async ValueTask<WorkflowAlterationJobPage> PageJobsAsync(string planId, int pageSize, string? cursor = null, CancellationToken cancellationToken = default)
    {
        if (pageSize <= 0 || pageSize > RuntimeWorkflowAlterationEfModule.MaximumPageSize)
            throw new ArgumentOutOfRangeException(nameof(pageSize));

        var scopeKey = Key(RequireScope());
        _ = ReadPlan(await RequirePlan(planId, cancellationToken), scopeKey, planId);
        long afterOrdinal = -1;
        string? afterId = null;
        if (cursor is not null)
        {
            var payload = Encoding.UTF8.GetString(_codec.Decode(JobCursorPurpose, cursor)).Split('\u001f');
            if (payload.Length != 4 || payload[0] != EfRelationalIdentity.Encode(scopeKey) || payload[1] != EfRelationalIdentity.Encode(planId) || !long.TryParse(payload[2], out afterOrdinal))
                throw new ArgumentException("The alteration job cursor is invalid or belongs to another persistence scope.", nameof(cursor));
            afterId = DecodeCursorIdentity(payload[3], nameof(cursor));
        }
        var rows = await _context.WorkflowAlterationJobs.AsNoTracking().Where(x => x.ScopeKey == EfRelationalIdentity.Encode(scopeKey) && x.ScopeKeyHash == EfRelationalIdentity.Hash(scopeKey) && x.PlanId == EfRelationalIdentity.Encode(planId) && x.PlanIdHash == EfRelationalIdentity.Hash(planId) && (x.CaptureOrdinal > afterOrdinal || x.CaptureOrdinal == afterOrdinal && x.JobIdOrderKey.CompareTo(afterId) > 0)).OrderBy(x => x.CaptureOrdinal).ThenBy(x => x.JobIdOrderKey).Take(pageSize + 1).ToListAsync(cancellationToken); var has = rows.Count > pageSize; if (has) rows.RemoveAt(pageSize); return new(rows.Select(x => ReadJob(x, scopeKey)).ToArray(), has ? _codec.Encode(JobCursorPurpose, Encoding.UTF8.GetBytes($"{EfRelationalIdentity.Encode(scopeKey)}\u001f{EfRelationalIdentity.Encode(planId)}\u001f{rows[^1].CaptureOrdinal}\u001f{EfRelationalIdentity.Encode(rows[^1].JobIdOrderKey)}")) : null, has);
    }

    public async ValueTask<WorkflowAlterationJobState?> ClaimNextAsync(string planId, string ownerId, DateTimeOffset now, TimeSpan leaseDuration, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(planId);
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        if (leaseDuration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(leaseDuration));

        var scopeKey = Key(RequireScope());
        var nowTicks = now.UtcTicks;
        for (var attempt = 0; attempt < TransitionAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var planRow = await RequirePlan(planId, cancellationToken);
            var plan = ReadPlan(planRow, scopeKey, planId);
            if (plan.Status is not (WorkflowAlterationPlanStatus.Queued or WorkflowAlterationPlanStatus.Running or WorkflowAlterationPlanStatus.Cancelling))
                return null;

            var row = await _context.WorkflowAlterationJobs
                .Where(x => x.ScopeKey == EfRelationalIdentity.Encode(scopeKey) &&
                            x.ScopeKeyHash == EfRelationalIdentity.Hash(scopeKey) &&
                            x.PlanId == EfRelationalIdentity.Encode(plan.PlanId) &&
                            x.PlanIdHash == EfRelationalIdentity.Hash(plan.PlanId) &&
                            ((x.Status == (int)WorkflowAlterationJobStatus.Running && x.ClaimableAtUtcTicks <= nowTicks) ||
                             (x.Status == (int)WorkflowAlterationJobStatus.Pending && plan.Status != WorkflowAlterationPlanStatus.Cancelling && x.ClaimableAtUtcTicks <= nowTicks)))
                .OrderBy(x => x.Status == (int)WorkflowAlterationJobStatus.Running ? 0 : 1)
                .ThenBy(x => x.ClaimableAtUtcTicks)
                .ThenBy(x => x.JobIdOrderKey)
                .FirstOrDefaultAsync(cancellationToken);
            if (row is null)
                return null;

            var old = ReadJob(row, scopeKey);
            var claim = new WorkflowAlterationJobClaim(ownerId, Convert.ToHexString(RandomNumberGenerator.GetBytes(16)), now.Add(leaseDuration));
            var updated = new WorkflowAlterationJobState(old.JobId, old.PlanId, old.WorkflowExecutionId, old.TenantPartition, old.CaptureOrdinal, WorkflowAlterationJobStatus.Running, claim, checked(old.AttemptCount + 1), old.Outcomes, old.CheckpointCommitId, old.SafeFailure, old.CreatedAt, old.StartedAt ?? now, old.CompletedAt, checked(old.Revision + 1), old.CapturedConcurrency);
            CopyJob(row, updated, scopeKey);
            if (plan.Status == WorkflowAlterationPlanStatus.Queued)
            {
                var runningPlan = CopyPlan(plan, status: WorkflowAlterationPlanStatus.Running, startedAt: now, revision: checked(plan.Revision + 1));
                CopyPlan(planRow, runningPlan, scopeKey);
            }

            try
            {
                await _context.SaveChangesAsync(cancellationToken);
                return updated;
            }
            catch (DbUpdateException)
            {
                _context.ChangeTracker.Clear();
            }
        }

        throw new WorkflowAlterationConcurrencyException(planId);
    }

    public async ValueTask<WorkflowAlterationPlanState> ReconcileAsync(string planId, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var scopeKey = Key(RequireScope()); var row = await RequirePlan(planId, cancellationToken); var plan = ReadPlan(row, scopeKey, planId); if (plan.SealedAt is null || IsTerminal(plan.Status)) return plan; var counts = await GetJobCountsAsync(planId, cancellationToken); if (counts.Pending != 0 || counts.Running != 0) return plan; var status = plan.Status == WorkflowAlterationPlanStatus.Cancelling ? WorkflowAlterationPlanStatus.Cancelled : counts.Failed == 0 ? WorkflowAlterationPlanStatus.Completed : WorkflowAlterationPlanStatus.CompletedWithFailures; var p = CopyPlan(plan, status: status, completedAt: now, replaceCompletedAt: true, succeededJobCount: counts.Succeeded, failedJobCount: counts.Failed, cancelledJobCount: counts.Cancelled, targetCount: counts.Total, revision: checked(plan.Revision + 1)); CopyPlan(row, p, scopeKey); await SaveConcurrency(row, cancellationToken, planId); return p;
    }

    public async ValueTask ValidateTerminalJobChangeAsync(WorkflowAlterationJobTerminalChange change, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(change);
        var scopeKey = Key(RequireScope());
        var row = await _context.WorkflowAlterationJobs.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == Id(scopeKey, change.JobId), cancellationToken)
            ?? throw new KeyNotFoundException($"Alteration job '{change.JobId}' was not found.");
        ValidateTerminalChange(ReadJob(row, scopeKey, change.JobId), change);
    }
    public async ValueTask ApplyTerminalJobChangeAsync(WorkflowAlterationJobTerminalChange change, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(change);
        EfRuntimeAlterationCheckpointParticipationGate.RejectIndependentTerminalWrite(_context);
        var scopeKey = Key(RequireScope());
        var row = await _context.WorkflowAlterationJobs
            .SingleOrDefaultAsync(x => x.Id == Id(scopeKey, change.JobId) && x.ScopeKey == EfRelationalIdentity.Encode(scopeKey) && x.ScopeKeyHash == EfRelationalIdentity.Hash(scopeKey) && x.JobId == EfRelationalIdentity.Encode(change.JobId) && x.JobIdHash == EfRelationalIdentity.Hash(change.JobId), cancellationToken)
            ?? throw new KeyNotFoundException($"Alteration job '{change.JobId}' was not found.");
        var old = ReadJob(row, scopeKey, change.JobId);
        ValidateTerminalChange(old, change);
        if (IsTerminal(old.Status))
            return;

        var updated = new WorkflowAlterationJobState(old.JobId, old.PlanId, old.WorkflowExecutionId, old.TenantPartition, old.CaptureOrdinal, change.Status, old.Claim, old.AttemptCount, change.Outcomes.ToArray(), change.CheckpointCommitId, change.SafeFailure, old.CreatedAt, old.StartedAt, change.CompletedAt, checked(old.Revision + 1), old.CapturedConcurrency);
        CopyJob(row, updated, scopeKey);
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            _context.ChangeTracker.Clear();
            throw new WorkflowAlterationClaimFenceException(change.JobId);
        }
    }

    /// <summary>
    /// The callback must enter the EF checkpoint writer on this exact DbContext with matching job evidence. The
    /// writer stages the terminal transition and marker in one transaction; a no-op or unrelated EF callback cannot
    /// establish that boundary and fails closed.
    /// </summary>
    public async ValueTask CommitTerminalJobChangeAtomicallyAsync(WorkflowAlterationJobTerminalChange change, Func<CancellationToken, ValueTask> commitWorkflowCheckpointAsync, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(change);
        ArgumentNullException.ThrowIfNull(commitWorkflowCheckpointAsync);
        cancellationToken.ThrowIfCancellationRequested();
        await ValidateTerminalJobChangeAsync(change, cancellationToken);
        using var gate = EfRuntimeAlterationCheckpointParticipationGate.Begin(_context, change, Key(RequireScope()));
        await commitWorkflowCheckpointAsync(cancellationToken);
        await gate.VerifyDurableAsync(cancellationToken);
    }

    private async Task<WorkflowAlterationPlanEntity> RequirePlan(string planId, CancellationToken ct) { ArgumentException.ThrowIfNullOrWhiteSpace(planId); var scopeKey = Key(RequireScope()); return await _context.WorkflowAlterationPlans.SingleOrDefaultAsync(x => x.Id == Id(scopeKey, planId) && x.ScopeKey == EfRelationalIdentity.Encode(scopeKey) && x.ScopeKeyHash == EfRelationalIdentity.Hash(scopeKey) && x.PlanId == EfRelationalIdentity.Encode(planId) && x.PlanIdHash == EfRelationalIdentity.Hash(planId), ct) ?? throw new KeyNotFoundException($"Alteration plan '{planId}' was not found."); }
    private string RequireScope() => _access.Current.Scope?.Value ?? throw new InvalidOperationException("Runtime EF alteration persistence requires an ordinary scoped persistence access context.");
    private static string Key(string scope) => scope;
    private static string DecodeCursorIdentity(string encoded, string parameterName)
    {
        try
        {
            return EfRelationalIdentity.Decode(encoded);
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException or InvalidOperationException)
        {
            throw new ArgumentException("The alteration cursor contains an invalid identity.", parameterName, exception);
        }
    }
    internal static string Id(string scope, string id) =>
        EfRelationalIdentity.Hash($"{scope.Length}:{scope}{id.Length}:{id}");
    private static bool IsTerminal(WorkflowAlterationPlanStatus status) => status is WorkflowAlterationPlanStatus.Completed or WorkflowAlterationPlanStatus.CompletedWithFailures or WorkflowAlterationPlanStatus.Failed or WorkflowAlterationPlanStatus.Cancelled;
    private static bool IsTerminal(WorkflowAlterationJobStatus status) => status is WorkflowAlterationJobStatus.Succeeded or WorkflowAlterationJobStatus.Failed or WorkflowAlterationJobStatus.Cancelled;
    private static string ActiveKey(DateTimeOffset at, string id) =>
        $"{at.UtcTicks:D19}:{ActiveOrderIdentity(id)}";

    private static string ActiveOrderIdentity(string id) =>
        Convert.ToHexString(EfRelationalIdentity.CreateOrderKey(id, RuntimeWorkflowAlterationEfModule.IdentityMaximumLength));

    private static bool ActiveKeyMatches(string key, string id)
    {
        var separator = key.IndexOf(':');
        return separator == 19 &&
               long.TryParse(key.AsSpan(0, separator), NumberStyles.None, CultureInfo.InvariantCulture, out _) &&
               key.EndsWith(":" + ActiveOrderIdentity(id), StringComparison.Ordinal);
    }
    private static string NextActiveKey(string current, DateTimeOffset servicedAt, string id)
    {
        var separator = current.IndexOf(':');
        if (separator <= 0 || !long.TryParse(current.AsSpan(0, separator), NumberStyles.None, CultureInfo.InvariantCulture, out var currentTicks))
            throw new InvalidDataException("The alteration plan active-order projection is invalid.");
        var nextTicks = servicedAt.UtcTicks > currentTicks ? servicedAt.UtcTicks : checked(currentTicks + 1);
        return $"{nextTicks:D19}:{ActiveOrderIdentity(id)}";
    }
    private static void EnsureRevision(WorkflowAlterationPlanState plan, long revision) { if (plan.Revision != revision) throw new WorkflowAlterationConcurrencyException(plan.PlanId); }
    private static void EnsureSameAdmission(WorkflowAlterationPlanState existing, WorkflowAlterationPlanState candidate)
    {
        if (!StringComparer.Ordinal.Equals(existing.CanonicalRequestHash, candidate.CanonicalRequestHash))
            throw new WorkflowAlterationIdempotencyConflictException(existing.PlanId);
    }
    private static IEnumerable<WorkflowAlterationCapturedTarget> NormalizeTargets(IReadOnlyCollection<WorkflowAlterationCapturedTarget> targets)
    {
        foreach (var group in targets.GroupBy(x => x.WorkflowExecutionId, StringComparer.Ordinal).OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            var target = group.First();
            if (group.Any(candidate => candidate != target))
                throw new InvalidOperationException($"Captured target '{group.Key}' was supplied with conflicting immutable evidence.");
            yield return target;
        }
    }
    private static bool EvidenceEquals(WorkflowAlterationJobState job, WorkflowAlterationJobTerminalChange change) => job.CheckpointCommitId == change.CheckpointCommitId && job.Status == change.Status && job.CompletedAt == change.CompletedAt && job.SafeFailure == change.SafeFailure && OutcomesEqual(job.Outcomes, change.Outcomes);
    private static bool OutcomesEqual(IReadOnlyList<WorkflowAlterationOutcome> left, IReadOnlyCollection<WorkflowAlterationOutcome> right)
    {
        var orderedRight = right.OrderBy(x => x.Ordinal).ToArray();
        if (left.Count != orderedRight.Length)
            return false;
        return left.OrderBy(x => x.Ordinal).Zip(orderedRight).All(pair =>
            pair.First.Ordinal == pair.Second.Ordinal &&
            StringComparer.Ordinal.Equals(pair.First.Kind, pair.Second.Kind) &&
            pair.First.SchemaVersion == pair.Second.SchemaVersion &&
            pair.First.Status == pair.Second.Status &&
            StringComparer.Ordinal.Equals(pair.First.Code, pair.Second.Code) &&
            StringComparer.Ordinal.Equals(pair.First.Message, pair.Second.Message) &&
            pair.First.RecordedAt == pair.Second.RecordedAt &&
            pair.First.StructuralMetadata.Count == pair.Second.StructuralMetadata.Count &&
            pair.First.StructuralMetadata.OrderBy(x => x.Key, StringComparer.Ordinal).SequenceEqual(pair.Second.StructuralMetadata.OrderBy(x => x.Key, StringComparer.Ordinal), KeyValuePairComparer.Instance));
    }
    private sealed class KeyValuePairComparer : IEqualityComparer<KeyValuePair<string, string>>
    {
        public static readonly KeyValuePairComparer Instance = new();
        public bool Equals(KeyValuePair<string, string> x, KeyValuePair<string, string> y) => StringComparer.Ordinal.Equals(x.Key, y.Key) && StringComparer.Ordinal.Equals(x.Value, y.Value);
        public int GetHashCode(KeyValuePair<string, string> obj) => HashCode.Combine(StringComparer.Ordinal.GetHashCode(obj.Key), StringComparer.Ordinal.GetHashCode(obj.Value));
    }
    internal static void ValidateTerminalChange(WorkflowAlterationJobState job, WorkflowAlterationJobTerminalChange change)
    {
        if (IsTerminal(job.Status))
        {
            if (!EvidenceEquals(job, change))
                throw new InvalidOperationException("Terminal alteration evidence conflicts with the stored result.");
            return;
        }

        if (job.Status != WorkflowAlterationJobStatus.Running ||
            job.Claim is null ||
            !StringComparer.Ordinal.Equals(job.Claim.Token, change.ClaimToken))
            throw new WorkflowAlterationClaimFenceException(job.JobId);
    }
    private static WorkflowAlterationPlanEntity ToPlan(WorkflowAlterationPlanState p, string scope, string id) => new() { Id = id, ScopeKey = EfRelationalIdentity.Encode(scope), ScopeKeyHash = EfRelationalIdentity.Hash(scope), PlanId = EfRelationalIdentity.Encode(p.PlanId), PlanIdHash = EfRelationalIdentity.Hash(p.PlanId), PlanIdOrderKey = Convert.ToHexString(EfRelationalIdentity.CreateOrderKey(p.PlanId, RuntimeWorkflowAlterationEfModule.IdentityMaximumLength)), TenantIdempotencyKey = EfRelationalIdentity.Encode(p.AuthorityScope.TenantPartition + "\u001f" + p.IdempotencyKeyHash), TenantIdempotencyKeyHash = EfRelationalIdentity.Hash(p.AuthorityScope.TenantPartition + "\u001f" + p.IdempotencyKeyHash), Status = (int)p.Status, ActiveOrderKey = ActiveKey(p.CreatedAt, p.PlanId), CreatedAtUtcTicks = p.CreatedAt.UtcTicks, Revision = p.Revision, CleanupDeletedCount = 0, ContentJson = RuntimeArtifactJson.Serialize(p), SchemaVersion = RuntimeWorkflowAlterationEfModule.SchemaVersion };
    private static WorkflowAlterationPlanState ReadPlan(WorkflowAlterationPlanEntity row, string scope, string? expectedPlanId = null)
    {
        var plan = RuntimeArtifactJson.Deserialize<WorkflowAlterationPlanState>(row.ContentJson);
        var idempotency = plan.AuthorityScope.TenantPartition + "\u001f" + plan.IdempotencyKeyHash;
        var valid =
            (expectedPlanId is null || StringComparer.Ordinal.Equals(plan.PlanId, expectedPlanId)) &&
            StringComparer.Ordinal.Equals(plan.AuthorityScope.TenantPartition, scope) &&
            row.Id == Id(scope, plan.PlanId) &&
            row.SchemaVersion == RuntimeWorkflowAlterationEfModule.SchemaVersion &&
            row.ScopeKey == EfRelationalIdentity.Encode(scope) &&
            row.ScopeKeyHash == EfRelationalIdentity.Hash(scope) &&
            row.PlanId == EfRelationalIdentity.Encode(plan.PlanId) &&
            row.PlanIdHash == EfRelationalIdentity.Hash(plan.PlanId) &&
            row.PlanIdOrderKey == Convert.ToHexString(EfRelationalIdentity.CreateOrderKey(plan.PlanId, RuntimeWorkflowAlterationEfModule.IdentityMaximumLength)) &&
            row.TenantIdempotencyKey == EfRelationalIdentity.Encode(idempotency) &&
            row.TenantIdempotencyKeyHash == EfRelationalIdentity.Hash(idempotency) &&
            ActiveKeyMatches(row.ActiveOrderKey, plan.PlanId) &&
            row.CreatedAtUtcTicks == plan.CreatedAt.UtcTicks &&
            row.Revision == plan.Revision &&
            row.Status == (int)plan.Status;
        if (!valid)
            throw new InvalidDataException("The alteration plan projections do not match its durable content.");

        ReadCleanup(row);
        return plan;
    }
    private static UnsealedCleanupIntent? ReadCleanup(WorkflowAlterationPlanEntity row)
    {
        if (row.CleanupTerminalStatus is null)
        {
            if (row.CleanupSafeFailureJson is not null || row.CleanupCompletedAtUtcTicks is not null || row.CleanupDeletedCount != 0)
                throw new InvalidDataException("The alteration cleanup projections are incomplete.");
            return null;
        }

        var status = (WorkflowAlterationPlanStatus)row.CleanupTerminalStatus.Value;
        if (status is not (WorkflowAlterationPlanStatus.Cancelled or WorkflowAlterationPlanStatus.Failed) || row.CleanupCompletedAtUtcTicks is null || row.CleanupDeletedCount < 0)
            throw new InvalidDataException("The alteration cleanup projections are invalid.");
        var safeFailure = row.CleanupSafeFailureJson is null ? null : RuntimeArtifactJson.Deserialize<WorkflowAlterationSafeFailure>(row.CleanupSafeFailureJson);
        if ((status == WorkflowAlterationPlanStatus.Failed) != (safeFailure is not null))
            throw new InvalidDataException("The alteration cleanup intent does not match its terminal status.");
        return new(status, safeFailure, new DateTimeOffset(new DateTime(row.CleanupCompletedAtUtcTicks.Value, DateTimeKind.Utc)), row.CleanupDeletedCount);
    }
    private static void CopyPlan(WorkflowAlterationPlanEntity r, WorkflowAlterationPlanState p, string scope, string? activeOrderKey = null, UnsealedCleanupIntent? cleanup = null, bool clearCleanup = false) { r.Status = (int)p.Status; r.ActiveOrderKey = activeOrderKey ?? r.ActiveOrderKey; r.CreatedAtUtcTicks = p.CreatedAt.UtcTicks; r.Revision = p.Revision; r.ContentJson = RuntimeArtifactJson.Serialize(p); if (clearCleanup) { r.CleanupTerminalStatus = null; r.CleanupSafeFailureJson = null; r.CleanupCompletedAtUtcTicks = null; r.CleanupDeletedCount = 0; } else if (cleanup is not null) { r.CleanupTerminalStatus = (int)cleanup.TerminalStatus; r.CleanupSafeFailureJson = cleanup.SafeFailure is null ? null : RuntimeArtifactJson.Serialize(cleanup.SafeFailure); r.CleanupCompletedAtUtcTicks = cleanup.CompletedAt.UtcTicks; r.CleanupDeletedCount = cleanup.DeletedCount; } }
    private static WorkflowAlterationPlanState CopyPlan(WorkflowAlterationPlanState p, WorkflowAlterationPlanStatus? status = null, string? captureCursor = null, bool replaceCaptureCursor = false, long? capturedSoFar = null, long? targetCount = null, long? succeededJobCount = null, long? failedJobCount = null, long? cancelledJobCount = null, DateTimeOffset? sealedAt = null, bool replaceSealedAt = false, DateTimeOffset? startedAt = null, bool replaceStartedAt = false, DateTimeOffset? completedAt = null, bool replaceCompletedAt = false, DateTimeOffset? cancellationRequestedAt = null, bool replaceCancellationRequestedAt = false, WorkflowAlterationSafeFailure? safeFailure = null, long? revision = null) => new(p.PlanId, p.AuthorityScope, p.SubmittedBy, p.IdempotencyKeyHash, p.CanonicalRequestHash, p.ProtectedPayload, p.Target, status ?? p.Status, p.CreatedAt, replaceCaptureCursor ? captureCursor : p.CaptureCursor, capturedSoFar ?? p.CapturedSoFar, targetCount ?? p.TargetCount, succeededJobCount ?? p.SucceededJobCount, failedJobCount ?? p.FailedJobCount, cancelledJobCount ?? p.CancelledJobCount, replaceSealedAt ? sealedAt : p.SealedAt, replaceStartedAt ? startedAt : p.StartedAt, replaceCompletedAt ? completedAt : p.CompletedAt, replaceCancellationRequestedAt ? cancellationRequestedAt : p.CancellationRequestedAt, safeFailure ?? p.SafeFailure, revision ?? p.Revision, p.AlterationDescriptors);
    private static WorkflowAlterationJobEntity ToJob(WorkflowAlterationJobState j, string scope) => new() { Id = Id(scope, j.JobId), ScopeKey = EfRelationalIdentity.Encode(scope), ScopeKeyHash = EfRelationalIdentity.Hash(scope), JobId = EfRelationalIdentity.Encode(j.JobId), JobIdHash = EfRelationalIdentity.Hash(j.JobId), JobIdOrderKey = Convert.ToHexString(EfRelationalIdentity.CreateOrderKey(j.JobId, RuntimeWorkflowAlterationEfModule.IdentityMaximumLength)), PlanId = EfRelationalIdentity.Encode(j.PlanId), PlanIdHash = EfRelationalIdentity.Hash(j.PlanId), WorkflowExecutionId = EfRelationalIdentity.Encode(j.WorkflowExecutionId), WorkflowExecutionIdOrderKey = Convert.ToHexString(EfRelationalIdentity.CreateOrderKey(j.WorkflowExecutionId, RuntimeWorkflowAlterationEfModule.IdentityMaximumLength)), WorkflowExecutionIdHash = EfRelationalIdentity.Hash(j.WorkflowExecutionId), TenantPartition = EfRelationalIdentity.Encode(j.TenantPartition), TenantPartitionHash = EfRelationalIdentity.Hash(j.TenantPartition), CaptureOrdinal = j.CaptureOrdinal, ClaimableAtUtcTicks = j.Status == WorkflowAlterationJobStatus.Pending ? j.CreatedAt.UtcTicks : j.Claim?.ExpiresAt.UtcTicks, Status = (int)j.Status, CheckpointCommitId = j.CheckpointCommitId is null ? null : EfRelationalIdentity.Encode(j.CheckpointCommitId), CheckpointCommitIdHash = j.CheckpointCommitId is null ? null : EfRelationalIdentity.Hash(j.CheckpointCommitId), Revision = j.Revision, ContentJson = RuntimeArtifactJson.Serialize(j), SchemaVersion = RuntimeWorkflowAlterationEfModule.SchemaVersion };
    internal static WorkflowAlterationJobState ReadJob(WorkflowAlterationJobEntity row, string scope, string? expectedJobId = null)
    {
        var job = RuntimeArtifactJson.Deserialize<WorkflowAlterationJobState>(row.ContentJson);
        var checkpoint = job.CheckpointCommitId;
        var claimableAt = job.Status == WorkflowAlterationJobStatus.Pending ? job.CreatedAt.UtcTicks : job.Claim?.ExpiresAt.UtcTicks;
        var valid =
            (expectedJobId is null || StringComparer.Ordinal.Equals(job.JobId, expectedJobId)) &&
            StringComparer.Ordinal.Equals(job.TenantPartition, scope) &&
            row.Id == Id(scope, job.JobId) &&
            row.SchemaVersion == RuntimeWorkflowAlterationEfModule.SchemaVersion &&
            row.ScopeKey == EfRelationalIdentity.Encode(scope) &&
            row.ScopeKeyHash == EfRelationalIdentity.Hash(scope) &&
            row.JobId == EfRelationalIdentity.Encode(job.JobId) &&
            row.JobIdHash == EfRelationalIdentity.Hash(job.JobId) &&
            row.JobIdOrderKey == Convert.ToHexString(EfRelationalIdentity.CreateOrderKey(job.JobId, RuntimeWorkflowAlterationEfModule.IdentityMaximumLength)) &&
            row.PlanId == EfRelationalIdentity.Encode(job.PlanId) &&
            row.PlanIdHash == EfRelationalIdentity.Hash(job.PlanId) &&
            row.WorkflowExecutionId == EfRelationalIdentity.Encode(job.WorkflowExecutionId) &&
            row.WorkflowExecutionIdHash == EfRelationalIdentity.Hash(job.WorkflowExecutionId) &&
            row.WorkflowExecutionIdOrderKey == Convert.ToHexString(EfRelationalIdentity.CreateOrderKey(job.WorkflowExecutionId, RuntimeWorkflowAlterationEfModule.IdentityMaximumLength)) &&
            row.TenantPartition == EfRelationalIdentity.Encode(job.TenantPartition) &&
            row.TenantPartitionHash == EfRelationalIdentity.Hash(job.TenantPartition) &&
            row.Revision == job.Revision &&
            row.Status == (int)job.Status &&
            row.CaptureOrdinal == job.CaptureOrdinal &&
            row.ClaimableAtUtcTicks == claimableAt &&
            row.CheckpointCommitId == (checkpoint is null ? null : EfRelationalIdentity.Encode(checkpoint)) &&
            row.CheckpointCommitIdHash == (checkpoint is null ? null : EfRelationalIdentity.Hash(checkpoint));
        if (!valid)
            throw new InvalidDataException("The alteration job projections do not match its durable content.");

        return job;
    }
    internal static void CopyJob(WorkflowAlterationJobEntity r, WorkflowAlterationJobState j, string scope) { var copy = ToJob(j, scope); r.JobId = copy.JobId; r.JobIdHash = copy.JobIdHash; r.JobIdOrderKey = copy.JobIdOrderKey; r.PlanId = copy.PlanId; r.PlanIdHash = copy.PlanIdHash; r.WorkflowExecutionId = copy.WorkflowExecutionId; r.WorkflowExecutionIdOrderKey = copy.WorkflowExecutionIdOrderKey; r.WorkflowExecutionIdHash = copy.WorkflowExecutionIdHash; r.TenantPartition = copy.TenantPartition; r.TenantPartitionHash = copy.TenantPartitionHash; r.CaptureOrdinal = copy.CaptureOrdinal; r.ClaimableAtUtcTicks = copy.ClaimableAtUtcTicks; r.Status = copy.Status; r.CheckpointCommitId = copy.CheckpointCommitId; r.CheckpointCommitIdHash = copy.CheckpointCommitIdHash; r.Revision = copy.Revision; r.ContentJson = copy.ContentJson; }
    private async Task SaveConcurrency(WorkflowAlterationPlanEntity row, CancellationToken ct, string id) { try { await _context.SaveChangesAsync(ct); } catch (DbUpdateException) { _context.ChangeTracker.Clear(); throw new WorkflowAlterationConcurrencyException(id); } }
}
