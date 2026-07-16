using System.Text.Json.Nodes;

namespace Elsa.Persistence.Groundwork.Serialization;

/// <summary>Adds the workflow activation's role-owned root variable frame.</summary>
public sealed class WorkflowExecutionStateDocumentV3ToV4Upcaster : IGroundworkRuntimeDocumentUpcaster
{
    public string DocumentKind => ElsaRuntimeStorageManifest.WorkflowExecutionStateDocumentKind;
    public int FromVersion => 3;

    public JsonObject Upcast(JsonObject content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var state = content["state"] as JsonObject
            ?? throw new InvalidOperationException("Workflow execution state document has no 'state' object.");
        if (!state.ContainsKey("rootVariableFrame"))
            state["rootVariableFrame"] = null;
        return content;
    }
}
