using System.Text.Json.Nodes;
using Elsa.Persistence.Groundwork.Exceptions;

namespace Elsa.Persistence.Groundwork.Serialization;

/// <summary>
/// Removes the obsolete output-capture declarations from executable nodes.
/// Empty declarations carry no information and can be discarded, while populated declarations cannot
/// be reconstructed under the atomic activity-result model and therefore require the executable to be republished.
/// </summary>
public sealed class WorkflowExecutableDocumentV4ToV5Upcaster : IGroundworkRuntimeDocumentUpcaster
{
    public string DocumentKind => ElsaRuntimeStorageManifest.WorkflowExecutableDocumentKind;
    public int FromVersion => 4;

    public JsonObject Upcast(JsonObject content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var executable = content["executable"] as JsonObject
            ?? throw InvalidDocument("The workflow executable document has no 'executable' object.");
        var root = executable["rootActivity"] as JsonObject
            ?? throw InvalidDocument("The workflow executable document has no root activity.");

        ValidateOutputCaptures(root);
        RemoveEmptyOutputCaptures(root);
        return content;
    }

    private static void ValidateOutputCaptures(JsonObject node)
    {
        if (node.TryGetPropertyValue("outputCaptures", out var outputCaptures))
        {
            if (outputCaptures is not JsonObject { Count: 0 })
            {
                var nodeId = node["executableNodeId"] is JsonValue id && id.TryGetValue<string>(out var value)
                    ? value
                    : "<unknown>";
                throw InvalidDocument(
                    $"Executable node '{nodeId}' contains non-empty or malformed 'outputCaptures' that cannot be reconstructed " +
                    "under the atomic activity-result model. Republish the workflow executable.");
            }
        }

        VisitChildren(node, ValidateOutputCaptures);
    }

    private static void RemoveEmptyOutputCaptures(JsonObject node)
    {
        node.Remove("outputCaptures");
        VisitChildren(node, RemoveEmptyOutputCaptures);
    }

    private static void VisitChildren(JsonObject node, Action<JsonObject> visit)
    {
        if (node["childSlots"] is not JsonArray childSlots)
            return;

        foreach (var childSlot in childSlots.OfType<JsonObject>())
            if (childSlot["activities"] is JsonArray activities)
                foreach (var child in activities.OfType<JsonObject>())
                    visit(child);
    }

    private static GroundworkRuntimeDocumentVersionException InvalidDocument(string reason) =>
        new(
            $"Cannot upcast persisted document kind '{ElsaRuntimeStorageManifest.WorkflowExecutableDocumentKind}' " +
            $"from version 4 to version 5. {reason}");
}
