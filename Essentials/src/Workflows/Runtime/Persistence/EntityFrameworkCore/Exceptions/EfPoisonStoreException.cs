namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Exceptions;

/// <summary>
/// Boundary exception of the EF scheduler-poison store's fenced record, as its sibling stores have theirs; provider
/// exception types do not cross it.
/// </summary>
public sealed class EfPoisonStoreException(
    string operation,
    string identity,
    string message,
    Exception innerException) : InvalidOperationException(message, innerException)
{
    public string Operation { get; } = operation;

    public string Identity { get; } = identity;
}
