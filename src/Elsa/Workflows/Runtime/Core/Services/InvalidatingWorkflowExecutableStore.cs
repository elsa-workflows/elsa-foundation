using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Diagnostics;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

/// <summary>
/// Preserves direct reads while invalidating shared-cache entries after provider-authoritative mutations.
/// </summary>
public sealed class InvalidatingWorkflowExecutableStore : WorkflowExecutableStoreDecorator
{
    private readonly WorkflowExecutableCache _cache;
    private readonly string? _partition;

    public InvalidatingWorkflowExecutableStore(
        IWorkflowExecutableStore inner,
        WorkflowExecutableCache cache,
        string? partition)
        : base(inner)
    {
        ArgumentNullException.ThrowIfNull(cache);
        if (partition is not null)
            ArgumentException.ThrowIfNullOrWhiteSpace(partition);

        _cache = cache;
        _partition = partition;
    }

    public override async ValueTask SaveAsync(
        WorkflowExecutable executable,
        CancellationToken cancellationToken = default)
    {
        await Inner.SaveAsync(executable, cancellationToken);
        Invalidate(executable.Identity.ArtifactId, WorkflowExecutableCacheTelemetry.SaveReason);
    }

    public override async ValueTask SaveBatchAsync(
        IReadOnlyList<WorkflowExecutable> executables,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(executables);
        await Inner.SaveBatchAsync(executables, cancellationToken);
        foreach (var executable in executables)
            Invalidate(executable.Identity.ArtifactId, WorkflowExecutableCacheTelemetry.SaveReason);
    }

    public override async ValueTask<bool> DeleteAsync(
        string artifactId,
        CancellationToken cancellationToken = default)
    {
        var deleted = await Inner.DeleteAsync(artifactId, cancellationToken);
        Invalidate(artifactId, WorkflowExecutableCacheTelemetry.DeleteReason);
        return deleted;
    }

    public override async ValueTask<bool> DeleteAsync(
        WorkflowExecutableDeletionGuard guard,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        var deleted = await Inner.DeleteAsync(guard, now, cancellationToken);
        if (deleted)
            Invalidate(guard.ArtifactId, WorkflowExecutableCacheTelemetry.DeleteReason);
        return deleted;
    }

    private void Invalidate(string artifactId, string reason)
    {
        if (_partition is null)
            _cache.InvalidateAllPartitions(artifactId, reason);
        else
            _cache.Invalidate(_partition, artifactId, reason);
    }
}
