using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Dispatches workflow execution starts through the workflow execution agent boundary.
/// </summary>
public interface IWorkflowStartDispatcher
{
    /// <summary>
    /// Dispatches a start of the stored artifact, gated on its Source References (ADR 0040). The dispatch is refused
    /// with a <see cref="Elsa.Workflows.Runtime.Core.Exceptions.WorkflowExecutableReferenceRejectedException"/> when the
    /// artifact carries references but none is a live reference of <paramref name="requiredScope"/> (published dispatch
    /// requires a live Published reference; a test-run dispatch requires a live TestRun reference and its ExpiresAt is
    /// enforced). An artifact with no references at all is dispatched unchanged (direct/seeded runtime path).
    /// </summary>
    ValueTask<WorkflowExecutionStartDispatchResult> DispatchAsync(
        WorkflowExecutionStartDispatchRequest request,
        WorkflowExecutableReferenceScope requiredScope = WorkflowExecutableReferenceScope.Published,
        CancellationToken cancellationToken = default);
}
