using System.Text.Json.Nodes;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Persistence.Groundwork.Serialization;

/// <summary>
/// Names the role-owned invocation fields introduced by value-flow document version 2. Legacy activity
/// documents remain readable for inspection, but are explicitly marked incompatible for execution because
/// their pinned contract and all-or-nothing input snapshot cannot be reconstructed deterministically.
/// </summary>
public sealed class ActivityExecutionStateDocumentV1ToV2Upcaster : IGroundworkRuntimeDocumentUpcaster
{
    public string DocumentKind => ElsaRuntimeStorageManifest.ActivityExecutionStateDocumentKind;
    public int FromVersion => 1;

    public JsonObject Upcast(JsonObject content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var state = content["state"] as JsonObject
            ?? throw new InvalidOperationException("Activity execution state document has no 'state' object.");
        var execution = state["execution"] as JsonObject
            ?? throw new InvalidOperationException("Activity execution state document has no 'state.execution' object.");
        var activityType = execution["activityType"]?.GetValue<string>()
            ?? throw new InvalidOperationException("Activity execution state document has no activity type.");
        var activityTypeVersion = execution["activityTypeVersion"]?.GetValue<string>()
            ?? throw new InvalidOperationException("Activity execution state document has no activity type version.");
        var status = state["status"]?.GetValue<int>() ?? (int)ActivityExecutionStatus.Scheduled;
        var incompatibility = status == (int)ActivityExecutionStatus.Scheduled
            ? "The legacy scheduled invocation has no pinned activity-contract fingerprint. Republish and reschedule it with a value-flow v2 executable."
            : "The legacy invocation may already have started, but has no complete pinned input snapshot. Its inputs cannot be reconstructed without reevaluation.";

        state["documentVersion"] = ActivityExecutionValueFlowDocumentVersions.Current;
        state["contractIdentity"] = new JsonObject
        {
            ["activityTypeKey"] = activityType,
            ["contractVersion"] = activityTypeVersion,
            ["schemaFingerprint"] = "legacy:unverified"
        };
        state["inputSnapshot"] = null;
        state["attempts"] = new JsonArray();
        state["privateState"] = null;
        state["completion"] = null;
        state["fault"] = null;
        state["triggerRegistrations"] = new JsonArray();
        state["triggerDeliveries"] = new JsonArray();
        state["valueFlowCompatibility"] = new JsonObject
        {
            ["sourceVersion"] = ActivityExecutionValueFlowDocumentVersions.Legacy,
            ["targetVersion"] = ActivityExecutionValueFlowDocumentVersions.Current,
            ["isCompatible"] = false,
            ["incompatibilityCode"] = "VF-ACT-009",
            ["reason"] = incompatibility
        };

        return content;
    }
}
