using System.Text.Json.Nodes;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Persistence.Groundwork.Serialization;

/// <summary>
/// Adds the neutral classification and source-provenance values introduced after the historical v1 execution-state
/// shape. Legacy executions remain explicitly unknown and do not invent an authoritative source reference.
/// </summary>
public sealed class WorkflowExecutionStateDocumentV1ToV2Upcaster : IGroundworkRuntimeDocumentUpcaster
{
    public string DocumentKind => ElsaRuntimeStorageManifest.WorkflowExecutionStateDocumentKind;
    public int FromVersion => 1;

    public JsonObject Upcast(JsonObject content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var state = content["state"] as JsonObject
            ?? throw new InvalidOperationException("Legacy workflow-execution-state document has no 'state' object.");
        state["runKind"] = (int)WorkflowRunKind.Unknown;
        state["pinnedSource"] = null;
        return content;
    }
}
