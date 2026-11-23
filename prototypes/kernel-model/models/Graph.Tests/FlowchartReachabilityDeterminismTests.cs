using Kernel.Graph.Internal.Policies;
using Kernel.Graph.Models;
using Xunit;
using static Kernel.Graph.Tests.GraphHarness;

namespace Kernel.Graph.Tests;

/// <summary>
/// Main's characterization byte-locks pinned to the reachability join (<c>FlowchartExecutionDeterminismCharacterizationTests</c>,
/// CT-1 to CT-3), with main's golden blob, diagnostic sequence and race ids copied unchanged.
/// </summary>
public sealed class FlowchartReachabilityDeterminismTests
{
    private const string MultiFeatureGoldenJson =
        """{"rootExecutionScopeId":"scope:root","scopes":[{"executionScopeId":"scope:root","kind":0,"parentExecutionScopeId":null,"createdByNodeId":null,"startConnectionId":null,"ownerNodeId":"node-a","loopIterationKey":null,"status":0,"metadata":{}},{"executionScopeId":"scope:12","kind":3,"parentExecutionScopeId":"scope:root","createdByNodeId":"node-b","startConnectionId":"node-b:again-\u003Enode-a","ownerNodeId":"node-a","loopIterationKey":"node-a:1","status":0,"metadata":{}}],"executionPaths":[{"executionPathId":"path:root","parentExecutionPathId":null,"executionScopeId":"scope:root","currentNodeId":"node-a","incomingConnectionId":null,"schedulingActivityExecutionId":null,"status":2,"iterationKey":null,"lastOutcomeNames":["Done"]},{"executionPathId":"path:69","parentExecutionPathId":null,"executionScopeId":"scope:12","currentNodeId":"node-f","incomingConnectionId":"node-e:Done-\u003Enode-f","schedulingActivityExecutionId":"actexec-e","status":0,"iterationKey":"node-a:1","lastOutcomeNames":[]}],"arrivals":[],"activeChildren":[{"childActivityExecutionId":null,"nodeId":"node-f","executionPathId":"path:69","executionScopeId":"scope:12","schedulingCause":"join"}],"diagnostics":[{"diagnosticId":"diag:8","kind":0,"nodeId":"node-b","connectionId":"node-a:Done-\u003Enode-b","executionPathId":"path:7","executionScopeId":"scope:root","message":"Flowchart scheduled node \u0027node-b\u0027.","details":{}},{"diagnosticId":"diag:13","kind":4,"nodeId":"node-a","connectionId":"node-b:again-\u003Enode-a","executionPathId":"path:7","executionScopeId":"scope:12","message":"Flowchart created loop iteration scope \u0027scope:12\u0027 for loopback to \u0027node-a\u0027.","details":{}},{"diagnosticId":"diag:19","kind":0,"nodeId":"node-a","connectionId":"node-b:again-\u003Enode-a","executionPathId":"path:18","executionScopeId":"scope:12","message":"Flowchart scheduled node \u0027node-a\u0027.","details":{}},{"diagnosticId":"diag:28","kind":0,"nodeId":"node-b","connectionId":"node-a:Done-\u003Enode-b","executionPathId":"path:27","executionScopeId":"scope:12","message":"Flowchart scheduled node \u0027node-b\u0027.","details":{}},{"diagnosticId":"diag:37","kind":0,"nodeId":"node-c","connectionId":"node-b:done-\u003Enode-c","executionPathId":"path:36","executionScopeId":"scope:12","message":"Flowchart scheduled node \u0027node-c\u0027.","details":{}},{"diagnosticId":"diag:46","kind":0,"nodeId":"node-d","connectionId":"node-c:Done-\u003Enode-d","executionPathId":"path:45","executionScopeId":"scope:12","message":"Flowchart scheduled node \u0027node-d\u0027.","details":{}},{"diagnosticId":"diag:53","kind":0,"nodeId":"node-e","connectionId":"node-c:Done-\u003Enode-e","executionPathId":"path:52","executionScopeId":"scope:12","message":"Flowchart scheduled node \u0027node-e\u0027.","details":{}},{"diagnosticId":"diag:60","kind":1,"nodeId":"node-f","connectionId":"node-d:Done-\u003Enode-f","executionPathId":"path:57","executionScopeId":"scope:12","message":"Flowchart path \u0027path:57\u0027 is waiting at implicit join \u0027node-f\u0027.","details":{}},{"diagnosticId":"diag:70","kind":2,"nodeId":"node-f","connectionId":"node-e:Done-\u003Enode-f","executionPathId":"path:69","executionScopeId":"scope:12","message":"Implicit join \u0027node-f\u0027 fired after 2 active arrival(s).","details":{}}],"sequence":72,"loopIterationCounters":{"node-a":1}}""";

    private static readonly string[] ExpectedDiagnosticSequence =
    [
        "diag:8:0", "diag:13:4", "diag:19:0", "diag:28:0", "diag:37:0", "diag:46:0",
        "diag:53:0", "diag:60:1", "diag:70:2"
    ];

    [Fact]
    public void CT1_MultiFeatureRun_PersistedBlobIsByteIdentical() => Assert.Equal(MultiFeatureGoldenJson, RunMultiFeature().RawState);

    [Fact]
    public void CT2_ForkJoinLoopRun_DiagnosticIdKindSequenceIsStable()
    {
        var run = RunMultiFeature();

        Assert.NotNull(run.FlowchartState);
        Assert.Equal(ExpectedDiagnosticSequence, run.FlowchartState.Diagnostics.Select(diagnostic => $"{diagnostic.DiagnosticId}:{(int)diagnostic.Kind}").ToArray());
    }

    [Fact]
    public void CT3_FirstWinsRace_ScopeIdAndLosingCancellationsAreDeterministic()
    {
        var run = Run(FlowchartPolicyKinds.ReachabilityJoin,
            [new("node-a"), new("node-b"), new("node-c"), new("node-d")],
            [Connection("node-a", "node-b"), Connection("node-a", "node-c"), Connection("node-b", "node-d"), Connection("node-c", "node-d")],
            startNodeId: "node-a",
            nodeMetadata: new Dictionary<string, FlowchartNodeMetadata> { ["node-a"] = new(FlowchartPolicyKinds.FirstWins) },
            executionIds: ["actexec-flowchart", "actexec-a", "actexec-b", "actexec-c", "actexec-d"]);

        Assert.NotNull(run.FlowchartState);
        Assert.Equal(["scope:3"], run.FlowchartState.Scopes.Where(scope => scope.Kind == ExecutionScopeKind.Race).Select(scope => scope.ExecutionScopeId).ToArray());
        Assert.Equal(["diag:21", "diag:33"], run.FlowchartState.Diagnostics.Where(diagnostic => diagnostic.Kind == FlowchartDiagnosticKind.Canceled).Select(diagnostic => diagnostic.DiagnosticId).ToArray());
    }

    private static GraphRun RunMultiFeature() =>
        Run(FlowchartPolicyKinds.ReachabilityJoin,
            [new("node-a"), new("node-b"), new("node-c"), new("node-d"), new("node-e"), new("node-f")],
            [
                Connection("node-a", "node-b"),
                Connection("node-b", "node-a", "again"),
                Connection("node-b", "node-c", "done"),
                Connection("node-c", "node-d"),
                Connection("node-c", "node-e"),
                Connection("node-d", "node-f"),
                Connection("node-e", "node-f")
            ],
            startNodeId: "node-a",
            nodeMetadata: new Dictionary<string, FlowchartNodeMetadata> { ["node-b"] = new("test/loop-once-then-decide") },
            executionIds: ["actexec-flowchart", "actexec-a1", "actexec-b1", "actexec-a2", "actexec-b2", "actexec-c", "actexec-d", "actexec-e", "actexec-f"],
            policies: [new ScriptedPolicy("test/loop-once-then-decide", "node-a", "node-c")]);
}
