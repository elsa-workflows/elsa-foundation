namespace Elsa.Workflows.Runtime.Core.Exceptions;

public sealed class WorkflowExecutableNotFoundException : Exception
{
    public WorkflowExecutableNotFoundException(string artifactId)
        : base($"Workflow executable artifact '{artifactId}' does not exist.")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactId);

        ArtifactId = artifactId;
    }

    public string ArtifactId { get; }
}
