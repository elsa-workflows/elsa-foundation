using System.Text.Json;
using Elsa.Activities.Flowchart.Internal;
using Elsa.Activities.Flowchart.Models;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;
using FlowchartActivity = Elsa.Activities.Flowchart.Activities.Flowchart;

namespace Elsa.Activities.Flowchart.Tests;

/// <summary>
/// ADR 0047 D3 guardrail. Proves the publish-time routing index that <see cref="FlowchartGraph"/> builds at
/// materialization routes byte-for-byte identically to the pre-D3 linear-scan walk it replaced — the D3
/// non-negotiable: "the routing DECISIONS must be exactly identical — same successors, same order, same behavior
/// on no-match/terminal." Every representative Flowchart shape (straight-line, multi-outcome branch, fan-in join,
/// parallel fork, ordered multi-target, empty) is routed through BOTH the index-backed
/// <see cref="FlowchartGraph.SelectOutboundConnections"/> and the retained
/// <see cref="FlowchartGraph.SelectOutboundConnectionsByScan"/> reference for every node × outcome-set combination,
/// asserting order-sensitive equality. Inbound routing is compared the same way. Finally the memo carried on the
/// materialized <see cref="ExecutableNode"/> is proven to (a) build the structure once and (b) never diverge from a
/// freshly built graph.
/// </summary>
public sealed class FlowchartRoutingTableDifferentialTests
{
    // Outcome sets exercised against every source node in every shape: the default (empty ⇒ Done), single ports,
    // a superset, and a port that matches nothing (the no-match/terminal path).
    private static readonly IReadOnlyCollection<string>[] OutcomeSets =
    [
        [],
        ["Done"],
        ["True"],
        ["False"],
        ["Alt"],
        ["Done", "Alt"],
        ["True", "False"],
        ["does-not-exist"]
    ];

    public static TheoryData<string, FlowchartGraph> Corpus()
    {
        var data = new TheoryData<string, FlowchartGraph>();
        foreach (var (name, graph) in BuildCorpus())
            data.Add(name, graph);
        return data;
    }

    [Theory]
    [MemberData(nameof(Corpus))]
    public void IndexOutboundRouting_MatchesLinearScan_ForEverySourceAndOutcomeSet(string name, FlowchartGraph graph)
    {
        _ = name;
        foreach (var node in graph.Children)
        {
            foreach (var outcomes in OutcomeSets)
            {
                var byIndex = graph.SelectOutboundConnections(node.ExecutableNodeId, outcomes);
                var byScan = graph.SelectOutboundConnectionsByScan(node.ExecutableNodeId, outcomes);

                Assert.Equal(Fingerprint(byScan), Fingerprint(byIndex));

                // SelectTargets rides the same index; assert it agrees with the scan-derived target order too.
                var scanTargets = byScan.Select(connection => connection.Target.NodeId).ToArray();
                if (scanTargets.Distinct(StringComparer.Ordinal).Count() == scanTargets.Length)
                {
                    var indexTargets = graph.SelectTargets(node.ExecutableNodeId, outcomes)
                        .Select(target => target.ExecutableNodeId)
                        .ToArray();
                    Assert.Equal(scanTargets, indexTargets);
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(Corpus))]
    public void IndexInboundRouting_MatchesLinearScan_ForEveryTarget(string name, FlowchartGraph graph)
    {
        _ = name;
        foreach (var node in graph.Children)
        {
            var byIndex = graph.GetInboundConnections(node.ExecutableNodeId);
            var byScan = graph.GetInboundConnectionsByScan(node.ExecutableNodeId);
            Assert.Equal(Fingerprint(byScan), Fingerprint(byIndex));
        }
    }

    [Fact]
    public void Memo_BuildsRoutingStructureOnce_AndNeverDivergesFromAFreshBuild()
    {
        var node = NewFlowchartNode(
            "branch",
            ["a", "b", "c"],
            [
                Connection("a", "b", "True"),
                Connection("a", "c", "False")
            ]);

        RoutingStructureMaterializationDiagnostics.Reset();
        var first = node.GetOrAddRoutingStructure(FlowchartGraph.From);
        var second = node.GetOrAddRoutingStructure(FlowchartGraph.From);

        Assert.Same(first, second);
        Assert.Equal(1, RoutingStructureMaterializationDiagnostics.Count);

        // The memo must not diverge from routing the walk would produce off a freshly built graph.
        var fresh = FlowchartGraph.From(node);
        foreach (var outcomes in OutcomeSets)
        {
            Assert.Equal(
                Fingerprint(fresh.SelectOutboundConnectionsByScan("a", outcomes)),
                Fingerprint(first.SelectOutboundConnections("a", outcomes)));
        }
    }

    private static IEnumerable<(string Name, FlowchartGraph Graph)> BuildCorpus()
    {
        yield return ("straight-line", Graph("straight", ["a", "b", "c"],
            [Connection("a", "b"), Connection("b", "c")]));

        yield return ("multi-outcome-branch", Graph("branch", ["a", "b", "c"],
            [Connection("a", "b", "True"), Connection("a", "c", "False")]));

        yield return ("fan-in-join", Graph("join", ["a", "b", "c"],
            [Connection("a", "c"), Connection("b", "c")]));

        yield return ("parallel-fork", Graph("fork", ["a", "b", "c"],
            [Connection("a", "b"), Connection("a", "c")]));

        // Multiple same-source, same-port connections in a fixed order — the order-preservation case.
        yield return ("ordered-multi-target", Graph("ordered", ["a", "b", "c", "d"],
            [Connection("a", "d"), Connection("a", "b"), Connection("a", "c")]));

        yield return ("mixed-ports", Graph("mixed", ["a", "b", "c", "d"],
            [Connection("a", "b", "Done"), Connection("a", "c", "Alt"), Connection("a", "d", "Alt")]));

        yield return ("empty", Graph("empty", ["a", "b"], []));
    }

    private static FlowchartGraph Graph(string id, IReadOnlyList<string> childIds, IReadOnlyList<FlowchartConnection> connections) =>
        FlowchartGraph.From(NewFlowchartNode(id, childIds, connections));

    private static (string, string, string, string?)[] Fingerprint(IReadOnlyCollection<FlowchartConnection> connections) =>
        connections
            .Select(connection => (connection.Source.NodeId, connection.Source.Port, connection.Target.NodeId, connection.Id))
            .ToArray();

    private static FlowchartConnection Connection(string source, string target, string? port = null) =>
        new(new FlowchartEndpoint(source, port), new FlowchartEndpoint(target));

    private static ExecutableNode NewFlowchartNode(string id, IReadOnlyList<string> childIds, IReadOnlyList<FlowchartConnection> connections)
    {
        var children = childIds.Select(NewLeaf).ToArray();
        return new ExecutableNode(
            $"node-flowchart-{id}",
            $"authored-flowchart-{id}",
            typeof(FlowchartActivity).FullName!,
            "1.0.0",
            "elsa.flowchart",
            JsonSerializer.SerializeToElement(new { }),
            new Dictionary<string, RuntimeInputBinding>(),
            new Dictionary<string, string>(),
            childSlots: [new ExecutableChildSlot(FlowchartActivity.ActivitiesSlotName, children)],
            structure: new ExecutableActivityStructure(
                FlowchartActivity.StructureKind,
                FlowchartActivity.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(new FlowchartStructure(connections: connections, startNodeId: childIds.FirstOrDefault()))));
    }

    private static ExecutableNode NewLeaf(string id) =>
        new(
            id,
            $"authored-{id}",
            "leaf",
            "1.0.0",
            "clr",
            JsonSerializer.SerializeToElement(new { }),
            new Dictionary<string, RuntimeInputBinding>(),
            new Dictionary<string, string>());
}
