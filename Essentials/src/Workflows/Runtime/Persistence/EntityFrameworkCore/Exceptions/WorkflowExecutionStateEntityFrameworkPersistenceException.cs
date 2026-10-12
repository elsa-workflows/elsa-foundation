namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Exceptions;

/// <summary>Stable EF persistence boundary for durable workflow execution state (R10); provider exception types do not cross it.</summary>
public sealed class WorkflowExecutionStateEntityFrameworkPersistenceException(
    string operation,
    string identity,
    string message,
    Exception innerException) : InvalidOperationException(message, innerException)
{
    public string Operation { get; } = operation;

    public string Identity { get; } = identity;
}
