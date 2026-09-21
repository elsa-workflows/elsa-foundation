using Elsa.Activities.Runtime.Core.Models;
using Elsa.Primitives.Models;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

public sealed class ActivityTransitionContractTests
{
    [Fact]
    public void Complete_carries_one_result_and_one_outcome()
    {
        var transition = ActivityTransition.Complete(new Result("receipt-1"), "Charged");
        var completion = Assert.IsAssignableFrom<IActivityCompletionTransition<Result>>(transition);

        Assert.Equal(ActivityTransitionKind.Complete, transition.Kind);
        Assert.Equal("receipt-1", completion.Result.ReceiptId);
        Assert.Equal(typeof(Result), completion.ResultType);
        Assert.Equal("Charged", completion.Outcome);
    }

    [Fact]
    public void Suspend_requires_typed_trigger_expectations()
    {
        Assert.Throws<ArgumentException>(() => ActivityTransition.Suspend<Result, State>(new State("waiting"), []));

        var transition = ActivityTransition.Suspend<Result, State>(
            new State("waiting"),
            [new ActivityTriggerExpectation("approval", "Event", new ValueTypeDescriptor("String"))]);
        var suspension = Assert.IsAssignableFrom<IActivitySuspensionTransition<State>>(transition);

        Assert.Equal(ActivityTransitionKind.Suspend, transition.Kind);
        Assert.Equal("waiting", suspension.State.Status);
        Assert.Equal(typeof(State), suspension.StateType);
        Assert.Single(suspension.Triggers);
    }

    [Fact]
    public void Fault_and_cancel_are_distinct_from_successful_outcomes()
    {
        var fault = ActivityTransition.Fault<Result>(new ActivityFault("payment.declined", "Declined"));
        var cancel = ActivityTransition.Cancel<Result>("Workflow cancelled");

        Assert.Equal(ActivityTransitionKind.Fault, fault.Kind);
        Assert.Equal(ActivityTransitionKind.Cancel, cancel.Kind);
    }

    private sealed record Result(string ReceiptId);
    private sealed record State(string Status);
}
