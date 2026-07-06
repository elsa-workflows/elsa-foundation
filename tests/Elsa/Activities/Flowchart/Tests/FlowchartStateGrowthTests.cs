using Elsa.Activities.Flowchart.Contracts;
using Elsa.Activities.Flowchart.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Flowchart.Tests;

/// <summary>
/// #382 / W32: the persisted <see cref="FlowchartExecutionState"/> blob must not grow O(n) with loop
/// iterations. Consumed arrivals and unreachable terminal paths are pruned on save, diagnostics are
/// capped, and stale loop-iteration scopes are pruned too — iteration-key numbering now derives from the
/// explicit monotonic <see cref="FlowchartExecutionState.LoopIterationCounters"/> per owner node, not
/// from the live scope count, so old scopes are prunable without ever letting a later iteration reuse an
/// earlier key. The counter therefore witnesses that every iteration got a distinct key even though the
/// scopes themselves stay bounded. The cap value (200) is deliberately hard-coded here rather than
/// referencing the engine constant, so reverting the engine fix makes this test fail instead of not
/// compile.
/// </summary>
public sealed class FlowchartStateGrowthTests
{
    private const int Iterations = 100;

    [Fact]
    public async Task LoopHeavyFlowchart_PersistedStateStaysBounded()
    {
        var loopPolicy = new LoopNPolicy(Iterations);
        var ids = new List<string> { "actexec-flowchart" };
        for (var i = 0; i <= Iterations; i++)
        {
            ids.Add($"actexec-a{i}");
            ids.Add($"actexec-b{i}");
        }
        ids.Add("actexec-c");

        await using var fixture = await FlowchartRuntimeFixture.CreateAsync(ids, services =>
        {
            services.AddSingleton<IFlowchartPolicy>(loopPolicy);
            // A 100-iteration loop needs more drain cycles than the default 64; the limit exists to
            // catch runaway drains, not to bound legitimate long loops.
            services.AddSingleton(new WorkflowDrainOrchestratorOptions(maxDrainCycles: 4096));
        });
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
                ["node-b"] = new(LoopNPolicy.Kind)
            });

        await fixture.ExecuteAsync(executable);

        var state = await fixture.GetFlowchartStateAsync();

        // Loop semantics preserved: the loop body really ran once per iteration plus the initial pass,
        // and the post-loop node ran exactly once.
        var activityStates = await fixture.Provider.GetRequiredService<IActivityExecutionStateStore>().ListAsync("wfexec-1");
        Assert.Equal(Iterations + 1, activityStates.Count(activityState => activityState.Execution.ExecutableNodeId == "node-a"));
        Assert.Single(activityStates.Where(activityState => activityState.Execution.ExecutableNodeId == "node-c"));

        // Iteration numbering survives pruning: the monotonic per-owner counter climbed once per loopback
        // (node-a is the loop owner reached by the backward edge), proving no iteration key was ever reused
        // even though the scopes that carried those keys have been pruned away.
        Assert.Equal(Iterations, state.LoopIterationCounters["node-a"]);

        // Bounded persisted state: consumed arrivals pruned, terminal paths pruned (root retained),
        // stale loop-iteration scopes pruned, diagnostics capped — none of these may scale with Iterations.
        Assert.DoesNotContain(state.Arrivals, arrival => arrival.Status == FlowchartArrivalStatus.Consumed);
        Assert.True(state.ExecutionPaths.Count <= 4,
            $"Expected bounded ExecutionPaths after {Iterations} iterations, found {state.ExecutionPaths.Count}.");
        Assert.True(state.Scopes.Count(scope => scope.Kind == ExecutionScopeKind.LoopIteration) <= 4,
            $"Expected bounded LoopIteration scopes after {Iterations} iterations, found {state.Scopes.Count(scope => scope.Kind == ExecutionScopeKind.LoopIteration)}.");
        Assert.True(state.Diagnostics.Count <= 200,
            $"Expected Diagnostics capped at 200 after {Iterations} iterations, found {state.Diagnostics.Count}.");
    }

    private sealed class LoopNPolicy(int iterations) : IFlowchartPolicy
    {
        public const string Kind = "test/loop-n";
        private int _calls;

        public string PolicyKind => Kind;
        public string DisplayName => "Loop N";

        public FlowchartPolicyDecision Execute(IFlowchartPolicyContext context)
        {
            var target = _calls++ < iterations ? "node-a" : "node-c";
            return new([
                new FlowchartPolicyCommand(FlowchartPolicyCommandKind.ScheduleNode, nodeId: target)
            ]);
        }
    }
}
