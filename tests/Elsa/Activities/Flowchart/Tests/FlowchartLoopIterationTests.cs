using Elsa.Activities.Flowchart.Contracts;
using Elsa.Activities.Flowchart.Exceptions;
using Elsa.Activities.Flowchart.Internal.Policies;
using Elsa.Activities.Flowchart.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Flowchart.Tests;

public sealed class FlowchartLoopIterationTests
{
    [Fact]
    public async Task BackwardPolicyRoute_CreatesLoopIterationScope()
    {
        var loopPolicy = new LoopOncePolicy();
        await using var fixture = await FlowchartRuntimeFixture.CreateAsync(
            ["actexec-flowchart", "actexec-a1", "actexec-b1", "actexec-a2", "actexec-b2", "actexec-c"],
            services => services.AddSingleton<IFlowchartPolicy>(loopPolicy));
        var executable = fixture.NewExecutable(
            children:
            [
                fixture.NewProbeNode("node-a"),
                fixture.NewProbeNode("node-b"),
                fixture.NewProbeNode("node-c")
            ],
            connections:
            [
                fixture.NewConnection("node-a", "node-b"),
                fixture.NewConnection("node-b", "node-a", "again"),
                fixture.NewConnection("node-b", "node-c", "done")
            ],
            startNodeId: "node-a",
            nodeMetadata: new Dictionary<string, FlowchartNodeMetadata>
            {
                ["node-b"] = new(LoopOncePolicy.Kind)
            });

        await fixture.ExecuteAsync(executable);

        var state = await fixture.GetFlowchartStateAsync();
        var activityStates = await fixture.Provider.GetRequiredService<IActivityExecutionStateStore>().ListAsync("wfexec-1");
        // W32: the loop-iteration scope minted on the backward edge to node-a is pruned once the run
        // completes (no live path references it). Its durable evidence is the monotonic per-owner counter,
        // which records that exactly one loopback iteration was created for node-a.
        Assert.Equal(1, state.LoopIterationCounters["node-a"]);
        Assert.Single(activityStates.Where(activityState => activityState.Execution.ExecutableNodeId == "node-c"));
    }

    [Fact]
    public async Task LoopIterationKey_PreventsStaleJoinArrivalsFromSatisfyingLaterIteration()
    {
        var loopPolicy = new LoopOncePolicy();
        await using var fixture = await FlowchartRuntimeFixture.CreateAsync(
            ["actexec-flowchart", "actexec-a1", "actexec-b1", "actexec-a2", "actexec-b2", "actexec-c"],
            services => services.AddSingleton<IFlowchartPolicy>(loopPolicy));
        var executable = fixture.NewExecutable(
            children:
            [
                fixture.NewProbeNode("node-a"),
                fixture.NewProbeNode("node-b"),
                fixture.NewProbeNode("node-c")
            ],
            connections:
            [
                fixture.NewConnection("node-a", "node-b"),
                fixture.NewConnection("node-b", "node-a", "again"),
                fixture.NewConnection("node-b", "node-c", "done")
            ],
            startNodeId: "node-a",
            nodeMetadata: new Dictionary<string, FlowchartNodeMetadata>
            {
                ["node-b"] = new(LoopOncePolicy.Kind)
            });

        await fixture.ExecuteAsync(executable);

        var state = await fixture.GetFlowchartStateAsync();
        // #382 / W32: completed iteration paths and their now-unreferenced loop-iteration scopes are pruned
        // from the persisted blob; the iteration identity evidence lives in the monotonic per-owner counter.
        // No Waiting path may remain — a stale first-iteration arrival must not have satisfied the later
        // iteration's join.
        Assert.Equal(1, state.LoopIterationCounters["node-a"]);
        Assert.DoesNotContain(state.ExecutionPaths, path => path.Status == ExecutionPathStatus.Waiting);
    }

    [Fact]
    public async Task AmbiguousLoopbackValidation_RejectsLoopbackIntoMultiInboundTarget()
    {
        await using var fixture = await FlowchartRuntimeFixture.CreateAsync(["actexec-flowchart"]);
        var executable = fixture.NewExecutable(
            children:
            [
                fixture.NewProbeNode("node-a"),
                fixture.NewProbeNode("node-b"),
                fixture.NewProbeNode("node-c")
            ],
            connections:
            [
                fixture.NewConnection("node-a", "node-b"),
                fixture.NewConnection("node-b", "node-c"),
                fixture.NewConnection("node-c", "node-b")
            ],
            startNodeId: "node-a");

        await fixture.ExecuteAsync(executable);

        var states = await fixture.Provider.GetRequiredService<IActivityExecutionStateStore>().ListAsync("wfexec-1");
        Assert.Contains(states, state => state.Execution.ExecutableNodeId == "node-flowchart" && state.FaultCount > 0);
    }

    private sealed class LoopOncePolicy : IFlowchartPolicy
    {
        public const string Kind = "test/loop-once";
        private int _calls;

        public string PolicyKind => Kind;
        public string DisplayName => "Loop Once";

        public FlowchartPolicyDecision Execute(IFlowchartPolicyContext context)
        {
            var target = _calls++ == 0 ? "node-a" : "node-c";
            return new([
                new FlowchartPolicyCommand(FlowchartPolicyCommandKind.ScheduleNode, nodeId: target)
            ]);
        }
    }
}
