using System.Text.Json.Nodes;

namespace Elsa.Persistence.Groundwork.Serialization;

/// <summary>Adds permissive legacy input semantics and an empty direct-dependency snapshot.</summary>
public sealed class WorkflowExecutableDocumentV3ToV4Upcaster : IGroundworkRuntimeDocumentUpcaster
{
    public string DocumentKind => ElsaRuntimeStorageManifest.WorkflowExecutableDocumentKind;
    public int FromVersion => 3;

    public JsonObject Upcast(JsonObject content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var executable = content["executable"] as JsonObject
            ?? throw new InvalidOperationException("Workflow executable document has no 'executable' object.");
        executable["inputContract"] = null;
        executable["dependencies"] = new JsonArray();
        return content;
    }
}
