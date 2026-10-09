using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Canary.Fixtures;

/// <summary>
/// The canary's structural activity (spec 188, T078, scenario S4): it schedules its one child, and its child-completion
/// callback, which structural parent evaluation activates on its committed snapshot, throws an exception whose message
/// holds the value <see cref="Primary"/> was hydrated with. <see cref="Companion"/> is undeclared and bound to a
/// non-secret literal, as on <see cref="CanaryActivity"/>. It records what it was hydrated with, and does nothing else
/// with the value.
/// </summary>
public sealed class CanaryStructuralActivity(CanaryRecorder recorder) : StructuralActivity,
    IRuntimeStructuralActivity,
    IRuntimeActivityChildCompletionHandler
{
    [ActivityInput(IsCredential = true)]
    public string? Primary { get; set; }

    [ActivityInput]
    public string? Companion { get; set; }

    public ValueTask<RuntimeStructuralContinuation> ExecuteStructureAsync(IRuntimeActivityExecutionContext context)
    {
        recorder.Add(context.WorkflowExecutionId, CanaryRecorder.Execute, Primary, plain: null);
        var child = context.ExecutableNode.ChildSlots.SelectMany(slot => slot.Activities).Single();
        context.ScheduleChildActivity(child.ExecutableNodeId, context.ActivityExecutionState.InvocationId);
        return ValueTask.FromResult(RuntimeStructuralContinuation.Defer);
    }

    public ValueTask<RuntimeStructuralContinuation> OnChildCompletedAsync(ActivityChildCompletedContext context)
    {
        var parent = (IRuntimeActivityExecutionContext)context.ParentContext;
        recorder.Add(parent.WorkflowExecutionId, CanaryRecorder.ChildCompleted, Primary, plain: null);
        throw new CanaryFailureException(CanaryActivity.FailureText(Primary));
    }
}

/// <summary>
/// The canary's notified structural activity (spec 188, scenario S4b): it schedules its one child, a
/// <see cref="CanaryNotifyingChildActivity"/>, and its child-notification callback, which the parent-notification handler
/// activates on its committed snapshot, throws an exception whose message holds the value <see cref="Primary"/> was
/// hydrated with. The child never completes, so the notification callback is the only callback of the parent the run
/// reaches, and the fault boundary S4b exercises is the notification handler's, not the completion handler's.
/// </summary>
public sealed class CanaryNotifiedStructuralActivity(CanaryRecorder recorder) : StructuralActivity,
    IRuntimeStructuralActivity,
    IRuntimeActivityChildNotificationHandler
{
    [ActivityInput(IsCredential = true)]
    public string? Primary { get; set; }

    [ActivityInput]
    public string? Companion { get; set; }

    public ValueTask<RuntimeStructuralContinuation> ExecuteStructureAsync(IRuntimeActivityExecutionContext context)
    {
        recorder.Add(context.WorkflowExecutionId, CanaryRecorder.Execute, Primary, plain: null);
        var child = context.ExecutableNode.ChildSlots.SelectMany(slot => slot.Activities).Single();
        context.ScheduleChildActivity(child.ExecutableNodeId, context.ActivityExecutionState.InvocationId);
        return ValueTask.FromResult(RuntimeStructuralContinuation.Defer);
    }

    public ValueTask<RuntimeStructuralContinuation> OnChildNotifiedAsync(IRuntimeActivityExecutionContext context, ActivityChildNotifiedContext notification)
    {
        recorder.Add(context.WorkflowExecutionId, CanaryRecorder.ChildNotified, Primary, plain: null);
        throw new CanaryFailureException(CanaryActivity.FailureText(Primary));
    }
}

/// <summary>
/// The child a <see cref="CanaryNotifiedStructuralActivity"/> schedules: it notifies its parent, schedules its one child
/// (a suspended <see cref="CanaryActivity"/> no stimulus resumes) and defers, so it never completes.
/// </summary>
public sealed class CanaryNotifyingChildActivity : StructuralActivity, IRuntimeStructuralActivity
{
    public const string NotificationCode = "canary-notice";

    public ValueTask<RuntimeStructuralContinuation> ExecuteStructureAsync(IRuntimeActivityExecutionContext context)
    {
        context.RequestParentNotification(NotificationCode);
        var child = context.ExecutableNode.ChildSlots.SelectMany(slot => slot.Activities).Single();
        context.ScheduleChildActivity(child.ExecutableNodeId, context.ActivityExecutionState.InvocationId);
        return ValueTask.FromResult(RuntimeStructuralContinuation.Defer);
    }
}

/// <summary>The child a <see cref="CanaryStructuralActivity"/> schedules: it reads no input and completes.</summary>
public sealed class CanaryChildActivity : Activity<ActivityUnit>
{
    protected override ValueTask<ActivityTransition<ActivityUnit>> ExecuteAsync(ActivityExecutionContext context) =>
        ValueTask.FromResult(ActivityTransition.Complete(ActivityUnit.Value, ActivityOutcomes.Done));
}
