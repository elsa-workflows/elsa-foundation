using System.Text.Json;
using Elsa.Activities.Flowchart.Internal;
using Elsa.Activities.Flowchart.Models;
using Xunit;

namespace Elsa.Activities.Flowchart.Tests;

/// <summary>
/// Wire-safety lock for the persisted <see cref="FlowchartExecutionState"/> blob (#382 / W32, constitution
/// §E6). The engine serializes the state with <see cref="JsonSerializerDefaults.Web"/> and <b>no string-enum
/// converter</b>, so all four state enums are persisted as <b>ordinals</b>: the member order of
/// <see cref="ExecutionPathStatus"/>, <see cref="FlowchartArrivalStatus"/>, <see cref="ExecutionScopeKind"/>,
/// <see cref="ExecutionScopeStatus"/> and <see cref="FlowchartDiagnosticKind"/> is frozen wire surface, not
/// just the names. The golden fixture below pins property names, casing, and those numeric encodings,
/// including the W32 <see cref="FlowchartExecutionState.LoopIterationCounters"/> field (last serialized
/// member). It is deliberately built from an <b>unpruned</b> state (terminal paths, a consumed arrival) so
/// the round-trip test also proves any unpruned state deserializes losslessly — pruning narrows what is
/// written, never what can be read.
/// </summary>
public sealed class FlowchartExecutionStateGoldenTests
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private const string GoldenJson =
        """
        {"rootExecutionScopeId":"scope:root","scopes":[{"executionScopeId":"scope:root","kind":0,"parentExecutionScopeId":null,"createdByNodeId":null,"startConnectionId":null,"ownerNodeId":"node-start","loopIterationKey":null,"status":0,"metadata":{}},{"executionScopeId":"scope:branch","kind":1,"parentExecutionScopeId":"scope:root","createdByNodeId":"node-fork","startConnectionId":"conn:1","ownerNodeId":null,"loopIterationKey":null,"status":1,"metadata":{}},{"executionScopeId":"scope:join","kind":2,"parentExecutionScopeId":"scope:root","createdByNodeId":null,"startConnectionId":null,"ownerNodeId":null,"loopIterationKey":null,"status":2,"metadata":{}},{"executionScopeId":"scope:loop","kind":3,"parentExecutionScopeId":"scope:root","createdByNodeId":null,"startConnectionId":null,"ownerNodeId":"node-loop","loopIterationKey":"node-loop:1","status":3,"metadata":{"k":"v"}},{"executionScopeId":"scope:race","kind":4,"parentExecutionScopeId":null,"createdByNodeId":null,"startConnectionId":null,"ownerNodeId":null,"loopIterationKey":null,"status":4,"metadata":{}}],"executionPaths":[{"executionPathId":"path:root","parentExecutionPathId":null,"executionScopeId":"scope:root","currentNodeId":"node-start","incomingConnectionId":null,"schedulingActivityExecutionId":null,"status":0,"iterationKey":null,"lastOutcomeNames":[]},{"executionPathId":"path:2","parentExecutionPathId":"path:root","executionScopeId":"scope:root","currentNodeId":"node-join","incomingConnectionId":"conn:1","schedulingActivityExecutionId":"actexec-1","status":1,"iterationKey":"node-loop:1","lastOutcomeNames":["Done"]},{"executionPathId":"path:3","parentExecutionPathId":null,"executionScopeId":"scope:branch","currentNodeId":null,"incomingConnectionId":null,"schedulingActivityExecutionId":null,"status":2,"iterationKey":null,"lastOutcomeNames":[]},{"executionPathId":"path:4","parentExecutionPathId":null,"executionScopeId":"scope:branch","currentNodeId":null,"incomingConnectionId":null,"schedulingActivityExecutionId":null,"status":3,"iterationKey":null,"lastOutcomeNames":[]},{"executionPathId":"path:5","parentExecutionPathId":null,"executionScopeId":"scope:race","currentNodeId":null,"incomingConnectionId":null,"schedulingActivityExecutionId":null,"status":4,"iterationKey":null,"lastOutcomeNames":[]}],"arrivals":[{"arrivalId":"arrival:1","executionPathId":"path:2","executionScopeId":"scope:root","sourceNodeId":"node-a","targetNodeId":"node-join","connectionId":"conn:1","sourcePort":"Done","producingActivityExecutionId":"actexec-1","status":0,"iterationKey":"node-loop:1"},{"arrivalId":"arrival:2","executionPathId":"path:3","executionScopeId":"scope:branch","sourceNodeId":"node-b","targetNodeId":"node-join","connectionId":"conn:2","sourcePort":"Done","producingActivityExecutionId":"actexec-2","status":1,"iterationKey":null}],"activeChildren":[{"childActivityExecutionId":"actexec-0","nodeId":"node-start","executionPathId":"path:root","executionScopeId":"scope:root","schedulingCause":"start"}],"diagnostics":[{"diagnosticId":"diag:1","kind":0,"nodeId":"node-start","connectionId":null,"executionPathId":"path:root","executionScopeId":"scope:root","message":"scheduled","details":{}},{"diagnosticId":"diag:2","kind":2,"nodeId":"node-join","connectionId":"conn:1","executionPathId":null,"executionScopeId":null,"message":"joined","details":{}},{"diagnosticId":"diag:3","kind":4,"nodeId":"node-loop","connectionId":null,"executionPathId":null,"executionScopeId":null,"message":"loop","details":{}},{"diagnosticId":"diag:4","kind":8,"nodeId":null,"connectionId":null,"executionPathId":null,"executionScopeId":null,"message":"faulted","details":{"reason":"x"}}],"sequence":42,"loopIterationCounters":{"node-loop":1}}
        """;

    [Fact]
    public void StateMetadataKey_IsFrozenWireLiteral()
    {
        Assert.Equal("elsa.flowchart.executionState", FlowchartExecutionEngine.StateMetadataKey);
    }

    [Fact]
    public void SerializedState_MatchesGoldenFixture()
    {
        var actual = JsonSerializer.Serialize(NewCanonicalState(), SerializerOptions);

        Assert.Equal(GoldenJson, actual);
    }

    [Fact]
    public void GoldenFixture_RoundTripsLosslessly()
    {
        var deserialized = JsonSerializer.Deserialize<FlowchartExecutionState>(GoldenJson, SerializerOptions);

        Assert.NotNull(deserialized);
        Assert.Equal(GoldenJson, JsonSerializer.Serialize(deserialized, SerializerOptions));
    }

    [Fact]
    public void UnprunedBlob_DeserializesWithAllRecordsIntact()
    {
        // Pruning narrows what gets written, never what can be read: an unpruned state (full collections,
        // terminal paths, consumed arrivals) must load intact. Guards the read side so a resumed workflow
        // whose last checkpoint predates a given prune pass still deserializes without loss.
        var state = JsonSerializer.Deserialize<FlowchartExecutionState>(GoldenJson, SerializerOptions);

        Assert.NotNull(state);
        Assert.Equal("scope:root", state.RootExecutionScopeId);
        Assert.Equal(42, state.Sequence);
        Assert.Equal(5, state.Scopes.Count);
        Assert.Equal(5, state.ExecutionPaths.Count);
        Assert.Equal(2, state.Arrivals.Count);
        Assert.Single(state.ActiveChildren);
        Assert.Equal(4, state.Diagnostics.Count);
        // Terminal/consumed records are readable, not rejected.
        Assert.Contains(state.ExecutionPaths, path => path.Status == ExecutionPathStatus.Completed);
        Assert.Contains(state.ExecutionPaths, path => path.Status == ExecutionPathStatus.Canceled);
        Assert.Contains(state.ExecutionPaths, path => path.Status == ExecutionPathStatus.Faulted);
        Assert.Contains(state.Arrivals, arrival => arrival.Status == FlowchartArrivalStatus.Consumed);
        // Spot-check nested values survive the trip.
        var waiting = Assert.Single(state.ExecutionPaths, path => path.Status == ExecutionPathStatus.Waiting);
        Assert.Equal("node-loop:1", waiting.IterationKey);
        Assert.Equal(["Done"], waiting.LastOutcomeNames);
        var loopScope = Assert.Single(state.Scopes, scope => scope.Kind == ExecutionScopeKind.LoopIteration);
        Assert.Equal("node-loop:1", loopScope.LoopIterationKey);
        Assert.Equal("v", loopScope.Metadata["k"]);
        // The W32 monotonic counter round-trips as the last member.
        Assert.Equal(1, state.LoopIterationCounters["node-loop"]);
    }

    /// <summary>
    /// Canonical fully-populated state: every <see cref="ExecutionPathStatus"/> member, both
    /// <see cref="FlowchartArrivalStatus"/> members, every <see cref="ExecutionScopeKind"/> and
    /// <see cref="ExecutionScopeStatus"/> member, and diagnostic kinds spanning the first and last
    /// ordinals so any enum reorder breaks the fixture.
    /// </summary>
    private static FlowchartExecutionState NewCanonicalState() => new(
        rootExecutionScopeId: "scope:root",
        scopes:
        [
            new ExecutionScope("scope:root", ExecutionScopeKind.Root, ownerNodeId: "node-start"),
            new ExecutionScope("scope:branch", ExecutionScopeKind.Branch, parentExecutionScopeId: "scope:root", createdByNodeId: "node-fork", startConnectionId: "conn:1", status: ExecutionScopeStatus.Waiting),
            new ExecutionScope("scope:join", ExecutionScopeKind.Join, parentExecutionScopeId: "scope:root", status: ExecutionScopeStatus.Completed),
            new ExecutionScope("scope:loop", ExecutionScopeKind.LoopIteration, parentExecutionScopeId: "scope:root", ownerNodeId: "node-loop", loopIterationKey: "node-loop:1", status: ExecutionScopeStatus.Canceled, metadata: new Dictionary<string, string> { ["k"] = "v" }),
            new ExecutionScope("scope:race", ExecutionScopeKind.Race, status: ExecutionScopeStatus.Faulted)
        ],
        executionPaths:
        [
            new ExecutionPath("path:root", "scope:root", currentNodeId: "node-start"),
            new ExecutionPath("path:2", "scope:root", parentExecutionPathId: "path:root", currentNodeId: "node-join", incomingConnectionId: "conn:1", schedulingActivityExecutionId: "actexec-1", status: ExecutionPathStatus.Waiting, iterationKey: "node-loop:1", lastOutcomeNames: ["Done"]),
            new ExecutionPath("path:3", "scope:branch", status: ExecutionPathStatus.Completed),
            new ExecutionPath("path:4", "scope:branch", status: ExecutionPathStatus.Canceled),
            new ExecutionPath("path:5", "scope:race", status: ExecutionPathStatus.Faulted)
        ],
        arrivals:
        [
            new FlowchartArrival("arrival:1", "path:2", "scope:root", "node-a", "node-join", "conn:1", "Done", "actexec-1", iterationKey: "node-loop:1"),
            new FlowchartArrival("arrival:2", "path:3", "scope:branch", "node-b", "node-join", "conn:2", "Done", "actexec-2", FlowchartArrivalStatus.Consumed)
        ],
        activeChildren:
        [
            new FlowchartActiveChild("node-start", "path:root", "scope:root", "start", "actexec-0")
        ],
        diagnostics:
        [
            new FlowchartDiagnosticEvent("diag:1", FlowchartDiagnosticKind.Scheduled, "scheduled", "node-start", null, "path:root", "scope:root"),
            new FlowchartDiagnosticEvent("diag:2", FlowchartDiagnosticKind.Joined, "joined", "node-join", "conn:1"),
            new FlowchartDiagnosticEvent("diag:3", FlowchartDiagnosticKind.LoopIteration, "loop", "node-loop"),
            new FlowchartDiagnosticEvent("diag:4", FlowchartDiagnosticKind.Faulted, "faulted", details: new Dictionary<string, string> { ["reason"] = "x" })
        ],
        sequence: 42,
        loopIterationCounters: new Dictionary<string, int> { ["node-loop"] = 1 });
}
