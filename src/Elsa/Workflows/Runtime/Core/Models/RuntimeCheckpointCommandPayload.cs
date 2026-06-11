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
        ArgumentNullException.ThrowIfNull(pinnedExecutable);
        ArgumentException.ThrowIfNullOrWhiteSpace(checkpointName);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        var activityExecutionIdSnapshot = (activityExecutionIds ?? []).ToArray();
        if (activityExecutionIdSnapshot.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Activity execution IDs cannot contain blank values.", nameof(activityExecutionIds));

        if (activityExecutionIdSnapshot.Distinct(StringComparer.Ordinal).Count() != activityExecutionIdSnapshot.Length)
            throw new ArgumentException("Activity execution IDs cannot contain duplicates.", nameof(activityExecutionIds));

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
