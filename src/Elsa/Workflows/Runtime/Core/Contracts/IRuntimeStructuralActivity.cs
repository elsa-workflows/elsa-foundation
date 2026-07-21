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
