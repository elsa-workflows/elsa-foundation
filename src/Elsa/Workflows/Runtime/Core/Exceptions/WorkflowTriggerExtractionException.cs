namespace Elsa.Workflows.Runtime.Core.Exceptions;

/// <summary>
/// Thrown when a published workflow node is marked as a start-trigger but its stimulus cannot be extracted at
/// publish time (W7, E3-1) — for example no registered provider recognizes the activity type, or its stimulus
/// key is not an authored literal. It fails the publish so an unroutable trigger is never silently persisted.
/// </summary>
public sealed class WorkflowTriggerExtractionException : Exception
{
    public WorkflowTriggerExtractionException(string artifactId, string executableNodeId, string message)
        : base(message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactId);
        ArgumentException.ThrowIfNullOrWhiteSpace(executableNodeId);

        ArtifactId = artifactId;
        ExecutableNodeId = executableNodeId;
    }

    public string ArtifactId { get; }
    public string ExecutableNodeId { get; }
}
