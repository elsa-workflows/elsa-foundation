using System.Text.Json;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>Metadata keys for the frozen decision bound to a compiled activity node.</summary>
public static class ExecutableDecisionPinMetadata
{
    /// <summary>The historical pin, retained under its original executable identity.</summary>
    public const string HistoricalKey = "decisions.artifact.v1";

    /// <summary>A pin included in current executable content identity.</summary>
    public const string BehavioralKey = "decisions.artifact.v2";

    /// <summary>Reads one supported pin. Missing, ambiguous and oversized material is refused.</summary>
    public static string? Read(IReadOnlyDictionary<string, string> metadata)
    {
        var hasBehavioral = metadata.TryGetValue(BehavioralKey, out var behavioral);
        var hasHistorical = metadata.TryGetValue(HistoricalKey, out var historical);
        if (hasBehavioral == hasHistorical)
            return null;

        var pin = hasBehavioral ? behavioral : historical;
        return pin is { Length: > 0 and <= 2 * 1024 * 1024 } ? pin : null;
    }

    /// <summary>Whether the behaviorally hashed pin attests that its workflow input sources permit a plain read.</summary>
    public static bool HasVerifiedWorkflowInputs(IReadOnlyDictionary<string, string> metadata)
    {
        if (!metadata.ContainsKey(BehavioralKey) || Read(metadata) is not { } pin)
            return false;
        try
        {
            using var document = JsonDocument.Parse(pin, new JsonDocumentOptions { MaxDepth = 16 });
            return document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.EnumerateObject().Where(property => property.NameEquals("inputProtectionVersion"))
                    .Take(2).ToArray() is [{ Value.ValueKind: JsonValueKind.Number } marker] &&
                marker.Value.TryGetInt32(out var version) && version == 1;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
