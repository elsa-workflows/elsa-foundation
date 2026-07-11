namespace Elsa.Workflows.Runtime.Core.Exceptions;

/// <summary>
/// A typed publication-time failure raised before trigger or recurring-schedule state is mutated.
/// </summary>
public sealed class WorkflowTriggerPreflightException : Exception
{
    public WorkflowTriggerPreflightException(
        string artifactId,
        string executableNodeId,
        string activityType,
        IReadOnlyCollection<string> providerIds,
        string facet,
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactId);
        ArgumentException.ThrowIfNullOrWhiteSpace(executableNodeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(activityType);
        ArgumentNullException.ThrowIfNull(providerIds);
        ArgumentException.ThrowIfNullOrWhiteSpace(facet);

        ArtifactId = artifactId;
        ExecutableNodeId = executableNodeId;
        ActivityType = activityType;
        ProviderIds = providerIds.ToArray();
        Facet = facet;
    }

    public string ArtifactId { get; }
    public string ExecutableNodeId { get; }
    public string ActivityType { get; }
    public IReadOnlyCollection<string> ProviderIds { get; }
    public string Facet { get; }
}
