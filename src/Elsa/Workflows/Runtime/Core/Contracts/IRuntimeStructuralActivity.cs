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
/// Opt-in marker for a structural activity whose child-completion/child-fault callback needs the parent's
/// direct, non-terminal child executions (spec 119 D4). Only a parent implementing this pays the extra
/// parent-scoped read; the runtime then populates
/// <see cref="IRuntimeActivityExecutionContext.GetLiveChildActivities"/> for the callback. Used by the BPMN
/// event-based gateway to resolve a losing sibling catch's live child activity-execution id before staging
/// its subtree cancellation (spec 112).
/// </summary>
public interface IRuntimeLiveChildActivityConsumer;

/// <summary>
/// Opt-in marker for a structural activity that reads its enclosing container-scoped variable values during
/// its structural evaluations (spec 123 D1, sibling of <see cref="IRuntimeLiveChildActivityConsumer"/>). Only
/// an activity implementing this pays the extra visible-frame projection; the runtime then populates
/// <see cref="IRuntimeActivityExecutionContext.TryReadScopedVariableValue"/> for the invoke,
/// child-completion/child-fault, and bookmark-resume evaluations from committed frame state. The read is
/// read-only, committed-state-backed, and spoof-proof (built from the activity's own lexical ancestor chain);
/// a non-marker activity always reads <see langword="false"/>. Used by <c>BpmnProcess</c> to read a
/// collection-mode multi-instance host's collection variable once at loop start.
/// </summary>
public interface IRuntimeScopedVariableReader;
