using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>Additive bounded query capability for workflow-dispatch stores.</summary>
public interface IWorkflowDispatchQueryStore
{
    ValueTask<IReadOnlyCollection<WorkflowDispatchRecord>> QueryAsync(
        WorkflowDispatchQuery query,
        CancellationToken cancellationToken = default);
}

/// <summary>Additive deletion capability used by guarded dispatch retention.</summary>
public interface IWorkflowDispatchDeleteStore
{
    ValueTask DeleteAsync(string dispatchId, CancellationToken cancellationToken = default);
}

/// <summary>Projects executable artifacts pinned by nonterminal dispatch records for garbage collection.</summary>
public interface IWorkflowDispatchRetentionRootStore
{
    ValueTask<IReadOnlyCollection<string>> ListPinnedExecutableArtifactIdsAsync(
        CancellationToken cancellationToken = default);
}
