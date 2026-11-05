namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>Delivery-owner observations, separately durable from native execution effects.</summary>
public static class RuntimeHistoryDeliveryFacts
{
    public static IReadOnlyList<string> Kinds { get; } = Array.AsReadOnly(new[]
    {
        "DeliveryReceived", "DeliveryDispatched", "DeliveryDuplicate", "DeliveryUnmatched", "DeliveryRejected", "DeliveryDeferred", "DeliveryFaulted"
    });

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
