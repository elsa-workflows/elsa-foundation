namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// Activity-owned request to cancel one scheduled child's activity-execution subtree (spec 112).
/// Staged during a structural child-completion/child-fault evaluation and applied atomically in the
/// same checkpoint commit as the parent's continuation.
/// </summary>
public sealed class RuntimeChildSubtreeCancellationRequest
{
    public RuntimeChildSubtreeCancellationRequest(
        string childActivityExecutionId,
        string reason,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(childActivityExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        ChildActivityExecutionId = childActivityExecutionId;
        Reason = reason;
        Metadata = RuntimeModelMetadata.Snapshot(metadata ?? new Dictionary<string, string>());
    }

    public string ChildActivityExecutionId { get; }
    public string Reason { get; }
    public IReadOnlyDictionary<string, string> Metadata { get; }
}
