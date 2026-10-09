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

/// <summary>The child a <see cref="CanaryStructuralActivity"/> schedules: it reads no input and completes.</summary>
public sealed class CanaryChildActivity : Activity<ActivityUnit>
{
    protected override ValueTask<ActivityTransition<ActivityUnit>> ExecuteAsync(ActivityExecutionContext context) =>
        ValueTask.FromResult(ActivityTransition.Complete(ActivityUnit.Value, ActivityOutcomes.Done));
}
