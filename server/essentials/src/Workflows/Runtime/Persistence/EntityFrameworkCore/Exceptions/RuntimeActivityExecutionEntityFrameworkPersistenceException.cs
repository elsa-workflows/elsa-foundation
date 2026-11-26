namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Exceptions;

/// <summary>Stable EF persistence boundary for Runtime activity-execution state, inspection and hierarchy providers.</summary>
public sealed class RuntimeActivityExecutionEntityFrameworkPersistenceException(
    string operation,
    string identity,
    string message,
    Exception innerException) : InvalidOperationException(message, innerException)
{
    public string Operation { get; } = operation;

    public string Identity { get; } = identity;
}
