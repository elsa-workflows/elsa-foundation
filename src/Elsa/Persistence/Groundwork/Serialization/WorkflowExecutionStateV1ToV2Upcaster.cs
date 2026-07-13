using System.Text.Json.Nodes;

namespace Elsa.Persistence.Groundwork.Serialization;

/// <summary>Adds the durable published/test-run origin introduced by workflow execution state schema v2.</summary>
public sealed class WorkflowExecutionStateV1ToV2Upcaster : IGroundworkRuntimeDocumentUpcaster
{
    public string DocumentKind => ElsaRuntimeStorageManifest.WorkflowExecutionStateDocumentKind;

    public int FromVersion => 1;

    public JsonObject Upcast(JsonObject content)
    {
        if (content["state"] is not JsonObject state)
            throw new InvalidOperationException("Workflow execution state v1 content is missing its state object.");

        // Version 1 predates test-run provenance and all historical records came from the published/default lane.
        state["origin"] = 0;
        return content;
    }
}
