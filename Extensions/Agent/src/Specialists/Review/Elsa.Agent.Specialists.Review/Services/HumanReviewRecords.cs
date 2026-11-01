using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Agent.Specialists.Review.Activities;
using Elsa.Agent.Specialists.Review.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Agent.Specialists.Review.Services;

/// <summary>Reads the review's own durable native records; it never invents state of its own.</summary>
internal static class HumanReviewRecords
{
    private static readonly ValueTypeDescriptor PayloadTypeValue = HumanReview.Registration("wait").PayloadType;

    public static ValueTypeDescriptor PayloadType => PayloadTypeValue;

    public static HumanReviewState ReadState(ActivityExecutionState activity)
    {
        if (activity.Execution.ActivityType != HumanReviewIds.ActivityType ||
            activity.PrivateState?.Value.InlineValue is not { ValueKind: JsonValueKind.Object } value)
            throw HumanReviewException.Invalid("The activity is not a pending human review.");
        HumanReviewState? state;
        try { state = value.Deserialize<HumanReviewState>(); }
        catch (JsonException) { throw HumanReviewException.Invalid("The human review state is unreadable."); }
        if (state is null || state.FormatVersion != HumanReviewIds.FormatVersion ||
            state.WorkflowExecutionId != activity.Execution.WorkflowExecutionId || state.ActivityExecutionId != activity.InvocationId ||
            state.ExecutableNodeId != activity.Execution.ExecutableNodeId ||
            ReviewDecisionCodec.HashContent(state.Content) != state.ContentHash ||
            ReviewDecisionCodec.WaitId(state.WorkflowExecutionId, state.ActivityExecutionId, state.ContentHash) != state.WaitId)
            throw HumanReviewException.Invalid("The human review state does not match its activity.");
        return state;
    }

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// The pinned review of a live or resolved activity. Native completion drops private state and compacts the
    /// consumed payload, so a resolved review is rebuilt from its terminal typed result.
    /// </summary>
    public static HumanReviewState? ReadPinned(ActivityExecutionState activity, string tenantId, string? partition)
    {
        if (activity.Execution.ActivityType != HumanReviewIds.ActivityType) return null;
        if (activity.PrivateState is not null) return ReadState(activity);
        if (ReadResult(activity) is not { } result || partition is null) return null;
        var waitId = ReviewDecisionCodec.WaitId(activity.Execution.WorkflowExecutionId, activity.InvocationId, result.ContentHash);
        return new(HumanReviewIds.FormatVersion, tenantId, partition, activity.Execution.WorkflowExecutionId, activity.InvocationId,
            activity.Execution.ExecutableNodeId, result.ContentHash, waitId, result.Content, result.Title, result.Notes);
    }

    /// <summary>The admitted decision: the in-flight consumed envelope, else the terminal result it produced.</summary>
    public static AdmittedReviewDecision? Admitted(ActivityExecutionState activity, ReviewDecisionCodec codec)
    {
        if (ConsumedDecision(activity) is not { } consumed) return null;
        var result = ReadResult(activity);
        if (consumed.Payload.InlineValue is { } inline)
        {
            try
            {
                var claims = ReadDelivery(codec, inline);
                return new(claims.RequestId, claims.ActorId, claims.Decision, claims.Comment);
            }
            catch (HumanReviewException) when (result is not null)
            {
                // A completed review keeps its typed result even when the in-flight envelope is no longer readable.
            }
        }
        return result is not null && Enum.TryParse<ReviewDecision>(result.Decision, out var decision)
            ? new(result.RequestId, result.DecidedBy, decision, result.Comment)
            : throw HumanReviewException.Invalid("The admitted human review decision is unreadable.");
    }

    private static HumanReviewResult? ReadResult(ActivityExecutionState activity)
    {
        if (activity.Status != ActivityExecutionStatus.Completed || activity.Completion?.Result.InlineValue is not { ValueKind: JsonValueKind.Object } value)
            return null;
        HumanReviewResult? result;
        try { result = value.Deserialize<HumanReviewResult>(Web); }
        catch (JsonException) { throw HumanReviewException.Invalid("The human review result is unreadable."); }
        if (result is null || result.Content.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null ||
            ReviewDecisionCodec.HashContent(result.Content) != result.ContentHash)
            throw HumanReviewException.Invalid("The human review result does not match its content.");
        return result;
    }

    public static ReviewDecisionClaims ReadDelivery(ReviewDecisionCodec codec, JsonElement? input)
    {
        if (input is not { ValueKind: JsonValueKind.Object } value)
            throw HumanReviewException.Invalid("The decision payload is missing.");
        ReviewDecisionDelivery? delivery;
        try { delivery = value.Deserialize<ReviewDecisionDelivery>(); }
        catch (JsonException) { throw HumanReviewException.Invalid("The decision payload is unreadable."); }
        if (delivery is null || delivery.FormatVersion != HumanReviewIds.FormatVersion)
            throw HumanReviewException.Invalid("The decision payload format is unsupported.");
        return codec.Read(delivery.Envelope);
    }

    public static bool IsDecisionMetadata(RuntimeTypedTriggerDeliveryMetadata? metadata) =>
        metadata is not null && metadata.ProviderId == HumanReviewIds.ProviderId && SameType(metadata.PayloadType, PayloadTypeValue);

    /// <summary>The consumed human review decision, if native admission already claimed one.</summary>
    public static ActivityTriggerDelivery? ConsumedDecision(ActivityExecutionState activity) =>
        activity.TriggerDeliveries?.SingleOrDefault(delivery => delivery.Status == ActivityTriggerDeliveryStatus.Consumed &&
            delivery.ProviderId == HumanReviewIds.ProviderId && SameType(delivery.PayloadType, PayloadTypeValue));

    /// <summary>
    /// True only when this exact payload is the durable claim of the sole open attempt: the attempt claim
    /// metadata names it, its trigger delivery is this delivery, and the consumed record carries the same
    /// provider, type, deduplication key and payload bytes. The runtime fences the owning work item first.
    /// </summary>
    public static bool IsClaimedReplay(ActivityExecutionState activity, RuntimeResumeBookmarkCommandPayload payload)
    {
        if (activity.Status != ActivityExecutionStatus.Running || payload.TriggerDelivery is not { } metadata ||
            !activity.Metadata.TryGetValue(RuntimeMetadataKeys.ActivityAttemptActivationClaim, out var claimedAttemptId) ||
            !activity.Metadata.ContainsKey(RuntimeMetadataKeys.ActivityAttemptActivationClaimWorkItemId))
            return false;
        var open = activity.Attempts?.Where(attempt => attempt.EndedAt is null).ToArray() ?? [];
        if (open.Length != 1 || open[0].AttemptId != claimedAttemptId || open[0].TriggerDeliveryId != metadata.DeliveryId)
            return false;
        var consumed = activity.TriggerDeliveries?.Where(delivery => delivery.Status == ActivityTriggerDeliveryStatus.Consumed).ToArray() ?? [];
        return consumed.Length == 1 && consumed[0].DeliveryId == metadata.DeliveryId && consumed[0].ProviderId == metadata.ProviderId &&
            consumed[0].DeduplicationKey == metadata.DeduplicationKey && SameType(consumed[0].PayloadType, metadata.PayloadType) &&
            consumed[0].Payload.InlineValue is { } stored && payload.Input is { } incoming && JsonElement.DeepEquals(stored, incoming);
    }

    private static bool SameType(ValueTypeDescriptor left, ValueTypeDescriptor right) =>
        left.Alias == right.Alias && left.CollectionKind == right.CollectionKind && left.SchemaVersion == right.SchemaVersion &&
        left.Schema?.GetRawText() == right.Schema?.GetRawText();
}

/// <summary>The decision native admission consumed for one review.</summary>
internal sealed record AdmittedReviewDecision(string RequestId, string ActorId, ReviewDecision Decision, string? Comment);
