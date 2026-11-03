using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Reads one exact Run (its Workflow Instance) and, where one is selected, one exact activity execution of it, for
/// consumers outside the Workflows Runtime API (such as Weaver) that may couple only to <c>.Core</c> contracts. It reads
/// what the run endpoints read: the instance, its incidents, a bounded page of its activity executions and the selected
/// activity execution's value evidence. A Run is never changed: this contract offers reads only, and no retry, cancel,
/// delete or dispatch.
/// </summary>
/// <remarks>
/// This is a single-implementation replacement contract in the current application context: exactly one implementation is
/// installed, and a second registration fails startup. Consumers must fail closed when no implementation or several
/// implementations are installed. The read applies the run endpoints' own per-run authority (structure inspection, value
/// inspection and payload resolution, each decided by the Runtime API for the current request's principal); the consumer
/// decides the endpoint permission (<c>workflows/runtime:read</c>) before it calls. A withheld value stays withheld and is
/// never inferred; a value or a part of the Run that is not available is reported with its
/// <see cref="WorkflowRunUnavailableReason"/>. An in-progress Run is reported as it was at
/// <see cref="WorkflowRunInspectionDetails.ReadAt"/>. An infrastructure failure propagates.
/// <para>
/// An incident message is disclosed only when its activity, and the one causal child it propagated from (the fault
/// recorder's causal activity execution, <c>IActivityFaultCausation</c>, a structural parent's completed child, or the
/// child whose parent notification the parent was handling), are established non-sensitive. A consumer's fault while it
/// evaluates a child's control batches names no child, so its message is always withheld. This assumes that a propagated
/// fault message names its child but never carries the child's own exception text: only one causal level is checked. A
/// runtime that copies a child's text into its parent's incident breaks that assumption and must not be inspected through
/// this contract.
/// </para>
/// </remarks>
public interface IWorkflowRunInspection
{
    /// <summary>
    /// The exact Run, or <see langword="null"/> when no Run has this Workflow Instance identity or the caller may not inspect
    /// it; the two are not distinguished, as the run endpoints answer both with 404.
    /// </summary>
    ValueTask<WorkflowRunInspectionDetails?> GetAsync(WorkflowRunInspectionRequest request, CancellationToken cancellationToken = default);
}
