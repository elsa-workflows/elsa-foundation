using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Diagnostics;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

/// <summary>
/// Decorates a durable executable store with bounded process-local caching of immutable artifacts.
/// </summary>
public sealed class CachingWorkflowExecutableStore : WorkflowExecutableStoreDecorator
{
    private const string DefaultPartition = "default";

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
        : base(inner)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentException.ThrowIfNullOrWhiteSpace(partition);
        ArgumentNullException.ThrowIfNull(load);

        _cache = cache;
        _partition = partition;
        _load = load;
    }

    public override async ValueTask SaveAsync(WorkflowExecutable executable, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(executable);
        ArgumentException.ThrowIfNullOrWhiteSpace(executable.Identity.ArtifactId);

        await Inner.SaveAsync(executable, cancellationToken);
        _cache.Invalidate(_partition, executable.Identity.ArtifactId, WorkflowExecutableCacheTelemetry.SaveReason);
    }

    public override async ValueTask<bool> DeleteAsync(string artifactId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactId);

        var deleted = await Inner.DeleteAsync(artifactId, cancellationToken);
        _cache.Invalidate(_partition, artifactId, WorkflowExecutableCacheTelemetry.DeleteReason);
        return deleted;
    }

    public override async ValueTask<bool> DeleteAsync(
        WorkflowExecutableDeletionGuard guard,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(guard);

        var deleted = await Inner.DeleteAsync(guard, now, cancellationToken);
        if (deleted)
            _cache.Invalidate(_partition, guard.ArtifactId, WorkflowExecutableCacheTelemetry.DeleteReason);
        return deleted;
    }

    public override ValueTask<WorkflowExecutable?> FindAsync(
        string artifactId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactId);
        return _cache.FindAsync(_partition, artifactId, _load, cancellationToken);
    }
}
