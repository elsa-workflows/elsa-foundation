using System.Text.Json.Nodes;
using Groundwork.Documents.Serialization;

namespace Elsa.Persistence.Groundwork.Serialization;

/// <summary>
/// Admits v4 source references into the v5 envelope. The new optional activity-presentation
/// sidecar remains absent for historical references.
/// </summary>
public sealed class WorkflowExecutableSourceReferenceDocumentV4ToV5Upcaster : IDocumentJsonUpcaster
{
    public string DocumentKind =>
        ElsaRuntimeStorageManifest.WorkflowExecutableSourceReferenceDocumentKind;

    public int FromVersion => 4;

    public JsonObject Upcast(JsonObject content)
    {
        ArgumentNullException.ThrowIfNull(content);
        return content;
    }
}
