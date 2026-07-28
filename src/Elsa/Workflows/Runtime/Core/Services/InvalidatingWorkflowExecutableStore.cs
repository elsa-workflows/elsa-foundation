using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Diagnostics;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

/// <summary>
/// Preserves direct reads while invalidating shared-cache entries after provider-authoritative mutations.
/// </summary>
public sealed class InvalidatingWorkflowExecutableStore : IWorkflowExecutableStore
{
    private readonly IWorkflowExecutableStore _inner;
    private readonly WorkflowExecutableCache _cache;
    private readonly string? _partition;

    public InvalidatingWorkflowExecutableStore(
        IWorkflowExecutableStore inner,
        WorkflowExecutableCache cache,
        string? partition)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(cache);
        if (partition is not null)
            ArgumentException.ThrowIfNullOrWhiteSpace(partition);

        _inner = inner;
        _cache = cache;
        _partition = partition;
    }

    public async ValueTask SaveAsync(
        WorkflowExecutable executable,
        CancellationToken cancellationToken = default)
    {
        await _inner.SaveAsync(executable, cancellationToken);
        Invalidate(executable.Identity.ArtifactId, WorkflowExecutableCacheTelemetry.SaveReason);
    }

    public async ValueTask<bool> DeleteAsync(
        string artifactId,
        CancellationToken cancellationToken = default)
    {
        var deleted = await _inner.DeleteAsync(artifactId, cancellationToken);
        Invalidate(artifactId, WorkflowExecutableCacheTelemetry.DeleteReason);
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
        var deleted = await _inner.DeleteAsync(guard, now, cancellationToken);
        if (deleted)
            Invalidate(guard.ArtifactId, WorkflowExecutableCacheTelemetry.DeleteReason);
        return deleted;
    }

    public ValueTask<WorkflowExecutable?> FindAsync(
        string artifactId,
        CancellationToken cancellationToken = default) =>
        _inner.FindAsync(artifactId, cancellationToken);

    public ValueTask<RuntimeStorePage<WorkflowExecutable>> ListPageAsync(
        RuntimeStorePageRequest request,
        CancellationToken cancellationToken = default) =>
        _inner.ListPageAsync(request, cancellationToken);

    private void Invalidate(string artifactId, string reason)
    {
        if (_partition is null)
            _cache.InvalidateAllPartitions(artifactId, reason);
        else
            _cache.Invalidate(_partition, artifactId, reason);
    }
}
