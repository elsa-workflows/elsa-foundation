using System.Text.Json.Nodes;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Persistence.Groundwork.Serialization;

/// <summary>Adds typed iteration-frame activation and durable iteration-frame state.</summary>
public sealed class ActivityExecutionStateDocumentV3ToV4Upcaster : IGroundworkRuntimeDocumentUpcaster
{
    public string DocumentKind => ElsaRuntimeStorageManifest.ActivityExecutionStateDocumentKind;
    public int FromVersion => 3;

    public JsonObject Upcast(JsonObject content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var state = content["state"] as JsonObject
            ?? throw new InvalidOperationException("Activity execution state document has no 'state' object.");
        if (!state.ContainsKey("iterationVariableFrame"))
            state["iterationVariableFrame"] = null;
        if (!state.ContainsKey("iterationFrameRequest"))
            state["iterationFrameRequest"] = null;

        var provenanceMetadata = state["provenance"]?["metadata"] as JsonObject;
        if (provenanceMetadata is not null && LegacyIterationKeys.Any(provenanceMetadata.ContainsKey))
        {
            state["valueFlowCompatibility"] = new JsonObject
            {
                ["sourceVersion"] = ActivityExecutionValueFlowDocumentVersions.Current,
                ["targetVersion"] = ActivityExecutionValueFlowDocumentVersions.Current,
                ["isCompatible"] = false,
                ["incompatibilityCode"] = "VF-ACT-009",
                ["reason"] = "The legacy in-flight loop activation stores its iteration values in metadata. A typed iteration frame cannot be reconstructed safely; restart from a newly scheduled iteration."
            };
        }
        return content;
    }

    private static readonly string[] LegacyIterationKeys =
    [
        RuntimeMetadataKeys.LoopIterationOwnerNodeId,
        RuntimeMetadataKeys.LoopIterationItemName,
        RuntimeMetadataKeys.LoopIterationItemValue,
        RuntimeMetadataKeys.LoopIterationIndexName,
        RuntimeMetadataKeys.LoopIterationIndexValue
    ];
}
