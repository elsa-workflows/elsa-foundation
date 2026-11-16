using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Exceptions;

/// <summary>Ordinary source selection could not read the authoritative references; no eligibility decision was made.</summary>
public sealed class WorkflowExecutableSourceReadException : Exception
{
    public WorkflowExecutableSourceReadException(string artifactId, WorkflowExecutableReferenceScope requiredScope, Exception innerException)
        : base("Workflow executable source references could not be read for ordinary selection.", innerException)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactId);
        ArgumentNullException.ThrowIfNull(innerException);
        ArtifactId = artifactId;
        RequiredScope = requiredScope;
    }

    public string ArtifactId { get; }
    public WorkflowExecutableReferenceScope RequiredScope { get; }
}
