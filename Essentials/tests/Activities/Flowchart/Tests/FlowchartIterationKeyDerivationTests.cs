using Elsa.Activities.Flowchart.Internal.Policies;
using Elsa.Activities.Flowchart.Models;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;
using static Elsa.Activities.Flowchart.Tests.FlowchartConformanceCorpus;

namespace Elsa.Activities.Flowchart.Tests;

/// <summary>
/// ADR 0064 WU-4: a path's loop-iteration identity is derived from the scope tree — the
/// <see cref="ExecutionScope.LoopIterationKey"/> of its nearest enclosing
/// <see cref="ExecutionScopeKind.LoopIteration"/> ancestor — rather than copied down from the path that
/// emitted it. Two records of one fact can disagree; one cannot.
/// <para>
/// The test asserts the invariant against the <b>persisted</b> state rather than against the derivation code,
/// so it stays a statement about the durable model: whatever a path claims its iteration is, the scope tree
/// must say the same. The shape is chosen to be the one where a copy could drift — a first-wins race nested
/// inside a loop body, so the scope a path lives in is not itself the loop-iteration scope and the answer has
/// to come from walking up.
/// </para>
/// </summary>
public sealed class FlowchartIterationKeyDerivationTests
{
    [Theory]
    [MemberData(nameof(FlowchartConformanceCorpusTests.JoinPolicies), MemberType = typeof(FlowchartConformanceCorpusTests))]
    public async Task EveryPathsIterationKey_MatchesItsNearestEnclosingLoopIterationScope(string joinPolicyKind)
    {
        var gate = new ScriptedPolicy("iteration/gate", "node-head", "node-head", "node-end");
        var run = await RunAsync(joinPolicyKind, fixture => fixture.NewExecutable(
            children:
            [
                fixture.NewProbeNode("node-head"),
                fixture.NewProbeNode("node-fork"),
                fixture.NewProbeNode("node-b"),
                fixture.NewProbeNode("node-c"),
                fixture.NewProbeNode("node-join"),
                fixture.NewProbeNode("node-gate"),
                fixture.NewProbeNode("node-end")
            ],
            connections:
            [
                fixture.NewConnection("node-head", "node-fork"),
                fixture.NewConnection("node-fork", "node-b"),
                fixture.NewConnection("node-fork", "node-c"),
                fixture.NewConnection("node-b", "node-join"),
                fixture.NewConnection("node-c", "node-join"),
                fixture.NewConnection("node-join", "node-gate"),
                fixture.NewConnection("node-gate", "node-head", "again"),
                fixture.NewConnection("node-gate", "node-end", "done")
            ],
            startNodeId: "node-head",
            nodeMetadata: new Dictionary<string, FlowchartNodeMetadata>
            {
                ["node-fork"] = new(FlowchartPolicyKinds.FirstWins),
                ["node-gate"] = new(gate.PolicyKind)
            }),
            gate);

        Assert.Equal(WorkflowExecutionStatus.Completed, run.Status);
        Assert.NotNull(run.FlowchartState);
        var state = run.FlowchartState;

        // The shape this test needs actually occurred: a race scope parented to a loop-iteration scope, which
        // is the nesting where a copied key and the tree could drift apart.
        Assert.Contains(state.Scopes, scope =>
            scope.Kind == ExecutionScopeKind.Race &&
            state.Scopes.Any(parent =>
                StringComparer.Ordinal.Equals(parent.ExecutionScopeId, scope.ParentExecutionScopeId) &&
                parent.Kind == ExecutionScopeKind.LoopIteration));

        foreach (var path in state.ExecutionPaths)
            Assert.Equal(NearestLoopIterationKey(state, path.ExecutionScopeId), path.IterationKey);

        foreach (var arrival in state.Arrivals)
            Assert.Equal(NearestLoopIterationKey(state, arrival.ExecutionScopeId), arrival.IterationKey);

        // Numbering still comes from the monotonic per-owner counter, so deriving the key changed where it is
        // read from and not what it counts (#382 / W32).
        Assert.Equal(2, state.LoopIterationCounters["node-head"]);
    }

    private static string? NearestLoopIterationKey(FlowchartExecutionState state, string executionScopeId)
    {
        var scopeId = executionScopeId;
        for (var depth = 0; depth <= state.Scopes.Count; depth++)
        {
            var scope = state.Scopes.FirstOrDefault(item => StringComparer.Ordinal.Equals(item.ExecutionScopeId, scopeId));
            if (scope is null)
                return null;
            if (scope.Kind == ExecutionScopeKind.LoopIteration)
                return scope.LoopIterationKey;
            if (scope.ParentExecutionScopeId is not { } parentScopeId)
                return null;
            scopeId = parentScopeId;
        }

        return null;
    }
}
