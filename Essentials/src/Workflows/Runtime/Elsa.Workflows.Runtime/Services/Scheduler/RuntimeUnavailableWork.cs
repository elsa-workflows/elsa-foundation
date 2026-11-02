using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Services.Scheduler;

/// <summary>One delayed-release policy shared by dispatch and coalescing visibility persistence.</summary>
internal static class RuntimeUnavailableWork
{
    internal static bool SupportsDelayedRelease(WorkflowExecutionCommandKind kind) => kind is
        WorkflowExecutionCommandKind.ResumeBookmark or WorkflowExecutionCommandKind.InvokeActivity or
        WorkflowExecutionCommandKind.CompleteActivity or WorkflowExecutionCommandKind.NotifyParentActivity or
        WorkflowExecutionCommandKind.DeliverChildControl or WorkflowExecutionCommandKind.Checkpoint;
}

/// <summary>Whether the exact active claim can persist its requested deferral visibility.</summary>
internal interface IRuntimeSchedulerWorkDeferralCapability
{
    bool CanDefer(RuntimeSchedulerWorkClaim claim);

    /// <summary>
    /// Whether a coalescing segment buffered a non-retaining checkpoint while this claim's item was in flight. That
    /// checkpoint is the item's effect, which the segment's flush makes durable; outside a session it would have
    /// consumed the item.
    /// </summary>
    bool HoldsBufferedEffect(RuntimeSchedulerWorkClaim claim);
}
