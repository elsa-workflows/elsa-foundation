using System.Text.Json.Nodes;

namespace Elsa.Persistence.Groundwork.Serialization;

/// <summary>Adds the structural activity activation's role-owned variable frame.</summary>
public sealed class ActivityExecutionStateDocumentV2ToV3Upcaster : IGroundworkRuntimeDocumentUpcaster
{
    public string DocumentKind => ElsaRuntimeStorageManifest.ActivityExecutionStateDocumentKind;
    public int FromVersion => 2;

    public JsonObject Upcast(JsonObject content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var state = content["state"] as JsonObject
            ?? throw new InvalidOperationException("Activity execution state document has no 'state' object.");
        if (!state.ContainsKey("variableFrame"))
            state["variableFrame"] = null;
        return content;
    }
}
