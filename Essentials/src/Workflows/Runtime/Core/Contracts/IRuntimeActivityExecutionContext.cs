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
    /// The executable node id of the matched trigger binding that started this run,
    /// populated from the committed workflow-started seed (spoof-proof, not user input). Null for direct
    /// (non-stimulus) starts. A structural trigger activity (e.g. <c>BpmnProcess</c>) compares it to
    /// <c>ExecutableNodeId</c> to tell whether it was the node that started this run.
    /// </summary>
    string? TriggerNodeId { get; }

    /// <summary>
    /// The matched trigger binding's free-form metadata map, carried verbatim from
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

    /// <summary>
    /// Stages the same child schedule as the overload above, delivered not before <paramref name="notBefore"/> (R-7,
    /// spec 261). Valid wherever scheduling a child is valid. A <paramref name="notBefore"/> that is null, or at or before
    /// the commit, schedules the child as usual. A later one commits the child's schedule work item in this evaluation's
    /// checkpoint as a post-commit intent whose outbox item becomes available at that time; the checkpoint is then
    /// durable in every persistence mode, and the item never enters the run's scheduler queue early, so it holds back no
    /// other work of the run. A due time more than <c>RuntimeDelayedScheduleOptions.MaxDelay</c> ahead faults the
    /// evaluation with <c>runtime.delayed-schedule.too-far</c>; a host without the outbox sweep faults it with
    /// <c>runtime.delayed-schedule.unavailable</c>. When the schedule is delivered, a run that is gone or terminal, or a
    /// scheduling parent that is no longer running, acknowledges it without scheduling the child.
    /// </summary>
    void ScheduleChildActivity(
        string executableNodeId,
        string? schedulingActivityExecutionId,
        IReadOnlyDictionary<string, string>? metadata,
        ActivitySchedulingProvenance? schedulingProvenance,
        LoopIterationScopeRequest? iterationFrame,
        DateTimeOffset? notBefore);

    IReadOnlyCollection<RuntimeChildActivityScheduleRequest> GetChildActivityScheduleRequests();

    /// <summary>
    /// The parent's direct, non-terminal child activity executions, populated by the runtime
    /// only during a child-completion/child-fault evaluation and only when the parent activity implements
    /// <c>IRuntimeLiveChildActivityConsumer</c>; empty otherwise. Read-only and spoof-proof (projected from
    /// committed activity-execution state). A structural activity that races sibling children (the BPMN
    /// event-based gateway) resolves a losing sibling's activity-execution id from it — keyed by executable
    /// node id — to stage the sibling's subtree cancellation via <see cref="RequestChildSubtreeCancellation"/>.
    /// </summary>
    IReadOnlyCollection<RuntimeLiveChildActivity> GetLiveChildActivities();

    /// <summary>
    /// Reads the committed value of a container-scoped variable visible to this activity,
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
    /// The envelope is returned as committed, including a <see cref="ValuePresence.Withheld"/> one, which carries no
    /// value: a reader that needs the value refuses it with <c>VF-ACT-010</c>
    /// (<c>SecretBindingDiagnostics.WithheldVariableNotResolved</c>) and never reads it as null or absent.
    /// </remarks>
    bool TryReadScopedVariableValue(string variableName, out ValueEnvelope? envelope);

    /// <summary>
    /// Stages cancellation of one scheduled child's activity-execution subtree. Only valid
    /// during a child-completion, child-fault or child-notification evaluation with a <c>Defer</c> or
    /// <c>Complete</c> continuation; applied atomically in the same checkpoint commit as the continuation.
    /// </summary>
    void RequestChildSubtreeCancellation(
        string childActivityExecutionId,
        string reason,
        IReadOnlyDictionary<string, string>? metadata = null);

    IReadOnlyCollection<RuntimeChildSubtreeCancellationRequest> GetChildSubtreeCancellationRequests();

    /// <summary>
    /// Stages absorption of the child fault this evaluation is processing. Only valid
    /// during a child-fault evaluation with a <c>Defer</c> or <c>Complete</c> continuation;
    /// <paramref name="incidentId"/> must name the evaluation's incident. Applied atomically in the
    /// same checkpoint commit as the continuation.
    /// </summary>
    void RequestChildFaultAbsorption(
        string incidentId,
        string reason,
        IReadOnlyDictionary<string, string>? metadata = null);

    IReadOnlyCollection<RuntimeChildFaultAbsorptionRequest> GetChildFaultAbsorptionRequests();

    /// <summary>
    /// Stages a request to keep the child fault this evaluation is processing blocking until an operator intervenes,
    /// for example through the <c>RetryFaultedActivity</c> alteration. Only valid during a child-fault evaluation with a
    /// <c>Defer</c> continuation, at most once, and never together with
    /// <see cref="RequestChildFaultAbsorption"/>; <paramref name="incidentId"/> must name the evaluation's incident. In
    /// the same checkpoint commit as the continuation the incident stays blocking and gets a
    /// <c>WaitForIntervention</c> outcome from the <c>StructuralIntervention</c> source carrying
    /// <paramref name="metadata"/>, so no incident strategy acts on it and the run stays running. The faulted child's
    /// subtree is not cancelled. A violation faults the evaluation.
    /// </summary>
    void RequestChildFaultIntervention(
        string incidentId,
        string reason,
        IReadOnlyDictionary<string, string>? metadata = null);

    /// <summary>
    /// Stages the same request as <see cref="RequestChildFaultIntervention(string, string, IReadOnlyDictionary{string, string}?)"/>
    /// and offers the <paramref name="decisions"/> an operator may take on the wait through the
    /// <c>DecideIntervention</c> alteration (spec 260 R-5): 1 to 8 dotted lower-case codes of at most 64 characters, none
    /// starting <c>runtime.</c>. The runtime records them, in offer order, as <c>runtime.interventionDecisions</c> on the
    /// wait-for-intervention outcome metadata, with the requester as <c>runtime.interventionRequestedBy</c>, and later
    /// delivers the chosen one to <c>IRuntimeActivityInterventionDecisionHandler</c>. Offering decisions from an
    /// activity that does not implement that callback, or an invalid offer, faults the evaluation.
    /// </summary>
    void RequestChildFaultIntervention(
        string incidentId,
        string reason,
        IReadOnlyDictionary<string, string>? metadata,
        IReadOnlyCollection<string> decisions);

    IReadOnlyCollection<RuntimeChildFaultInterventionRequest> GetChildFaultInterventionRequests();

    /// <summary>
    /// Stages a non-terminal, coded notification from this still-running structural child to its own
    /// committed parent. The notification always and only reaches the notifying child's
    /// committed parent — there is no target parameter (spoof-proofing). Valid during any of the child's own
    /// evaluations that end in a <c>Defer</c>, <c>Complete</c>, or intentional <c>Fault</c> continuation. With
    /// <c>Defer</c> or <c>Complete</c>, it commits atomically as an ordered durable notification work item. With
    /// <c>Fault</c>, it is accepted only as part of the same durable child-fault parent outcome; cancellation remains
    /// incompatible. Staging from a root activity (no committed parent) faults the evaluation. Multiple notifications
    /// per evaluation are allowed and preserve staging order.
    /// </summary>
    void RequestParentNotification(string code, System.Text.Json.JsonElement? payload = null);

    IReadOnlyCollection<RuntimeParentNotificationRequest> GetParentNotificationRequests();

    /// <summary>
    /// Stages a coded notification to the nearest committed ancestor whose executable node has
    /// <paramref name="ancestorConsumerKey"/>, skipping every composite in between. The runtime resolves the target
    /// from this activity's committed ancestry; the activity never names an execution. Only valid during an initial
    /// structural execution that ends in a <c>Complete</c> continuation: the notification is enqueued ahead of this
    /// activity's own completion, so the ancestor evaluates it first. When no such ancestor exists the activity
    /// faults with <c>runtime.ancestor-notification.target-not-found</c>.
    /// </summary>
    void RequestAncestorNotification(string ancestorConsumerKey, string code, System.Text.Json.JsonElement? payload = null);

    /// <summary>
    /// Stages a coded notification to the nearest committed ancestor that matches <paramref name="selector"/>: a
    /// consumer key (as the overload above), or an activity type key, optionally narrowed to one authored node. The
    /// selector can end the walk at a reusable-activity boundary and can be optional, in which case no match sends
    /// nothing instead of faulting. Valid in an initial structural execution, or in a child-completion evaluation,
    /// that ends in a <c>Complete</c> continuation; either way the notification is enqueued ahead of this activity's
    /// own completion. The receiver learns which of its direct children the notification came through.
    /// </summary>
    void RequestAncestorNotification(RuntimeAncestorSelector selector, string code, System.Text.Json.JsonElement? payload = null);

    IReadOnlyCollection<RuntimeAncestorNotificationRequest> GetAncestorNotificationRequests();
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
