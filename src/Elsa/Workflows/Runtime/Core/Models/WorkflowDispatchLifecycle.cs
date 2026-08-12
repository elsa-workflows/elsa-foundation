using System.Globalization;
using Elsa.Workflows.Runtime.Core.Constants;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>Shared identity and monotonic-lifecycle rules used by every dispatch store.</summary>
public static class WorkflowDispatchLifecycle
{
    public const string StartChildIntentKind = "Elsa.Activities.DispatchWorkflow.StartChild";
    public const string ResumeParentIntentKind = "Elsa.Activities.DispatchWorkflow.ResumeParent";
    public const string DiagnosticCodeMetadataKey = "runtime.diagnostic.code";
    public const string DiagnosticCategoryMetadataKey = "runtime.diagnostic.category";
    public const string ChildStartDeliveryFailedCode = "child-start-delivery-failed";
    public const string DeliveryCategory = "delivery";
    public const string CancellationPolicyMetadataKey = "runtime.dispatch.cancelChildOnParentCancellation";
    public const string CancellationStateMetadataKey = "runtime.dispatch.cancellationState";
    public const string CancelledBeforeAdmissionState = "parent-before-admission";
    public const string CancellationRequestedState = "parent-cancellation-requested";
    public const string ScopeCancelledBeforeAdmissionState = "test-scope-before-admission";
    public const string ScopeCancellationRequestedState = "test-scope-cancellation-requested";
    public const string DeliveryGenerationMetadataKey = "runtime.dispatch.deliveryGeneration";
    public const string DeliveryDeadLetterIdMetadataKey = "runtime.dispatch.deliveryDeadLetterId";
    public const string DeliveryIncidentIdMetadataKey = "runtime.dispatch.deliveryIncidentId";
    public const string DeliveryAttemptCountMetadataKey = "runtime.dispatch.deliveryAttemptCount";
    public const string DeliveryFirstAttemptAtMetadataKey = "runtime.dispatch.deliveryFirstAttemptAt";
    public const string DeliveryFailedAtMetadataKey = "runtime.dispatch.deliveryFailedAt";
    public const string DeliveryRedriveRequestIdMetadataKey = "runtime.dispatch.deliveryRedriveRequestId";

    public static void SetEffectiveCancellationPolicy(
        IDictionary<string, string> metadata,
        WorkflowDispatchMode mode,
        bool cancelChildOnParentCancellation)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        metadata[CancellationPolicyMetadataKey] =
            (mode == WorkflowDispatchMode.WaitForCompletion && cancelChildOnParentCancellation).ToString().ToLowerInvariant();
    }

    /// <summary>Reads the persisted policy; legacy waited records default to enabled and detached records to disabled.</summary>
    public static bool IsCancellationPropagationEnabled(WorkflowDispatchRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (record.Mode != WorkflowDispatchMode.WaitForCompletion)
            return false;
        if (!record.Metadata.TryGetValue(CancellationPolicyMetadataKey, out var value))
            return true;
        return value switch
        {
            "true" => true,
            "false" => false,
            _ => throw new InvalidOperationException($"Workflow dispatch '{record.DispatchId}' carries an invalid cancellation policy.")
        };
    }

    public static bool WasCancelledBeforeAdmission(WorkflowDispatchRecord record) =>
        HasCancellationState(record, CancelledBeforeAdmissionState) ||
        HasCancellationState(record, ScopeCancelledBeforeAdmissionState);

    public static bool IsCancellationRequested(WorkflowDispatchRecord record) =>
        HasCancellationState(record, CancellationRequestedState);

    public static bool IsTestScopeCancellationRequested(WorkflowDispatchRecord record) =>
        HasCancellationState(record, ScopeCancellationRequestedState);

    public static WorkflowDispatchRecord CancelTestScopeBeforeAdmission(
        WorkflowDispatchRecord record,
        DateTimeOffset requestedAt)
    {
        ValidateTestScopeCleanupTarget(record);
        if (record.Status != WorkflowDispatchStatus.Pending)
            throw new InvalidOperationException($"Workflow dispatch '{record.DispatchId}' must be Pending before test-scope admission can be cancelled.");
        return WithCancellationState(record, WorkflowDispatchStatus.Cancelled, ScopeCancelledBeforeAdmissionState, requestedAt);
    }

    public static WorkflowDispatchRecord MarkTestScopeCancellationRequested(
        WorkflowDispatchRecord record,
        DateTimeOffset requestedAt)
    {
        ValidateTestScopeCleanupTarget(record);
        if (record.Status != WorkflowDispatchStatus.Started)
            throw new InvalidOperationException($"Workflow dispatch '{record.DispatchId}' must be Started before test-scope cancellation can be requested.");
        return WithCancellationState(record, WorkflowDispatchStatus.Started, ScopeCancellationRequestedState, requestedAt);
    }

    public static void ValidateTestScopeCancellationIntent(
        WorkflowDispatchRecord record,
        RuntimePostCommitIntent intent)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(intent);
        var identity = new WorkflowDispatchIdentity(record.ParentWorkflowExecutionId, record.ParentActivityExecutionId);
        if (!StringComparer.Ordinal.Equals(intent.IntentId, identity.ChildCancelIntentId) ||
            !StringComparer.Ordinal.Equals(intent.WorkflowExecutionId, record.ParentWorkflowExecutionId) ||
            !StringComparer.Ordinal.Equals(intent.ActivityExecutionId, record.ParentActivityExecutionId) ||
            !StringComparer.Ordinal.Equals(intent.IdempotencyKey, identity.ChildCancelIdempotencyKey) ||
            !intent.Metadata.TryGetValue(RuntimeMetadataKeys.DispatchId, out var dispatchId) ||
            !StringComparer.Ordinal.Equals(dispatchId, record.DispatchId) ||
            !intent.Metadata.TryGetValue(RuntimeMetadataKeys.ChildWorkflowExecutionId, out var childId) ||
            !StringComparer.Ordinal.Equals(childId, record.ChildWorkflowExecutionId))
        {
            throw new InvalidOperationException("The workflow test-scope cancellation intent conflicts with the persisted dispatch.");
        }
    }

    public static WorkflowDispatchRecord CancelBeforeAdmission(
        WorkflowDispatchRecord record,
        DateTimeOffset requestedAt)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (record.Status != WorkflowDispatchStatus.Pending)
            throw new InvalidOperationException($"Workflow dispatch '{record.DispatchId}' must be Pending before admission can be cancelled.");
        return WithCancellationState(record, WorkflowDispatchStatus.Cancelled, CancelledBeforeAdmissionState, requestedAt);
    }

    public static WorkflowDispatchRecord MarkCancellationRequested(
        WorkflowDispatchRecord record,
        DateTimeOffset requestedAt)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (record.Status != WorkflowDispatchStatus.Started)
            throw new InvalidOperationException($"Workflow dispatch '{record.DispatchId}' must be Started before child cancellation can be requested.");
        return WithCancellationState(record, WorkflowDispatchStatus.Started, CancellationRequestedState, requestedAt);
    }

    public static WorkflowDispatchRecord Transition(
        WorkflowDispatchRecord record,
        WorkflowDispatchStatus status,
        DateTimeOffset updatedAt)
    {
        ArgumentNullException.ThrowIfNull(record);

        var candidate = new WorkflowDispatchRecord(
            record.DispatchId,
            record.ParentWorkflowExecutionId,
            record.ParentActivityExecutionId,
            record.ChildWorkflowExecutionId,
            record.ChildExecutable,
            record.ChildSource,
            record.Mode,
            status,
            record.CorrelationId,
            record.TenantId,
            record.Partition,
            record.RunKind,
            record.Authority,
            record.InputDescriptors,
            record.CreatedAt,
            updatedAt,
            record.Metadata,
            record.DispatchNestingDepth,
            record.TestScope);
        ValidateTransition(record, candidate);
        return candidate;
    }

    public static WorkflowDispatchRecord TransitionToDispatchFailed(
        WorkflowDispatchRecord record,
        DateTimeOffset updatedAt)
    {
        ArgumentNullException.ThrowIfNull(record);
        var metadata = record.Metadata.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        metadata[DiagnosticCodeMetadataKey] = ChildStartDeliveryFailedCode;
        metadata[DiagnosticCategoryMetadataKey] = DeliveryCategory;
        var candidate = new WorkflowDispatchRecord(
            record.DispatchId,
            record.ParentWorkflowExecutionId,
            record.ParentActivityExecutionId,
            record.ChildWorkflowExecutionId,
            record.ChildExecutable,
            record.ChildSource,
            record.Mode,
            WorkflowDispatchStatus.DispatchFailed,
            record.CorrelationId,
            record.TenantId,
            record.Partition,
            record.RunKind,
            record.Authority,
            record.InputDescriptors,
            record.CreatedAt,
            updatedAt,
            metadata,
            record.DispatchNestingDepth,
            record.TestScope);
        ValidateTransition(record, candidate);
        return candidate;
    }

    /// <summary>Creates the safe terminal projection for exhausted child-start delivery.</summary>
    public static WorkflowDispatchRecord TransitionToDispatchFailed(
        WorkflowDispatchRecord record,
        string deadLetterId,
        int generation,
        int attemptCount,
        DateTimeOffset firstAttemptAt,
        DateTimeOffset failedAt)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentException.ThrowIfNullOrWhiteSpace(deadLetterId);
        if (generation < 0)
            throw new ArgumentOutOfRangeException(nameof(generation));
        if (attemptCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(attemptCount), "A delivery dead letter requires at least one attempt.");
        if (firstAttemptAt == default)
            throw new ArgumentOutOfRangeException(nameof(firstAttemptAt));
        if (failedAt < firstAttemptAt)
            throw new ArgumentOutOfRangeException(nameof(failedAt), "Final delivery failure cannot precede the first attempt.");

        var identity = new WorkflowDispatchIdentity(record.ParentWorkflowExecutionId, record.ParentActivityExecutionId);
        var metadata = record.Metadata.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        metadata[DiagnosticCodeMetadataKey] = ChildStartDeliveryFailedCode;
        metadata[DiagnosticCategoryMetadataKey] = DeliveryCategory;
        metadata[DeliveryGenerationMetadataKey] = generation.ToString(CultureInfo.InvariantCulture);
        metadata[DeliveryDeadLetterIdMetadataKey] = deadLetterId;
        metadata[DeliveryIncidentIdMetadataKey] = identity.DeliveryIncidentId(generation);
        metadata[DeliveryAttemptCountMetadataKey] = attemptCount.ToString(CultureInfo.InvariantCulture);
        metadata[DeliveryFirstAttemptAtMetadataKey] = firstAttemptAt.ToString("O", CultureInfo.InvariantCulture);
        metadata[DeliveryFailedAtMetadataKey] = failedAt.ToString("O", CultureInfo.InvariantCulture);
        var candidate = new WorkflowDispatchRecord(
            record.DispatchId,
            record.ParentWorkflowExecutionId,
            record.ParentActivityExecutionId,
            record.ChildWorkflowExecutionId,
            record.ChildExecutable,
            record.ChildSource,
            record.Mode,
            WorkflowDispatchStatus.DispatchFailed,
            record.CorrelationId,
            record.TenantId,
            record.Partition,
            record.RunKind,
            record.Authority,
            record.InputDescriptors,
            record.CreatedAt,
            failedAt > record.UpdatedAt ? failedAt : record.UpdatedAt,
            metadata,
            record.DispatchNestingDepth,
            record.TestScope);
        ValidateTransition(record, candidate);
        return candidate;
    }

    public static bool IsTerminal(WorkflowDispatchStatus status) => status is
        WorkflowDispatchStatus.Completed or
        WorkflowDispatchStatus.Faulted or
        WorkflowDispatchStatus.Cancelled or
        WorkflowDispatchStatus.DispatchFailed;

    public static void ValidateNew(WorkflowDispatchRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (record.Status != WorkflowDispatchStatus.Pending)
            throw new InvalidOperationException($"New workflow dispatch '{record.DispatchId}' must start in Pending status.");
        ValidateMetadata(record);
    }

    public static void ValidateTransition(WorkflowDispatchRecord existing, WorkflowDispatchRecord candidate)
    {
        ArgumentNullException.ThrowIfNull(existing);
        ArgumentNullException.ThrowIfNull(candidate);

        if (RecordsEqual(existing, candidate))
            return;
        if (!ImmutableFieldsEqual(existing, candidate))
            throw new InvalidOperationException($"Workflow dispatch '{candidate.DispatchId}' already exists with conflicting immutable identity or context.");
        if (existing.Metadata.TryGetValue(CancellationStateMetadataKey, out var existingCancellationState) &&
            (!candidate.Metadata.TryGetValue(CancellationStateMetadataKey, out var candidateCancellationState) ||
             !StringComparer.Ordinal.Equals(existingCancellationState, candidateCancellationState)))
        {
            throw new InvalidOperationException(
                $"Workflow dispatch '{candidate.DispatchId}' cannot remove or replace its durable cancellation state.");
        }
        if (candidate.UpdatedAt < existing.UpdatedAt)
            throw new InvalidOperationException($"Workflow dispatch '{candidate.DispatchId}' cannot move its update timestamp backwards.");
        ValidateMetadata(candidate);

        var addsCancellationRequest =
            existing.Status == WorkflowDispatchStatus.Started &&
            candidate.Status == WorkflowDispatchStatus.Started &&
            !existing.Metadata.ContainsKey(CancellationStateMetadataKey) &&
            (IsCancellationRequested(candidate) || IsTestScopeCancellationRequested(candidate));
        var validTransition = addsCancellationRequest || existing.Status switch
        {
            WorkflowDispatchStatus.Pending => candidate.Status is
                WorkflowDispatchStatus.Started or
                WorkflowDispatchStatus.Completed or
                WorkflowDispatchStatus.Faulted or
                WorkflowDispatchStatus.Cancelled or
                WorkflowDispatchStatus.DispatchFailed,
            WorkflowDispatchStatus.Started => candidate.Status is
                WorkflowDispatchStatus.Completed or
                WorkflowDispatchStatus.Faulted or
                WorkflowDispatchStatus.Cancelled or
                WorkflowDispatchStatus.DispatchFailed,
            _ => false
        };
        if (!validTransition)
            throw new InvalidOperationException($"Workflow dispatch '{candidate.DispatchId}' cannot transition from '{existing.Status}' to '{candidate.Status}'.");
    }

    /// <summary>Ensures Pending creation is parent-owned and checkpoint lifecycle projection is child-owned.</summary>
    public static void ValidateCheckpointOwnership(string workflowExecutionId, WorkflowDispatchRecord record)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentNullException.ThrowIfNull(record);

        var ownerExecutionId = record.Status == WorkflowDispatchStatus.Pending
            ? record.ParentWorkflowExecutionId
            : record.ChildWorkflowExecutionId;
        if (!StringComparer.Ordinal.Equals(workflowExecutionId, ownerExecutionId))
        {
            var ownerKind = record.Status == WorkflowDispatchStatus.Pending ? "parent" : "child";
            throw new InvalidOperationException(
                $"Workflow dispatch '{record.DispatchId}' status '{record.Status}' must be committed by its {ownerKind} workflow execution.");
        }
    }

    public static bool ImmutableFieldsEqual(WorkflowDispatchRecord left, WorkflowDispatchRecord right) =>
        StringComparer.Ordinal.Equals(left.DispatchId, right.DispatchId) &&
        StringComparer.Ordinal.Equals(left.ParentWorkflowExecutionId, right.ParentWorkflowExecutionId) &&
        StringComparer.Ordinal.Equals(left.ParentActivityExecutionId, right.ParentActivityExecutionId) &&
        StringComparer.Ordinal.Equals(left.ChildWorkflowExecutionId, right.ChildWorkflowExecutionId) &&
        Equals(left.ChildExecutable, right.ChildExecutable) &&
        Equals(left.ChildSource, right.ChildSource) &&
        left.Mode == right.Mode &&
        StringComparer.Ordinal.Equals(left.CorrelationId, right.CorrelationId) &&
        StringComparer.Ordinal.Equals(left.TenantId, right.TenantId) &&
        Equals(left.Partition, right.Partition) &&
        left.RunKind == right.RunKind &&
        WorkflowTestScope.ContextEquals(left.TestScope, right.TestScope) &&
        AuthorityEquals(left.Authority, right.Authority) &&
        left.InputDescriptors.SequenceEqual(right.InputDescriptors) &&
        left.DispatchNestingDepth == right.DispatchNestingDepth &&
        left.CreatedAt == right.CreatedAt &&
        ImmutableMetadataEquals(left.Metadata, right.Metadata);

    public static bool RecordsEqual(WorkflowDispatchRecord left, WorkflowDispatchRecord right) =>
        ImmutableFieldsEqual(left, right) &&
        left.Status == right.Status &&
        left.UpdatedAt == right.UpdatedAt &&
        MetadataEquals(left.Metadata, right.Metadata);

    /// <summary>
    /// Resolves stronger durable child evidence before committing a final start-delivery failure. A visible child
    /// repairs Pending to Started; a business-terminal dispatch remains unchanged; no evidence returns null. A state
    /// at the deterministic child ID with conflicting retained context fails closed.
    /// </summary>
    public static WorkflowDispatchRecord? ResolveSuccessfulChildDelivery(
        WorkflowDispatchRecord dispatch,
        WorkflowExecutionState? childExecution,
        DateTimeOffset observedAt)
    {
        ArgumentNullException.ThrowIfNull(dispatch);
        if (observedAt == default)
            throw new ArgumentOutOfRangeException(nameof(observedAt));
        if (dispatch.Status is
            WorkflowDispatchStatus.Completed or
            WorkflowDispatchStatus.Faulted or
            WorkflowDispatchStatus.Cancelled)
        {
            return dispatch;
        }
        if (dispatch.Status == WorkflowDispatchStatus.DispatchFailed)
            return null;
        if (childExecution is null)
            return null;

        var matches =
            StringComparer.Ordinal.Equals(childExecution.WorkflowExecutionId, dispatch.ChildWorkflowExecutionId) &&
            WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(
                childExecution.PinnedExecutable,
                dispatch.ChildExecutable) &&
            (childExecution.PinnedSource is null || Equals(childExecution.PinnedSource, dispatch.ChildSource)) &&
            StringComparer.Ordinal.Equals(
                childExecution.ParentWorkflowExecutionId,
                dispatch.ParentWorkflowExecutionId) &&
            StringComparer.Ordinal.Equals(childExecution.CorrelationId, dispatch.CorrelationId) &&
            StringComparer.Ordinal.Equals(childExecution.TenantId, dispatch.TenantId) &&
            Equals(childExecution.Partition, dispatch.Partition) &&
            childExecution.RunKind == dispatch.RunKind &&
            childExecution.DispatchNestingDepth == dispatch.DispatchNestingDepth &&
            WorkflowTestScope.ContextEquals(childExecution.TestScope, dispatch.TestScope) &&
            childExecution.Authority is not null &&
            AuthorityEquals(childExecution.Authority, dispatch.Authority);
        if (!matches)
        {
            throw new InvalidOperationException(
                $"Workflow execution '{dispatch.ChildWorkflowExecutionId}' conflicts with its retained workflow dispatch context.");
        }

        return dispatch.Status == WorkflowDispatchStatus.Pending
            ? dispatch.TransitionTo(
                WorkflowDispatchStatus.Started,
                observedAt > dispatch.UpdatedAt ? observedAt : dispatch.UpdatedAt)
            : dispatch;
    }

    public static string? ReadSafeDiagnosticCode(WorkflowDispatchRecord record) =>
        record.Metadata.TryGetValue(DiagnosticCodeMetadataKey, out var value) &&
        StringComparer.Ordinal.Equals(value, ChildStartDeliveryFailedCode)
            ? value
            : null;

    public static string? ReadSafeDiagnosticCategory(WorkflowDispatchRecord record) =>
        record.Metadata.TryGetValue(DiagnosticCategoryMetadataKey, out var value) &&
        StringComparer.Ordinal.Equals(value, DeliveryCategory)
            ? value
            : null;

    public static string? ReadDeliveryIncidentId(WorkflowDispatchRecord record) =>
        IsDeliveryFailure(record) &&
        record.Metadata.TryGetValue(DeliveryIncidentIdMetadataKey, out var incidentId) &&
        !string.IsNullOrWhiteSpace(incidentId)
            ? incidentId
            : null;

    public static string? ReadDeliveryDeadLetterId(WorkflowDispatchRecord record) =>
        IsDeliveryFailure(record) &&
        record.Metadata.TryGetValue(DeliveryDeadLetterIdMetadataKey, out var deadLetterId) &&
        !string.IsNullOrWhiteSpace(deadLetterId)
            ? deadLetterId
            : null;

    /// <summary>Legacy dispatches without delivery metadata belong to original generation zero.</summary>
    public static int ReadDeliveryGeneration(WorkflowDispatchRecord record) =>
        TryReadNonNegativeInt(record, DeliveryGenerationMetadataKey, out var generation) ? generation : 0;

    public static int ReadDeliveryAttemptCount(WorkflowDispatchRecord record) =>
        TryReadNonNegativeInt(record, DeliveryAttemptCountMetadataKey, out var attemptCount) ? attemptCount : 0;

    public static DateTimeOffset? ReadDeliveryFirstAttemptAt(WorkflowDispatchRecord record) =>
        IsDeliveryFailure(record) &&
        record.Metadata.TryGetValue(DeliveryFirstAttemptAtMetadataKey, out var value) &&
        DateTimeOffset.TryParseExact(value, "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var firstAttemptAt)
            ? firstAttemptAt
            : null;

    public static DateTimeOffset? ReadDeliveryFailedAt(WorkflowDispatchRecord record) =>
        IsDeliveryFailure(record) &&
        record.Metadata.TryGetValue(DeliveryFailedAtMetadataKey, out var value) &&
        DateTimeOffset.TryParseExact(value, "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var failedAt)
            ? failedAt
            : null;

    public static bool IsRedriveEligible(WorkflowDispatchRecord record) =>
        record.Mode == WorkflowDispatchMode.FireAndForget &&
        record.Status == WorkflowDispatchStatus.DispatchFailed &&
        ReadDeliveryIncidentId(record) is not null &&
        ReadDeliveryDeadLetterId(record) is not null &&
        ReadDeliveryAttemptCount(record) > 0 &&
        ReadDeliveryFailedAt(record) is not null;

    public static string? ReadDeliveryRedriveRequestId(WorkflowDispatchRecord record) =>
        record.Metadata.TryGetValue(DeliveryRedriveRequestIdMetadataKey, out var requestId) &&
        !string.IsNullOrWhiteSpace(requestId)
            ? requestId
            : null;

    /// <summary>
    /// Creates the only sanctioned DispatchFailed-to-Pending transition. Ordinary dispatch saves deliberately
    /// continue to reject terminal reopening; atomic redrive stores call this builder only after validating the
    /// matching failed-final outbox item in the same mutation boundary.
    /// </summary>
    public static WorkflowDispatchRecord RedriveDelivery(
        WorkflowDispatchRecord record,
        string requestId,
        DateTimeOffset requestedAt)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);
        if (requestedAt == default)
            throw new ArgumentOutOfRangeException(nameof(requestedAt), "A workflow dispatch redrive requires a recorded request time.");
        if (!IsRedriveEligible(record))
            throw new InvalidOperationException($"Workflow dispatch '{record.DispatchId}' is not eligible for delivery redrive.");

        var generation = checked(ReadDeliveryGeneration(record) + 1);
        var metadata = record.Metadata.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        metadata.Remove(DiagnosticCodeMetadataKey);
        metadata.Remove(DiagnosticCategoryMetadataKey);
        metadata.Remove(DeliveryDeadLetterIdMetadataKey);
        metadata.Remove(DeliveryIncidentIdMetadataKey);
        metadata.Remove(DeliveryAttemptCountMetadataKey);
        metadata.Remove(DeliveryFirstAttemptAtMetadataKey);
        metadata.Remove(DeliveryFailedAtMetadataKey);
        metadata[DeliveryGenerationMetadataKey] = generation.ToString(CultureInfo.InvariantCulture);
        metadata[DeliveryRedriveRequestIdMetadataKey] = requestId;

        return new WorkflowDispatchRecord(
            record.DispatchId,
            record.ParentWorkflowExecutionId,
            record.ParentActivityExecutionId,
            record.ChildWorkflowExecutionId,
            record.ChildExecutable,
            record.ChildSource,
            record.Mode,
            WorkflowDispatchStatus.Pending,
            record.CorrelationId,
            record.TenantId,
            record.Partition,
            record.RunKind,
            record.Authority,
            record.InputDescriptors,
            record.CreatedAt,
            requestedAt > record.UpdatedAt ? requestedAt : record.UpdatedAt,
            metadata,
            record.DispatchNestingDepth,
            record.TestScope);
    }

    private static bool AuthorityEquals(WorkflowExecutionAuthoritySnapshot left, WorkflowExecutionAuthoritySnapshot right) =>
        StringComparer.Ordinal.Equals(left.SystemIdentity, right.SystemIdentity) &&
        StringComparer.Ordinal.Equals(left.RootInitiator, right.RootInitiator) &&
        MetadataEquals(left.Metadata, right.Metadata);

    private static bool MetadataEquals(IReadOnlyDictionary<string, string> left, IReadOnlyDictionary<string, string> right) =>
        left.Count == right.Count &&
        left.All(entry => right.TryGetValue(entry.Key, out var value) && StringComparer.Ordinal.Equals(entry.Value, value));

    private static bool ImmutableMetadataEquals(
        IReadOnlyDictionary<string, string> left,
        IReadOnlyDictionary<string, string> right)
    {
        var leftImmutable = left.Where(item => !IsMutableMetadataKey(item.Key)).ToArray();
        var rightImmutable = right.Where(item => !IsMutableMetadataKey(item.Key)).ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        return leftImmutable.Length == rightImmutable.Count &&
               leftImmutable.All(item => rightImmutable.TryGetValue(item.Key, out var value) && StringComparer.Ordinal.Equals(item.Value, value));
    }

    private static bool IsMutableMetadataKey(string key) =>
        StringComparer.Ordinal.Equals(key, DiagnosticCodeMetadataKey) ||
        StringComparer.Ordinal.Equals(key, DiagnosticCategoryMetadataKey) ||
        StringComparer.Ordinal.Equals(key, CancellationStateMetadataKey) ||
        StringComparer.Ordinal.Equals(key, DeliveryGenerationMetadataKey) ||
        StringComparer.Ordinal.Equals(key, DeliveryDeadLetterIdMetadataKey) ||
        StringComparer.Ordinal.Equals(key, DeliveryIncidentIdMetadataKey) ||
        StringComparer.Ordinal.Equals(key, DeliveryAttemptCountMetadataKey) ||
        StringComparer.Ordinal.Equals(key, DeliveryFirstAttemptAtMetadataKey) ||
        StringComparer.Ordinal.Equals(key, DeliveryFailedAtMetadataKey) ||
        StringComparer.Ordinal.Equals(key, DeliveryRedriveRequestIdMetadataKey);

    private static bool HasCancellationState(WorkflowDispatchRecord record, string expected) =>
        record.Metadata.TryGetValue(CancellationStateMetadataKey, out var value) &&
        StringComparer.Ordinal.Equals(value, expected);

    private static WorkflowDispatchRecord WithCancellationState(
        WorkflowDispatchRecord record,
        WorkflowDispatchStatus status,
        string state,
        DateTimeOffset requestedAt)
    {
        if (requestedAt == default)
            throw new ArgumentOutOfRangeException(nameof(requestedAt), "A dispatch cancellation transition requires a recorded timestamp.");
        var metadata = record.Metadata.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        metadata[CancellationStateMetadataKey] = state;
        var updatedAt = requestedAt > record.UpdatedAt ? requestedAt : record.UpdatedAt;
        var candidate = new WorkflowDispatchRecord(
            record.DispatchId,
            record.ParentWorkflowExecutionId,
            record.ParentActivityExecutionId,
            record.ChildWorkflowExecutionId,
            record.ChildExecutable,
            record.ChildSource,
            record.Mode,
            status,
            record.CorrelationId,
            record.TenantId,
            record.Partition,
            record.RunKind,
            record.Authority,
            record.InputDescriptors,
            record.CreatedAt,
            updatedAt,
            metadata,
            record.DispatchNestingDepth,
            record.TestScope);
        ValidateTransition(record, candidate);
        return candidate;
    }

    private static void ValidateMetadata(WorkflowDispatchRecord record)
    {
        ValidateCancellationMetadata(record);
        ValidateDiagnostics(record);
    }

    private static void ValidateCancellationMetadata(WorkflowDispatchRecord record)
    {
        if (record.Metadata.TryGetValue(CancellationPolicyMetadataKey, out var policy) &&
            policy is not ("true" or "false"))
        {
            throw new InvalidOperationException($"Workflow dispatch '{record.DispatchId}' carries an invalid cancellation policy.");
        }
        if (record.Mode == WorkflowDispatchMode.FireAndForget && StringComparer.Ordinal.Equals(policy, "true"))
            throw new InvalidOperationException($"Fire-and-forget workflow dispatch '{record.DispatchId}' cannot enable cancellation propagation.");

        var state = record.Metadata.GetValueOrDefault(CancellationStateMetadataKey);
        if (state is null)
            return;
        if (StringComparer.Ordinal.Equals(state, CancelledBeforeAdmissionState) && record.Status == WorkflowDispatchStatus.Cancelled)
            return;
        if (StringComparer.Ordinal.Equals(state, CancellationRequestedState) && record.Status != WorkflowDispatchStatus.Pending)
            return;
        if (StringComparer.Ordinal.Equals(state, ScopeCancelledBeforeAdmissionState) &&
            record.Status == WorkflowDispatchStatus.Cancelled &&
            IsTestScopeCleanupTarget(record))
            return;
        if (StringComparer.Ordinal.Equals(state, ScopeCancellationRequestedState) &&
            record.Status != WorkflowDispatchStatus.Pending &&
            IsTestScopeCleanupTarget(record))
            return;
        throw new InvalidOperationException($"Workflow dispatch '{record.DispatchId}' carries an invalid cancellation state.");
    }

    private static void ValidateTestScopeCleanupTarget(WorkflowDispatchRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (!IsTestScopeCleanupTarget(record))
            throw new InvalidOperationException($"Workflow dispatch '{record.DispatchId}' is not an eligible detached test-scope cleanup target.");
    }

    private static bool IsTestScopeCleanupTarget(WorkflowDispatchRecord record) =>
        record.Mode == WorkflowDispatchMode.FireAndForget &&
        record.RunKind == WorkflowRunKind.TestRun &&
        record.TestScope is not null;

    private static void ValidateDiagnostics(WorkflowDispatchRecord record)
    {
        var code = record.Metadata.GetValueOrDefault(DiagnosticCodeMetadataKey);
        var category = record.Metadata.GetValueOrDefault(DiagnosticCategoryMetadataKey);
        if (code is null && category is null)
            return;
        if (record.Status != WorkflowDispatchStatus.DispatchFailed ||
            !StringComparer.Ordinal.Equals(code, ChildStartDeliveryFailedCode) ||
            !StringComparer.Ordinal.Equals(category, DeliveryCategory))
        {
            throw new InvalidOperationException($"Workflow dispatch '{record.DispatchId}' carries an unsupported diagnostic classification.");
        }

        var deliveryFields = new[]
        {
            DeliveryGenerationMetadataKey,
            DeliveryDeadLetterIdMetadataKey,
            DeliveryIncidentIdMetadataKey,
            DeliveryAttemptCountMetadataKey,
            DeliveryFirstAttemptAtMetadataKey,
            DeliveryFailedAtMetadataKey
        };
        var fieldCount = deliveryFields.Count(record.Metadata.ContainsKey);
        if (fieldCount == 0)
            return;
        if (fieldCount != deliveryFields.Length ||
            !TryReadNonNegativeInt(record, DeliveryGenerationMetadataKey, out var generation) ||
            !TryReadNonNegativeInt(record, DeliveryAttemptCountMetadataKey, out var attemptCount) ||
            attemptCount <= 0 ||
            string.IsNullOrWhiteSpace(record.Metadata[DeliveryDeadLetterIdMetadataKey]) ||
            !DateTimeOffset.TryParseExact(record.Metadata[DeliveryFirstAttemptAtMetadataKey], "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var firstAttemptAt) ||
            !DateTimeOffset.TryParseExact(record.Metadata[DeliveryFailedAtMetadataKey], "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var failedAt) ||
            failedAt < firstAttemptAt ||
            failedAt > record.UpdatedAt)
        {
            throw new InvalidOperationException($"Workflow dispatch '{record.DispatchId}' carries incomplete delivery-failure evidence.");
        }

        var identity = new WorkflowDispatchIdentity(record.ParentWorkflowExecutionId, record.ParentActivityExecutionId);
        if (!StringComparer.Ordinal.Equals(record.Metadata[DeliveryIncidentIdMetadataKey], identity.DeliveryIncidentId(generation)))
            throw new InvalidOperationException($"Workflow dispatch '{record.DispatchId}' carries a noncanonical delivery incident identity.");
    }

    private static bool IsDeliveryFailure(WorkflowDispatchRecord record) =>
        record.Status == WorkflowDispatchStatus.DispatchFailed &&
        StringComparer.Ordinal.Equals(ReadSafeDiagnosticCode(record), ChildStartDeliveryFailedCode) &&
        StringComparer.Ordinal.Equals(ReadSafeDiagnosticCategory(record), DeliveryCategory);

    private static bool TryReadNonNegativeInt(WorkflowDispatchRecord record, string key, out int value)
    {
        value = 0;
        return record.Metadata.TryGetValue(key, out var raw) &&
               int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out value) &&
               value >= 0;
    }
}
