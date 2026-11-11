namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Exceptions;

/// <summary>Stable EF persistence boundary for scheduler poison recorded under an execution fence; provider exception types do not cross it.</summary>
/// <remarks>
/// Naming exception (docs/reference/elsa-naming.md, R1 and R2): the name is longer than the rules allow and repeats its
/// namespace. It keeps the pattern of its siblings in this folder,
/// <see cref="WorkflowExecutionStateEntityFrameworkPersistenceException"/> and
/// <see cref="BookmarkStateEntityFrameworkPersistenceException"/>, so a store's boundary exception can be found by its
/// store's name.
/// </remarks>
public sealed class WorkflowSchedulerPoisonEntityFrameworkPersistenceException(
    string operation,
    string identity,
    string message,
    Exception innerException) : InvalidOperationException(message, innerException)
{
    public string Operation { get; } = operation;

    public string Identity { get; } = identity;
}
