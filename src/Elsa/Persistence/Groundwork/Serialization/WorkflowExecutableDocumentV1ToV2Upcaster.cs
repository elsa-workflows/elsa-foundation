using System.Text.Json.Nodes;

namespace Elsa.Persistence.Groundwork.Serialization;

/// <summary>
/// Adds the provider-owned retention coordination state introduced for race-safe executable GC.
/// Historical executable documents begin with no root writers and no deletion reservation.
/// </summary>
public sealed class WorkflowExecutableDocumentV1ToV2Upcaster : IGroundworkRuntimeDocumentUpcaster
{
    public string DocumentKind => ElsaRuntimeStorageManifest.WorkflowExecutableDocumentKind;

    public int FromVersion => 1;

    public JsonObject Upcast(JsonObject content)
    {
        ArgumentNullException.ThrowIfNull(content);
        content["rootWriteLeases"] = new JsonObject();
        content["deletionGuard"] = null;
        return content;
    }
}
