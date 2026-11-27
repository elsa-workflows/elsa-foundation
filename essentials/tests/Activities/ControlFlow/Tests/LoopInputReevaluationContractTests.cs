using Elsa.Workflows.Runtime.Core.Contracts;
using Xunit;

namespace Elsa.Activities.ControlFlow.Tests;

/// <summary>
/// Locks the #972 defect-2 design decision: only condition-driven loop composites re-materialize their
/// inputs on child completion. Collection-driven loops (<c>ForEach</c>/<c>For</c>) keep the input snapshot
/// pinned at first activation — a body mutation must NOT be able to change the loop bound mid-iteration —
/// and one-shot branch composites have nothing to re-evaluate.
/// </summary>
public sealed class LoopInputReevaluationContractTests
{
    [Theory]
    [InlineData(typeof(Elsa.Activities.While.Activities.While))]
    [InlineData(typeof(Elsa.Activities.Do.Activities.Do))]
    public void Condition_driven_loops_reevaluate_inputs_on_child_completion(Type activityType) =>
        Assert.True(typeof(IRuntimeRematerializeInputsOnChildCompletion).IsAssignableFrom(activityType));

    [Theory]
    [InlineData(typeof(Elsa.Activities.ForEach.Activities.ForEach))]
    [InlineData(typeof(Elsa.Activities.For.Activities.For))]
    [InlineData(typeof(Elsa.Activities.If.Activities.If))]
    public void Collection_driven_loops_and_branches_keep_their_pinned_input_snapshot(Type activityType) =>
        Assert.False(typeof(IRuntimeRematerializeInputsOnChildCompletion).IsAssignableFrom(activityType));
}
