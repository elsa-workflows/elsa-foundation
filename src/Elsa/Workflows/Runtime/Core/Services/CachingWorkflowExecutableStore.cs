using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Diagnostics;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

/// <summary>
/// Decorates a durable executable store with bounded process-local caching of immutable artifacts.
/// </summary>
public sealed class CachingWorkflowExecutableStore : IWorkflowExecutableStore
{
    private const string DefaultPartition = "default";

    private readonly IWorkflowExecutableStore _inner;
    private readonly WorkflowExecutableCache _cache;
    private readonly string _partition;
    private readonly Func<string, CancellationToken, ValueTask<WorkflowExecutable?>> _load;

    public CachingWorkflowExecutableStore(IWorkflowExecutableStore inner, WorkflowExecutableCacheOptions options)
        : this(
            inner,
            new WorkflowExecutableCache(options),
            DefaultPartition,
            (artifactId, cancellationToken) => inner.FindAsync(artifactId, cancellationToken))
    {
    }

    /// <summary>
    /// Creates one scoped adapter over shared cache state. The load callback must own any resources it
    /// retains after this adapter's dependency-injection scope is disposed.
    /// </summary>
    public CachingWorkflowExecutableStore(
        IWorkflowExecutableStore inner,
        WorkflowExecutableCache cache,
        string partition,
        Func<string, CancellationToken, ValueTask<WorkflowExecutable?>> load)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentException.ThrowIfNullOrWhiteSpace(partition);
        ArgumentNullException.ThrowIfNull(load);

        _inner = inner;
        _cache = cache;
        _partition = partition;
        _load = load;
    }

    public async ValueTask SaveAsync(WorkflowExecutable executable, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(executable);
        ArgumentException.ThrowIfNullOrWhiteSpace(executable.Identity.ArtifactId);

        await _inner.SaveAsync(executable, cancellationToken);
        _cache.Invalidate(_partition, executable.Identity.ArtifactId, WorkflowExecutableCacheTelemetry.SaveReason);
    }

    public async ValueTask<bool> DeleteAsync(string artifactId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactId);

        var deleted = await _inner.DeleteAsync(artifactId, cancellationToken);
        _cache.Invalidate(_partition, artifactId, WorkflowExecutableCacheTelemetry.DeleteReason);
        return deleted;
    }

    public ValueTask<WorkflowExecutableRootWriteLease?> TryAcquireRootWriteLeaseAsync(
        string artifactId,
        string leaseId,
        DateTimeOffset expiresAt,
        DateTimeOffset now,
        CancellationToken cancellationToken = default) =>
        _inner.TryAcquireRootWriteLeaseAsync(artifactId, leaseId, expiresAt, now, cancellationToken);

    public ValueTask<bool> RenewRootWriteLeaseAsync(
        WorkflowExecutableRootWriteLease lease,
        DateTimeOffset expiresAt,
        DateTimeOffset now,
        CancellationToken cancellationToken = default) =>
        _inner.RenewRootWriteLeaseAsync(lease, expiresAt, now, cancellationToken);

    public ValueTask ReleaseRootWriteLeaseAsync(
        WorkflowExecutableRootWriteLease lease,
        CancellationToken cancellationToken = default) =>
        _inner.ReleaseRootWriteLeaseAsync(lease, cancellationToken);

    public ValueTask<WorkflowExecutableDeletionGuard?> TryBeginDeletionAsync(
        string artifactId,
        string operationId,
        DateTimeOffset expiresAt,
        DateTimeOffset now,
        CancellationToken cancellationToken = default) =>
        _inner.TryBeginDeletionAsync(artifactId, operationId, expiresAt, now, cancellationToken);

    public ValueTask<bool> CancelDeletionAsync(
        WorkflowExecutableDeletionGuard guard,
        CancellationToken cancellationToken = default) =>
        _inner.CancelDeletionAsync(guard, cancellationToken);

    public async ValueTask<bool> DeleteAsync(
        WorkflowExecutableDeletionGuard guard,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(guard);

        var deleted = await _inner.DeleteAsync(guard, now, cancellationToken);
        if (deleted)
            _cache.Invalidate(_partition, guard.ArtifactId, WorkflowExecutableCacheTelemetry.DeleteReason);
        return deleted;
    }

    public ValueTask<WorkflowExecutable?> FindAsync(
        string artifactId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactId);
        return _cache.FindAsync(_partition, artifactId, _load, cancellationToken);
    }

    public ValueTask<RuntimeStorePage<WorkflowExecutable>> ListPageAsync(
        RuntimeStorePageRequest request,
        CancellationToken cancellationToken = default) =>
        _inner.ListPageAsync(request, cancellationToken);
}
