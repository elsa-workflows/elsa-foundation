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
}
