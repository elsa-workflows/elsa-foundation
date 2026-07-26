using System.Collections.ObjectModel;
using System.Text.Json;

namespace Elsa.Workflows.Runtime.Core.Models.Alterations;

/// <summary>Stable, host-visible identity for one alteration payload schema.</summary>
public sealed record WorkflowAlterationDescriptor
{
    public WorkflowAlterationDescriptor(string kind, int schemaVersion, string displayName, string? description = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentOutOfRangeException.ThrowIfLessThan(schemaVersion, 1);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        Kind = kind;
        SchemaVersion = schemaVersion;
        DisplayName = displayName;
        Description = description;
    }

    public string Kind { get; }
    public int SchemaVersion { get; }
    public string DisplayName { get; }
    public string? Description { get; }
}

/// <summary>Deferred alteration data. The enclosing plan protects this value before it is persisted.</summary>
public sealed record WorkflowAlterationEnvelope
{
    public WorkflowAlterationEnvelope(string kind, int schemaVersion, JsonElement payload)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentOutOfRangeException.ThrowIfLessThan(schemaVersion, 1);
        if (payload.ValueKind is JsonValueKind.Undefined)
            throw new ArgumentException("An alteration payload must be a defined JSON value.", nameof(payload));

        Kind = kind;
        SchemaVersion = schemaVersion;
        Payload = payload.Clone();
    }

    public string Kind { get; }
    public int SchemaVersion { get; }
    public JsonElement Payload { get; }
}

/// <summary>Authorization facts sealed when an alteration plan is admitted.</summary>
public sealed record WorkflowAlterationAuthorityScope
{
    public WorkflowAlterationAuthorityScope(string tenantPartition, string systemIdentity, string rootInitiator, IReadOnlyDictionary<string, string>? metadata = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantPartition);
        ArgumentException.ThrowIfNullOrWhiteSpace(systemIdentity);
        ArgumentException.ThrowIfNullOrWhiteSpace(rootInitiator);

        TenantPartition = tenantPartition;
        SystemIdentity = systemIdentity;
        RootInitiator = rootInitiator;
        Metadata = WorkflowAlterationModelSnapshots.Snapshot(metadata);
    }

    public string TenantPartition { get; }
    public string SystemIdentity { get; }
    public string RootInitiator { get; }
    public IReadOnlyDictionary<string, string> Metadata { get; }
}

/// <summary>Safe submitting-operator evidence. It contains no authentication token or session material.</summary>
public sealed record WorkflowAlterationOperatorProvenance
{
    public WorkflowAlterationOperatorProvenance(string subject, string? correlationId, string? displayName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        Subject = subject;
        CorrelationId = correlationId;
        DisplayName = displayName;
    }

    public string Subject { get; }
    public string? CorrelationId { get; }
    public string? DisplayName { get; }
}

/// <summary>Exactly one execution target selector for a submitted plan.</summary>
public sealed record WorkflowAlterationTargetSelector
{
    public WorkflowAlterationTargetSelector(IReadOnlyCollection<string>? executionIds, WorkflowAlterationQuerySelector? query)
    {
        var normalizedIds = NormalizeExecutionIds(executionIds);
        if ((normalizedIds.Count == 0) == (query is null))
            throw new ArgumentException("An alteration target selector must contain exactly one of explicit execution IDs or a query selector.");

        ExecutionIds = normalizedIds;
        Query = query;
    }

    public IReadOnlyList<string> ExecutionIds { get; }
    public WorkflowAlterationQuerySelector? Query { get; }

    public static WorkflowAlterationTargetSelector ForExecutionIds(IReadOnlyCollection<string> executionIds) => new(executionIds, null);
    public static WorkflowAlterationTargetSelector ForQuery(WorkflowAlterationQuerySelector query) => new(null, query ?? throw new ArgumentNullException(nameof(query)));

    private static IReadOnlyList<string> NormalizeExecutionIds(IReadOnlyCollection<string>? ids)
    {
        if (ids is null)
            return [];

        var normalized = ids.Select(id => id?.Trim() ?? string.Empty).ToArray();
        if (normalized.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Execution IDs cannot be blank.", nameof(ids));
        return normalized.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    }
}

/// <summary>Frozen, authorization-scoped workflow execution filter for target capture.</summary>
public sealed record WorkflowAlterationQuerySelector
{
    public WorkflowAlterationQuerySelector(
        string? definitionId = null,
        WorkflowExecutionStatus? status = null,
        WorkflowRunKind? runKind = null,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        string? correlationId = null,
        string? workflowExecutionId = null,
        string? artifactId = null,
        bool matchAllAuthorized = false)
    {
        if (from is not null && to is not null && from > to)
            throw new ArgumentException("The query start timestamp cannot be after its end timestamp.");
        if (status is { } specifiedStatus && !Enum.IsDefined(specifiedStatus))
            throw new ArgumentOutOfRangeException(nameof(status));
        if (runKind is { } specifiedRunKind && !Enum.IsDefined(specifiedRunKind))
            throw new ArgumentOutOfRangeException(nameof(runKind));
        if (!matchAllAuthorized && string.IsNullOrWhiteSpace(definitionId) && status is null && runKind is null && from is null && to is null && string.IsNullOrWhiteSpace(correlationId) && string.IsNullOrWhiteSpace(workflowExecutionId) && string.IsNullOrWhiteSpace(artifactId))
            throw new ArgumentException("An otherwise empty execution query requires matchAllAuthorized.");

        DefinitionId = NormalizeOptional(definitionId);
        Status = status;
        RunKind = runKind;
        From = from;
        To = to;
        CorrelationId = NormalizeOptional(correlationId);
        WorkflowExecutionId = NormalizeOptional(workflowExecutionId);
        ArtifactId = NormalizeOptional(artifactId);
        MatchAllAuthorized = matchAllAuthorized;
    }

    public string? DefinitionId { get; }
    public WorkflowExecutionStatus? Status { get; }
    public WorkflowRunKind? RunKind { get; }
    public DateTimeOffset? From { get; }
    public DateTimeOffset? To { get; }
    public string? CorrelationId { get; }
    public string? WorkflowExecutionId { get; }
    public string? ArtifactId { get; }
    public bool MatchAllAuthorized { get; }

    private static string? NormalizeOptional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>Plaintext submission used only before payload protection.</summary>
public sealed record WorkflowAlterationSubmission
{
    public WorkflowAlterationSubmission(WorkflowAlterationAuthorityScope authorityScope, WorkflowAlterationTargetSelector target, IReadOnlyCollection<WorkflowAlterationEnvelope> alterations)
    {
        ArgumentNullException.ThrowIfNull(authorityScope);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(alterations);
        if (alterations.Count == 0)
            throw new ArgumentException("An alteration submission must contain at least one envelope.", nameof(alterations));

        AuthorityScope = authorityScope;
        Target = target;
        Alterations = alterations.ToArray();
    }

    public WorkflowAlterationAuthorityScope AuthorityScope { get; }
    public WorkflowAlterationTargetSelector Target { get; }
    public IReadOnlyList<WorkflowAlterationEnvelope> Alterations { get; }
}

/// <summary>Authenticated ciphertext retained by a durable alteration plan.</summary>
public sealed record ProtectedWorkflowAlterationPayload
{
    public ProtectedWorkflowAlterationPayload(string keyId, string algorithm, string ciphertext)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyId);
        ArgumentException.ThrowIfNullOrWhiteSpace(algorithm);
        ArgumentException.ThrowIfNullOrWhiteSpace(ciphertext);
        KeyId = keyId;
        Algorithm = algorithm;
        Ciphertext = ciphertext;
    }

    public string KeyId { get; }
    public string Algorithm { get; }
    public string Ciphertext { get; }
}

public enum WorkflowAlterationPlanStatus
{
    CapturingTargets,
    Queued,
    Running,
    Cancelling,
    Completed,
    CompletedWithFailures,
    Failed,
    Cancelled
}

public enum WorkflowAlterationJobStatus
{
    Pending,
    Running,
    Succeeded,
    Failed,
    Cancelled
}

public enum WorkflowAlterationOutcomeStatus
{
    Succeeded,
    Failed,
    Skipped
}

/// <summary>Bounded, client-safe diagnostic evidence.</summary>
public sealed record WorkflowAlterationSafeFailure
{
    public WorkflowAlterationSafeFailure(string code, string? message = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        if (message?.Length > 512)
            throw new ArgumentOutOfRangeException(nameof(message), "A safe alteration failure message is limited to 512 characters.");
        Code = code;
        Message = message;
    }

    public string Code { get; }
    public string? Message { get; }
}

/// <summary>Durable target-plan state. Deferred envelopes remain in <see cref="ProtectedPayload"/> only.</summary>
public sealed record WorkflowAlterationPlanState
{
    public WorkflowAlterationPlanState(
        string planId,
        WorkflowAlterationAuthorityScope authorityScope,
        WorkflowAlterationOperatorProvenance submittedBy,
        string idempotencyKeyHash,
        string canonicalRequestHash,
        ProtectedWorkflowAlterationPayload protectedPayload,
        WorkflowAlterationTargetSelector target,
        WorkflowAlterationPlanStatus status,
        DateTimeOffset createdAt,
        string? captureCursor = null,
        long capturedSoFar = 0,
        long targetCount = 0,
        long succeededJobCount = 0,
        long failedJobCount = 0,
        long cancelledJobCount = 0,
        DateTimeOffset? sealedAt = null,
        DateTimeOffset? startedAt = null,
        DateTimeOffset? completedAt = null,
        DateTimeOffset? cancellationRequestedAt = null,
        WorkflowAlterationSafeFailure? safeFailure = null,
        long revision = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(planId);
        ArgumentNullException.ThrowIfNull(authorityScope);
        ArgumentNullException.ThrowIfNull(submittedBy);
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKeyHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalRequestHash);
        ArgumentNullException.ThrowIfNull(protectedPayload);
        ArgumentNullException.ThrowIfNull(target);
        if (capturedSoFar < 0 || targetCount < 0 || succeededJobCount < 0 || failedJobCount < 0 || cancelledJobCount < 0 || revision < 0)
            throw new ArgumentOutOfRangeException(nameof(capturedSoFar), "Alteration plan counters and revision cannot be negative.");
        if ((status is WorkflowAlterationPlanStatus.Queued or WorkflowAlterationPlanStatus.Running or WorkflowAlterationPlanStatus.Completed or WorkflowAlterationPlanStatus.CompletedWithFailures) && sealedAt is null)
            throw new ArgumentException("A queued, running, or completed alteration plan must be sealed.", nameof(sealedAt));
        if ((status is WorkflowAlterationPlanStatus.Completed or WorkflowAlterationPlanStatus.CompletedWithFailures or WorkflowAlterationPlanStatus.Failed or WorkflowAlterationPlanStatus.Cancelled) && completedAt is null)
            throw new ArgumentException("A terminal alteration plan must have a completion timestamp.", nameof(completedAt));

        PlanId = planId;
        AuthorityScope = authorityScope;
        SubmittedBy = submittedBy;
        IdempotencyKeyHash = idempotencyKeyHash;
        CanonicalRequestHash = canonicalRequestHash;
        ProtectedPayload = protectedPayload;
        Target = target;
        Status = status;
        CreatedAt = createdAt;
        CaptureCursor = captureCursor;
        CapturedSoFar = capturedSoFar;
        TargetCount = targetCount;
        SucceededJobCount = succeededJobCount;
        FailedJobCount = failedJobCount;
        CancelledJobCount = cancelledJobCount;
        SealedAt = sealedAt;
        StartedAt = startedAt;
        CompletedAt = completedAt;
        CancellationRequestedAt = cancellationRequestedAt;
        SafeFailure = safeFailure;
        Revision = revision;
    }

    public string PlanId { get; }
    public WorkflowAlterationAuthorityScope AuthorityScope { get; }
    public WorkflowAlterationOperatorProvenance SubmittedBy { get; }
    public string IdempotencyKeyHash { get; }
    public string CanonicalRequestHash { get; }
    public ProtectedWorkflowAlterationPayload ProtectedPayload { get; }
    public WorkflowAlterationTargetSelector Target { get; }
    public WorkflowAlterationPlanStatus Status { get; }
    public DateTimeOffset CreatedAt { get; }
    public string? CaptureCursor { get; }
    public long CapturedSoFar { get; }
    public long TargetCount { get; }
    public long SucceededJobCount { get; }
    public long FailedJobCount { get; }
    public long CancelledJobCount { get; }
    public DateTimeOffset? SealedAt { get; }
    public DateTimeOffset? StartedAt { get; }
    public DateTimeOffset? CompletedAt { get; }
    public DateTimeOffset? CancellationRequestedAt { get; }
    public WorkflowAlterationSafeFailure? SafeFailure { get; }
    public long Revision { get; }

    public static WorkflowAlterationPlanState CreateCapturing(
        string planId,
        WorkflowAlterationAuthorityScope authorityScope,
        WorkflowAlterationOperatorProvenance submittedBy,
        string idempotencyKeyHash,
        string canonicalRequestHash,
        ProtectedWorkflowAlterationPayload protectedPayload,
        WorkflowAlterationTargetSelector target,
        DateTimeOffset createdAt) =>
        new(planId, authorityScope, submittedBy, idempotencyKeyHash, canonicalRequestHash, protectedPayload, target, WorkflowAlterationPlanStatus.CapturingTargets, createdAt);
}

/// <summary>Immutable optimistic facts captured for one target before job execution.</summary>
public sealed record WorkflowAlterationCapturedConcurrency(
    string? WorkflowStateRevision,
    long? RootVariableFrameRevision,
    WorkflowExecutableIdentity? PinnedExecutable);

/// <summary>Lease carried by an at-least-once alteration job worker.</summary>
public sealed record WorkflowAlterationJobClaim
{
    public WorkflowAlterationJobClaim(string ownerId, string token, DateTimeOffset expiresAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        OwnerId = ownerId;
        Token = token;
        ExpiresAt = expiresAt;
    }

    public string OwnerId { get; }
    public string Token { get; }
    public DateTimeOffset ExpiresAt { get; }
}

/// <summary>Safe, ordered outcome for one submitted alteration envelope.</summary>
public sealed record WorkflowAlterationOutcome
{
    public WorkflowAlterationOutcome(int ordinal, string kind, int schemaVersion, WorkflowAlterationOutcomeStatus status, string code, string? message, DateTimeOffset recordedAt, IReadOnlyDictionary<string, string>? structuralMetadata = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(ordinal);
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentOutOfRangeException.ThrowIfLessThan(schemaVersion, 1);
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        if (message?.Length > 512)
            throw new ArgumentOutOfRangeException(nameof(message));
        var metadata = WorkflowAlterationModelSnapshots.Snapshot(structuralMetadata);
        if (metadata.Keys.Any(key => key.Contains("payload", StringComparison.OrdinalIgnoreCase) || key.Contains("value", StringComparison.OrdinalIgnoreCase) || key.Contains("exception", StringComparison.OrdinalIgnoreCase) || key.Contains("stack", StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("Alteration outcome metadata must contain structural identifiers only.", nameof(structuralMetadata));

        Ordinal = ordinal;
        Kind = kind;
        SchemaVersion = schemaVersion;
        Status = status;
        Code = code;
        Message = message;
        RecordedAt = recordedAt;
        StructuralMetadata = metadata;
    }

    public int Ordinal { get; }
    public string Kind { get; }
    public int SchemaVersion { get; }
    public WorkflowAlterationOutcomeStatus Status { get; }
    public string Code { get; }
    public string? Message { get; }
    public DateTimeOffset RecordedAt { get; }
    public IReadOnlyDictionary<string, string> StructuralMetadata { get; }
}

/// <summary>One durable target/job state; terminal forms later join the runtime checkpoint.</summary>
public sealed record WorkflowAlterationJobState
{
    public WorkflowAlterationJobState(string jobId, string planId, string workflowExecutionId, string tenantPartition, long captureOrdinal, WorkflowAlterationJobStatus status, WorkflowAlterationJobClaim? claim, int attemptCount, IReadOnlyCollection<WorkflowAlterationOutcome> outcomes, string? checkpointCommitId, WorkflowAlterationSafeFailure? safeFailure, DateTimeOffset createdAt, DateTimeOffset? startedAt, DateTimeOffset? completedAt, long revision)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobId);
        ArgumentException.ThrowIfNullOrWhiteSpace(planId);
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantPartition);
        ArgumentOutOfRangeException.ThrowIfNegative(captureOrdinal);
        ArgumentOutOfRangeException.ThrowIfNegative(attemptCount);
        ArgumentOutOfRangeException.ThrowIfNegative(revision);
        ArgumentNullException.ThrowIfNull(outcomes);
        if (status == WorkflowAlterationJobStatus.Running && claim is null)
            throw new ArgumentException("A running alteration job requires a claim.", nameof(claim));
        if ((status is WorkflowAlterationJobStatus.Succeeded or WorkflowAlterationJobStatus.Failed) && completedAt is null)
            throw new ArgumentException("A terminal actor-executed alteration job requires a completion timestamp.", nameof(completedAt));
        if (outcomes.Select(outcome => outcome.Ordinal).Distinct().Count() != outcomes.Count)
            throw new ArgumentException("An alteration job cannot contain duplicate outcome ordinals.", nameof(outcomes));

        JobId = jobId;
        PlanId = planId;
        WorkflowExecutionId = workflowExecutionId;
        TenantPartition = tenantPartition;
        CaptureOrdinal = captureOrdinal;
        Status = status;
        Claim = claim;
        AttemptCount = attemptCount;
        Outcomes = outcomes.OrderBy(outcome => outcome.Ordinal).ToArray();
        CheckpointCommitId = checkpointCommitId;
        SafeFailure = safeFailure;
        CreatedAt = createdAt;
        StartedAt = startedAt;
        CompletedAt = completedAt;
        Revision = revision;
    }

    public string JobId { get; }
    public string PlanId { get; }
    public string WorkflowExecutionId { get; }
    public string TenantPartition { get; }
    public long CaptureOrdinal { get; }
    public WorkflowAlterationJobStatus Status { get; }
    public WorkflowAlterationJobClaim? Claim { get; }
    public int AttemptCount { get; }
    public IReadOnlyList<WorkflowAlterationOutcome> Outcomes { get; }
    public string? CheckpointCommitId { get; }
    public WorkflowAlterationSafeFailure? SafeFailure { get; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset? StartedAt { get; }
    public DateTimeOffset? CompletedAt { get; }
    public long Revision { get; }
}

internal static class WorkflowAlterationModelSnapshots
{
    public static IReadOnlyDictionary<string, string> Snapshot(IReadOnlyDictionary<string, string>? values)
    {
        var snapshot = values?.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal) ?? new Dictionary<string, string>(StringComparer.Ordinal);
        if (snapshot.Keys.Any(string.IsNullOrWhiteSpace) || snapshot.Values.Any(value => value is null))
            throw new ArgumentException("Metadata cannot contain blank keys or null values.", nameof(values));
        return new ReadOnlyDictionary<string, string>(snapshot);
    }
}
