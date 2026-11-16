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
/// An incident message is disclosed only when its activity, and the causal child it propagated from (the fault recorder's
/// causal activity execution, <c>IActivityFaultCausation</c>, a structural parent's completed child, or the child whose
/// parent notification the parent was handling), are established non-sensitive, and so on through that child's own
/// incidents in this Run, because a propagated message can carry a descendant's text (a nested BPMN process copies its
/// child's fault message); a chain deeper than a bounded depth (eight in the default implementation) counts as sensitive. An incident a child-control consumer raised,
/// or one recorded for poisoned scheduler work, is always withheld: its message may echo another Run's batches or an
/// arbitrary handler's exception text. A child-control consumer's returned fault or failed disposal recorded before the
/// runtime named the calling child carries neither marker and is not recognized; such Runs exist only in stores written by
/// builds before #3736 (pre-4.0 previews). Neither is a fault an older BPMN interpreter deferred and raised on a later
/// child's callback, which names that later child; Bpmn.Semantics 0.5.0 no longer defers faults.
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
