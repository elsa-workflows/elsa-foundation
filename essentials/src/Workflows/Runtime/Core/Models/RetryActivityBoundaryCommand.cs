using System.Text.Json.Serialization;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// Semantic retry of one failed activity boundary. The exact workflow artifact is part of the command so retry
/// cannot drift to a newer publication.
/// </summary>
public sealed class RetryActivityBoundaryCommand
{
    [JsonConstructor]
    public RetryActivityBoundaryCommand(
        string activityExecutionId,
        string retryActivityExecutionId,
        WorkflowExecutableIdentity pinnedExecutable,
        string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(activityExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(retryActivityExecutionId);
        if (StringComparer.Ordinal.Equals(activityExecutionId, retryActivityExecutionId))
            throw new ArgumentException("A boundary retry must use a fresh activity execution id.", nameof(retryActivityExecutionId));
        ArgumentNullException.ThrowIfNull(pinnedExecutable);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        ActivityExecutionId = activityExecutionId;
        RetryActivityExecutionId = retryActivityExecutionId;
        PinnedExecutable = pinnedExecutable;
        Reason = reason;
    }

    public string ActivityExecutionId { get; }
    public string RetryActivityExecutionId { get; }
    public WorkflowExecutableIdentity PinnedExecutable { get; }
    public string Reason { get; }
}
