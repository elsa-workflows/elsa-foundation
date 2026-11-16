using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>Additive bounded query capability for workflow-dispatch stores.</summary>
public interface IWorkflowDispatchQueryStore
{
    ValueTask<IReadOnlyCollection<WorkflowDispatchRecord>> QueryAsync(
        WorkflowDispatchQuery query,
        CancellationToken cancellationToken = default);
}

/// <summary>Additive snapshot-fenced deletion capability used by guarded dispatch retention.</summary>
public interface IWorkflowDispatchDeleteStore
{
    /// <summary>Deletes a dispatch by identity without snapshot fencing.</summary>
    /// <remarks>
    /// Retained for source compatibility. Retention collectors use <see cref="TryDeleteAsync"/> so a concurrent
    /// redrive cannot be deleted from a stale terminal selection.
    /// </remarks>
    ValueTask DeleteAsync(string dispatchId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException(
            "This dispatch store supports snapshot-fenced retention only. Use TryDeleteAsync.");

    /// <summary>
    /// Deletes the dispatch only when the current persisted record still equals the terminal snapshot selected by
    /// retention. Returns <see langword="true"/> when that snapshot is absent after the call, or
    /// <see langword="false"/> when a different current snapshot won the fence.
    /// </summary>
    ValueTask<bool> TryDeleteAsync(
        WorkflowDispatchRecord expected,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(false);
}

/// <summary>Projects executable artifacts pinned by nonterminal dispatch records for garbage collection.</summary>
public interface IWorkflowDispatchRetentionRootStore
{
    ValueTask<IReadOnlyCollection<string>> ListPinnedExecutableArtifactIdsAsync(
        CancellationToken cancellationToken = default);
}
