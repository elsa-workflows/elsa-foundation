namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>Delivery-owner observations, separately durable from native execution effects.</summary>
public static class RuntimeHistoryDeliveryFacts
{
    /// <summary>Supplementary diagnostics on an unchanged direct-delivery exception, never persisted history or source authority.</summary>
    /// <remarks>
    /// The value is an immutable <c>IReadOnlyList&lt;object&gt;</c>. Owned entries are
    /// <c>KeyValuePair&lt;WeakReference&lt;object&gt;, Exception&gt;</c>: the weak reference identifies the exact live
    /// dispatch-request invocation and the exception retains its failed failure-observation write. Previous entries
    /// remain diagnostic only; opaque entries, including diagnostic-listener failures, never affect classification.
    /// A caller already holding a verified source may correlate only its own newly-created
    /// request by reference; neither an old entry nor an opaque prior value grants current source authority.
    /// Weak references avoid retaining request payloads. This key and its values must never enter history metadata.
    /// </remarks>
    public const string ObservationFailureDataKey = "Elsa.Runtime.History.DeliveryObservationFailures";

    public static IReadOnlyList<string> Kinds { get; } = Array.AsReadOnly(new[]
    {
        "DeliveryReceived", "DeliveryDispatched", "DeliveryDuplicate", "DeliveryUnmatched", "DeliveryRejected", "DeliveryDeferred", "DeliveryFaulted"
    });

    public static IReadOnlyList<string> OwnerObservationKinds { get; } = Array.AsReadOnly(new[]
    {
        "ChildStartAttemptObserved", "ChildStartDispositionObserved", "StimulusRoutingAttemptObserved", "StimulusRoutingDispositionObserved"
    });
    private static readonly HashSet<string> OwnerReasons = new(StringComparer.Ordinal)
    {
        "handler-invocation-observed", "routing-source-unavailable-observed", "routing-failure-observed", "routing-capacity-shed-observed",
        "saved-cohort-routing-acknowledged-observed", "source-terminal-or-retired-observed", "committed-child-outcome-observed",
        "durable-start-forwarding-observed", "existing-child-start-observed", "child-start-accepted-faulted-observed",
        "child-start-queue-acceptance-observed", "child-start-failure-observed"
    };

    public static RuntimeHistoryObservation CreateOwnerObservation(RuntimeHistoryCapturePolicy policy, string workflowExecutionId, string receiptId,
        string name, DateTimeOffset occurredAt, RuntimeHistoryCausation cause, string? activityExecutionId = null,
        string? relatedWorkflowExecutionId = null, string? reasonCode = null, string? attemptId = null)
    {
        ArgumentNullException.ThrowIfNull(policy); ArgumentNullException.ThrowIfNull(cause);
        ArgumentException.ThrowIfNullOrWhiteSpace(receiptId);
        if (!OwnerObservationKinds.Contains(name, StringComparer.Ordinal)) throw new ArgumentOutOfRangeException(nameof(name));
        if (receiptId.Length > 1024 || relatedWorkflowExecutionId is not null && (string.IsNullOrWhiteSpace(relatedWorkflowExecutionId) || relatedWorkflowExecutionId.Length > 1024) || cause.WorkflowExecutionId != workflowExecutionId || string.IsNullOrWhiteSpace(cause.HistoryEventId) ||
            string.IsNullOrWhiteSpace(cause.DeliveryId) || reasonCode is not null && !OwnerReasons.Contains(reasonCode))
            throw new ArgumentException("Owner observations require exact source causation and a closed disposition.");
        var metadata = new Dictionary<string, string>();
        if (relatedWorkflowExecutionId is not null) metadata["history.relatedWorkflowExecutionId"] = relatedWorkflowExecutionId;
        if (reasonCode is not null) metadata["history.reasonCode"] = reasonCode;
        var e = new RuntimeHistoryEvent("source-history:" + RuntimeHistoryIdentity.CreateBinding([workflowExecutionId, receiptId, name]),
            workflowExecutionId, 0, RuntimeHistoryEventCategory.DeliveryObservation, name, occurredAt, activityExecutionId, metadata: metadata)
        { Authority = RuntimeHistoryAuthority.DeliveryObservation, Causation = cause, AttemptId = attemptId };
        RuntimeHistorySafety.ValidateMetadata(e);
        return new(policy, e);
    }

    public static RuntimeHistoryObservation Create(RuntimeHistoryCapturePolicy policy, string workflowExecutionId, string receiptId,
        string name, DateTimeOffset occurredAt, string stimulusType, string? bookmarkId = null, string? activityExecutionId = null,
        string? executableNodeId = null, RuntimeHistoryCausation? cause = null, string? reasonCode = null)
    {
        if (!Kinds.Contains(name, StringComparer.Ordinal)) throw new ArgumentOutOfRangeException(nameof(name));
        var metadata = new Dictionary<string, string> { ["history.stimulusType"] = stimulusType };
        if (reasonCode is not null) metadata["history.reasonCode"] = reasonCode;
        var e = new RuntimeHistoryEvent("delivery-history:" + RuntimeHistoryIdentity.CreateBinding([workflowExecutionId, receiptId, name]),
            workflowExecutionId, 0, RuntimeHistoryEventCategory.DeliveryObservation, name, occurredAt, activityExecutionId,
            executableNodeId, metadata: metadata)
        { Authority = RuntimeHistoryAuthority.DeliveryObservation, BookmarkId = bookmarkId, Causation = cause };
        RuntimeHistorySafety.ValidateMetadata(e);
        return new(policy, e);
    }
}
