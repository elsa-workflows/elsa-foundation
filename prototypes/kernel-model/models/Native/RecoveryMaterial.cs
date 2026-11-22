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
        ["nodes"] = Values.From(workflow.Nodes.Values.OrderBy(n => n.Id, StringComparer.Ordinal).Select(n => new
        {
            n.Id, Type = n.Type.AssemblyQualifiedName, Module = n.Type.Module.ModuleVersionId, n.Slot, n.External, n.Background,
            Children = n.Children.Select(c => c.Id).ToArray(), n.Connections,
            Gateways = n.Gateways.OrderBy(p => p.Key, StringComparer.Ordinal).ToArray(), Captures = n.Captures.OrderBy(p => p.Key, StringComparer.Ordinal).ToArray(),
            Inputs = n.Inputs.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => new
                { p.Key, Kind = p.Value.GetType().FullName, Value = JsonSerializer.SerializeToNode(p.Value, p.Value.GetType()) }).ToArray()
        }))
    };
}
