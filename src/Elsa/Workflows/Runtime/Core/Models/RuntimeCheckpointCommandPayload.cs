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
        string reason)
    {
        if (pinnedExecutable is null)
            throw new ArgumentNullException(nameof(PinnedExecutable));

        if (string.IsNullOrWhiteSpace(checkpointName))
            throw new ArgumentException("Checkpoint name cannot be blank.", nameof(CheckpointName));

        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Reason cannot be blank.", nameof(Reason));

        var activityExecutionIdSnapshot = (activityExecutionIds ?? []).ToArray();
        if (activityExecutionIdSnapshot.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Activity execution IDs cannot contain blank values.", nameof(ActivityExecutionIds));

        if (activityExecutionIdSnapshot.Distinct(StringComparer.Ordinal).Count() != activityExecutionIdSnapshot.Length)
            throw new ArgumentException("Activity execution IDs cannot contain duplicates.", nameof(ActivityExecutionIds));

        PinnedExecutable = pinnedExecutable;
        CheckpointName = checkpointName;
        ActivityExecutionIds = activityExecutionIdSnapshot;
        Reason = reason;
    }

    public WorkflowExecutableIdentity PinnedExecutable { get; }
    public string CheckpointName { get; }
    public IReadOnlyCollection<string> ActivityExecutionIds { get; }
    public string Reason { get; }
}
