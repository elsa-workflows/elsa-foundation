using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Runtime-owned activity execution context extensions used by activities that schedule child executable nodes.
/// </summary>
public interface IRuntimeActivityExecutionContext : IActivityExecutionContext
{
    string WorkflowExecutionId { get; }
    WorkflowExecutableIdentity PinnedExecutable { get; }
    RuntimeSchedulerWorkItem SchedulerWorkItem { get; }
    ExecutableNode ExecutableNode { get; }
    ActivityExecutionState ActivityExecutionState { get; }

    /// <summary>
    /// The executable node id of the matched trigger binding that started this run (spec 089 D / 117 D4),
    /// populated from the committed workflow-started seed (spoof-proof, not user input). Null for direct
    /// (non-stimulus) starts. A structural trigger activity (e.g. <c>BpmnProcess</c>) compares it to
    /// <c>ExecutableNodeId</c> to tell whether it was the node that started this run.
    /// </summary>
    string? TriggerNodeId { get; }

    /// <summary>
    /// The matched trigger binding's free-form metadata map (spec 117 D4), carried verbatim from
    /// <c>WorkflowTriggerBinding.Metadata</c> and seeded on its own reserved durable channel. Null for direct
    /// starts and for stimulus starts whose binding carried no metadata. A structural trigger activity reads
    /// per-descriptor routing facets from it (e.g. the BPMN start element id under <c>"bpmn.startElementId"</c>).
    /// </summary>
    IReadOnlyDictionary<string, string>? TriggerMetadata { get; }

    void ScheduleChildActivity(
        string executableNodeId,
        string? schedulingActivityExecutionId = null,
        IReadOnlyDictionary<string, string>? metadata = null,
        ActivitySchedulingProvenance? schedulingProvenance = null,
        LoopIterationScopeRequest? iterationFrame = null);

    IReadOnlyCollection<RuntimeChildActivityScheduleRequest> GetChildActivityScheduleRequests();

    /// <summary>
    /// The parent's direct, non-terminal child activity executions (spec 119 D4), populated by the runtime
    /// only during a child-completion/child-fault evaluation and only when the parent activity implements
    /// <c>IRuntimeLiveChildActivityConsumer</c>; empty otherwise. Read-only and spoof-proof (projected from
    /// committed activity-execution state). A structural activity that races sibling children (the BPMN
    /// event-based gateway) resolves a losing sibling's activity-execution id from it — keyed by executable
    /// node id — to stage the sibling's subtree cancellation via <see cref="RequestChildSubtreeCancellation"/>.
    /// </summary>
    IReadOnlyCollection<RuntimeLiveChildActivity> GetLiveChildActivities();

    /// <summary>
    /// Reads the committed value of a container-scoped variable visible to this activity (spec 123 D1),
    /// populated by the runtime only for an activity implementing <c>IRuntimeScopedVariableReader</c> and only
    /// during an invoke, child-completion/child-fault, or bookmark-resume evaluation.
    /// </summary>
    /// <remarks>
    /// Resolution is by variable <paramref name="variableName"/> across this activity's own visible lexical
    /// frame chain only — own iteration frame → own container frame → ancestors' container/iteration frames →
    /// the workflow root frame — with the innermost scope winning for a shadowed name (the
    /// <c>VariableScope.TryGetValueByName</c> precedent). It is read-only and spoof-proof: the chain is
    /// projected from this activity's own committed <c>ActivityExecutionState</c> ancestry, so out-of-chain
    /// scopes are unreachable by construction. It exposes <b>committed</b> values only — a value staged by an
    /// intrinsic in this or a concurrent evaluation becomes visible only once committed, on a later
    /// evaluation's basis. Returns <see langword="false"/> with <paramref name="envelope"/> <see langword="null"/>
    /// when the name resolves to no visible declared variable, and <b>always</b> when the seam was not populated
    /// (a non-marker activity, or a handler path that did not populate it) — it never throws on an unpopulated
    /// seam, parallel to <see cref="GetLiveChildActivities"/> returning empty.
    /// </remarks>
    bool TryReadScopedVariableValue(string variableName, out ValueEnvelope? envelope);

    /// <summary>
    /// Stages cancellation of one scheduled child's activity-execution subtree (spec 112). Only valid
    /// during a child-completion/child-fault evaluation with a <c>Defer</c> or <c>Complete</c>
    /// continuation; applied atomically in the same checkpoint commit as the continuation.
    /// </summary>
    void RequestChildSubtreeCancellation(
        string childActivityExecutionId,
        string reason,
        IReadOnlyDictionary<string, string>? metadata = null);

    IReadOnlyCollection<RuntimeChildSubtreeCancellationRequest> GetChildSubtreeCancellationRequests();

    /// <summary>
    /// Stages absorption of the child fault this evaluation is processing (spec 115). Only valid
    /// during a child-fault evaluation with a <c>Defer</c> or <c>Complete</c> continuation;
    /// <paramref name="incidentId"/> must name the evaluation's incident. Applied atomically in the
    /// same checkpoint commit as the continuation.
    /// </summary>
    void RequestChildFaultAbsorption(
        string incidentId,
        string reason,
        IReadOnlyDictionary<string, string>? metadata = null);

    IReadOnlyCollection<RuntimeChildFaultAbsorptionRequest> GetChildFaultAbsorptionRequests();
}

/// <summary>
/// Narrow activity-facing capability for staging one cross-execution workflow dispatch.
/// This is a specialized orchestration command, not a workflow-value or service-location channel.
/// </summary>
public interface IWorkflowDispatchStager
{
    void StageWorkflowDispatch(WorkflowDispatchCheckpointRequest request);
}

/// <summary>
/// Engine-facing side of the workflow-dispatch staging seam.
/// Requests are isolated by runtime invocation identity because transient activities can be activated in child scopes.
/// </summary>
public interface IWorkflowDispatchStagingAccessor
{
    void Reset(string workflowExecutionId, string activityExecutionId);
    WorkflowDispatchCheckpointRequest? TakeWorkflowDispatch(string workflowExecutionId, string activityExecutionId);
}
