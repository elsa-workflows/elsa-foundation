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
    private readonly IExecutableActivityTemplateStore _activityTemplateStore;
    private readonly IWorkflowExecutionStateStore? _workflowExecutionStateStore;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<WorkflowExecutableReferenceGarbageCollector> _logger;

    public WorkflowExecutableReferenceGarbageCollector(
        IWorkflowExecutableStore executableStore,
        IWorkflowExecutableSourceReferenceStore sourceReferenceStore,
        TimeProvider timeProvider,
        ILogger<WorkflowExecutableReferenceGarbageCollector> logger)
        : this(executableStore, sourceReferenceStore, new InMemoryExecutableActivityTemplateStore(), timeProvider, logger)
    {
    }

    public WorkflowExecutableReferenceGarbageCollector(
        IWorkflowExecutableStore executableStore,
        IWorkflowExecutableSourceReferenceStore sourceReferenceStore,
        IExecutableActivityTemplateStore activityTemplateStore,
        TimeProvider timeProvider,
        ILogger<WorkflowExecutableReferenceGarbageCollector> logger)
    {
        ArgumentNullException.ThrowIfNull(executableStore);
        ArgumentNullException.ThrowIfNull(sourceReferenceStore);
        ArgumentNullException.ThrowIfNull(activityTemplateStore);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _executableStore = executableStore;
        _sourceReferenceStore = sourceReferenceStore;
        _activityTemplateStore = activityTemplateStore;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public WorkflowExecutableReferenceGarbageCollector(
        IWorkflowExecutableStore executableStore,
        IWorkflowExecutableSourceReferenceStore sourceReferenceStore,
        IExecutableActivityTemplateStore activityTemplateStore,
        IWorkflowExecutionStateStore workflowExecutionStateStore,
        TimeProvider timeProvider,
        ILogger<WorkflowExecutableReferenceGarbageCollector> logger)
        : this(executableStore, sourceReferenceStore, activityTemplateStore, timeProvider, logger)
    {
        ArgumentNullException.ThrowIfNull(workflowExecutionStateStore);
        _workflowExecutionStateStore = workflowExecutionStateStore;
    }

    public ValueTask<WorkflowExecutableReferenceSweepResult> SweepAsync(CancellationToken cancellationToken = default) =>
        SweepAsync(_timeProvider.GetUtcNow(), cancellationToken);

    public async ValueTask<WorkflowExecutableReferenceSweepResult> SweepAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var executables = (await _executableStore.ListAsync(cancellationToken)).ToArray();
        var templates = (await _activityTemplateStore.ListAsync(cancellationToken)).ToArray();
        var protectedArtifactIds = await GetLiveExecutionArtifactIdsAsync(executables, templates, cancellationToken);

        // Drop expired/retired references only after execution liveness has released the pinned material. A draft
        // test run may remain suspended beyond its nominal reference expiry; deleting its wrapper or activity
        // template at that point would make a later bookmark resume unrecoverable after restart.
        var deletedReferenceIds = new List<string>();
        var references = await _sourceReferenceStore.ListAsync(cancellationToken: cancellationToken);
        foreach (var reference in references.Where(x => x.DeletedAt is not null || x.IsExpired(now)))
        {
            if (protectedArtifactIds.Contains(reference.ArtifactId))
                continue;

            if (await _sourceReferenceStore.DeleteAsync(reference.SourceReferenceId, cancellationToken))
                deletedReferenceIds.Add(reference.SourceReferenceId);
        }

        // Query 2 — among every stored artifact, delete those no live reference points at any more. Listing all
        // artifacts (rather than only those touched above) also reaps artifacts orphaned by any earlier partial
        // sweep, keeping the store self-healing.
        var artifactIds = executables.Select(executable => executable.Identity.ArtifactId);
        var unreferencedArtifactIds = await _sourceReferenceStore.ListUnreferencedArtifactIdsAsync(artifactIds, now, cancellationToken);

        var deletedArtifactCount = 0;
        foreach (var artifactId in unreferencedArtifactIds)
        {
            if (protectedArtifactIds.Contains(artifactId))
                continue;
            if (await _executableStore.DeleteAsync(artifactId, cancellationToken))
                deletedArtifactCount++;
        }

        // Reusable activity templates use the same Source Reference lifetime contract. Published activity
        // versions and draft test runs point directly at their template id, so template GC is reference-derived
        // and never consults mutable Design state.
        var templateIds = templates.Select(template => template.TemplateId);
        var unreferencedTemplateIds = await _sourceReferenceStore.ListUnreferencedArtifactIdsAsync(templateIds, now, cancellationToken);
        var deletedActivityTemplateCount = 0;
        foreach (var templateId in unreferencedTemplateIds)
        {
            if (protectedArtifactIds.Contains(templateId))
                continue;
            if (await _activityTemplateStore.DeleteAsync(templateId, cancellationToken))
                deletedActivityTemplateCount++;
        }

        if ((deletedReferenceIds.Count > 0 || deletedArtifactCount > 0 || deletedActivityTemplateCount > 0) && _logger.IsEnabled(LogLevel.Debug))
            _logger.LogDebug(
                "Reference GC sweep removed {ReferenceCount} expired/retired reference(s), {ArtifactCount} unreferenced workflow artifact(s), and {ActivityTemplateCount} unreferenced activity template(s)",
                deletedReferenceIds.Count,
                deletedArtifactCount,
                deletedActivityTemplateCount);

        return new WorkflowExecutableReferenceSweepResult(deletedReferenceIds.Count, deletedArtifactCount, deletedActivityTemplateCount);
    }

    private async ValueTask<HashSet<string>> GetLiveExecutionArtifactIdsAsync(
        IReadOnlyCollection<WorkflowExecutable> executables,
        IReadOnlyCollection<ExecutableActivityTemplate> templates,
        CancellationToken cancellationToken)
    {
        var protectedIds = new HashSet<string>(StringComparer.Ordinal);
        if (_workflowExecutionStateStore is null)
            return protectedIds;

        var liveArtifactIds = (await _workflowExecutionStateStore.ListAsync(cancellationToken))
            .Where(x => !x.Status.IsTerminal())
            .Select(x => x.PinnedExecutable.ArtifactId)
            .ToHashSet(StringComparer.Ordinal);
        protectedIds.UnionWith(liveArtifactIds);

        var liveTemplateHashes = executables
            .Where(x => liveArtifactIds.Contains(x.Identity.ArtifactId))
            .Select(x => x.CompatibilityMetadata.GetValueOrDefault("activity.templateHash"))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToHashSet(StringComparer.Ordinal);
        protectedIds.UnionWith(templates
            .Where(x => liveTemplateHashes.Contains(x.TemplateHash))
            .Select(x => x.TemplateId));
        return protectedIds;
    }
}
