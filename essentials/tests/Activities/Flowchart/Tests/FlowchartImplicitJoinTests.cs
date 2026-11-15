using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Flowchart.Tests;

public sealed class FlowchartImplicitJoinTests
{
    [Fact]
    public async Task DiamondFlowchart_RunsReconvergedActivityOnceAfterBothActiveBranchesArrive()
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

        var states = await fixture.Provider.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync("wfexec-1");
        var reconverged = states.Where(state => state.Execution.ExecutableNodeId == "node-d").ToArray();
        Assert.Single(reconverged);
        Assert.Equal(ActivityExecutionStatus.Completed, reconverged[0].Status);
    }

    [Fact]
    public async Task UntakenDecisionBranch_DoesNotBlockImplicitJoin()
    {
        await using var fixture = await FlowchartRuntimeFixture.CreateAsync([
            "actexec-flowchart",
            "actexec-a",
            "actexec-b",
            "actexec-d"
        ]);
        var executable = fixture.NewExecutable(
            children:
            [
                fixture.NewProbeNode("node-a", ["Left"]),
                fixture.NewProbeNode("node-b"),
                fixture.NewProbeNode("node-c"),
                fixture.NewProbeNode("node-d")
            ],
            connections:
            [
                fixture.NewConnection("node-a", "node-b", "Left"),
                fixture.NewConnection("node-a", "node-c", "Right"),
                fixture.NewConnection("node-b", "node-d"),
                fixture.NewConnection("node-c", "node-d")
            ],
            startNodeId: "node-a");

        await fixture.ExecuteAsync(executable);

        var states = await fixture.Provider.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync("wfexec-1");
        Assert.DoesNotContain(states, state => state.Execution.ExecutableNodeId == "node-c");
        Assert.Single(states.Where(state => state.Execution.ExecutableNodeId == "node-d"));
        Assert.Equal(WorkflowExecutionStatus.Completed, (await fixture.Provider.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync("wfexec-1"))?.Status);
    }

    [Fact]
    public async Task WaitingJoin_ReleasesWhenRemainingActiveBranchTakesTerminalOutcome()
    {
        await using var fixture = await FlowchartRuntimeFixture.CreateAsync([
            "actexec-flowchart",
            "actexec-a",
            "actexec-b",
            "actexec-c",
            "actexec-e",
            "actexec-d"
        ]);
        var executable = fixture.NewExecutable(
            children:
            [
                fixture.NewProbeNode("node-a"),
                fixture.NewProbeNode("node-b"),
                fixture.NewProbeNode("node-c", ["Skip"]),
                fixture.NewProbeNode("node-d"),
                fixture.NewProbeNode("node-e")
            ],
            connections:
            [
                fixture.NewConnection("node-a", "node-b"),
                fixture.NewConnection("node-a", "node-c"),
                fixture.NewConnection("node-b", "node-d"),
                fixture.NewConnection("node-c", "node-d", "Join"),
                fixture.NewConnection("node-c", "node-e", "Skip")
            ],
            startNodeId: "node-a");

        await fixture.ExecuteAsync(executable);

        var states = await fixture.Provider.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync("wfexec-1");
        Assert.Single(states.Where(state => state.Execution.ExecutableNodeId == "node-d"));
        Assert.Single(states.Where(state => state.Execution.ExecutableNodeId == "node-e"));
        Assert.Equal(WorkflowExecutionStatus.Completed, (await fixture.Provider.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync("wfexec-1"))?.Status);
    }
}
