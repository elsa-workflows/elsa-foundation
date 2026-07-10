using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.Logging;

namespace Elsa.Workflows.Runtime.Core.Services;

/// <summary>
/// The ADR 0040 reference garbage collector. A sweep is two store queries, not new distributed machinery:
/// <list type="number">
/// <item>hard-delete every <see cref="WorkflowExecutableSourceReference"/> that is retired or past its expiry;</item>
/// <item>delete every content-addressed artifact that no live reference points at any more.</item>
/// </list>
/// The order matters: dropping the doomed references first is what turns a retired/expired reference into a
/// candidate for its artifact's removal. Deletes are idempotent (a concurrent sweep that already removed a row is a
/// no-op), so overlapping sweeps are safe.
/// </summary>
public sealed class WorkflowExecutableReferenceGarbageCollector : IWorkflowExecutableReferenceGarbageCollector
{
    private readonly IWorkflowExecutableStore _executableStore;
    private readonly IWorkflowExecutableSourceReferenceStore _sourceReferenceStore;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<WorkflowExecutableReferenceGarbageCollector> _logger;

    public WorkflowExecutableReferenceGarbageCollector(
        IWorkflowExecutableStore executableStore,
        IWorkflowExecutableSourceReferenceStore sourceReferenceStore,
        TimeProvider timeProvider,
        ILogger<WorkflowExecutableReferenceGarbageCollector> logger)
    {
        ArgumentNullException.ThrowIfNull(executableStore);
        ArgumentNullException.ThrowIfNull(sourceReferenceStore);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _executableStore = executableStore;
        _sourceReferenceStore = sourceReferenceStore;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public ValueTask<WorkflowExecutableReferenceSweepResult> SweepAsync(CancellationToken cancellationToken = default) =>
        SweepAsync(_timeProvider.GetUtcNow(), cancellationToken);

    public async ValueTask<WorkflowExecutableReferenceSweepResult> SweepAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        // Query 1 — drop expired/retired references. Their artifacts become GC candidates once the references are gone.
        var deletedReferenceIds = await _sourceReferenceStore.DeleteExpiredOrRetiredAsync(now, cancellationToken);

        // Query 2 — among every stored artifact, delete those no live reference points at any more. Listing all
        // artifacts (rather than only those touched above) also reaps artifacts orphaned by any earlier partial
        // sweep, keeping the store self-healing.
        var artifactIds = (await _executableStore.ListAsync(cancellationToken)).Select(executable => executable.Identity.ArtifactId);
        var unreferencedArtifactIds = await _sourceReferenceStore.ListUnreferencedArtifactIdsAsync(artifactIds, now, cancellationToken);

        var deletedArtifactCount = 0;
        foreach (var artifactId in unreferencedArtifactIds)
        {
            if (await _executableStore.DeleteAsync(artifactId, cancellationToken))
                deletedArtifactCount++;
        }

        if ((deletedReferenceIds.Count > 0 || deletedArtifactCount > 0) && _logger.IsEnabled(LogLevel.Debug))
            _logger.LogDebug(
                "Reference GC sweep removed {ReferenceCount} expired/retired reference(s) and {ArtifactCount} unreferenced artifact(s)",
                deletedReferenceIds.Count,
                deletedArtifactCount);

        return new WorkflowExecutableReferenceSweepResult(deletedReferenceIds.Count, deletedArtifactCount);
    }
}
