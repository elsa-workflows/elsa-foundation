namespace Elsa.Workflows.Runtime.Distributed.Persistence.EntityFrameworkCore.Exceptions;

public sealed class ExecutionCommandTransportEntityFrameworkPersistenceException(
    string operation,
    string identity,
    string message,
    Exception innerException) : InvalidOperationException(message, innerException)
{
    public string Operation { get; } = operation;
    public string Identity { get; } = identity;
}
