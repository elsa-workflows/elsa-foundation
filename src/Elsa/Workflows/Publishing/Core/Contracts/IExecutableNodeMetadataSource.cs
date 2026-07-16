using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Publishing.Core.Contracts;

/// <summary>Returns runtime-owned metadata claims for compiled executable nodes.</summary>
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
}
