namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>Immutable execution authority and root-initiator attribution.</summary>
public sealed class WorkflowExecutionAuthoritySnapshot
{
    public WorkflowExecutionAuthoritySnapshot(
        string systemIdentity,
        string rootInitiator,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(systemIdentity);
        ArgumentException.ThrowIfNullOrWhiteSpace(rootInitiator);

        SystemIdentity = systemIdentity;
        RootInitiator = rootInitiator;
        Metadata = RuntimeModelMetadata.Snapshot(metadata);
    }

    public string SystemIdentity { get; }
    public string RootInitiator { get; }
    public IReadOnlyDictionary<string, string> Metadata { get; }

    public static WorkflowExecutionAuthoritySnapshot CreateRoot(
        string requestedBy,
        IReadOnlyDictionary<string, string>? metadata = null) =>
        new(requestedBy, requestedBy, metadata);
}
