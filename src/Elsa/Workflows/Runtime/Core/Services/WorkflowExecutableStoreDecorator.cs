using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

/// <summary>
/// Pass-through base for <see cref="IWorkflowExecutableStore"/> decorators: every member forwards to the
/// wrapped store, so a decorator overrides only the operations it actually intercepts.
/// </summary>
public abstract class WorkflowExecutableStoreDecorator : IWorkflowExecutableStore
{
    protected WorkflowExecutableStoreDecorator(IWorkflowExecutableStore inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        Inner = inner;
    }

    protected IWorkflowExecutableStore Inner { get; }

    public virtual ValueTask SaveAsync(WorkflowExecutable executable, CancellationToken cancellationToken = default) =>
        Inner.SaveAsync(executable, cancellationToken);

    public virtual ValueTask SaveBatchAsync(
        IReadOnlyList<WorkflowExecutable> executables,
        CancellationToken cancellationToken = default) =>
        Inner.SaveBatchAsync(executables, cancellationToken);

    public virtual ValueTask<bool> DeleteAsync(string artifactId, CancellationToken cancellationToken = default) =>
        Inner.DeleteAsync(artifactId, cancellationToken);

    public virtual ValueTask<WorkflowExecutableRootWriteLease?> TryAcquireRootWriteLeaseAsync(
        string artifactId,
        string leaseId,
        DateTimeOffset expiresAt,
        DateTimeOffset now,
        CancellationToken cancellationToken = default) =>
        Inner.TryAcquireRootWriteLeaseAsync(artifactId, leaseId, expiresAt, now, cancellationToken);

    public virtual ValueTask<bool> RenewRootWriteLeaseAsync(
        WorkflowExecutableRootWriteLease lease,
        DateTimeOffset expiresAt,
        DateTimeOffset now,
        CancellationToken cancellationToken = default) =>
        Inner.RenewRootWriteLeaseAsync(lease, expiresAt, now, cancellationToken);

    public virtual ValueTask ReleaseRootWriteLeaseAsync(
        WorkflowExecutableRootWriteLease lease,
        CancellationToken cancellationToken = default) =>
        Inner.ReleaseRootWriteLeaseAsync(lease, cancellationToken);

    public virtual ValueTask<WorkflowExecutableDeletionGuard?> TryBeginDeletionAsync(
        string artifactId,
        string operationId,
        DateTimeOffset expiresAt,
        DateTimeOffset now,
        CancellationToken cancellationToken = default) =>
        Inner.TryBeginDeletionAsync(artifactId, operationId, expiresAt, now, cancellationToken);

    public virtual ValueTask<bool> CancelDeletionAsync(
        WorkflowExecutableDeletionGuard guard,
        CancellationToken cancellationToken = default) =>
        Inner.CancelDeletionAsync(guard, cancellationToken);

    public virtual ValueTask<bool> DeleteAsync(
        WorkflowExecutableDeletionGuard guard,
        DateTimeOffset now,
        CancellationToken cancellationToken = default) =>
        Inner.DeleteAsync(guard, now, cancellationToken);

    public virtual ValueTask<WorkflowExecutable?> FindAsync(
        string artifactId,
        CancellationToken cancellationToken = default) =>
        Inner.FindAsync(artifactId, cancellationToken);

    public virtual ValueTask<RuntimeStorePage<WorkflowExecutable>> ListPageAsync(
        RuntimeStorePageRequest request,
        CancellationToken cancellationToken = default) =>
        Inner.ListPageAsync(request, cancellationToken);
}
