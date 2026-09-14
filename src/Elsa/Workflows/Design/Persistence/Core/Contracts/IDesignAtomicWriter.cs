using Elsa.Workflows.Design.Persistence.Core.Models;

namespace Elsa.Workflows.Design.Persistence.Core.Contracts;

/// <summary>
/// Provider-neutral atomic boundary for design mutations. Implementations persist the operation
/// marker in the same transaction as staged changes and must make replay, conflict, and result
/// integrity decisions before invoking volatile source reads.
/// </summary>
public interface IDesignAtomicWriter
{
    Task<T> ExecuteAsync<T>(
        DesignOperationKey operationKey,
        string operationKind,
        object requestMaterial,
        Func<CancellationToken, Task<T>> stage,
        CancellationToken cancellationToken = default);
}
