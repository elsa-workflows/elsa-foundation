namespace Elsa.Workflows.Publishing.Core.Models;

public sealed class WorkflowExecutableCompilationException(
    string? definitionId,
    string? definitionVersionId,
    string message,
    Exception innerException)
    : ArgumentException(message, innerException)
{
    public string? DefinitionId { get; } = definitionId;
    public string? DefinitionVersionId { get; } = definitionVersionId;
}
