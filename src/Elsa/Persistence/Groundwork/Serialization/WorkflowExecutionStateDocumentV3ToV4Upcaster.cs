using System.Text.Json.Nodes;

namespace Elsa.Persistence.Groundwork.Serialization;

/// <summary>Adds the root depth used by executions created before durable dispatch nesting lineage.</summary>
public sealed class WorkflowExecutionStateDocumentV3ToV4Upcaster : IGroundworkRuntimeDocumentUpcaster
{
    public string DocumentKind => ElsaRuntimeStorageManifest.WorkflowExecutionStateDocumentKind;
    public int FromVersion => 3;

    public JsonObject Upcast(JsonObject content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var state = content["state"] as JsonObject
            ?? throw new InvalidOperationException("Workflow execution state document has no 'state' object.");
        state["dispatchNestingDepth"] = 0;
        return content;
    }
}
