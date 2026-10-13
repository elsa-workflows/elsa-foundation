using System.Text.Json;
using System.Text.Json.Nodes;

namespace Elsa.Studio.Preferences.Core;

/// <summary>
/// Canonical Home layout document: widget ids, order, size, visibility and per-widget options.
/// The reserved admin-default subject stores the tenant/host default; a missing user document
/// falls back to that default, then to the built-in layout.
/// </summary>
public static class StudioHomeLayout
{
    public const string DefaultSubjectId = "__admin_default__";
    public const int SchemaVersion = 1;

    public static readonly IReadOnlyList<string> AllowedSizes =
        ["small", "medium", "wide", "full", "S", "M", "L"];

    public static readonly JsonElement BuiltInValue = Parse("""
        {
          "widgets": [
            { "id": "home.get-started", "size": "full", "visible": true },
            { "id": "home.recent-workflows", "size": "medium", "visible": true },
            { "id": "home.needs-attention", "size": "medium", "visible": true },
            { "id": "home.recent-runs", "size": "full", "visible": true }
          ]
        }
        """);

    public static bool IsValid(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object)
            return false;
        if (!value.TryGetProperty("widgets", out var widgets) || widgets.ValueKind != JsonValueKind.Array)
            return false;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in widgets.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object)
                return false;
            if (!entry.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.String)
                return false;
            var widgetId = id.GetString();
            if (string.IsNullOrWhiteSpace(widgetId) || widgetId.Length > 128 || !seen.Add(widgetId))
                return false;
            if (entry.TryGetProperty("size", out var size))
            {
                if (size.ValueKind != JsonValueKind.String)
                    return false;
                var sizeValue = size.GetString();
                if (sizeValue is null || !AllowedSizes.Contains(sizeValue, StringComparer.Ordinal))
                    return false;
            }
            if (entry.TryGetProperty("visible", out var visible) &&
                visible.ValueKind is not JsonValueKind.True and not JsonValueKind.False)
                return false;
            if (entry.TryGetProperty("options", out var options) &&
                options.ValueKind is not JsonValueKind.Object and not JsonValueKind.Undefined and not JsonValueKind.Null)
                return false;
        }

        return true;
    }

    public static JsonElement Normalize(JsonElement value)
    {
        var root = JsonNode.Parse(value.GetRawText()) as JsonObject
            ?? throw new InvalidOperationException("Home layout value must be an object.");
        if (root["widgets"] is not JsonArray widgets)
            throw new InvalidOperationException("Home layout value must contain a widgets array.");

        foreach (var node in widgets)
        {
            if (node is not JsonObject entry)
                continue;
            var size = entry["size"]?.GetValue<string>();
            entry["size"] = NormalizeSize(size);
            if (entry["visible"] is null)
                entry["visible"] = true;
        }

        using var document = JsonDocument.Parse(root.ToJsonString());
        return document.RootElement.Clone();
    }

    public static string NormalizeSize(string? size) => size switch
    {
        "S" => "small",
        "M" => "medium",
        "L" => "full",
        "small" or "medium" or "wide" or "full" => size,
        _ => "medium"
    };

    private static JsonElement Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
