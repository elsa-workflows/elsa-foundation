using Elsa.Activities.Flowchart.Contracts;
using Elsa.Activities.Flowchart.Exceptions;
using Elsa.Activities.Flowchart.Internal;
using Elsa.Activities.Flowchart.Internal.Policies;
using Elsa.Activities.Flowchart.Models;
using Xunit;

namespace Elsa.Activities.Flowchart.Tests;

public sealed class FlowchartPolicyContractTests
{
    [Fact]
    public void Registry_ResolvesRegisteredPolicyByKind()
    {
        var policy = new DirectContinuationFlowchartPolicy();
        var registry = new FlowchartPolicyRegistry([policy]);

        var resolved = registry.GetRequired(FlowchartPolicyKinds.DirectContinuation);

        Assert.Same(policy, resolved);
    }

    [Fact]
    public void Registry_RejectsDuplicatePolicyKind()
    {
        var registry = new FlowchartPolicyRegistry([new DirectContinuationFlowchartPolicy()]);

        var exception = Assert.Throws<FlowchartExecutionException>(() => registry.Register(new DuplicateDirectContinuationPolicy()));

        Assert.Contains(FlowchartPolicyKinds.DirectContinuation, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Registry_NormalizesRegisteredPolicyKind()
    {
        var registry = new FlowchartPolicyRegistry([new WhitespacePolicy()]);

        var resolved = registry.GetRequired("test/whitespace");

        Assert.IsType<WhitespacePolicy>(resolved);
        Assert.Throws<FlowchartExecutionException>(() => registry.Register(new DuplicateWhitespacePolicy()));
    }

    [Fact]
    public void Registry_ThrowsForMissingPolicyKind()
    {
        var registry = new FlowchartPolicyRegistry([]);

        var exception = Assert.Throws<FlowchartExecutionException>(() => registry.GetRequired("missing"));

        Assert.Contains("missing", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void PolicyContext_ExposesReadOnlySnapshotInputs()
    {
        var state = new FlowchartExecutionState("scope:root");
        var connections = new[] { new FlowchartConnection(new FlowchartEndpoint("a"), new FlowchartEndpoint("b")) };
        var outcomes = new[] { "Done" };

        var context = new FlowchartPolicyContext(FlowchartPolicyTrigger.ChildCompleted, state, connections, "a", outcomes);
        outcomes[0] = "Changed";

        Assert.Equal(FlowchartPolicyTrigger.ChildCompleted, context.Trigger);
        Assert.Same(state, context.State);
        Assert.Equal("a", context.CurrentNodeId);
        Assert.Equal(["Done"], context.OutcomeNames);
        Assert.Single(context.Connections);
    }

    private sealed class DuplicateDirectContinuationPolicy : IFlowchartPolicy
    {
        public string PolicyKind => FlowchartPolicyKinds.DirectContinuation;
        public string DisplayName => "Duplicate";
        public FlowchartPolicyDecision Execute(IFlowchartPolicyContext context) => new();
    }

    private sealed class WhitespacePolicy : IFlowchartPolicy
    {
        public string PolicyKind => " test/whitespace ";
        public string DisplayName => "Whitespace";
        public FlowchartPolicyDecision Execute(IFlowchartPolicyContext context) => new();
    }

    private sealed class DuplicateWhitespacePolicy : IFlowchartPolicy
    {
        public string PolicyKind => "test/whitespace";
        public string DisplayName => "Duplicate Whitespace";
        public FlowchartPolicyDecision Execute(IFlowchartPolicyContext context) => new();
    }
}
