using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Modularity.Core.Models;

namespace Elsa.Modularity.Nuplane.Services;

/// <summary>
/// Keeps secret configuration values out of the feature catalog. It fails closed: a value is shown only when a setting
/// declares it and does not mark it <c>[ManifestSetting(Secret = true)]</c>. Everything else is replaced by
/// <see cref="Placeholder"/>: secret settings, and keys no setting declares, which covers a feature whose package is
/// gone, an object-typed setting the manifest cannot describe, and colon-path keys such as <c>Keys:primary</c>. A
/// hidden value that is unset (null, empty string, empty object or array) passes through so it still reads as unset.
/// When an apply request sends the placeholder back, <see cref="Restore"/> puts the stored value in its place, so a
/// client that round-trips the catalog cannot overwrite a real secret with the mask.
/// </summary>
public static class SecretSettingMask
{
    /// <summary>The value the catalog shows in place of a hidden value.</summary>
    public const string Placeholder = "********";

    private static readonly JsonElement s_placeholder = JsonSerializer.SerializeToElement(Placeholder);

    /// <summary>Returns <paramref name="configuration"/> with every set hidden value replaced by the placeholder.</summary>
    public static JsonElement MaskConfiguration(JsonElement configuration, IReadOnlyList<FeatureSettingDescriptor> settings)
    {
        // Configuration binding matches keys case-insensitively, so a name must be recognised however it is cased. A
        // name any source declares secret stays hidden even if another declares it plain.
        var visibleNames = settings.Where(x => !x.Secret).Select(x => x.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        visibleNames.ExceptWith(settings.Where(x => x.Secret).Select(x => x.Name));

        return Rewrite(configuration, visibleNames.Contains, (_, value) => IsUnset(value) ? value : s_placeholder);
    }

    /// <summary>
    /// Returns <paramref name="settings"/> with the default value of every secret setting masked. Defaults come from
    /// the feature type's initializers, which could read the environment rather than hold a literal.
    /// </summary>
    public static IReadOnlyList<FeatureSettingDescriptor> MaskDefaults(IReadOnlyList<FeatureSettingDescriptor> settings) =>
        settings
            .Select(x => x.Secret && x.DefaultValue is { } value && !IsUnset(value) ? x with { DefaultValue = s_placeholder } : x)
            .ToArray();

    /// <summary>
    /// Returns <paramref name="requested"/> with every value that still carries the placeholder replaced by its value in
    /// <paramref name="stored"/>, or removed when nothing is stored for it. The placeholder means "unchanged" whatever
    /// the key's current visibility: which keys are hidden follows the installed packages, which can change between a
    /// client's read and its apply without changing the revision, and the placeholder must never be saved as a value.
    /// </summary>
    public static JsonElement Restore(JsonElement requested, JsonElement? stored) =>
        Rewrite(requested, _ => false, (name, value) =>
            value.ValueKind is JsonValueKind.String && value.GetString() == Placeholder ? FindProperty(stored, name) : value);

    private static JsonElement Rewrite(JsonElement configuration, Func<string, bool> isVisible, Func<string, JsonElement, JsonElement?> rewriteHidden)
    {
        if (configuration.ValueKind is not JsonValueKind.Object)
            return configuration;

        var result = new JsonObject();
        foreach (var property in configuration.EnumerateObject())
        {
            var value = isVisible(property.Name) ? property.Value : rewriteHidden(property.Name, property.Value);
            if (value is { } kept)
                result[property.Name] = JsonNode.Parse(kept.GetRawText());
        }

        return JsonSerializer.SerializeToElement(result);
    }

    private static JsonElement? FindProperty(JsonElement? configuration, string name) =>
        configuration is { ValueKind: JsonValueKind.Object } obj
            ? obj.EnumerateObject()
                .Where(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase))
                .Select(x => (JsonElement?)x.Value)
                .FirstOrDefault()
            : null;

    private static bool IsUnset(JsonElement value) =>
        value.ValueKind switch
        {
            JsonValueKind.Null or JsonValueKind.Undefined => true,
            JsonValueKind.String => value.GetString() is "",
            JsonValueKind.Object => !value.EnumerateObject().Any(),
            JsonValueKind.Array => value.GetArrayLength() == 0,
            _ => false
        };
}
