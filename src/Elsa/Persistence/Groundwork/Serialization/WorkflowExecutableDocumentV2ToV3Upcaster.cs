using System.Text.Json.Nodes;

namespace Elsa.Persistence.Groundwork.Serialization;

/// <summary>
/// Adds the role-owned binding fields introduced in executable format v3. The legacy payload is retained
/// behind the temporary invocation adapter and the executable is marked for republishing; the upcaster does
/// not guess a portable target type that the historical artifact never stored.
/// </summary>
public sealed class WorkflowExecutableDocumentV2ToV3Upcaster : IGroundworkRuntimeDocumentUpcaster
{
    public string DocumentKind => ElsaRuntimeStorageManifest.WorkflowExecutableDocumentKind;
    public int FromVersion => 2;

    public JsonObject Upcast(JsonObject content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var executable = content["executable"] as JsonObject
            ?? throw new InvalidOperationException("Workflow executable document has no 'executable' object.");
        var compatibility = executable["compatibilityMetadata"] as JsonObject ?? new JsonObject();
        compatibility["runtime.valueFlowCompatibility"] = "VF-ACT-009:republish-required";
        executable["compatibilityMetadata"] = compatibility;

        var root = executable["rootActivity"] as JsonObject
            ?? throw new InvalidOperationException("Workflow executable document has no root activity.");
        AddBindingFields(root);
        return content;
    }

    private static void AddBindingFields(JsonObject node)
    {
        if (node["inputBindings"] is JsonObject bindings)
        {
            foreach (var (_, value) in bindings)
            {
                if (value is not JsonObject binding)
                    continue;

                var inputName = binding["inputName"]?.GetValue<string>()
                    ?? throw new InvalidOperationException("A legacy runtime input binding has no input name.");
                binding["inputKey"] = inputName;
                binding["targetType"] = new JsonObject
                {
                    ["alias"] = "Elsa.Any",
                    ["collectionKind"] = "Single",
                    ["schema"] = null,
                    ["schemaVersion"] = null
                };
                binding["effectivePolicy"] = new JsonObject
                {
                    ["lifecycle"] = 0,
                    ["storage"] = 0,
                    ["isSensitive"] = false,
                    ["requiresEncryption"] = false,
                    ["redactionMode"] = null,
                    ["retentionPolicy"] = null,
                    ["metadata"] = new JsonObject()
                };
                binding["literal"] = null;
                binding["workflowRequest"] = null;
                binding["variable"] = null;
                binding["activityResult"] = null;
            }
        }

        if (node["childSlots"] is not JsonArray childSlots)
            return;

        foreach (var childSlot in childSlots.OfType<JsonObject>())
            if (childSlot["activities"] is JsonArray activities)
                foreach (var child in activities.OfType<JsonObject>())
                    AddBindingFields(child);
    }
}
