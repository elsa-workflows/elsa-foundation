using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>Engine-only protocol for activities that schedule and coordinate structural children.</summary>
public interface IRuntimeStructuralActivity
{
    ValueTask<RuntimeStructuralContinuation> ExecuteStructureAsync(IRuntimeActivityExecutionContext context);
}

/// <summary>Engine-only protocol for evaluating a structure after one of its children completes.</summary>
public interface IRuntimeActivityChildCompletionHandler
{
    ValueTask<RuntimeStructuralContinuation> OnChildCompletedAsync(ActivityChildCompletedContext context);
}

/// <summary>Engine-only protocol for evaluating a structure after one of its children faults.</summary>
public interface IRuntimeActivityChildFaultHandler
{
    ValueTask<RuntimeStructuralContinuation> OnChildFaultedAsync(ActivityChildFaultedContext context);
}

/// <summary>
/// Engine-only protocol for evaluating a structure when one of its still-running children raises a coded
/// notification to it. The child keeps running; only a parent that implements this
/// interface receives the callback (opt-in, mirroring the completion/fault handlers) — a non-implementing
/// parent silently acks the notification. The callback may return any continuation and, exactly as in a
/// child-completion evaluation, may stage child schedules and seam-A subtree cancellations under the existing
/// exclusion rules (so an interrupting consumer tears the notifying child down in the same commit).
/// </summary>
public interface IRuntimeActivityChildNotificationHandler
{
    ValueTask<RuntimeStructuralContinuation> OnChildNotifiedAsync(IRuntimeActivityExecutionContext context, ActivityChildNotifiedContext notification);
}

/// <summary>
/// Opt-in marker for a structural activity whose child-completion/child-fault callback needs the parent's
/// direct, non-terminal child executions. Only a parent implementing this pays the extra
/// parent-scoped read; the runtime then populates
/// <see cref="IRuntimeActivityExecutionContext.GetLiveChildActivities"/> for the callback. Used by the BPMN
/// event-based gateway to resolve a losing sibling catch's live child activity-execution id before staging
/// its subtree cancellation.
/// </summary>
public interface IRuntimeLiveChildActivityConsumer;

/// <summary>
/// Opt-in marker for a structural activity that reads its enclosing container-scoped variable values during
/// its structural evaluations (sibling of <see cref="IRuntimeLiveChildActivityConsumer"/>). Only
/// an activity implementing this pays the extra visible-frame projection; the runtime then populates
/// <see cref="IRuntimeActivityExecutionContext.TryReadScopedVariableValue"/> for the invoke,
/// child-completion/child-fault, and bookmark-resume evaluations from committed frame state. The read is
/// read-only, committed-state-backed, and spoof-proof (built from the activity's own lexical ancestor chain);
/// a non-marker activity always reads <see langword="false"/>. Used by <c>BpmnProcess</c> to read a
/// collection-mode multi-instance host's collection variable once at loop start.
/// </summary>
public interface IRuntimeScopedVariableReader;

/// <summary>
/// Opt-in marker for a structural activity whose input snapshot is re-materialized from live committed
/// variable frames before every child-completion/child-fault evaluation (issue #977, sibling of
/// <see cref="IRuntimeLiveChildActivityConsumer"/> and <see cref="IRuntimeScopedVariableReader"/>). Only an
/// activity implementing this pays the extra per-evaluation materialization; the re-materialized snapshot is
/// transient — the pinned <c>ActivityExecutionState.InputSnapshot</c> committed at activation stays immutable,
/// so retries and history keep the original invocation record while the structural callback reads
/// inputs that reflect state the completed child mutated (e.g. a container-scoped variable write).
/// Used by <c>While</c>, whose loop condition must observe body mutations to terminate.
/// </summary>
public interface IRuntimeRematerializeInputsOnChildCompletion;
