namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>Immutable test-run identity and persistence context propagated through runtime execution.</summary>
public sealed record WorkflowTestScope
{
    public WorkflowTestScope(
        string scopeId,
        DateTimeOffset expiresAt,
        string? tenantId,
        WorkflowExecutionPartition partition)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scopeId);
        ArgumentNullException.ThrowIfNull(partition);
        if (expiresAt == default || expiresAt == DateTimeOffset.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(expiresAt), "A workflow test scope requires a finite expiry.");
        if (tenantId is not null && string.IsNullOrWhiteSpace(tenantId))
            throw new ArgumentException("A workflow test-scope tenant ID cannot be blank when provided.", nameof(tenantId));

        ScopeId = scopeId;
        ExpiresAt = expiresAt;
        TenantId = tenantId;
        Partition = partition;
    }

    public string ScopeId { get; }
    public DateTimeOffset ExpiresAt { get; }
    public string? TenantId { get; }
    public WorkflowExecutionPartition Partition { get; }

    /// <summary>Returns true at and after the finite expiry boundary.</summary>
    public bool IsExpired(DateTimeOffset observedAt) => ExpiresAt <= observedAt;

    /// <summary>Compares every immutable scope fact using provider-neutral ordinal semantics.</summary>
    public static bool ContextEquals(WorkflowTestScope? left, WorkflowTestScope? right) =>
        ReferenceEquals(left, right) ||
        left is not null &&
        right is not null &&
        StringComparer.Ordinal.Equals(left.ScopeId, right.ScopeId) &&
        left.ExpiresAt == right.ExpiresAt &&
        StringComparer.Ordinal.Equals(left.TenantId, right.TenantId) &&
        StringComparer.Ordinal.Equals(left.Partition.Value, right.Partition.Value);
}

public enum WorkflowTestScopeState
{
    Open = 0,
    Closing = 1,
    Closed = 2
}

public enum WorkflowTestScopeCloseReason
{
    Expired = 0,
    ExplicitTeardown = 1
}

/// <summary>Durable monotonic lifecycle evidence for one immutable test scope.</summary>
public sealed record WorkflowTestScopeRecord
{
    public WorkflowTestScopeRecord(
        WorkflowTestScope scope,
        WorkflowTestScopeState state,
        DateTimeOffset createdAt,
        DateTimeOffset? closingAt = null,
        DateTimeOffset? closedAt = null,
        WorkflowTestScopeCloseReason? closeReason = null)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (!Enum.IsDefined(state))
            throw new ArgumentOutOfRangeException(nameof(state));
        if (createdAt == default || createdAt >= scope.ExpiresAt)
            throw new ArgumentOutOfRangeException(nameof(createdAt), "A workflow test scope must be created before it expires.");
        if (state == WorkflowTestScopeState.Open && (closingAt is not null || closedAt is not null || closeReason is not null))
            throw new ArgumentException("An open workflow test scope cannot carry close evidence.", nameof(state));
        if (state != WorkflowTestScopeState.Open && (closingAt is null || closeReason is null))
            throw new ArgumentException("A closing workflow test scope requires close time and reason.", nameof(state));
        if (state == WorkflowTestScopeState.Closing && closedAt is not null)
            throw new ArgumentException("A closing workflow test scope cannot carry a closed time.", nameof(closedAt));
        if (state == WorkflowTestScopeState.Closed && closedAt is null)
            throw new ArgumentException("A closed workflow test scope requires a closed time.", nameof(closedAt));
        if (closingAt < createdAt)
            throw new ArgumentOutOfRangeException(nameof(closingAt), "A workflow test scope cannot close before creation.");
        if (closedAt < closingAt)
            throw new ArgumentOutOfRangeException(nameof(closedAt), "A workflow test scope cannot complete closure before closing begins.");

        Scope = scope;
        State = state;
        CreatedAt = createdAt;
        ClosingAt = closingAt;
        ClosedAt = closedAt;
        CloseReason = closeReason;
    }

    public WorkflowTestScope Scope { get; }
    public WorkflowTestScopeState State { get; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset? ClosingAt { get; }
    public DateTimeOffset? ClosedAt { get; }
    public WorkflowTestScopeCloseReason? CloseReason { get; }
}

public sealed record WorkflowTestScopeCloseRequest
{
    public WorkflowTestScopeCloseRequest(
        string scopeId,
        WorkflowTestScopeCloseReason reason,
        DateTimeOffset requestedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scopeId);
        if (!Enum.IsDefined(reason))
            throw new ArgumentOutOfRangeException(nameof(reason));
        if (requestedAt == default)
            throw new ArgumentOutOfRangeException(nameof(requestedAt));

        ScopeId = scopeId;
        Reason = reason;
        RequestedAt = requestedAt;
    }

    public string ScopeId { get; }
    public WorkflowTestScopeCloseReason Reason { get; }
    public DateTimeOffset RequestedAt { get; }
}

public enum WorkflowTestScopeCloseDisposition
{
    Accepted = 0,
    AlreadyClosing = 1,
    AlreadyClosed = 2,
    NotFound = 3
}

public sealed record WorkflowTestScopeCloseResult(
    WorkflowTestScopeCloseDisposition Disposition,
    WorkflowTestScopeRecord? Record);

/// <summary>Bounded lifecycle query for expiry and closing-scope resumption.</summary>
public sealed record WorkflowTestScopePageQuery(
    DateTimeOffset ObservedAt,
    int PageSize,
    WorkflowTestScopeState? State = null,
    string? ContinuationToken = null)
{
    public void Validate()
    {
        if (ObservedAt == default)
            throw new ArgumentOutOfRangeException(nameof(ObservedAt));
        if (PageSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(PageSize), "A workflow test-scope page size must be greater than zero.");
        if (State is { } state && !Enum.IsDefined(state))
            throw new ArgumentOutOfRangeException(nameof(State));
        if (ContinuationToken is not null && string.IsNullOrWhiteSpace(ContinuationToken))
            throw new ArgumentException("A workflow test-scope continuation token cannot be blank.", nameof(ContinuationToken));
    }
}

public sealed record WorkflowTestScopePage(
    IReadOnlyList<WorkflowTestScopeRecord> Items,
    string? ContinuationToken);

/// <summary>Bounded progress from one detached-dispatch cleanup page.</summary>
public sealed record WorkflowTestScopeCleanupResult
{
    public WorkflowTestScopeCleanupResult(
        int inspected,
        int cancelledBeforeAdmission,
        int cancellationQueued,
        int terminalUnchanged,
        int remainingLive,
        string? continuationToken = null)
    {
        if (inspected < 0 || cancelledBeforeAdmission < 0 || cancellationQueued < 0 || terminalUnchanged < 0 || remainingLive < 0)
            throw new ArgumentOutOfRangeException(nameof(inspected), "Workflow test-scope cleanup counts cannot be negative.");
        if (cancelledBeforeAdmission + cancellationQueued + terminalUnchanged > inspected)
            throw new ArgumentException("Workflow test-scope cleanup outcome counts cannot exceed the inspected count.", nameof(inspected));
        if (continuationToken is not null && string.IsNullOrWhiteSpace(continuationToken))
            throw new ArgumentException("A workflow test-scope continuation token cannot be blank.", nameof(continuationToken));

        Inspected = inspected;
        CancelledBeforeAdmission = cancelledBeforeAdmission;
        CancellationQueued = cancellationQueued;
        TerminalUnchanged = terminalUnchanged;
        RemainingLive = remainingLive;
        ContinuationToken = continuationToken;
    }

    public int Inspected { get; }
    public int CancelledBeforeAdmission { get; }
    public int CancellationQueued { get; }
    public int TerminalUnchanged { get; }
    public int RemainingLive { get; }
    public string? ContinuationToken { get; }
}

/// <summary>Shared provider-neutral lifecycle transitions for workflow test scopes.</summary>
public static class WorkflowTestScopeTransitions
{
    public static WorkflowTestScopeRecord Create(
        WorkflowTestScopeRecord? existing,
        WorkflowTestScope requested,
        DateTimeOffset createdAt)
    {
        ArgumentNullException.ThrowIfNull(requested);
        if (existing is null)
            return new WorkflowTestScopeRecord(requested, WorkflowTestScopeState.Open, createdAt);
        if (!WorkflowTestScope.ContextEquals(existing.Scope, requested))
            throw new InvalidOperationException("A workflow test scope already exists with conflicting immutable context.");
        return existing;
    }

    public static WorkflowTestScopeCloseResult Close(
        WorkflowTestScopeRecord? existing,
        WorkflowTestScopeCloseRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (existing is null)
            return new WorkflowTestScopeCloseResult(WorkflowTestScopeCloseDisposition.NotFound, null);
        if (!StringComparer.Ordinal.Equals(existing.Scope.ScopeId, request.ScopeId))
            throw new InvalidOperationException("The workflow test-scope close request does not match the stored scope.");
        if (existing.State == WorkflowTestScopeState.Closed)
            return new WorkflowTestScopeCloseResult(WorkflowTestScopeCloseDisposition.AlreadyClosed, existing);
        if (existing.State == WorkflowTestScopeState.Closing)
            return new WorkflowTestScopeCloseResult(WorkflowTestScopeCloseDisposition.AlreadyClosing, existing);
        if (request.RequestedAt < existing.CreatedAt)
            throw new InvalidOperationException("A workflow test scope cannot close before creation.");
        if (request.Reason == WorkflowTestScopeCloseReason.Expired && !existing.Scope.IsExpired(request.RequestedAt))
            throw new InvalidOperationException("A workflow test scope cannot close as expired before its expiry boundary.");

        var closing = new WorkflowTestScopeRecord(
            existing.Scope,
            WorkflowTestScopeState.Closing,
            existing.CreatedAt,
            request.RequestedAt,
            closeReason: request.Reason);
        return new WorkflowTestScopeCloseResult(WorkflowTestScopeCloseDisposition.Accepted, closing);
    }

    public static WorkflowTestScopeRecord Complete(WorkflowTestScopeRecord existing, DateTimeOffset completedAt)
    {
        ArgumentNullException.ThrowIfNull(existing);
        if (existing.State == WorkflowTestScopeState.Closed)
            return existing;
        if (existing.State != WorkflowTestScopeState.Closing)
            throw new InvalidOperationException("Only a closing workflow test scope can complete closure.");

        return new WorkflowTestScopeRecord(
            existing.Scope,
            WorkflowTestScopeState.Closed,
            existing.CreatedAt,
            existing.ClosingAt,
            completedAt,
            existing.CloseReason);
    }
}
