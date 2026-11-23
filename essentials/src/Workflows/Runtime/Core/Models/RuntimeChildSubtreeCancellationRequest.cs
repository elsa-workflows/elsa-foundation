namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// Activity-owned request to cancel one scheduled child's activity-execution subtree.
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

    private RuntimeChildSubtreeCancellationRequest(string reason, IReadOnlyDictionary<string, string>? metadata)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        ChildActivityExecutionId = string.Empty;
        Reason = reason;
        Metadata = RuntimeModelMetadata.Snapshot(metadata ?? new Dictionary<string, string>());
        TargetsEveryLiveChild = true;
    }

    /// <summary>
    /// A request to cancel the subtree of every child that is still live when the evaluation commits. The runtime
    /// resolves the children itself, so a parent that does not track its children's activity-execution ids can end
    /// them all, for example a Flowchart that ends on <c>Break</c>.
    /// </summary>
    public static RuntimeChildSubtreeCancellationRequest ForEveryLiveChild(string reason, IReadOnlyDictionary<string, string>? metadata = null) =>
        new(reason, metadata);

    /// <summary>The targeted child; empty when <see cref="TargetsEveryLiveChild"/>.</summary>
    public string ChildActivityExecutionId { get; }

    /// <summary>Whether this request targets every live child rather than one.</summary>
    public bool TargetsEveryLiveChild { get; }
    public string Reason { get; }
    public IReadOnlyDictionary<string, string> Metadata { get; }
}
