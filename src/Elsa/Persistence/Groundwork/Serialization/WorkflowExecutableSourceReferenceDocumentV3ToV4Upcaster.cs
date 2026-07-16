using System.Text.Json.Nodes;

namespace Elsa.Persistence.Groundwork.Serialization;

/// <summary>Adds the neutral tenant scope used by historical source references.</summary>
public sealed class WorkflowExecutableSourceReferenceDocumentV3ToV4Upcaster : IGroundworkRuntimeDocumentUpcaster
{
    public string DocumentKind => ElsaRuntimeStorageManifest.WorkflowExecutableSourceReferenceDocumentKind;
    public int FromVersion => 3;

    public JsonObject Upcast(JsonObject content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var reference = content["reference"] as JsonObject
            ?? throw new InvalidOperationException("Workflow executable source-reference document has no 'reference' object.");
        reference["tenantId"] = null;
        return content;
    }
}
