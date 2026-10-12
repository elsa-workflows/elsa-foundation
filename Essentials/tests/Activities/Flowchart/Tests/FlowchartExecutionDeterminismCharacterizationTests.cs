using Elsa.Activities.Flowchart.Contracts;
using Elsa.Activities.Flowchart.Internal;
using Elsa.Activities.Flowchart.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Flowchart.Tests;

/// <summary>
/// Characterization byte-locks for the <see cref="FlowchartExecutionEngine"/> decomposition (W30a / #275).
/// <para>
/// The engine derives every record id (<c>path:N</c>, <c>scope:N</c>, <c>arrival:N</c>, <c>diag:N</c>) from
/// <see cref="FlowchartExecutionState.Sequence"/>, which is bumped by nearly every state builder. The exact ids
/// the persisted blob carries are therefore a pure function of the order and count of state mutations. A pure
/// decomposition that reorders, adds, or drops a single <c>Sequence</c>-bumping operation would silently corrupt
/// that id stream. These tests pin the id stream end-to-end over a live runtime run so any such drift fails
/// loudly. They are written to be green on the pre-refactor engine (green-on-revert), before any extraction.
/// </para>
/// <list type="bullet">
/// <item><b>CT-1</b> — full persisted-blob byte-lock over a multi-feature run (a backward loop iteration,
/// a named-outcome decision, and a fork/implicit-join), the strongest guard: it pins the whole id sequence.</item>
/// <item><b>CT-2</b> — the ordered <c>(DiagnosticId, Kind)</c> sequence for that same multi-feature run.</item>
/// <item><b>CT-3</b> — first-wins race scope-id determinism plus the losing-path cancellation diagnostics.</item>
/// </list>
/// </summary>
public sealed class FlowchartExecutionDeterminismCharacterizationTests
{
    /// <summary>
    /// These byte-locks were frozen against the pre-decomposition engine and their value is that they stay
    /// green on a revert of #275. ADR 0064 moved the shipping default to the dead-path join, which emits
    /// arrivals on untaken edges and so legitimately mints a different id stream — re-freezing them under it
    /// would quietly retire the green-on-revert property they exist for. They stay pinned to the semantics
    /// they characterize; <see cref="FlowchartDeadPathDeterminismTests"/> carries the equivalent lock for the
    /// default.
    /// </summary>
    private static void PinReachabilityJoin(IServiceCollection services) =>
        services.AddSingleton(new FlowchartOptions { JoinPolicyKind = Internal.Policies.FlowchartPolicyKinds.ReachabilityJoin });

    // CT-1: byte-identical persisted blob for the multi-feature run below. Any reordered/added/dropped
    // Sequence bump corrupts an id and fails this. The > (>) and ' (') escapes are the Web
    // serializer's own output — pinned literally. Re-frozen after the backward-edge fix (DFS back-edge
    // classification): only the genuine loop-closing edge node-b →("again") node-a is a backward edge, so a
    // single loop-iteration scope (scope:12, owner node-a) is minted for the one loopback. The forward edge
    // node-a → node-b no longer (wrongly) mints an owner "node-b" iteration scope on each pass, so the
    // W32-completed loop-iteration scope is pruned and loopIterationCounters carries only "node-a":1. The
    // path/scope/arrival/diag id stream through sequence:72 is a pure function of the corrected mutation count.
    // Terminal execution closes private state by contract, so the final in-memory-only completion diagnostics are
    // deliberately outside this persisted byte lock.
    private const string MultiFeatureGoldenJson =
        """{"rootExecutionScopeId":"scope:root","scopes":[{"executionScopeId":"scope:root","kind":0,"parentExecutionScopeId":null,"createdByNodeId":null,"startConnectionId":null,"ownerNodeId":"node-a","loopIterationKey":null,"status":0,"metadata":{}},{"executionScopeId":"scope:12","kind":3,"parentExecutionScopeId":"scope:root","createdByNodeId":"node-b","startConnectionId":"node-b:again-\u003Enode-a","ownerNodeId":"node-a","loopIterationKey":"node-a:1","status":0,"metadata":{}}],"executionPaths":[{"executionPathId":"path:root","parentExecutionPathId":null,"executionScopeId":"scope:root","currentNodeId":"node-a","incomingConnectionId":null,"schedulingActivityExecutionId":null,"status":2,"iterationKey":null,"lastOutcomeNames":["Done"]},{"executionPathId":"path:69","parentExecutionPathId":null,"executionScopeId":"scope:12","currentNodeId":"node-f","incomingConnectionId":"node-e:Done-\u003Enode-f","schedulingActivityExecutionId":"actexec-e","status":0,"iterationKey":"node-a:1","lastOutcomeNames":[]}],"arrivals":[],"activeChildren":[{"childActivityExecutionId":null,"nodeId":"node-f","executionPathId":"path:69","executionScopeId":"scope:12","schedulingCause":"join"}],"diagnostics":[{"diagnosticId":"diag:8","kind":0,"nodeId":"node-b","connectionId":"node-a:Done-\u003Enode-b","executionPathId":"path:7","executionScopeId":"scope:root","message":"Flowchart scheduled node \u0027node-b\u0027.","details":{}},{"diagnosticId":"diag:13","kind":4,"nodeId":"node-a","connectionId":"node-b:again-\u003Enode-a","executionPathId":"path:7","executionScopeId":"scope:12","message":"Flowchart created loop iteration scope \u0027scope:12\u0027 for loopback to \u0027node-a\u0027.","details":{}},{"diagnosticId":"diag:19","kind":0,"nodeId":"node-a","connectionId":"node-b:again-\u003Enode-a","executionPathId":"path:18","executionScopeId":"scope:12","message":"Flowchart scheduled node \u0027node-a\u0027.","details":{}},{"diagnosticId":"diag:28","kind":0,"nodeId":"node-b","connectionId":"node-a:Done-\u003Enode-b","executionPathId":"path:27","executionScopeId":"scope:12","message":"Flowchart scheduled node \u0027node-b\u0027.","details":{}},{"diagnosticId":"diag:37","kind":0,"nodeId":"node-c","connectionId":"node-b:done-\u003Enode-c","executionPathId":"path:36","executionScopeId":"scope:12","message":"Flowchart scheduled node \u0027node-c\u0027.","details":{}},{"diagnosticId":"diag:46","kind":0,"nodeId":"node-d","connectionId":"node-c:Done-\u003Enode-d","executionPathId":"path:45","executionScopeId":"scope:12","message":"Flowchart scheduled node \u0027node-d\u0027.","details":{}},{"diagnosticId":"diag:53","kind":0,"nodeId":"node-e","connectionId":"node-c:Done-\u003Enode-e","executionPathId":"path:52","executionScopeId":"scope:12","message":"Flowchart scheduled node \u0027node-e\u0027.","details":{}},{"diagnosticId":"diag:60","kind":1,"nodeId":"node-f","connectionId":"node-d:Done-\u003Enode-f","executionPathId":"path:57","executionScopeId":"scope:12","message":"Flowchart path \u0027path:57\u0027 is waiting at implicit join \u0027node-f\u0027.","details":{}},{"diagnosticId":"diag:70","kind":2,"nodeId":"node-f","connectionId":"node-e:Done-\u003Enode-f","executionPathId":"path:69","executionScopeId":"scope:12","message":"Implicit join \u0027node-f\u0027 fired after 2 active arrival(s).","details":{}}],"sequence":72,"loopIterationCounters":{"node-a":1}}""";

    [Fact]
    public async Task CT1_MultiFeatureRun_PersistedBlobIsByteIdentical()
    {
        var (fixture, executable) = await BuildMultiFeatureRunAsync();
        await using var _ = fixture;

        await fixture.ExecuteAsync(executable);

        var serialized = await fixture.GetRawFlowchartStateAsync();
        Assert.Equal(MultiFeatureGoldenJson, serialized);
    }

    [Fact]
    public async Task CT2_ForkJoinLoopRun_DiagnosticIdKindSequenceIsStable()
    {
        var (fixture, executable) = await BuildMultiFeatureRunAsync();
        await using var _ = fixture;

        await fixture.ExecuteAsync(executable);

        var state = await fixture.GetFlowchartStateAsync();
        var sequence = state.Diagnostics.Select(diagnostic => $"{diagnostic.DiagnosticId}:{(int)diagnostic.Kind}").ToArray();
        Assert.Equal(ExpectedDiagnosticSequence, sequence);
    }

    private static readonly string[] ExpectedDiagnosticSequence =
    [
        "diag:8:0", "diag:13:4", "diag:19:0", "diag:28:0", "diag:37:0", "diag:46:0",
        "diag:53:0", "diag:60:1", "diag:70:2"
    ];

    [Fact]
    public async Task CT3_FirstWinsRace_ScopeIdAndLosingCancellationsAreDeterministic()
    {
        // Uses the built-in FirstWins policy (registered by the feature) — node-a forks to node-b and
        // node-c under a first-wins race; the first branch to reach the reconvergence at node-d wins and
        // the losing branch is canceled. The race scope id and the losing-path cancellation diagnostic ids
        // are Sequence-derived, so this pins that a reordered scope/diagnostic bump can't drift them.
        await using var fixture = await FlowchartRuntimeFixture.CreateAsync(
            ["actexec-flowchart", "actexec-a", "actexec-b", "actexec-c", "actexec-d"],
            PinReachabilityJoin);
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
            startNodeId: "node-a",
            nodeMetadata: new Dictionary<string, FlowchartNodeMetadata>
            {
                ["node-a"] = new(Internal.Policies.FlowchartPolicyKinds.FirstWins)
            });

        await fixture.ExecuteAsync(executable);

        var state = await fixture.GetFlowchartStateAsync();
        var raceScopeIds = state.Scopes.Where(scope => scope.Kind == ExecutionScopeKind.Race).Select(scope => scope.ExecutionScopeId).ToArray();
        var canceledDiagnostics = state.Diagnostics.Where(diagnostic => diagnostic.Kind == FlowchartDiagnosticKind.Canceled).Select(diagnostic => diagnostic.DiagnosticId).ToArray();
        Assert.Equal(ExpectedRaceScopeIds, raceScopeIds);
        Assert.Equal(ExpectedRaceCancellationDiagnosticIds, canceledDiagnostics);
    }

    private static readonly string[] ExpectedRaceScopeIds = ["scope:3"];
    private static readonly string[] ExpectedRaceCancellationDiagnosticIds = ["diag:21", "diag:33"];

    /// <summary>
    /// A single flowchart exercising three engine features in one run. node-a is the loop head (single
    /// inbound — the backward edge only). node-b is a policy node that loops back to node-a once ("again"),
    /// then on its second pass takes the named "done" outcome to node-c (a decision). node-c then forks to
    /// node-d and node-e, which reconverge at node-f (an implicit join). One run therefore spans a
    /// loop-iteration scope, a named-outcome decision, and a fork/implicit-join — so the persisted blob's
    /// id stream (path/scope/arrival/diag) covers all three feature families.
    /// </summary>
    private static async Task<(FlowchartRuntimeFixture Fixture, Elsa.Workflows.Runtime.Core.Models.WorkflowExecutable Executable)> BuildMultiFeatureRunAsync()
    {
        var loopPolicy = new LoopOnceThenDecidePolicy();
        var fixture = await FlowchartRuntimeFixture.CreateAsync(
            [
                "actexec-flowchart",
                "actexec-a1", "actexec-b1",
                "actexec-a2", "actexec-b2",
                "actexec-c", "actexec-d", "actexec-e", "actexec-f"
            ],
            services =>
            {
                services.AddSingleton<IFlowchartPolicy>(loopPolicy);
                PinReachabilityJoin(services);
            });
        var executable = fixture.NewExecutable(
            children:
            [
                fixture.NewProbeNode("node-a"),
                fixture.NewProbeNode("node-b"),
                fixture.NewProbeNode("node-c"),
                fixture.NewProbeNode("node-d"),
                fixture.NewProbeNode("node-e"),
                fixture.NewProbeNode("node-f")
            ],
            connections:
            [
                fixture.NewConnection("node-a", "node-b"),
                fixture.NewConnection("node-b", "node-a", "again"),
                fixture.NewConnection("node-b", "node-c", "done"),
                fixture.NewConnection("node-c", "node-d"),
                fixture.NewConnection("node-c", "node-e"),
                fixture.NewConnection("node-d", "node-f"),
                fixture.NewConnection("node-e", "node-f")
            ],
            startNodeId: "node-a",
            nodeMetadata: new Dictionary<string, FlowchartNodeMetadata>
            {
                ["node-b"] = new(LoopOnceThenDecidePolicy.Kind)
            });

        return (fixture, executable);
    }

    private sealed class LoopOnceThenDecidePolicy : IFlowchartPolicy
    {
        public const string Kind = "test/loop-once-then-decide";
        private int _calls;

        public string PolicyKind => Kind;
        public string DisplayName => "Loop Once Then Decide";

        public FlowchartPolicyDecision Execute(IFlowchartPolicyContext context)
        {
            var target = _calls++ == 0 ? "node-a" : "node-c";
            return new([
                new FlowchartPolicyCommand(FlowchartPolicyCommandKind.ScheduleNode, nodeId: target)
            ]);
        }
    }
}
