using System.Text.Json.Nodes;

namespace Elsa.Persistence.Groundwork.Serialization;

/// <summary>
/// Adds the pinned activity contract and workflow-intrinsic fields introduced in executable format v4.
/// Historical nodes have no recoverable contract or intrinsic identity, so the fields are neutral nulls.
/// </summary>
public sealed class WorkflowExecutableDocumentV3ToV4Upcaster : IGroundworkRuntimeDocumentUpcaster
{
    public string DocumentKind => ElsaRuntimeStorageManifest.WorkflowExecutableDocumentKind;
    public int FromVersion => 3;

    public JsonObject Upcast(JsonObject content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var executable = content["executable"] as JsonObject
            ?? throw new InvalidOperationException("Workflow executable document has no 'executable' object.");
        var root = executable["rootActivity"] as JsonObject
            ?? throw new InvalidOperationException("Workflow executable document has no root activity.");
        AddValueFlowFields(root);
        return content;
    }

    private static void AddValueFlowFields(JsonObject node)
    {
        AddNullIfMissing(node, "activityContract");
        AddNullIfMissing(node, "intrinsicKind");
        AddNullIfMissing(node, "intrinsicVariable");

        if (node["childSlots"] is not JsonArray childSlots)
            return;

        foreach (var childSlot in childSlots.OfType<JsonObject>())
            if (childSlot["activities"] is JsonArray activities)
                foreach (var child in activities.OfType<JsonObject>())
                    AddValueFlowFields(child);
    }

    private static void AddNullIfMissing(JsonObject node, string propertyName)
    {
        if (!node.ContainsKey(propertyName))
            node[propertyName] = null;
    }
}
