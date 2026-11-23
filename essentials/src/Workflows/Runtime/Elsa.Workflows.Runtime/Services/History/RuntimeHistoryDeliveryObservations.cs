using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Services.History;

/// <summary>The bookmark-delivery owner's closed observations, separate from native execution effects.</summary>
internal static class RuntimeHistoryDeliveryObservations
{
    internal static IReadOnlyList<string> Kinds { get; } = Array.AsReadOnly(new[]
    { "DeliveryReceived", "DeliveryDispatched", "DeliveryDuplicate", "DeliveryUnmatched", "DeliveryRejected", "DeliveryDeferred", "DeliveryFaulted" });

    // Supplementary diagnostics stay on the original thrown exception, never in history/source metadata.
    internal const string FailureDataKey = "Elsa.Runtime.History.DeliveryObservationFailures";

    internal static RuntimeHistoryObservation Create(RuntimeHistoryCapturePolicy policy, string workflowExecutionId, string receiptId,
        string name, DateTimeOffset occurredAt, string stimulusType, string? bookmarkId, string? activityExecutionId,
        string? executableNodeId, RuntimeHistoryCausation? cause, string? reasonCode = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(receiptId);
        var metadata = new Dictionary<string, string> { ["history.stimulusType"] = stimulusType };
        if (reasonCode is not null) metadata["history.reasonCode"] = reasonCode;
        var e = new RuntimeHistoryEvent("delivery-history:" + RuntimeHistoryIdentity.CreateBinding([workflowExecutionId, receiptId, name]),
            workflowExecutionId, 0, RuntimeHistoryEventCategory.DeliveryObservation, name, occurredAt, activityExecutionId,
            executableNodeId, metadata: metadata)
            { Authority = RuntimeHistoryAuthority.DeliveryObservation, BookmarkId = bookmarkId, Causation = cause };
        var observation = new RuntimeHistoryObservation(policy, e);
        RuntimeHistorySafety.ValidateObservation(observation);
        return observation;
    }
}
