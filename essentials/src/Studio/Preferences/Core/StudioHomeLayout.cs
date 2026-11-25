using System.Text.Json;
using System.Text.RegularExpressions;
using Elsa.Serialization.Core;

namespace Elsa.Studio.Preferences.Core;

/// <summary>
/// Canonical Home layout document: widget ids, order, size, visibility and per-widget options.
/// The reserved admin-default subject stores the tenant/host default; a missing user document
/// falls back to that default, then to the built-in layout.
/// </summary>
public static partial class StudioHomeLayout
{
    public const string DefaultSubjectId = "__admin_default__";
    public const int SchemaVersion = 1;
    public const int MaxWidgetCount = 50;
    public const string WidgetIdPattern = "^[a-z0-9][a-z0-9._-]{0,63}(:[a-z0-9._-]{1,64})?$";

    public static readonly IReadOnlyList<string> AllowedSizes =
        ["small", "medium", "wide", "full", "S", "M", "L"];

    /// <summary>
    /// First-party Home widget ids used by the built-in layout. PUT accepts any id that
    /// matches <see cref="WidgetIdPattern"/> so extension <c>registerHomeWidget</c> ids persist.
    /// </summary>
    public static readonly IReadOnlySet<string> CoreWidgetIds = new HashSet<string>(StringComparer.Ordinal)
    {
        "home.get-started",
        "home.quick-actions",
        "home.recent-workflows",
        "home.needs-attention",
        "home.recent-runs",
        "home.tasks",
        "attention.queue",
        "workflows.portfolio",
        "workflows.run-health"
    };

    public static readonly JsonElement BuiltInValue = Parse("""
        {
          "widgets": [
            { "id": "home.get-started", "size": "full", "visible": true },
            { "id": "home.quick-actions", "size": "medium", "visible": true },
            { "id": "home.recent-workflows", "size": "medium", "visible": true },
            { "id": "home.recent-runs", "size": "medium", "visible": true },
            { "id": "home.needs-attention", "size": "medium", "visible": true },
            { "id": "home.tasks", "size": "medium", "visible": true }
          ]
        }
        """);

    public static bool IsValid(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object)
            return false;
        if (!value.TryGetProperty("widgets", out var widgets) || widgets.ValueKind != JsonValueKind.Array)
            return false;
        if (widgets.GetArrayLength() > MaxWidgetCount)
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
            if (!IsValidWidgetId(widgetId))
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

    public static bool IsValidWidgetId(string? widgetId) =>
        !string.IsNullOrWhiteSpace(widgetId) && WidgetIdRegex().IsMatch(widgetId);

    [GeneratedRegex(WidgetIdPattern, RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture)]
    private static partial Regex WidgetIdRegex();

    public static JsonElement Normalize(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("Home layout value must be an object.");
        if (!value.TryGetProperty("widgets", out var widgets) || widgets.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("Home layout value must contain a widgets array.");
        if (!widgets.EnumerateArray().Any(NeedsNormalization)) return value.Clone();

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Encoder = JsonBaseline.Options.Encoder }))
        {
            writer.WriteStartObject();
            foreach (var property in value.EnumerateObject())
            {
                writer.WritePropertyName(property.Name);
                if (property.Name != "widgets") { WriteRaw(property.Value); continue; }
                writer.WriteStartArray();
                foreach (var entry in widgets.EnumerateArray())
                {
                    if (!NeedsNormalization(entry)) { WriteRaw(entry); continue; }
                    writer.WriteStartObject();
                    foreach (var member in entry.EnumerateObject())
                    {
                        // Only these two owned metadata fields change. In particular, extension
                        // widget options retain their original JSON tokens and inner whitespace.
                        if (member.Name is "size" or "visible") continue;
                        writer.WritePropertyName(member.Name);
                        WriteRaw(member.Value);
                    }
                    writer.WriteString("size", NormalizeSize(entry.TryGetProperty("size", out var size) ? size.GetString() : null));
                    writer.WriteBoolean("visible", !entry.TryGetProperty("visible", out var visible) || visible.ValueKind == JsonValueKind.Null || visible.GetBoolean());
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
            }
            writer.WriteEndObject();
            void WriteRaw(JsonElement element) => writer.WriteRawValue(element.GetRawText(), skipInputValidation: true);
        }
        using var document = JsonDocument.Parse(stream.ToArray());
        return document.RootElement.Clone();

        static bool NeedsNormalization(JsonElement entry) => entry.ValueKind == JsonValueKind.Object &&
            (!entry.TryGetProperty("size", out var size) || size.GetString() != NormalizeSize(size.GetString()) ||
             !entry.TryGetProperty("visible", out var visible) || visible.ValueKind == JsonValueKind.Null);
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
