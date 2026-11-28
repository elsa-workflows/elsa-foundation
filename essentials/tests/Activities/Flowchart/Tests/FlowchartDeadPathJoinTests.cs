using Elsa.Activities.Flowchart.Contracts;
using Elsa.Activities.Flowchart.Exceptions;
using Elsa.Activities.Flowchart.Internal;
using Elsa.Activities.Flowchart.Internal.Policies;
using Xunit;

namespace Elsa.Activities.Flowchart.Tests;

public sealed class FlowchartDeadPathJoinTests
{
    [Fact]
    public void JoinPolicyKind_MustNameARegisteredJoinPolicy()
    {
        var registry = new FlowchartPolicyRegistry([new DeadPathJoinFlowchartPolicy(), new MergeFlowchartPolicy()]);

        var missing = Assert.Throws<FlowchartExecutionException>(
            () => new FlowchartJoinCoordinator(registry, new FlowchartOptions { JoinPolicyKind = "nope" }).JoinPolicy);
        Assert.Contains("nope", missing.Message, StringComparison.Ordinal);

        // Registered, but not a join policy — a configuration mistake that must not silently pick something else.
        var wrongKind = Assert.Throws<FlowchartExecutionException>(
            () => new FlowchartJoinCoordinator(registry, new FlowchartOptions { JoinPolicyKind = FlowchartPolicyKinds.Merge }).JoinPolicy);
        Assert.Contains(nameof(IFlowchartJoinPolicy), wrongKind.Message, StringComparison.Ordinal);
    }
}
