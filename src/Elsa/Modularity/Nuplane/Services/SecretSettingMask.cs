using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Modularity.Core.Models;

namespace Elsa.Modularity.Nuplane.Services;

/// <summary>
/// Keeps the values of settings declared <c>[ManifestSetting(Secret = true)]</c> out of the feature catalog. A set
/// secret is replaced by <see cref="Placeholder"/> so the catalog still shows that a value exists; an unset one
/// (null, empty string, empty object or array) passes through so it still reads as unset. When an apply request
/// sends the placeholder back, <see cref="Restore"/> puts the stored value in its place, so a client that
/// round-trips the catalog cannot overwrite a real secret with the mask.
/// </summary>
public static class SecretSettingMask
{
    /// <summary>The value the catalog shows in place of a set secret.</summary>
    public const string Placeholder = "********";

    private static readonly JsonElement s_placeholder = JsonSerializer.SerializeToElement(Placeholder);

    /// <summary>Returns <paramref name="configuration"/> with every set secret setting replaced by the placeholder.</summary>
    public static JsonElement MaskConfiguration(JsonElement configuration, IReadOnlyList<FeatureSettingDescriptor> settings) =>
        Rewrite(configuration, settings, (_, value) => IsUnset(value) ? value : s_placeholder);

    /// <summary>
    /// Returns <paramref name="settings"/> with the default value of every secret setting masked. Defaults come from
    /// the feature type's initializers, which could read the environment rather than hold a literal.
    /// </summary>
    public static IReadOnlyList<FeatureSettingDescriptor> MaskDefaults(IReadOnlyList<FeatureSettingDescriptor> settings) =>
        settings
            .Select(x => x.Secret && x.DefaultValue is { } value && !IsUnset(value) ? x with { DefaultValue = s_placeholder } : x)
            .ToArray();

    /// <summary>
    /// Returns <paramref name="requested"/> with every secret setting that still carries the placeholder replaced by
    /// its value in <paramref name="stored"/>, or removed when nothing is stored for it.
    /// </summary>
    public static JsonElement Restore(JsonElement requested, JsonElement? stored, IReadOnlyList<FeatureSettingDescriptor> settings) =>
        Rewrite(requested, settings, (name, value) =>
            value.ValueKind is JsonValueKind.String && value.GetString() == Placeholder ? FindProperty(stored, name) : value);

    private static JsonElement Rewrite(
        JsonElement configuration,
        IReadOnlyList<FeatureSettingDescriptor> settings,
        Func<string, JsonElement, JsonElement?> rewriteSecret)
    {
        // Configuration binding matches keys case-insensitively, so a secret must be recognised however it is cased.
        var secretNames = settings.Where(x => x.Secret).Select(x => x.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (secretNames.Count == 0 || configuration.ValueKind is not JsonValueKind.Object)
            return configuration;

        var result = new JsonObject();
        foreach (var property in configuration.EnumerateObject())
        {
            var value = secretNames.Contains(property.Name) ? rewriteSecret(property.Name, property.Value) : property.Value;
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
