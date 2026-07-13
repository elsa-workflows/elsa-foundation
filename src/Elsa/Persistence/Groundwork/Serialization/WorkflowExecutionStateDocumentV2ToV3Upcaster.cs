using System.Globalization;
using System.Text.Json.Nodes;

namespace Elsa.Persistence.Groundwork.Serialization;

/// <summary>Adds the exact effective-timestamp key used by bounded workflow execution history queries.</summary>
public sealed class WorkflowExecutionStateDocumentV2ToV3Upcaster : IGroundworkRuntimeDocumentUpcaster
{
    public string DocumentKind => ElsaRuntimeStorageManifest.WorkflowExecutionStateDocumentKind;
    public int FromVersion => 2;

    public JsonObject Upcast(JsonObject content)
    {
        var state = content["state"] as JsonObject
            ?? throw new InvalidOperationException("Workflow execution state document has no 'state' object.");
        var timestampText = new[] { "updatedAt", "completedAt", "startedAt", "createdAt" }
            .Select(name => state[name]?.GetValue<string>())
            .FirstOrDefault(value => value is not null)
            ?? throw new InvalidOperationException("Workflow execution state document has no history timestamp.");
        var timestamp = DateTimeOffset.Parse(timestampText, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        content["historySortTicks"] = timestamp.UtcTicks;
        return content;
    }
}
