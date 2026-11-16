using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Secrets.Workflows.Tests.Support;

/// <summary>One value a secret-bound test activity was hydrated with: the run, the step that read it and the value.</summary>
public sealed record RecordedSecretValue(string WorkflowExecutionId, string Step, string? Value);

/// <summary>
/// Collects, per workflow execution, every value the test activities were hydrated with, in the order they ran. The
/// activities record here and nowhere else, so nothing they read reaches a result, a log or persisted state.
/// </summary>
public sealed class SecretValueRecorder
{
    public const string Execute = "execute";
    public const string Resume = "resume";
    public const string ChildCompleted = "child-completed";

    private readonly List<RecordedSecretValue> _entries = [];

    public void Add(string workflowExecutionId, string step, string? value)
    {
        lock (_entries)
            _entries.Add(new(workflowExecutionId, step, value));
    }

    /// <summary>The steps and values recorded for <paramref name="workflowExecutionId"/>, in the order they ran.</summary>
    public IReadOnlyList<(string Step, string? Value)> For(string workflowExecutionId)
    {
        lock (_entries)
            return _entries.Where(entry => entry.WorkflowExecutionId == workflowExecutionId).Select(entry => (entry.Step, entry.Value)).ToArray();
    }
}

/// <summary>A leaf that records its secret-bound <see cref="Text"/>, then completes.</summary>
public sealed class SecretReadingActivity(SecretValueRecorder recorder) : Activity<ActivityUnit>
{
    [ActivityInput]
    public string Text { get; set; } = null!;

    protected override ValueTask<ActivityTransition<ActivityUnit>> ExecuteAsync(ActivityExecutionContext context)
    {
        recorder.Add(context.WorkflowExecutionId, SecretValueRecorder.Execute, Text);
        return ValueTask.FromResult(ActivityTransition.Complete(ActivityUnit.Value, ActivityOutcomes.Done));
    }
}

/// <summary>The child a structural test parent schedules: it reads no input and completes.</summary>
public sealed class CompletingChildActivity : Activity<ActivityUnit>
{
    protected override ValueTask<ActivityTransition<ActivityUnit>> ExecuteAsync(ActivityExecutionContext context) =>
        ValueTask.FromResult(ActivityTransition.Complete(ActivityUnit.Value, ActivityOutcomes.Done));
}

public sealed record SecretWaitState(string Marker);

public sealed record SecretWaitTrigger(bool Released);

/// <summary>A leaf that records its secret-bound <see cref="Text"/>, suspends, and records it again when it is resumed.</summary>
public sealed class SecretWaitingActivity(SecretValueRecorder recorder) : StatefulActivity<ActivityUnit, SecretWaitState, SecretWaitTrigger>
{
    public const string ResumeTargetKey = "wait";
    public const string StimulusType = "TestSecretWait";
    public const string StimulusHash = "secret-wait:1";

    [ActivityInput]
    public string Text { get; set; } = null!;

    protected override ValueTask<ActivityTransition<ActivityUnit, SecretWaitState>> ExecuteAsync(ActivityExecutionContext context)
    {
        recorder.Add(context.WorkflowExecutionId, SecretValueRecorder.Execute, Text);
        return ValueTask.FromResult(Suspend(
            new SecretWaitState("waiting"),
            [new ActivityTriggerRegistration<SecretWaitTrigger>(ResumeTargetKey, StimulusType, StimulusHash)]));
    }

    protected override ValueTask<ActivityTransition<ActivityUnit, SecretWaitState>> ResumeAsync(ActivityResumeContext<SecretWaitState, SecretWaitTrigger> context)
    {
        recorder.Add(context.WorkflowExecutionId, SecretValueRecorder.Resume, Text);
        return ValueTask.FromResult(Complete(ActivityUnit.Value, ActivityOutcomes.Done));
    }
}

/// <summary>
/// A structural parent with a secret-bound <see cref="Text"/> over one child. It records the input when it schedules the
/// child and again in the child-completion evaluation, which completes it.
/// </summary>
public abstract class SecretParentActivityBase(SecretValueRecorder recorder) : StructuralActivity,
    IRuntimeStructuralActivity,
    IRuntimeActivityChildCompletionHandler
{
    [ActivityInput]
    public string Text { get; set; } = null!;

    public ValueTask<RuntimeStructuralContinuation> ExecuteStructureAsync(IRuntimeActivityExecutionContext context)
    {
        recorder.Add(context.WorkflowExecutionId, SecretValueRecorder.Execute, Text);
        var child = Assert.Single(Assert.Single(context.ExecutableNode.ChildSlots).Activities);
        context.ScheduleChildActivity(child.ExecutableNodeId, context.ActivityExecutionState.InvocationId);
        return ValueTask.FromResult(RuntimeStructuralContinuation.Defer);
    }

    public ValueTask<RuntimeStructuralContinuation> OnChildCompletedAsync(ActivityChildCompletedContext context)
    {
        var parent = Assert.IsAssignableFrom<IRuntimeActivityExecutionContext>(context.ParentContext);
        recorder.Add(parent.WorkflowExecutionId, SecretValueRecorder.ChildCompleted, Text);
        return ValueTask.FromResult(RuntimeStructuralContinuation.Complete());
    }
}

/// <summary>Evaluated on its committed snapshot when its child completes (structural parent evaluation).</summary>
public sealed class SecretReadingParentActivity(SecretValueRecorder recorder) : SecretParentActivityBase(recorder);

/// <summary>
/// Opts into re-materializing its inputs when its child completes, as <c>Do</c> and <c>While</c> do, so that evaluation
/// activates it a second time on a fresh, never persisted snapshot.
/// </summary>
public sealed class SecretRematerializingParentActivity(SecretValueRecorder recorder) : SecretParentActivityBase(recorder),
    IRuntimeRematerializeInputsOnChildCompletion;
