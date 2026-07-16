using System.Text.Json.Nodes;

namespace Elsa.Persistence.Groundwork.Serialization;

/// <summary>
/// Adds stable input identity and neutral sensitivity metadata to executable input bindings.
/// </summary>
public sealed class WorkflowExecutableDocumentV2ToV3Upcaster : IGroundworkRuntimeDocumentUpcaster
{
    public string DocumentKind => ElsaRuntimeStorageManifest.WorkflowExecutableDocumentKind;
    public int FromVersion => 2;

    public JsonObject Upcast(JsonObject content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var executable = RequireObject(content, "executable", "workflow-executable document");
        var rootActivity = RequireObject(executable, "rootActivity", "workflow executable");
        UpcastNode(rootActivity);
        return content;
    }

    private static void UpcastNode(JsonObject node)
    {
        var nodeId = node["executableNodeId"]?.GetValue<string>() ?? "<unknown>";
        var inputBindings = RequireObject(node, "inputBindings", $"executable node '{nodeId}'");

        foreach (var (bindingName, bindingNode) in inputBindings)
        {
            if (bindingNode is not JsonObject binding)
                throw new InvalidOperationException($"Executable node '{nodeId}' has no object value for input binding '{bindingName}'.");

            binding["inputKey"] = bindingName;
            binding["isSensitive"] = false;
        }

        var childSlots = RequireArray(node, "childSlots", $"executable node '{nodeId}'");
        foreach (var slotNode in childSlots)
        {
            if (slotNode is not JsonObject slot)
                throw new InvalidOperationException($"Executable node '{nodeId}' has a child slot that is not an object.");

            var activities = RequireArray(slot, "activities", $"child slot on executable node '{nodeId}'");
            foreach (var activityNode in activities)
            {
                if (activityNode is not JsonObject activity)
                    throw new InvalidOperationException($"Executable node '{nodeId}' has a child activity that is not an object.");
                UpcastNode(activity);
            }
        }
    }

    private static JsonObject RequireObject(JsonObject owner, string propertyName, string ownerName) =>
        owner[propertyName] as JsonObject
        ?? throw new InvalidOperationException($"The {ownerName} has no '{propertyName}' object.");

    private static JsonArray RequireArray(JsonObject owner, string propertyName, string ownerName) =>
        owner[propertyName] as JsonArray
        ?? throw new InvalidOperationException($"The {ownerName} has no '{propertyName}' array.");
}
