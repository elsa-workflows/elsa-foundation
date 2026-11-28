using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Inspection.Core;

/// <summary>Streams retained run state from the selected backend for an exact tenant.</summary>
/// <remarks>
/// An unavailable candidate is returned as null so consumers still account for it in a bounded scan.
/// Consumers apply per-run inspection authorization before using any returned state.
/// </remarks>
public interface IWorkflowRetainedRunSource
{
    IAsyncEnumerable<WorkflowExecutionState?> ReadAsync(string tenant, CancellationToken cancellationToken = default);
}
