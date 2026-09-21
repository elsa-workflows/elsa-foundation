using Elsa.Workflows.Runtime.Core.Models.Alterations;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>Immutable-key capture request used only by alteration target capture.</summary>
public sealed record WorkflowExecutionAlterationCaptureQuery
{
    public WorkflowExecutionAlterationCaptureQuery(
        string tenantPartition,
        string systemIdentity,
        string rootInitiator,
        IReadOnlyDictionary<string, string>? authorityMetadata,
        WorkflowAlterationQuerySelector selector,
        int pageSize,
        string? cursor = null)
    {
        TenantPartition = tenantPartition;
        SystemIdentity = systemIdentity;
        RootInitiator = rootInitiator;
        AuthorityMetadata = RuntimeModelMetadata.Snapshot(authorityMetadata);
        Selector = selector;
        PageSize = pageSize;
        Cursor = cursor;
    }

    public string TenantPartition { get; }
    public string SystemIdentity { get; }
    public string RootInitiator { get; }
    public IReadOnlyDictionary<string, string> AuthorityMetadata { get; }
    public WorkflowAlterationQuerySelector Selector { get; }
    public int PageSize { get; }
    public string? Cursor { get; }

    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(TenantPartition);
        ArgumentException.ThrowIfNullOrWhiteSpace(SystemIdentity);
        ArgumentException.ThrowIfNullOrWhiteSpace(RootInitiator);
        ArgumentNullException.ThrowIfNull(Selector);
        if (PageSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(PageSize));
        if (Cursor is not null && string.IsNullOrWhiteSpace(Cursor))
            throw new ArgumentException("The alteration capture cursor cannot be blank.", nameof(Cursor));
    }

    public string AuthorityPartitionKey =>
        WorkflowExecutionAuthoritySnapshot.PartitionKey(SystemIdentity, RootInitiator, AuthorityMetadata);
}

/// <summary>A bounded page ordered only by (tenant partition, workflow execution ID).</summary>
public sealed record WorkflowExecutionAlterationCapturePage(
    IReadOnlyList<WorkflowExecutionState> Items,
    string? NextCursor,
    bool HasNext);

/// <summary>One workflow target captured into an alteration plan before it is sealed.</summary>
public sealed record WorkflowAlterationCapturedTarget
{
    public WorkflowAlterationCapturedTarget(
        string workflowExecutionId,
        string tenantPartition,
        WorkflowAlterationCapturedConcurrency? capturedConcurrency = null,
        WorkflowAlterationSafeFailure? safeFailure = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantPartition);
        WorkflowExecutionId = workflowExecutionId;
        TenantPartition = tenantPartition;
        CapturedConcurrency = capturedConcurrency;
        SafeFailure = safeFailure;
    }

    public string WorkflowExecutionId { get; }
    public string TenantPartition { get; }
    public WorkflowAlterationCapturedConcurrency? CapturedConcurrency { get; }
    /// <summary>Capture-only safe failure, used for explicit IDs that were absent or inaccessible in the sealed scope.</summary>
    public WorkflowAlterationSafeFailure? SafeFailure { get; }
}

/// <summary>Tenant-scoped idempotent plan-admission result.</summary>
public sealed record WorkflowAlterationPlanAdmissionResult(WorkflowAlterationPlanState Plan, bool IsReplay);

/// <summary>
/// Bounded restart-recovery page of non-terminal alteration plans, ordered by a durable least-recently-serviced key
/// within the current persistence/tenant scope. The recurring coordinator resumes capture, dispatch, and
/// reconciliation from this page.
/// </summary>
public sealed record WorkflowAlterationActivePlanPage(
    IReadOnlyList<WorkflowAlterationPlanState> Items,
    string? NextCursor,
    bool HasNext);

/// <summary>Authoritative job-status summary for one plan; avoids materializing an unbounded cohort for inspection.</summary>
public sealed record WorkflowAlterationJobCounts(
    long Pending,
    long Running,
    long Succeeded,
    long Failed,
    long Cancelled)
{
    public long Total => checked(Pending + Running + Succeeded + Failed + Cancelled);
}

/// <summary>Bounded job page ordered by capture ordinal and job identity.</summary>
public sealed record WorkflowAlterationJobPage(
    IReadOnlyList<WorkflowAlterationJobState> Items,
    string? NextCursor,
    bool HasNext);

/// <summary>Terminal job evidence that must travel with the workflow checkpoint.</summary>
public sealed record WorkflowAlterationJobTerminalChange
{
    public WorkflowAlterationJobTerminalChange(
        string jobId,
        string claimToken,
        WorkflowAlterationJobStatus status,
        IReadOnlyCollection<WorkflowAlterationOutcome> outcomes,
        string checkpointCommitId,
        DateTimeOffset completedAt,
        WorkflowAlterationSafeFailure? safeFailure = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobId);
        ArgumentException.ThrowIfNullOrWhiteSpace(claimToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(checkpointCommitId);
        ArgumentNullException.ThrowIfNull(outcomes);
        if (status is not (WorkflowAlterationJobStatus.Succeeded or WorkflowAlterationJobStatus.Failed))
            throw new ArgumentOutOfRangeException(nameof(status), "Checkpoint evidence can terminalize only succeeded or failed actor jobs.");
        JobId = jobId;
        ClaimToken = claimToken;
        Status = status;
        Outcomes = outcomes;
        CheckpointCommitId = checkpointCommitId;
        CompletedAt = completedAt;
        SafeFailure = safeFailure;
    }

    public string JobId { get; }
    public string ClaimToken { get; }
    public WorkflowAlterationJobStatus Status { get; }
    public IReadOnlyCollection<WorkflowAlterationOutcome> Outcomes { get; }
    public string CheckpointCommitId { get; }
    public DateTimeOffset CompletedAt { get; }
    public WorkflowAlterationSafeFailure? SafeFailure { get; }
}
