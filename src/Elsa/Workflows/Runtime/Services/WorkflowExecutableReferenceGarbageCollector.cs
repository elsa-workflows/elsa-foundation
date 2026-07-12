using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Elsa.Workflows.Runtime.Core.Services;

/// <summary>
/// The ADR 0040 reference garbage collector:
/// <list type="number">
/// <item>hard-delete every <see cref="WorkflowExecutableSourceReference"/> that is retired or past its expiry;</item>
/// <item>select artifacts outside staging grace that no known retention root protects;</item>
/// <item>acquire a provider-backed deletion guard, recheck both root sets, and conditionally delete.</item>
/// </list>
/// The order matters: dropping the doomed references first is what turns a retired/expired reference into a
/// candidate for its artifact's removal. Root-write leases and deletion guards linearize concurrent root creation and
/// deletion across provider instances; overlapping sweeps remain safe and idempotent.
/// </summary>
public sealed class WorkflowExecutableReferenceGarbageCollector : IWorkflowExecutableReferenceGarbageCollector
{
    private readonly IWorkflowExecutableStore _executableStore;
    private readonly IWorkflowExecutableSourceReferenceStore _sourceReferenceStore;
    private readonly IWorkflowExecutionStateStore _workflowExecutionStateStore;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _artifactCreationGracePeriod;
    private readonly TimeSpan _deletionGuardTimeout;
    private readonly ILogger<WorkflowExecutableReferenceGarbageCollector> _logger;

    public WorkflowExecutableReferenceGarbageCollector(
        IWorkflowExecutableStore executableStore,
        IWorkflowExecutableSourceReferenceStore sourceReferenceStore,
        IWorkflowExecutionStateStore workflowExecutionStateStore,
        TimeProvider timeProvider,
        ILogger<WorkflowExecutableReferenceGarbageCollector> logger)
        : this(
            executableStore,
            sourceReferenceStore,
            workflowExecutionStateStore,
            Microsoft.Extensions.Options.Options.Create(new WorkflowExecutableGarbageCollectionOptions()),
            timeProvider,
            logger)
    {
    }

    public WorkflowExecutableReferenceGarbageCollector(
        IWorkflowExecutableStore executableStore,
        IWorkflowExecutableSourceReferenceStore sourceReferenceStore,
        IWorkflowExecutionStateStore workflowExecutionStateStore,
        IOptions<WorkflowExecutableGarbageCollectionOptions> options,
        TimeProvider timeProvider,
        ILogger<WorkflowExecutableReferenceGarbageCollector> logger)
    {
        ArgumentNullException.ThrowIfNull(executableStore);
        ArgumentNullException.ThrowIfNull(sourceReferenceStore);
        ArgumentNullException.ThrowIfNull(workflowExecutionStateStore);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        var artifactCreationGracePeriod = options.Value.ArtifactCreationGracePeriod;
        if (artifactCreationGracePeriod < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options), "The artifact creation grace period cannot be negative.");
        var deletionGuardTimeout = options.Value.DeletionGuardTimeout;
        if (deletionGuardTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options), "The deletion guard timeout must be positive.");

        _executableStore = executableStore;
        _sourceReferenceStore = sourceReferenceStore;
        _workflowExecutionStateStore = workflowExecutionStateStore;
        _timeProvider = timeProvider;
        _artifactCreationGracePeriod = artifactCreationGracePeriod;
        _deletionGuardTimeout = deletionGuardTimeout;
        _logger = logger;
    }

    public ValueTask<WorkflowExecutableReferenceSweepResult> SweepAsync(CancellationToken cancellationToken = default) =>
        SweepAsync(_timeProvider.GetUtcNow(), cancellationToken);

    public async ValueTask<WorkflowExecutableReferenceSweepResult> SweepAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        // Query 1 — drop expired/retired references. Their artifacts become GC candidates once the references are gone.
        var deletedReferenceIds = await _sourceReferenceStore.DeleteExpiredOrRetiredAsync(now, cancellationToken);

        // Query 2 — form candidates outside the staging grace that no retained execution pins. The grace closes
        // the expected gap between saving an immutable artifact and committing its first durable source root.
        var creationCutoff = now - _artifactCreationGracePeriod;
        var executionRoots = (await _workflowExecutionStateStore.ListPinnedExecutableArtifactIdsAsync(cancellationToken))
            .ToHashSet(StringComparer.Ordinal);
        var artifactIds = (await _executableStore.ListAsync(cancellationToken))
            .Where(executable => executable.CreatedAt <= creationCutoff)
            .Select(executable => executable.Identity.ArtifactId)
            .Where(artifactId => !executionRoots.Contains(artifactId))
            .ToArray();
        var unreferencedArtifactIds = await _sourceReferenceStore.ListUnreferencedArtifactIdsAsync(artifactIds, now, cancellationToken);

        var deletedArtifactCount = 0;
        foreach (var artifactId in unreferencedArtifactIds)
        {
            // Candidate selection is only a snapshot. The provider-backed deletion guard closes the otherwise
            // unavoidable gap between these final root queries and physical deletion: a root writer that acquired
            // its lease first makes TryBeginDeletion fail, while a deletion guard that won first blocks new leases.
            var guardNow = _timeProvider.GetUtcNow();
            var deletionGuard = await _executableStore.TryBeginDeletionAsync(
                artifactId,
                Guid.NewGuid().ToString("N"),
                guardNow + _deletionGuardTimeout,
                guardNow,
                cancellationToken);
            if (deletionGuard is null)
                continue;

            try
            {
                var stillUnreferenced = await _sourceReferenceStore.ListUnreferencedArtifactIdsAsync([artifactId], now, cancellationToken);
                if (!stillUnreferenced.Contains(artifactId, StringComparer.Ordinal))
                {
                    await CancelDeletionConservativelyAsync(deletionGuard);
                    continue;
                }

                var currentExecutionRoots = await _workflowExecutionStateStore.ListPinnedExecutableArtifactIdsAsync(cancellationToken);
                if (currentExecutionRoots.Contains(artifactId, StringComparer.Ordinal))
                {
                    await CancelDeletionConservativelyAsync(deletionGuard);
                    continue;
                }
            }
            catch (Exception exception)
            {
                // Root-query uncertainty always resolves in favor of retention. Cancellation uses an independent
                // token so a canceled sweep does not strand the guard until its expiry recovery path runs.
                await CancelDeletionConservativelyAsync(deletionGuard);
                if (exception is OperationCanceledException && cancellationToken.IsCancellationRequested)
                    throw;

                _logger.LogWarning(
                    exception,
                    "Reference GC retained executable {ArtifactId} because its final retention-root query failed",
                    artifactId);
                continue;
            }

            if (await _executableStore.DeleteAsync(deletionGuard, _timeProvider.GetUtcNow(), cancellationToken))
                deletedArtifactCount++;
        }

        if ((deletedReferenceIds.Count > 0 || deletedArtifactCount > 0) && _logger.IsEnabled(LogLevel.Debug))
            _logger.LogDebug(
                "Reference GC sweep removed {ReferenceCount} expired/retired reference(s) and {ArtifactCount} unreferenced artifact(s)",
                deletedReferenceIds.Count,
                deletedArtifactCount);

        return new WorkflowExecutableReferenceSweepResult(deletedReferenceIds.Count, deletedArtifactCount);
    }

    private async ValueTask CancelDeletionConservativelyAsync(WorkflowExecutableDeletionGuard guard)
    {
        try
        {
            await _executableStore.CancelDeletionAsync(guard, CancellationToken.None);
        }
        catch (Exception exception)
        {
            // A provider failure cannot make deletion unsafe: the unmatched guard eventually expires, and until then
            // root writers fail closed and retry instead of racing a physical delete.
            _logger.LogWarning(
                exception,
                "Reference GC could not cancel deletion guard {OperationId} for executable {ArtifactId}; expiry recovery will release it",
                guard.OperationId,
                guard.ArtifactId);
        }
    }
}
