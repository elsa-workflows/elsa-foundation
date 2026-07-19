namespace Elsa.Workflows.Design.Persistence.Core.Exceptions;

/// <summary>Raised when a persisted workflow-definition page cannot be read as a design document.</summary>
public sealed class WorkflowDefinitionPageDeserializationException(string documentId, Exception innerException)
    : Exception($"Workflow definition page document '{documentId}' could not be deserialized.", innerException)
{
    public string DocumentId { get; } = documentId;
}
