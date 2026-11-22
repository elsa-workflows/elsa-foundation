using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Publishing.Core.Contracts;

/// <summary>
/// Compatibility source retained while DispatchWorkflow moves to <see cref="IExecutableCompilationSource"/>.
/// New contributors register the generalized compilation source.
/// </summary>
public interface IExecutableNodeMetadataSource
{
    ValueTask<IReadOnlyCollection<ExecutableNodeMetadataContribution>> GetMetadataAsync(
        ExecutableNodeMetadataContext context,
        CancellationToken cancellationToken = default);
}

/// <summary>Applies all event-collected node metadata before executable hashing.</summary>
public interface IExecutableNodeMetadataEnricher
{
    ValueTask<ExecutableNode> EnrichAsync(
        WorkflowExecutableCompileRequest request,
        WorkflowExecutableCompileSource source,
        ExecutableNode rootActivity,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies the generalized compile fan-in and returns both enriched nodes and exact dependency claims.
    /// Existing implementations remain source/binary compatible through this default adapter.
    /// </summary>
    async ValueTask<ExecutableCompilationEnrichment> EnrichCompilationAsync(
        WorkflowExecutableCompileRequest request,
        WorkflowExecutableCompileSource source,
        ExecutableNode rootActivity,
        CancellationToken cancellationToken = default) =>
        new(await EnrichAsync(request, source, rootActivity, cancellationToken));
}
