using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Diagnostics;

namespace Elsa.Workflows.Runtime.Services.Executables;

/// <summary>
/// Preserves direct reads while invalidating shared-cache entries after provider-authoritative mutations.
/// </summary>
public sealed class InvalidatingWorkflowExecutableStore : WorkflowExecutableStoreDecorator, IWorkflowExecutableStore, IRuntimeBoundedSourceLeaseStore
{
    // Bounded source reads always reach the selected physical provider; process-local body caches
    // cannot establish a current physical incarnation or pre-hydration byte limits.
    ValueTask<RuntimeMaterialSourceSnapshot?> IWorkflowExecutableStore.FindMaterialBoundedAsync(string artifactId,
        PersistenceScope expectedScope, RuntimeSourceReadLimits limits, CancellationToken cancellationToken) =>
        Inner.FindMaterialBoundedAsync(artifactId, expectedScope, limits, cancellationToken);
    ValueTask<WorkflowExecutableRootWriteLease?> IRuntimeBoundedSourceLeaseStore.TryAcquireAsync(string artifactId, string leaseId,
        DateTimeOffset expiresAt, DateTimeOffset now, PersistenceScope expectedScope, RuntimeSourceReadLimits limits, CancellationToken cancellationToken) =>
        SourceLeases().TryAcquireAsync(artifactId, leaseId, expiresAt, now, expectedScope, limits, cancellationToken);
    ValueTask<bool> IRuntimeBoundedSourceLeaseStore.RenewAsync(WorkflowExecutableRootWriteLease lease, DateTimeOffset expiresAt,
        DateTimeOffset now, PersistenceScope expectedScope, RuntimeSourceReadLimits limits, CancellationToken cancellationToken) =>
        SourceLeases().RenewAsync(lease, expiresAt, now, expectedScope, limits, cancellationToken);
    ValueTask IRuntimeBoundedSourceLeaseStore.ReleaseAsync(WorkflowExecutableRootWriteLease lease, PersistenceScope expectedScope,
        RuntimeSourceReadLimits limits, CancellationToken cancellationToken) =>
        SourceLeases().ReleaseAsync(lease, expectedScope, limits, cancellationToken);
    private IRuntimeBoundedSourceLeaseStore SourceLeases() => Inner as IRuntimeBoundedSourceLeaseStore
        ?? throw new RuntimeSourceReadException("runtime_source_retention_profile_unsupported", true);

    IWorkflowExecutableStore IWorkflowExecutableStore.GetMaterialStore() => Inner.GetMaterialStore();
    bool IWorkflowExecutableStore.SupportsModelMaterial => Inner.SupportsModelMaterial;
    ValueTask<RuntimeExecutionMaterial?> IWorkflowExecutableStore.FindMaterialAsync(string artifactId, CancellationToken cancellationToken) =>
        Inner.FindMaterialAsync(artifactId, cancellationToken);
    ValueTask<RuntimeStorePage<RuntimeExecutionMaterial>> IWorkflowExecutableStore.ListMaterialPageAsync(RuntimeStorePageRequest request, CancellationToken cancellationToken) =>
        Inner.ListMaterialPageAsync(request, cancellationToken);
    async ValueTask IWorkflowExecutableStore.SaveModelAsync(RuntimeModelMaterial material, CancellationToken cancellationToken)
    {
        await Inner.SaveModelAsync(material, cancellationToken);
        Invalidate(material.Identity.ArtifactId, WorkflowExecutableCacheTelemetry.SaveReason);
    }
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
