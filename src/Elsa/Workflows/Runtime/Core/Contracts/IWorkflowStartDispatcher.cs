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
    /// artifact has no single authoritative live reference of <paramref name="requiredScope"/> (published dispatch
    /// requires a live Published reference; a test-run dispatch requires a live TestRun reference and its ExpiresAt is
    /// enforced). Scoped dispatch fails closed when the artifact has no source reference: the artifact's stored
    /// identity is never accepted as a substitute for publication provenance.
    /// </summary>
    /// <param name="dispatchOptions">
    /// Optional per-request dispatch options forwarded verbatim to the workflow execution agent (spec 089 FR-019).
    /// It carries the ambient request scope the in-process inline drain uses to build activity execution contexts.
    /// <c>null</c> ⇒ <see cref="WorkflowExecutionCommandDispatchOptions.Default"/> (identical to the pre-089 single-arg behavior).
    /// </param>
    ValueTask<WorkflowExecutionStartDispatchResult> DispatchAsync(
        WorkflowExecutionStartDispatchRequest request,
        WorkflowExecutableReferenceScope requiredScope = WorkflowExecutableReferenceScope.Published,
        WorkflowExecutionCommandDispatchOptions? dispatchOptions = null,
        CancellationToken cancellationToken = default);
}
