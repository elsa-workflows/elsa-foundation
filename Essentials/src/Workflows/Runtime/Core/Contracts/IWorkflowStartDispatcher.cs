using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Dispatches workflow execution starts through the workflow execution agent boundary.
/// </summary>
public interface IWorkflowStartDispatcher
{
    /// <summary>
    /// Dispatches a start of the stored artifact. Ordinary new starts are gated on Source References and refused
    /// with a <see cref="Elsa.Workflows.Runtime.Core.Exceptions.WorkflowExecutableReferenceRejectedException"/> when the
    /// artifact has no single authoritative live reference of <paramref name="requiredScope"/> (published dispatch
    /// requires a live Published reference; a test-run dispatch requires a live TestRun reference and its ExpiresAt is
    /// enforced). Public callers set <see cref="WorkflowExecutionStartDispatchRequest.ProvenanceRequirement"/> to
    /// <see cref="WorkflowExecutableProvenanceRequirement.RequireLiveReference"/> and fail closed. The request's
    /// explicit legacy-compatibility default exists only for internal seeded/direct artifacts that predate source
    /// references; those executions pin immutable content identity and null source provenance.
    /// Internal retained-dependency starts use their exact committed edge. Internal stimulus-delivery starts instead
    /// require an exact physically verified cohort and its retention-acceptance witness before even duplicate lookup;
    /// their captured publication may be retired. Ordinary policy, root scope, keys and shedding still apply.
    /// Saved duplicates must match the complete cohort pin and root scope, preserving their actual winning source.
    /// </summary>
    /// <param name="dispatchOptions">
    /// Optional per-request dispatch options forwarded verbatim to the workflow execution agent.
    /// It carries the ambient request scope the in-process inline drain uses to build activity execution contexts.
    /// <c>null</c> ⇒ <see cref="WorkflowExecutionCommandDispatchOptions.Default"/> (identical to the pre-089 single-arg behavior).
    /// </param>
    /// <exception cref="Elsa.Workflows.Runtime.Core.Exceptions.WorkflowExecutableStartRejectedException">Start policy or saved authority/entry/context/wire fidelity refuses the request before enqueue.</exception>
    /// <exception cref="Elsa.Workflows.Runtime.Core.Exceptions.StimulusDeliveryBoundaryException">The saved source provider cannot validate its physical source, cohort or publisher. Its source ID and cause are preserved; unavailable dependencies are distinguished.</exception>
    /// <exception cref="Elsa.Workflows.Runtime.Core.Exceptions.RuntimePostCommitOutboxStaleClaimException">The saved source's current owner/fence/deadline no longer admits this delivery.</exception>
    /// <exception cref="Elsa.Workflows.Runtime.Core.Exceptions.WorkflowExecutableSourceReadException">Ordinary live-reference selection cannot read authoritative references; artifact/scope and cause are preserved.</exception>
    /// <exception cref="Elsa.Workflows.Runtime.Core.Exceptions.WorkflowExecutableNotFoundException">A new start's pinned executable material is unavailable.</exception>
    /// <exception cref="OperationCanceledException">Requested cancellation propagates.</exception>
    ValueTask<WorkflowExecutionStartDispatchResult> DispatchAsync(
        WorkflowExecutionStartDispatchRequest request,
        WorkflowExecutableReferenceScope requiredScope = WorkflowExecutableReferenceScope.Published,
        WorkflowExecutionCommandDispatchOptions? dispatchOptions = null,
        CancellationToken cancellationToken = default);
}
