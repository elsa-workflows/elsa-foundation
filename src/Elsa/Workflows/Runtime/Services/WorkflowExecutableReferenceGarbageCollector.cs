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
    private readonly IExecutableActivityTemplateStore _activityTemplateStore;
    private readonly IWorkflowExecutionStateStore _workflowExecutionStateStore;
    private readonly IWorkflowDispatchRetentionRootStore? _workflowDispatchRootStore;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _artifactCreationGracePeriod;
    private readonly TimeSpan _deletionGuardTimeout;
    private readonly ILogger<WorkflowExecutableReferenceGarbageCollector> _logger;

    public WorkflowExecutableReferenceGarbageCollector(
        IWorkflowExecutableStore executableStore,
        IWorkflowExecutableSourceReferenceStore sourceReferenceStore,
        TimeProvider timeProvider,
        ILogger<WorkflowExecutableReferenceGarbageCollector> logger)
        : this(executableStore, sourceReferenceStore, new InMemoryExecutableActivityTemplateStore(), new InMemoryWorkflowExecutionStateStore(), timeProvider, logger)
    {
    }

    public WorkflowExecutableReferenceGarbageCollector(
        IWorkflowExecutableStore executableStore,
        IWorkflowExecutableSourceReferenceStore sourceReferenceStore,
        IExecutableActivityTemplateStore activityTemplateStore,
        TimeProvider timeProvider,
        ILogger<WorkflowExecutableReferenceGarbageCollector> logger)
        : this(executableStore, sourceReferenceStore, activityTemplateStore, new InMemoryWorkflowExecutionStateStore(), timeProvider, logger)
    {
    }

    public WorkflowExecutableReferenceGarbageCollector(
        IWorkflowExecutableStore executableStore,
        IWorkflowExecutableSourceReferenceStore sourceReferenceStore,
        IWorkflowExecutionStateStore workflowExecutionStateStore,
        TimeProvider timeProvider,
        ILogger<WorkflowExecutableReferenceGarbageCollector> logger)
        : this(
            executableStore,
            sourceReferenceStore,
            new InMemoryExecutableActivityTemplateStore(),
            workflowExecutionStateStore,
            null,
            Microsoft.Extensions.Options.Options.Create(new WorkflowExecutableGarbageCollectionOptions()),
            timeProvider,
            logger)
    {
    }

    public WorkflowExecutableReferenceGarbageCollector(
        IWorkflowExecutableStore executableStore,
        IWorkflowExecutableSourceReferenceStore sourceReferenceStore,
        IExecutableActivityTemplateStore activityTemplateStore,
        IWorkflowExecutionStateStore workflowExecutionStateStore,
        TimeProvider timeProvider,
        ILogger<WorkflowExecutableReferenceGarbageCollector> logger)
        : this(
            executableStore,
            sourceReferenceStore,
            activityTemplateStore,
            workflowExecutionStateStore,
            null,
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
        : this(
            executableStore,
            sourceReferenceStore,
            new InMemoryExecutableActivityTemplateStore(),
            workflowExecutionStateStore,
            null,
            options,
            timeProvider,
            logger)
    {
    }

    public WorkflowExecutableReferenceGarbageCollector(
        IWorkflowExecutableStore executableStore,
        IWorkflowExecutableSourceReferenceStore sourceReferenceStore,
        IExecutableActivityTemplateStore activityTemplateStore,
        IWorkflowExecutionStateStore workflowExecutionStateStore,
        IOptions<WorkflowExecutableGarbageCollectionOptions> options,
        TimeProvider timeProvider,
        ILogger<WorkflowExecutableReferenceGarbageCollector> logger)
        : this(
            executableStore,
            sourceReferenceStore,
            activityTemplateStore,
            workflowExecutionStateStore,
            null,
            options,
            timeProvider,
            logger)
    {
    }

    public WorkflowExecutableReferenceGarbageCollector(
        IWorkflowExecutableStore executableStore,
        IWorkflowExecutableSourceReferenceStore sourceReferenceStore,
        IWorkflowExecutionStateStore workflowExecutionStateStore,
        IWorkflowDispatchRetentionRootStore? workflowDispatchRootStore,
        IOptions<WorkflowExecutableGarbageCollectionOptions> options,
        TimeProvider timeProvider,
        ILogger<WorkflowExecutableReferenceGarbageCollector> logger)
        : this(
            executableStore,
            sourceReferenceStore,
            new InMemoryExecutableActivityTemplateStore(),
            workflowExecutionStateStore,
            workflowDispatchRootStore,
            options,
            timeProvider,
            logger)
    {
    }

    public WorkflowExecutableReferenceGarbageCollector(
        IWorkflowExecutableStore executableStore,
        IWorkflowExecutableSourceReferenceStore sourceReferenceStore,
        IExecutableActivityTemplateStore activityTemplateStore,
        IWorkflowExecutionStateStore workflowExecutionStateStore,
        IWorkflowDispatchRetentionRootStore? workflowDispatchRootStore,
        IOptions<WorkflowExecutableGarbageCollectionOptions> options,
        TimeProvider timeProvider,
        ILogger<WorkflowExecutableReferenceGarbageCollector> logger)
    {
        ArgumentNullException.ThrowIfNull(executableStore);
        ArgumentNullException.ThrowIfNull(sourceReferenceStore);
        ArgumentNullException.ThrowIfNull(activityTemplateStore);
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
        _activityTemplateStore = activityTemplateStore;
        _workflowExecutionStateStore = workflowExecutionStateStore;
        _workflowDispatchRootStore = workflowDispatchRootStore;
        _timeProvider = timeProvider;
        _artifactCreationGracePeriod = artifactCreationGracePeriod;
        _deletionGuardTimeout = deletionGuardTimeout;
        _logger = logger;
    }

    public ValueTask<WorkflowExecutableReferenceSweepResult> SweepAsync(CancellationToken cancellationToken = default) =>
        SweepAsync(_timeProvider.GetUtcNow(), cancellationToken);

    public async ValueTask<WorkflowExecutableReferenceSweepResult> SweepAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var executables = (await _executableStore.ListAsync(cancellationToken)).ToArray();
        var templates = (await _activityTemplateStore.ListAsync(cancellationToken)).ToArray();
        HashSet<string> protectedArtifactIds;
        HashSet<string> executionProtectedArtifactIds;
        try
        {
            protectedArtifactIds = await LoadProtectedClosureAsync(executables, templates, now, cancellationToken);
            var runtimeRootIds = await ListRuntimeRootIdsAsync(cancellationToken);
            executionProtectedArtifactIds = ResolveProtectedClosure(executables, runtimeRootIds);
        }
        catch (Exception exception)
        {
            if (exception is OperationCanceledException && cancellationToken.IsCancellationRequested)
                throw;

            _logger.LogWarning(exception, "Reference GC retained all executable material because protected dependency reachability could not be established");
            return new WorkflowExecutableReferenceSweepResult(0, 0, 0);
        }

        var retainedTemplateHashes = executables
            .Where(executable => executionProtectedArtifactIds.Contains(executable.Identity.ArtifactId))
            .Select(executable => executable.CompatibilityMetadata.GetValueOrDefault("activity.templateHash"))
            .Where(hash => !string.IsNullOrWhiteSpace(hash))
            .ToHashSet(StringComparer.Ordinal);
        var retainedTemplateIds = templates
            .Where(template => retainedTemplateHashes.Contains(template.TemplateHash))
            .Select(template => template.TemplateId)
            .ToHashSet(StringComparer.Ordinal);
        var retainedMaterialIds = executionProtectedArtifactIds
            .Concat(retainedTemplateIds)
            .ToHashSet(StringComparer.Ordinal);

        // Query 1 — drop expired/retired references unless a retained execution still pins their inspectable
        // executable/template graph. Terminal executions remain roots until their execution state is removed.
        var deletedReferenceIds = new List<string>();
        var doomedReferences = (await _sourceReferenceStore.ListAsync(cancellationToken: cancellationToken))
            .Where(reference => reference.DeletedAt is not null || reference.IsExpired(now))
            .Where(reference => !retainedMaterialIds.Contains(reference.ArtifactId))
            .ToArray();
        foreach (var reference in doomedReferences)
        {
            if (await _sourceReferenceStore.DeleteAsync(reference.SourceReferenceId, cancellationToken))
                deletedReferenceIds.Add(reference.SourceReferenceId);
        }

        // Query 2 — form candidates outside the staging grace that no live root's complete dependency closure
        // protects. The grace closes the expected gap between saving an immutable artifact and committing its first
        // durable source root. Any graph/query uncertainty retains every artifact for this sweep.
        var creationCutoff = now - _artifactCreationGracePeriod;
        var artifactIds = executables
            .Where(executable => executable.CreatedAt <= creationCutoff)
            .Select(executable => executable.Identity.ArtifactId)
            .Where(artifactId => !protectedArtifactIds.Contains(artifactId))
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

                var currentExecutables = await _executableStore.ListAsync(cancellationToken);
                var currentTemplates = await _activityTemplateStore.ListAsync(cancellationToken);
                var currentProtectedArtifactIds = await LoadProtectedClosureAsync(currentExecutables, currentTemplates, now, cancellationToken);
                if (currentProtectedArtifactIds.Contains(artifactId))
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

        var templateIds = templates
            .Where(template => template.CreatedAt <= creationCutoff)
            .Select(template => template.TemplateId)
            .Where(templateId => !retainedTemplateIds.Contains(templateId))
            .ToArray();
        var unreferencedTemplateIds = await _sourceReferenceStore.ListUnreferencedArtifactIdsAsync(templateIds, now, cancellationToken);
        var deletedActivityTemplateCount = 0;
        foreach (var templateId in unreferencedTemplateIds)
        {
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

    private async ValueTask<HashSet<string>> LoadProtectedClosureAsync(
        IReadOnlyCollection<WorkflowExecutable> executables,
        IReadOnlyCollection<ExecutableActivityTemplate> templates,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var sourceRoots = await _sourceReferenceStore.ListAsync(liveOnly: true, now: now, cancellationToken: cancellationToken);
        var runtimeRootIds = await ListRuntimeRootIdsAsync(cancellationToken);
        var executableIds = executables.Select(executable => executable.Identity.ArtifactId).ToHashSet(StringComparer.Ordinal);
        var templateIds = templates.Select(template => template.TemplateId).ToHashSet(StringComparer.Ordinal);
        var sourceRootIds = sourceRoots
            .Select(reference => reference.ArtifactId)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        foreach (var sourceRootId in sourceRootIds)
        {
            if (!executableIds.Contains(sourceRootId) && !templateIds.Contains(sourceRootId))
            {
                throw new WorkflowExecutableDependencyGraphException(
                    WorkflowExecutableDependencyGraphFailureKind.MissingArtifact,
                    $"Retention root '{sourceRootId}' points to missing executable material.");
            }
        }

        var rootIds = sourceRootIds
            .Where(executableIds.Contains)
            .Concat(runtimeRootIds)
            .ToArray();
        return ResolveProtectedClosure(executables, rootIds);
    }

    private async ValueTask<IReadOnlyCollection<string>> ListRuntimeRootIdsAsync(CancellationToken cancellationToken)
    {
        var roots = (await _workflowExecutionStateStore.ListPinnedExecutableArtifactIdsAsync(cancellationToken)).ToList();
        if (_workflowDispatchRootStore is not null)
            roots.AddRange(await _workflowDispatchRootStore.ListPinnedExecutableArtifactIdsAsync(cancellationToken));
        return roots;
    }

    private static HashSet<string> ResolveProtectedClosure(
        IReadOnlyCollection<WorkflowExecutable> executables,
        IEnumerable<string> rootArtifactIds)
    {
        var identitiesById = executables.ToDictionary(executable => executable.Identity.ArtifactId, StringComparer.Ordinal);
        var rootIds = rootArtifactIds
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (rootIds.Length == 0)
            return new HashSet<string>(StringComparer.Ordinal);

        var roots = new List<WorkflowExecutableIdentity>(rootIds.Length);
        foreach (var rootId in rootIds)
        {
            if (!identitiesById.TryGetValue(rootId, out var executable))
            {
                throw new WorkflowExecutableDependencyGraphException(
                    WorkflowExecutableDependencyGraphFailureKind.MissingArtifact,
                    $"Retention root '{rootId}' points to a missing executable artifact.");
            }

            roots.Add(executable.Identity);
        }

        return WorkflowExecutableDependencyGraph.ResolveClosure(roots, executables)
            .Select(executable => executable.Identity.ArtifactId)
            .ToHashSet(StringComparer.Ordinal);
    }
}
