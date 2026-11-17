using System.Text.Json.Nodes;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Constants;

namespace Elsa.Activities.Runtime.Tests.VariableLifecycle;

// Spec 1231 (S-7) test activities: step bodies that read and write variables through context.Variables.

/// <summary>Reads <c>count</c> through its session and writes <c>count + 1</c>.</summary>
public sealed class SessionIncrementActivity : Activity
{
    [ActivityInput]
    public VariableReference<int> Count { get; set; } = default!;

    protected override ValueTask<ActivityTransition<ActivityUnit>> ExecuteAsync(ActivityExecutionContext context)
    {
        var count = context.Variables.Get(Count);
        context.Variables.Set(Count, count + 1);
        return ValueTask.FromResult(ActivityTransition.Complete(ActivityUnit.Value, ActivityOutcomes.Done));
    }
}

/// <summary>Writes 42 to <c>count</c> without reading it (a blind session write).</summary>
public sealed class BlindSessionWriteActivity : Activity
{
    [ActivityInput]
    public VariableReference<int> Count { get; set; } = default!;

    protected override ValueTask<ActivityTransition<ActivityUnit>> ExecuteAsync(ActivityExecutionContext context)
    {
        context.Variables.Set(Count, 42);
        return ValueTask.FromResult(ActivityTransition.Complete(ActivityUnit.Value, ActivityOutcomes.Done));
    }
}

/// <summary>Writes 10 to <c>count</c> without reading it.</summary>
public sealed class SetTenActivity : Activity
{
    [ActivityInput]
    public VariableReference<int> Count { get; set; } = default!;

    protected override ValueTask<ActivityTransition<ActivityUnit>> ExecuteAsync(ActivityExecutionContext context)
    {
        context.Variables.Set(Count, 10);
        return ValueTask.FromResult(ActivityTransition.Complete(ActivityUnit.Value, ActivityOutcomes.Done));
    }
}

/// <summary>
/// Reads <c>count</c> as an input (<c>Seen</c>), reads it again in its session, and returns <c>Seen + 1</c>: a capture
/// computed from the input, which must keep the input's older version as its base (ADR 0092 decision 4).
/// </summary>
public sealed class InputThenSessionReadActivity : Activity<Incremented>
{
    [ActivityInput]
    public int Seen { get; set; }

    [ActivityInput]
    public ReadOnlyVariableReference<int> Count { get; set; } = default!;

    public static int LastSessionRead { get; private set; }

    protected override ValueTask<ActivityTransition<Incremented>> ExecuteAsync(ActivityExecutionContext context)
    {
        LastSessionRead = context.Variables.Get(Count);
        return ValueTask.FromResult(ActivityTransition.Complete(new Incremented(Seen + 1), ActivityOutcomes.Done));
    }
}

/// <summary>Writes 5 to <c>count</c> in its session and returns 7, captured into <c>count</c> in the same commit.</summary>
public sealed class WriteAndCaptureActivity : Activity<Incremented>
{
    [ActivityInput]
    public VariableReference<int> Count { get; set; } = default!;

    protected override ValueTask<ActivityTransition<Incremented>> ExecuteAsync(ActivityExecutionContext context)
    {
        context.Variables.Set(Count, 5);
        return ValueTask.FromResult(ActivityTransition.Complete(new Incremented(7), ActivityOutcomes.Done));
    }
}

/// <summary>Writes <c>count</c>, reads it back (read-your-writes) and returns what it read.</summary>
public sealed class ReadYourWritesActivity : Activity<Incremented>
{
    [ActivityInput]
    public VariableReference<int> Count { get; set; } = default!;

    protected override ValueTask<ActivityTransition<Incremented>> ExecuteAsync(ActivityExecutionContext context)
    {
        context.Variables.Set(Count, 7);
        return ValueTask.FromResult(ActivityTransition.Complete(new Incremented(context.Variables.Get(Count)), ActivityOutcomes.Done));
    }
}

/// <summary>Writes <c>count</c>, then throws: nothing it staged may be saved.</summary>
public sealed class WriteThenThrowActivity : Activity
{
    [ActivityInput]
    public VariableReference<int> Count { get; set; } = default!;

    protected override ValueTask<ActivityTransition<ActivityUnit>> ExecuteAsync(ActivityExecutionContext context)
    {
        context.Variables.Set(Count, 5);
        throw new InvalidOperationException("The body failed after its write.");
    }
}

/// <summary>Writes <c>count</c>, then returns a fault transition.</summary>
public sealed class WriteThenFaultActivity : Activity
{
    [ActivityInput]
    public VariableReference<int> Count { get; set; } = default!;

    protected override ValueTask<ActivityTransition<ActivityUnit>> ExecuteAsync(ActivityExecutionContext context)
    {
        context.Variables.Set(Count, 5);
        return ValueTask.FromResult(ActivityTransition.Fault<ActivityUnit>(new ActivityFault("test.fault", "The body returned a fault.")));
    }
}

/// <summary>Writes the value <c>count</c> already holds (0): a no-op (C4).</summary>
public sealed class WriteSameValueActivity : Activity
{
    [ActivityInput]
    public VariableReference<int> Count { get; set; } = default!;

    protected override ValueTask<ActivityTransition<ActivityUnit>> ExecuteAsync(ActivityExecutionContext context)
    {
        context.Variables.Set(Count, context.Variables.Get(Count));
        return ValueTask.FromResult(ActivityTransition.Complete(ActivityUnit.Value, ActivityOutcomes.Done));
    }
}

/// <summary>Calls <c>Set</c> on a read-only reference, which throws.</summary>
public sealed class SetReadOnlyActivity : Activity
{
    [ActivityInput]
    public ReadOnlyVariableReference<int> Count { get; set; } = default!;

    protected override ValueTask<ActivityTransition<ActivityUnit>> ExecuteAsync(ActivityExecutionContext context)
    {
#pragma warning disable CS0618 // The point of the test: the obsolete overload throws.
        context.Variables.Set(Count, 5);
#pragma warning restore CS0618
        return ValueTask.FromResult(ActivityTransition.Complete(ActivityUnit.Value, ActivityOutcomes.Done));
    }
}

/// <summary>Reads the <c>order</c> (an <c>Any</c> variable) as a JSON node, changes the copy and does not set it (C2).</summary>
public sealed class ChangeCopyWithoutSetActivity : Activity
{
    [ActivityInput]
    public VariableReference<JsonNode> Order { get; set; } = default!;

    protected override ValueTask<ActivityTransition<ActivityUnit>> ExecuteAsync(ActivityExecutionContext context)
    {
        var order = context.Variables.Get(Order)!;
        order["total"] = 999;
        return ValueTask.FromResult(ActivityTransition.Complete(ActivityUnit.Value, ActivityOutcomes.Done));
    }
}

/// <summary>Reads <c>order</c> as a JSON node, changes one member and sets the whole value back.</summary>
public sealed class ChangeJsonMemberActivity : Activity
{
    [ActivityInput]
    public VariableReference<JsonNode> Order { get; set; } = default!;

    protected override ValueTask<ActivityTransition<ActivityUnit>> ExecuteAsync(ActivityExecutionContext context)
    {
        var order = context.Variables.Get(Order)!;
        order["total"] = 1;
        context.Variables.Set(Order, order);
        return ValueTask.FromResult(ActivityTransition.Complete(ActivityUnit.Value, ActivityOutcomes.Done));
    }
}

/// <summary>The part of an order a narrower activity knows about.</summary>
public sealed class OrderTotal
{
    public int Total { get; set; }
}

/// <summary>Reads <c>order</c> as a narrower class (dropping its other members) and sets it back.</summary>
public sealed class NarrowOrderWriteActivity : Activity
{
    [ActivityInput]
    public VariableReference<OrderTotal> Order { get; set; } = default!;

    protected override ValueTask<ActivityTransition<ActivityUnit>> ExecuteAsync(ActivityExecutionContext context)
    {
        var order = context.Variables.Get(Order);
        order.Total = 2;
        context.Variables.Set(Order, order);
        return ValueTask.FromResult(ActivityTransition.Complete(ActivityUnit.Value, ActivityOutcomes.Done));
    }
}

/// <summary>A generic activity marked for free names: sets <c>other</c> to <c>count + 5</c> by name.</summary>
[AccessesUndeclaredVariables]
public sealed class FreeNameSetActivity : Activity
{
    protected override ValueTask<ActivityTransition<ActivityUnit>> ExecuteAsync(ActivityExecutionContext context)
    {
        context.Variables.Set("other", context.Variables.Get<int>("count") + 5);
        return ValueTask.FromResult(ActivityTransition.Complete(ActivityUnit.Value, ActivityOutcomes.Done));
    }
}

/// <summary>A marked activity that writes <c>count</c> and then a name no scope declares: the step faults, nothing is created.</summary>
[AccessesUndeclaredVariables]
public sealed class FreeNameUndeclaredActivity : Activity
{
    protected override ValueTask<ActivityTransition<ActivityUnit>> ExecuteAsync(ActivityExecutionContext context)
    {
        context.Variables.Set("count", 3);
        context.Variables.Set("total", 5);
        return ValueTask.FromResult(ActivityTransition.Complete(ActivityUnit.Value, ActivityOutcomes.Done));
    }
}

/// <summary>The same free-name call on an activity that is not marked: it throws.</summary>
public sealed class UnmarkedFreeNameActivity : Activity
{
    protected override ValueTask<ActivityTransition<ActivityUnit>> ExecuteAsync(ActivityExecutionContext context)
    {
        context.Variables.Set("count", 5);
        return ValueTask.FromResult(ActivityTransition.Complete(ActivityUnit.Value, ActivityOutcomes.Done));
    }
}

/// <summary>A marked activity that writes a value of the wrong type, and catches the exception: the step still faults.</summary>
[AccessesUndeclaredVariables]
public sealed class WrongTypeCaughtActivity : Activity
{
    protected override ValueTask<ActivityTransition<ActivityUnit>> ExecuteAsync(ActivityExecutionContext context)
    {
        try
        {
            context.Variables.Set("count", "not a number");
        }
        catch (InvalidOperationException)
        {
            // The session records the fault on the host side; catching it does not save the step.
        }

        return ValueTask.FromResult(ActivityTransition.Complete(ActivityUnit.Value, ActivityOutcomes.Done));
    }
}

/// <summary>Writes the container variable <c>inner</c> and the workflow variable <c>count</c>.</summary>
public sealed class ContainerWriteActivity : Activity
{
    [ActivityInput]
    public VariableReference<int> Inner { get; set; } = default!;

    protected override ValueTask<ActivityTransition<ActivityUnit>> ExecuteAsync(ActivityExecutionContext context)
    {
        context.Variables.Set(Inner, context.Variables.Get(Inner) + 1);
        return ValueTask.FromResult(ActivityTransition.Complete(ActivityUnit.Value, ActivityOutcomes.Done));
    }
}

/// <summary>Suspends without writing; on resume writes <c>Seen + 1</c> (computed from its input read) to <c>count</c>.</summary>
public sealed class ResumeWriteFromInputActivity : StatefulActivity<Incremented, WaitState, WaitTrigger>
{
    public const string ResumeTargetKey = "resume-write-input";

    [ActivityInput]
    public int Seen { get; set; }

    [ActivityInput]
    public VariableReference<int> Count { get; set; } = default!;

    protected override ValueTask<ActivityTransition<Incremented, WaitState>> ExecuteAsync(ActivityExecutionContext context) =>
        ValueTask.FromResult(Suspend(
            new WaitState("waiting"),
            [new ActivityTriggerRegistration<WaitTrigger>(ResumeTargetKey, StepVariableResume.StimulusType, StepVariableResume.StimulusHash)]));

    protected override ValueTask<ActivityTransition<Incremented, WaitState>> ResumeAsync(ActivityResumeContext<WaitState, WaitTrigger> context)
    {
        context.Variables.Set(Count, Seen + 1);
        return ValueTask.FromResult(Complete(new Incremented(Seen + 1), ActivityOutcomes.Done));
    }
}

/// <summary>Reads <c>count</c> in its session, suspends; on resume writes 100 to <c>count</c> with a fresh session.</summary>
public sealed class SessionReadThenResumeWriteActivity : StatefulActivity<Incremented, WaitState, WaitTrigger>
{
    public const string ResumeTargetKey = "resume-after-session-read";

    [ActivityInput]
    public VariableReference<int> Count { get; set; } = default!;

    protected override ValueTask<ActivityTransition<Incremented, WaitState>> ExecuteAsync(ActivityExecutionContext context)
    {
        _ = context.Variables.Get(Count);
        return ValueTask.FromResult(Suspend(
            new WaitState("waiting"),
            [new ActivityTriggerRegistration<WaitTrigger>(ResumeTargetKey, StepVariableResume.StimulusType, StepVariableResume.StimulusHash)]));
    }

    protected override ValueTask<ActivityTransition<Incremented, WaitState>> ResumeAsync(ActivityResumeContext<WaitState, WaitTrigger> context)
    {
        context.Variables.Set(Count, 100);
        return ValueTask.FromResult(Complete(new Incremented(100), ActivityOutcomes.Done));
    }
}

/// <summary>Writes 5 to <c>count</c> in its session when it suspends; on resume returns 6, captured into <c>count</c>.</summary>
public sealed class WriteOnSuspendThenCaptureActivity : StatefulActivity<Incremented, WaitState, WaitTrigger>
{
    public const string ResumeTargetKey = "write-on-suspend";

    [ActivityInput]
    public VariableReference<int> Count { get; set; } = default!;

    protected override ValueTask<ActivityTransition<Incremented, WaitState>> ExecuteAsync(ActivityExecutionContext context)
    {
        context.Variables.Set(Count, 5);
        return ValueTask.FromResult(Suspend(
            new WaitState("waiting"),
            [new ActivityTriggerRegistration<WaitTrigger>(ResumeTargetKey, StepVariableResume.StimulusType, StepVariableResume.StimulusHash)]));
    }

    protected override ValueTask<ActivityTransition<Incremented, WaitState>> ResumeAsync(ActivityResumeContext<WaitState, WaitTrigger> context) =>
        ValueTask.FromResult(Complete(new Incremented(6), ActivityOutcomes.Done));
}

/// <summary>The stimulus the resumable session leaves wait for.</summary>
public static class StepVariableResume
{
    public const string StimulusType = "TestStepVariableResume";
    public const string StimulusHash = "step-variable:resume";
}
