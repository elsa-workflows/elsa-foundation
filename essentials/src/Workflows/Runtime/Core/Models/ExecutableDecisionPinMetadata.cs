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
    public static bool HasVerifiedWorkflowInputs(IReadOnlyDictionary<string, string> metadata, string selection)
    {
        if (string.IsNullOrWhiteSpace(selection) || !metadata.ContainsKey(BehavioralKey) || Read(metadata) is not { } pin)
            return false;
        try
        {
            using var document = JsonDocument.Parse(pin, new JsonDocumentOptions { MaxDepth = 16, AllowDuplicateProperties = false });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 6)
                return false;

            var separator = selection.LastIndexOf('@');
            var selectedVersion = separator > 0 ? selection[(separator + 1)..] : "";
            var pinnedVersion = ReadString("versionId");
            return separator > 0 && selectedVersion.Length > 0 && ReadString("selection") == selection &&
                ReadString("assetId") == selection[..separator] && !string.IsNullOrWhiteSpace(ReadString("tenantId")) &&
                !string.IsNullOrWhiteSpace(pinnedVersion) && (selectedVersion == "latest" || selectedVersion == pinnedVersion) &&
                root.TryGetProperty("artifact", out var artifact) && artifact.ValueKind == JsonValueKind.Object &&
                artifact.TryGetProperty("contentHash", out var hash) &&
                hash.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(hash.GetString()) &&
                root.TryGetProperty("inputProtectionVersion", out var marker) &&
                marker.ValueKind == JsonValueKind.Number && marker.TryGetInt32(out var version) && version == 1;

            string? ReadString(string key) =>
                root.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
