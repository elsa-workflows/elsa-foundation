using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Stores split continuation state for runtime-owned operational coordination.
/// </summary>
public interface IExecutionLivenessStateStore
{
    /// <summary>
    /// Inserts or replaces operational state for the operational state key.
    /// </summary>
    ValueTask<ExecutionLivenessState> SaveAsync(ExecutionLivenessState state, CancellationToken cancellationToken = default);

    /// <summary>
    /// Conditionally creates or replaces operational state. Revision <c>0</c> means create-only; a positive revision
    /// means compare-and-swap against the current provider revision.
    /// </summary>
    ValueTask<ExecutionLivenessStateWriteResult> TrySaveAsync(
        ExecutionLivenessState state,
        long expectedRevision,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns operational state for the given workflow execution ID and operational state ID, or <see langword="null"/> if not found.
    /// </summary>
    ValueTask<ExecutionLivenessState?> FindAsync(string workflowExecutionId, string operationalStateId, CancellationToken cancellationToken = default);

    /// <summary>Returns operational state together with its provider-neutral optimistic-concurrency revision.</summary>
    ValueTask<VersionedExecutionLivenessState?> FindVersionedAsync(
        string workflowExecutionId,
        string operationalStateId,
        CancellationToken cancellationToken = default);

    /// <summary>Returns one finite, ordered page for a workflow execution.</summary>
    ValueTask<RuntimeStorePage<ExecutionLivenessState>> ListPageAsync(
        ExecutionLivenessStatePageQuery query,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromException<RuntimeStorePage<ExecutionLivenessState>>(
            new NotSupportedException("This execution-liveness store does not support bounded pages."));

    /// <summary>Returns one finite, ordered page across the current storage scope.</summary>
    ValueTask<RuntimeStorePage<ExecutionLivenessState>> ListAllPageAsync(
        RuntimeStorePageRequest query,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromException<RuntimeStorePage<ExecutionLivenessState>>(
            new NotSupportedException("This execution-liveness store does not support bounded pages."));

    /// <summary>
    /// Legacy complete traversal. New production reads must use <see cref="ListPageAsync"/>; commands that need
    /// every record use <see cref="RuntimeOperationalStorePagingExtensions.ListAllAsync(IExecutionLivenessStateStore,string,CancellationToken)"/> explicitly.
    /// </summary>
    async ValueTask<IReadOnlyCollection<ExecutionLivenessState>> ListAsync(string workflowExecutionId, CancellationToken cancellationToken = default) =>
        await RuntimeOperationalStorePagingExtensions.ListAllAsync(this, workflowExecutionId, cancellationToken);

    /// <summary>Legacy complete traversal; new production reads must use <see cref="ListAllPageAsync"/>.</summary>
    async ValueTask<IReadOnlyCollection<ExecutionLivenessState>> ListAllAsync(CancellationToken cancellationToken = default) =>
        await RuntimeOperationalStorePagingExtensions.ListAllAsync(this, cancellationToken);
}
