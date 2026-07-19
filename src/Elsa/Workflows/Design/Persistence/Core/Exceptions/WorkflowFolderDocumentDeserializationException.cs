namespace Elsa.Workflows.Design.Persistence.Core.Exceptions;

/// <summary>Signals a corrupt durable workflow-folder document without leaking serializer details through the API.</summary>
public sealed class WorkflowFolderDocumentDeserializationException(string documentId, Exception innerException)
    : InvalidOperationException($"Workflow-folder document '{documentId}' could not be read.", innerException);
