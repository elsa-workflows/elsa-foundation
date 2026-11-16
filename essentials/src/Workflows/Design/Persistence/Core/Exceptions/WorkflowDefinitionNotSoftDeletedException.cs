namespace Elsa.Workflows.Design.Persistence.Core.Exceptions;

/// <summary>
/// Thrown when permanent deletion is requested for a workflow definition that has not been soft-deleted.
/// </summary>
public sealed class WorkflowDefinitionNotSoftDeletedException(string definitionId)
    : InvalidOperationException(
        $"Workflow definition '{definitionId}' must be soft-deleted before permanent deletion.")
{
    public string DefinitionId { get; } = definitionId;
}
