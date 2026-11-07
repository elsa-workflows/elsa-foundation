using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>Canonical external-stimulus sources in one selected physical runtime boundary.</summary>
/// <remarks>
/// Provider replacement, not a contribution collection or an API queue. Source data and sender keys
/// grant no delivery authority. The claimed delivery path must validate the actual persisted source
/// and fence before a target admission. No routing producer is enabled by registering this store alone.
/// </remarks>
public interface IStimulusIngressSourceStore
{
    /// <summary>Read the retained winner in the complete immutable origin/key namespace.</summary>
    ValueTask<StimulusIngressSource?> FindByKeyAsync(StimulusDeliveryScope origin, string clientKey,
        CancellationToken cancellationToken = default);

    /// <summary>Read one exact source under current access and its full retained origin.</summary>
    ValueTask<StimulusIngressSource?> FindByIdAsync(StimulusDeliveryScope origin, string sourceId,
        CancellationToken cancellationToken = default);

    /// <summary>Install the whole first selection, with its physical retention roots, before effects.</summary>
    /// <remarks>
    /// Atomically validate current scope, compatible physical participants and fenced root grants.
    /// Key contention returns the persisted winner when its semantic request agrees; a contradictory
    /// request conflicts. Preserve an installed empty cohort. Never return a losing/uncommitted proposal.
    /// Ambiguous storage failure is not acceptance: callers resolve it through this same key/source,
    /// rather than selecting fresh waits or fabricating a publishing workflow.
    /// </remarks>
    ValueTask<StimulusIngressSource> InstallAsync(StimulusIngressSource candidate,
        CancellationToken cancellationToken = default);

    /// <summary>Read source material roots through the actual selected physical maintenance stores.</summary>
    /// <remarks>Refuse an unsupported/split boundary or unreadable evidence instead of returning an empty set.</remarks>
    ValueTask<IReadOnlyCollection<string>> ListPinnedExecutableArtifactIdsAsync(IWorkflowExecutableStore material,
        IWorkflowExecutableSourceReferenceStore references, IWorkflowExecutionStateStore executions,
        CancellationToken cancellationToken = default);
}
