using Kernel.Graph.Models;
using Xunit;
using static Kernel.Graph.Tests.GraphHarness;

namespace Kernel.Graph.Tests;

/// <summary>
/// Main's determinism byte-lock for the dead-path join (<c>FlowchartDeadPathDeterminismTests</c>), with main's expected
/// blob and diagnostic sequence copied unchanged: a loop iteration, a named-outcome decision and a fork with an
/// implicit join in one graph. Every record id derives from <see cref="FlowchartExecutionState.Sequence"/>, so a
/// byte-identical blob says the extracted engine makes the same state mutations as main's, in the same order.
/// </summary>
public sealed class FlowchartDeadPathDeterminismTests
{
    [Fact]
    public void MultiFeatureRun_PersistedBlobIsByteIdentical()
    {
        var run = RunMultiFeature();

        Assert.Equal(ExpectedBlob, run.RawState);
    }

    [Fact]
    public void MultiFeatureRun_DiagnosticIdKindSequenceIsStable()
    {
        var run = RunMultiFeature();

        Assert.NotNull(run.FlowchartState);
        var sequence = run.FlowchartState.Diagnostics.Select(diagnostic => $"{diagnostic.DiagnosticId}:{(int)diagnostic.Kind}").ToArray();
        Assert.Equal(ExpectedDiagnosticSequence, sequence);
    }

    private const string ExpectedBlob =
        """{"rootExecutionScopeId":"scope:root","scopes":[{"executionScopeId":"scope:root","kind":0,"parentExecutionScopeId":null,"createdByNodeId":null,"startConnectionId":null,"ownerNodeId":"node-a","loopIterationKey":null,"status":0,"metadata":{}},{"executionScopeId":"scope:12","kind":3,"parentExecutionScopeId":"scope:root","createdByNodeId":"node-b","startConnectionId":"node-b:again-\u003Enode-a","ownerNodeId":"node-a","loopIterationKey":"node-a:1","status":0,"metadata":{}}],"executionPaths":[{"executionPathId":"path:root","parentExecutionPathId":null,"executionScopeId":"scope:root","currentNodeId":"node-a","incomingConnectionId":null,"schedulingActivityExecutionId":null,"status":2,"iterationKey":null,"lastOutcomeNames":["Done"]},{"executionPathId":"path:99","parentExecutionPathId":null,"executionScopeId":"scope:12","currentNodeId":"node-f","incomingConnectionId":"node-e:Done-\u003Enode-f","schedulingActivityExecutionId":"actexec-e","status":0,"iterationKey":"node-a:1","lastOutcomeNames":[]}],"arrivals":[],"activeChildren":[{"childActivityExecutionId":null,"nodeId":"node-f","executionPathId":"path:99","executionScopeId":"scope:12","schedulingCause":"join"}],"diagnostics":[{"diagnosticId":"diag:8","kind":0,"nodeId":"node-b","connectionId":"node-a:Done-\u003Enode-b","executionPathId":"path:7","executionScopeId":"scope:root","message":"Flowchart scheduled node \u0027node-b\u0027.","details":{}},{"diagnosticId":"diag:13","kind":4,"nodeId":"node-a","connectionId":"node-b:again-\u003Enode-a","executionPathId":"path:7","executionScopeId":"scope:12","message":"Flowchart created loop iteration scope \u0027scope:12\u0027 for loopback to \u0027node-a\u0027.","details":{}},{"diagnosticId":"diag:19","kind":0,"nodeId":"node-a","connectionId":"node-b:again-\u003Enode-a","executionPathId":"path:18","executionScopeId":"scope:12","message":"Flowchart scheduled node \u0027node-a\u0027.","details":{}},{"diagnosticId":"diag:23","kind":3,"nodeId":"node-c","connectionId":"node-b:done-\u003Enode-c","executionPathId":"path:21","executionScopeId":"scope:root","message":"Flowchart marked connection \u0027node-b:done-\u003Enode-c\u0027 dead because node \u0027node-b\u0027 completed without taking it.","details":{}},{"diagnosticId":"diag:26","kind":3,"nodeId":"node-c","connectionId":null,"executionPathId":null,"executionScopeId":"scope:root","message":"Flowchart did not schedule node \u0027node-c\u0027: all 1 inbound arrival(s) were dead, so the deadness carries on to its successors.","details":{}},{"diagnosticId":"diag:29","kind":3,"nodeId":"node-d","connectionId":"node-c:Done-\u003Enode-d","executionPathId":"path:27","executionScopeId":"scope:root","message":"Flowchart marked connection \u0027node-c:Done-\u003Enode-d\u0027 dead because node \u0027node-c\u0027 completed without taking it.","details":{}},{"diagnosticId":"diag:32","kind":3,"nodeId":"node-d","connectionId":null,"executionPathId":null,"executionScopeId":"scope:root","message":"Flowchart did not schedule node \u0027node-d\u0027: all 1 inbound arrival(s) were dead, so the deadness carries on to its successors.","details":{}},{"diagnosticId":"diag:35","kind":3,"nodeId":"node-f","connectionId":"node-d:Done-\u003Enode-f","executionPathId":"path:33","executionScopeId":"scope:root","message":"Flowchart marked connection \u0027node-d:Done-\u003Enode-f\u0027 dead because node \u0027node-d\u0027 completed without taking it.","details":{}},{"diagnosticId":"diag:38","kind":3,"nodeId":"node-e","connectionId":"node-c:Done-\u003Enode-e","executionPathId":"path:36","executionScopeId":"scope:root","message":"Flowchart marked connection \u0027node-c:Done-\u003Enode-e\u0027 dead because node \u0027node-c\u0027 completed without taking it.","details":{}},{"diagnosticId":"diag:41","kind":3,"nodeId":"node-e","connectionId":null,"executionPathId":null,"executionScopeId":"scope:root","message":"Flowchart did not schedule node \u0027node-e\u0027: all 1 inbound arrival(s) were dead, so the deadness carries on to its successors.","details":{}},{"diagnosticId":"diag:44","kind":3,"nodeId":"node-f","connectionId":"node-e:Done-\u003Enode-f","executionPathId":"path:42","executionScopeId":"scope:root","message":"Flowchart marked connection \u0027node-e:Done-\u003Enode-f\u0027 dead because node \u0027node-e\u0027 completed without taking it.","details":{}},{"diagnosticId":"diag:49","kind":3,"nodeId":"node-f","connectionId":null,"executionPathId":null,"executionScopeId":"scope:root","message":"Flowchart did not schedule node \u0027node-f\u0027: all 2 inbound arrival(s) were dead, so the deadness carries on to its successors.","details":{}},{"diagnosticId":"diag:57","kind":0,"nodeId":"node-b","connectionId":"node-a:Done-\u003Enode-b","executionPathId":"path:56","executionScopeId":"scope:12","message":"Flowchart scheduled node \u0027node-b\u0027.","details":{}},{"diagnosticId":"diag:66","kind":0,"nodeId":"node-c","connectionId":"node-b:done-\u003Enode-c","executionPathId":"path:65","executionScopeId":"scope:12","message":"Flowchart scheduled node \u0027node-c\u0027.","details":{}},{"diagnosticId":"diag:68","kind":3,"nodeId":"node-a","connectionId":"node-b:again-\u003Enode-a","executionPathId":null,"executionScopeId":"scope:12","message":"Flowchart absorbed the dead path over loop-closing connection \u0027node-b:again-\u003Enode-a\u0027: a dead path never crosses into the next iteration.","details":{}},{"diagnosticId":"diag:76","kind":0,"nodeId":"node-d","connectionId":"node-c:Done-\u003Enode-d","executionPathId":"path:75","executionScopeId":"scope:12","message":"Flowchart scheduled node \u0027node-d\u0027.","details":{}},{"diagnosticId":"diag:83","kind":0,"nodeId":"node-e","connectionId":"node-c:Done-\u003Enode-e","executionPathId":"path:82","executionScopeId":"scope:12","message":"Flowchart scheduled node \u0027node-e\u0027.","details":{}},{"diagnosticId":"diag:90","kind":1,"nodeId":"node-f","connectionId":"node-d:Done-\u003Enode-f","executionPathId":"path:87","executionScopeId":"scope:12","message":"Flowchart path \u0027path:87\u0027 is waiting at implicit join \u0027node-f\u0027.","details":{}},{"diagnosticId":"diag:100","kind":2,"nodeId":"node-f","connectionId":"node-e:Done-\u003Enode-f","executionPathId":"path:99","executionScopeId":"scope:12","message":"Implicit join \u0027node-f\u0027 fired after 2 active arrival(s).","details":{}}],"sequence":102,"loopIterationCounters":{"node-a":1}}""";

    private static readonly string[] ExpectedDiagnosticSequence =
    [
    "diag:8:0", "diag:13:4", "diag:19:0", "diag:23:3", "diag:26:3", "diag:29:3", "diag:32:3", "diag:35:3",
    "diag:38:3", "diag:41:3", "diag:44:3", "diag:49:3", "diag:57:0", "diag:66:0", "diag:68:3", "diag:76:0",
    "diag:83:0", "diag:90:1", "diag:100:2"
    ];

    private static GraphRun RunMultiFeature()
    {
        var loop = new ScriptedPolicy("determinism/loop", "node-a", "node-c");
        return Run(null,
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
            nodeMetadata: new Dictionary<string, FlowchartNodeMetadata> { ["node-b"] = new(loop.PolicyKind) },
            executionIds:
            [
                "actexec-flowchart",
                "actexec-a1", "actexec-b1",
                "actexec-a2", "actexec-b2",
                "actexec-c", "actexec-d", "actexec-e", "actexec-f"
            ],
            policies: [loop]);
    }
}
