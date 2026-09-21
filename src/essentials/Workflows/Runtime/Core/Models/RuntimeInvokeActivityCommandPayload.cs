using System.Text.Json.Serialization;
using Elsa.Workflows.Runtime.Core.Contracts;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// Scheduler payload for invoking one running activity execution.
/// </summary>
public sealed class RuntimeInvokeActivityCommandPayload : IActivityCommandPayload
{
    public const string StartedActivityReason = "StartedActivity";

    [JsonConstructor]
    public RuntimeInvokeActivityCommandPayload(
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
