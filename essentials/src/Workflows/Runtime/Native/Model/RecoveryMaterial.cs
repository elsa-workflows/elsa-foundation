using System.Text.Json;
using System.Text.Json.Nodes;

namespace Kernel.Native;

// A conservative exact-declaration/direct-CLR-module fence for this in-memory experiment, not source installation,
// executable dependency-closure identity, a product fingerprint or cross-binary rollout qualification.
internal static class NativeRecoveryMaterial
{
    internal static JsonObject Describe(Workflow workflow) => new()
    {
        ["workflow"] = workflow.Id, ["root"] = workflow.Root.Id, ["variables"] = Values.From(workflow.Variables),
        ["parents"] = Values.From(workflow.Parents.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => new { p.Key, Parent = p.Value.Id })),
        ["nodes"] = new JsonArray(workflow.Nodes.Values.OrderBy(n => n.Id, StringComparer.Ordinal).Select(DescribeNode).ToArray())
    };

    /// <summary>A Switch's cases in order: each label and its condition's binding.</summary>
    internal static JsonArray DescribeCases(Node n) => new(n.Cases.Select(c => (JsonNode?)new JsonObject
    {
        ["Label"] = c.Label,
        ["Condition"] = DescribeBinding(c.Condition),
    }).ToArray());

    /// <summary>A GraphActivity's declaration: its inputs, variables and outputs in order, and its outcome maps by key; null for any other node.</summary>
    internal static JsonObject? DescribeGraph(Node n) => n.Graph is not { } g ? null : new JsonObject
    {
        ["Inputs"] = new JsonArray(g.Inputs.Select(i => (JsonNode?)new JsonObject { ["Name"] = i.Name, ["Required"] = i.Required, ["Default"] = DescribeBinding(i.Default) }).ToArray()),
        ["Variables"] = new JsonArray(g.Variables.Select(v => (JsonNode?)new JsonObject { ["Name"] = v.Name, ["Value"] = DescribeBinding(v.Value) }).ToArray()),
        ["Outputs"] = new JsonArray(g.Outputs.Select(o => (JsonNode?)new JsonObject { ["Name"] = o.Name, ["Value"] = DescribeBinding(o.Value) }).ToArray()),
        ["OutcomeMappings"] = new JsonObject(g.OutcomeMappings.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => KeyValuePair.Create(p.Key, (JsonNode?)p.Value))),
        ["CompleteOutcomes"] = new JsonObject(g.CompleteOutcomes.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => KeyValuePair.Create(p.Key, (JsonNode?)p.Value))),
    };

    private static JsonObject? DescribeBinding(Binding? b) =>
        b is null ? null : new JsonObject { ["Kind"] = b.GetType().FullName, ["Value"] = JsonSerializer.SerializeToNode(b, b.GetType()) };

    private static JsonObject DescribeNode(Node n)
    {
        var material = JsonSerializer.SerializeToNode(new
        {
            n.Id, Type = n.Type.AssemblyQualifiedName, Module = n.Type.Module.ModuleVersionId, n.Slot, n.External, n.Background,
            Children = n.Children.Select(c => c.Id).ToArray(), n.Connections,
            Gateways = n.Gateways.OrderBy(p => p.Key, StringComparer.Ordinal).ToArray(), Captures = n.Captures.OrderBy(p => p.Key, StringComparer.Ordinal).ToArray(),
            Inputs = n.Inputs.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => new
                { p.Key, Kind = p.Value.GetType().FullName, Value = JsonSerializer.SerializeToNode(p.Value, p.Value.GetType()) }).ToArray()
        })!.AsObject();
        if (n.JobTimeout is { } timeout) material["JobTimeoutTicks"] = timeout.Ticks;
        // A Switch's cases are part of what it is; a node without any describes as before.
        if (n.Cases.Count != 0) material["Cases"] = DescribeCases(n);
        if (n.Graph is not null) material["Graph"] = DescribeGraph(n);
        return material;
    }
}
