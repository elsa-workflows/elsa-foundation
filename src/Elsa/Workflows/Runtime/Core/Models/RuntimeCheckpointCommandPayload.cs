using System.Text.Json.Serialization;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// Scheduler payload for a named runtime checkpoint boundary.
/// </summary>
public sealed class RuntimeCheckpointCommandPayload
{
    public const string ActivityCompletionPropagationReason = "ActivityCompletionPropagation";

    [JsonConstructor]
    public RuntimeCheckpointCommandPayload(
        WorkflowExecutableIdentity pinnedExecutable,
        string checkpointName,
        IReadOnlyCollection<string>? activityExecutionIds,
        string reason,
        IReadOnlyCollection<RuntimePostCommitIntent>? postCommitIntents = null)
    {
        if (pinnedExecutable is null)
            throw new RuntimeCheckpointCommandPayloadValidationException("Pinned executable cannot be null.", nameof(pinnedExecutable));

        if (string.IsNullOrWhiteSpace(checkpointName))
            throw new RuntimeCheckpointCommandPayloadValidationException("Checkpoint name cannot be blank.", nameof(checkpointName));

        if (string.IsNullOrWhiteSpace(reason))
            throw new RuntimeCheckpointCommandPayloadValidationException("Reason cannot be blank.", nameof(reason));

        var activityExecutionIdSnapshot = (activityExecutionIds ?? []).ToArray();
        if (activityExecutionIdSnapshot.Any(string.IsNullOrWhiteSpace))
            throw new RuntimeCheckpointCommandPayloadValidationException("Activity execution IDs cannot contain blank values.", nameof(activityExecutionIds));

        if (activityExecutionIdSnapshot.Distinct(StringComparer.Ordinal).Count() != activityExecutionIdSnapshot.Length)
            throw new RuntimeCheckpointCommandPayloadValidationException("Activity execution IDs cannot contain duplicates.", nameof(activityExecutionIds));

        var postCommitIntentSnapshot = (postCommitIntents ?? []).ToArray();
        if (postCommitIntentSnapshot.Any(intent => intent is null))
            throw new RuntimeCheckpointCommandPayloadValidationException("Post-commit intents cannot contain null values.", nameof(postCommitIntents));

        if (postCommitIntentSnapshot
                .Select(intent => intent.IntentId)
                .Distinct(StringComparer.Ordinal)
                .Count() != postCommitIntentSnapshot.Length)
            throw new RuntimeCheckpointCommandPayloadValidationException("Post-commit intent IDs cannot contain duplicates.", nameof(postCommitIntents));

        PinnedExecutable = pinnedExecutable;
        CheckpointName = checkpointName;
        ActivityExecutionIds = activityExecutionIdSnapshot;
        Reason = reason;
        PostCommitIntents = postCommitIntentSnapshot;
    }

    public WorkflowExecutableIdentity PinnedExecutable { get; }
    public string CheckpointName { get; }
    public IReadOnlyCollection<string> ActivityExecutionIds { get; }
    public string Reason { get; }
    public IReadOnlyCollection<RuntimePostCommitIntent> PostCommitIntents { get; }
}

internal sealed class RuntimeCheckpointCommandPayloadValidationException(string message, string? paramName)
    : ArgumentException(message, paramName);
