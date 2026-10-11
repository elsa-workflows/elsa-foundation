namespace Elsa.Workflows.Design.Persistence.Core.Exceptions;

/// <summary>Raised when an exact workflow-version request is malformed or not forward-moving.</summary>
public sealed class WorkflowVersionSelectionException(string code, string message)
    : ArgumentException(message)
{
    public string Code { get; } = code;
}
