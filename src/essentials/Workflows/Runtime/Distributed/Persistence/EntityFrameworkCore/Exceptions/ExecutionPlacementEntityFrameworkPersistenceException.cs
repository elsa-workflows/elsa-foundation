namespace Elsa.Workflows.Runtime.Distributed.Persistence.EntityFrameworkCore.Exceptions;

/// <summary>
/// Stable execution-placement EF persistence failure boundary; provider exception types do not cross it.
/// </summary>
public sealed class ExecutionPlacementEntityFrameworkPersistenceException(
    string operation,
    string identity,
    string message,
    Exception innerException) : InvalidOperationException(message, innerException)
{
    public string Operation { get; } = operation;

    public string Identity { get; } = identity;
}
