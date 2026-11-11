namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Exceptions;

/// <summary>Stable EF persistence boundary for scheduler poison recorded under an execution fence; provider exception types do not cross it.</summary>
public sealed class WorkflowSchedulerPoisonEntityFrameworkPersistenceException(
    string operation,
    string identity,
    string message,
    Exception innerException) : InvalidOperationException(message, innerException)
{
    public string Operation { get; } = operation;

    public string Identity { get; } = identity;
}
