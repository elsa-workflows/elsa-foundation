using Elsa.Activities.Flowchart.Contracts;
using Elsa.Activities.Flowchart.Exceptions;
using Elsa.Activities.Flowchart.Internal.Policies;
using Elsa.Activities.Flowchart.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Flowchart.Tests;

public sealed class FlowchartExecutionEngineTests
{
    [Fact]
    public async Task ImplicitJoin_RecordsJoinedDiagnostic()
    {
        await using var fixture = await FlowchartRuntimeFixture.CreateAsync([
            "actexec-flowchart",
            "actexec-a",
            "actexec-b",
            "actexec-c",
            "actexec-d"
        ]);
        var executable = fixture.NewExecutable(
            children:
            [
                fixture.NewProbeNode("node-a"),
                fixture.NewProbeNode("node-b"),
                fixture.NewProbeNode("node-c"),
                fixture.NewProbeNode("node-d")
            ],
            connections:
            [
                fixture.NewConnection("node-a", "node-b"),
                fixture.NewConnection("node-a", "node-c"),
                fixture.NewConnection("node-b", "node-d"),
                fixture.NewConnection("node-c", "node-d")
            ],
            startNodeId: "node-a");

        await fixture.ExecuteAsync(executable);

        var state = await fixture.GetFlowchartStateAsync();
        Assert.Contains(state.Diagnostics, diagnostic => diagnostic.Kind == FlowchartDiagnosticKind.Joined && diagnostic.NodeId == "node-d");
    }

    [Fact]
    public void BuiltInForkPolicies_ReturnExpectedScheduleCommands()
    {
        var context = NewPolicyContext(["Done"]);

        Assert.Equal(["node-b"], new DecisionFlowchartPolicy().Execute(context).Commands.Select(command => command.NodeId));
        Assert.Equal(["node-b", "node-c"], new ParallelForkFlowchartPolicy().Execute(context).Commands.Select(command => command.NodeId));
        Assert.Equal(["node-b"], new InclusiveForkFlowchartPolicy().Execute(context).Commands.Select(command => command.NodeId));
        Assert.Equal(["node-b"], new MergeFlowchartPolicy().Execute(context).Commands.Select(command => command.NodeId));
        Assert.Equal(["node-b"], new FirstWinsFlowchartPolicy().Execute(context).Commands.Select(command => command.NodeId));
    }

    [Fact]
    public async Task InvalidPolicyCommand_FaultsFlowchartWithPolicyFailure()
    {
        await using var fixture = await FlowchartRuntimeFixture.CreateAsync(
            ["actexec-flowchart", "actexec-a"],
            services => services.AddSingleton<IFlowchartPolicy, InvalidPolicy>());
        var executable = fixture.NewExecutable(
            children: [fixture.NewProbeNode("node-a")],
            connections: [],
            startNodeId: "node-a",
            nodeMetadata: new Dictionary<string, FlowchartNodeMetadata>
            {
                ["node-a"] = new(InvalidPolicy.Kind)
            });

        await fixture.ExecuteAsync(executable);

        var state = await fixture.GetFlowchartStateAsync();
        Assert.Contains(state.Diagnostics, diagnostic => diagnostic.Kind == FlowchartDiagnosticKind.PolicyFailure && diagnostic.NodeId == "node-a");
    }

    private static IFlowchartPolicyContext NewPolicyContext(IReadOnlyCollection<string> outcomes) =>
        new TestPolicyContext(
            "node-a",
            outcomes,
            [
                new FlowchartConnection(new FlowchartEndpoint("node-a"), new FlowchartEndpoint("node-b")),
                new FlowchartConnection(new FlowchartEndpoint("node-a", "Other"), new FlowchartEndpoint("node-c"))
            ]);

    private sealed record TestPolicyContext(
        string? CurrentNodeId,
        IReadOnlyCollection<string> OutcomeNames,
        IReadOnlyCollection<FlowchartConnection> Connections) : IFlowchartPolicyContext
    {
        public FlowchartPolicyTrigger Trigger => FlowchartPolicyTrigger.ChildCompleted;
        public FlowchartExecutionState State { get; } = new("scope:root");
    }

    private sealed class InvalidPolicy : IFlowchartPolicy
    {
        public const string Kind = "test/invalid";
        public string PolicyKind => Kind;
        public string DisplayName => "Invalid";
        public FlowchartPolicyDecision Execute(IFlowchartPolicyContext context) => new([new FlowchartPolicyCommand(FlowchartPolicyCommandKind.ScheduleNode)]);
    }
}
