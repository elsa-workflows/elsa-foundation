using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Runtime.Tests.VariableLifecycle;
using Elsa.Activities.Testing;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

public sealed class StepWorkflowStateBoundaryTests
{
    [Fact]
    public async Task Unmarked_native_use_is_sticky_even_when_the_body_catches_the_refusal()
    {
        await using var harness = VariableLifecycleTestGraph.NewHarness("actexec-root");
        var run = await harness.RunAsync(StepVariableTestGraph.Single(StepVariableTestGraph.CountLeaf("node-root", typeof(UnmarkedStateUse))));
        Assert.Equal(ActivityExecutionStatus.Faulted, run.State("node-root").Status);
        Assert.Contains("VF-STATE-002", run.State("node-root").Fault!.Message, StringComparison.Ordinal);
        Assert.Equal(0, VariableLifecycleTestGraph.ReadCount(run));
        Assert.Null(run.WorkflowState!.CorrelationId);
    }

    [Fact]
    public void A_context_outside_an_evaluation_has_no_capability()
    {
        var context = new ActivityExecutionContext("run", "body", "attempt", "node", CancellationToken.None);
        Assert.Contains("VF-STATE-002", Assert.Throws<InvalidOperationException>(() => context.WorkflowState).Message, StringComparison.Ordinal);
    }
}

public sealed class UnmarkedStateUse : Activity
{
    [ActivityInput] public VariableReference<int> Count { get; set; } = default!;
    protected override ValueTask<ActivityTransition<ActivityUnit>> ExecuteAsync(ActivityExecutionContext context)
    {
        context.Variables.Set(Count, 42);
        try { context.WorkflowState.CorrelationId = "attempted"; }
        catch (InvalidOperationException) { }
        return ValueTask.FromResult(ActivityTransition.Complete(ActivityUnit.Value));
    }
}
