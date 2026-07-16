using System.Text.Json.Nodes;

namespace Elsa.Persistence.Groundwork.Serialization;

/// <summary>Adds typed trigger-payload metadata to durable timers.</summary>
public sealed class DurableTimerDocumentV1ToV2Upcaster : IGroundworkRuntimeDocumentUpcaster
{
    public string DocumentKind => ElsaRuntimeStorageManifest.DurableTimerDocumentKind;
    public int FromVersion => 1;

    public JsonObject Upcast(JsonObject content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var timer = content["timer"] as JsonObject
            ?? throw new InvalidOperationException("Durable timer document has no 'timer' object.");
        if (!timer.ContainsKey("payloadType"))
            timer["payloadType"] = null;
        if (!timer.ContainsKey("providerId"))
            timer["providerId"] = null;
        return content;
    }
}
