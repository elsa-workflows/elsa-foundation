using System.Text.Json.Serialization;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// Scheduler payload for starting one scheduled activity execution.
/// </summary>
public sealed class RuntimeStartActivityCommandPayload
{
    public const string ScheduledActivityReason = "ScheduledActivity";

    [JsonConstructor]
    public RuntimeStartActivityCommandPayload(
        WorkflowExecutableIdentity pinnedExecutable,
        string executableNodeId,
        string activityExecutionId,
        string reason)
    {
        ArgumentNullException.ThrowIfNull(pinnedExecutable);
        ArgumentException.ThrowIfNullOrWhiteSpace(executableNodeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(activityExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        PinnedExecutable = pinnedExecutable;
        ExecutableNodeId = executableNodeId;
        ActivityExecutionId = activityExecutionId;
        Reason = reason;
    }

    public WorkflowExecutableIdentity PinnedExecutable { get; }
    public string ExecutableNodeId { get; }
    public string ActivityExecutionId { get; }
    public string Reason { get; }
}
