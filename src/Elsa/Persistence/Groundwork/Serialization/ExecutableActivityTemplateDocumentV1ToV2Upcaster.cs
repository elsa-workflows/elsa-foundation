using System.Text.Json.Nodes;
using Groundwork.Documents.Serialization;

namespace Elsa.Persistence.Groundwork.Serialization;

/// <summary>
/// Converts template v1 executable nodes from one nested runtime descriptor to the canonical split
/// descriptor fields used by v2.
/// </summary>
public sealed class ExecutableActivityTemplateDocumentV1ToV2Upcaster : IDocumentJsonUpcaster
{
    public string DocumentKind => ElsaRuntimeStorageManifest.ExecutableActivityTemplateDocumentKind;
    public int FromVersion => 1;

    public JsonObject Upcast(JsonObject content)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (content["template"] is JsonObject template && template["root"] is JsonObject root)
            RewriteNode(root);
        return content;
    }

    private static void RewriteNode(JsonObject node)
    {
        if (node["descriptor"] is JsonObject descriptor)
        {
            node["descriptorType"] = descriptor["consumerKey"]?.DeepClone();
            node["descriptorSchemaVersion"] = descriptor["schemaVersion"]?.DeepClone();
            node["descriptorPayload"] = descriptor["payload"]?.DeepClone();
            node.Remove("descriptor");
        }

        if (node["childSlots"] is not JsonArray childSlots)
            return;

        foreach (var childSlot in childSlots.OfType<JsonObject>())
        foreach (var child in (childSlot["activities"] as JsonArray)?.OfType<JsonObject>() ?? [])
            RewriteNode(child);
    }
}
