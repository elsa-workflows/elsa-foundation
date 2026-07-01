using System.Security.Cryptography;
using System.Text.Json;
using Elsa.Modularity.Core.Models;
using Elsa.Platform.PackageManifests;

namespace Elsa.Modularity.Nuplane.Services;

/// <summary>
/// Shared mapping from a parsed <c>elsa-package.json</c> manifest onto the feature catalog, using the same
/// <c>Elsa.Platform.PackageManifests</c> wire contract the generator that produces these files is built against.
/// Used both by the package contributor (manifests inside installed Nuplane packages) and the bundled contributor
/// (manifests emitted by feature projects the host references directly).
/// </summary>
internal static class PackageManifestCatalogMapper
{
    /// <summary>Parses manifest bytes and computes a content hash. Returns a <see cref="PackageManifestReadResult"/>.</summary>
    public static PackageManifestReadResult ReadManifestBytes(byte[] bytes, string path)
    {
        var manifest = JsonSerializer.Deserialize<ElsaPackageManifest>(bytes, ManifestJsonSerializerOptions.Default);
        if (manifest is null)
            return PackageManifestReadResult.Missing("Package manifest was empty.");

        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        return PackageManifestReadResult.Found(path, hash, manifest);
    }

    /// <summary>Adds every feature declared in <paramref name="manifest"/> to the catalog. Existing builders
    /// (e.g. shell-enabled features seeded earlier) are enriched in place; their <c>Enabled</c> state is left untouched.</summary>
    public static void Apply(
        FeatureCatalogContributionContext context,
        ElsaPackageManifest manifest,
        string? manifestPath,
        string? manifestHash,
        string? packageId,
        string? packageVersion)
    {
        foreach (var feature in manifest.Features)
        {
            var featureName = GetString(feature.Extensions, "cshellsFeatureName");
            if (string.IsNullOrWhiteSpace(featureName))
                featureName = feature.Id;
            if (string.IsNullOrWhiteSpace(featureName))
                continue;

            var builder = context.GetOrAdd(featureName);
            builder.SourceKind = FeatureSourceKinds.Manifest;
            builder.DisplayName = string.IsNullOrWhiteSpace(feature.DisplayName) ? featureName : feature.DisplayName;
            builder.Description = feature.Description ?? builder.Description;
            builder.Categories = MergeCategories(feature.Category, feature.Categories);
            builder.PackageId = packageId;
            builder.PackageVersion = packageVersion;
            builder.Advanced = feature.Advanced;
            builder.Experimental = feature.Experimental;
            builder.Settings = MapSettings(feature.Settings);
            builder.ManifestPath = manifestPath;
            builder.ManifestHash = manifestHash;

            // The runtime contributor (which resolves the live CShells dependency graph) runs before this one and
            // wins when a feature is currently loaded; this only fills the gap for disabled/not-yet-loaded features.
            if (builder.Dependencies.Count == 0 && feature.Dependencies.Count > 0)
                builder.Dependencies = GetDependencies(feature, packageId);
        }
    }

    /// <summary>
    /// The generator qualifies a feature's own <c>[ShellFeature(DependsOn = ...)]</c> references with its declaring
    /// package ID (e.g. <c>"Elsa.JavaScript.JintEngine"</c>), but the runtime feature catalog keys features by their
    /// short CShells ID (e.g. <c>"JintEngine"</c>). Strip that same-package prefix so manifest-declared dependencies
    /// line up with the IDs the cascade (and the runtime-resolved path) actually use.
    /// </summary>
    private static IReadOnlyList<string> GetDependencies(FeatureManifest feature, string? packageId)
    {
        var prefix = string.IsNullOrWhiteSpace(packageId) ? null : packageId + ".";

        return feature.Dependencies
            .Select(dependency => dependency.FeatureId)
            .Where(featureId => !string.IsNullOrWhiteSpace(featureId))
            .Select(featureId => prefix is not null && featureId!.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                ? featureId[prefix.Length..]
                : featureId!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<FeatureSettingDescriptor> MapSettings(IReadOnlyList<FeatureSettingManifest> settings) =>
        settings
            .Where(setting => !string.IsNullOrWhiteSpace(setting.Name))
            .Select(ToSetting)
            .OrderBy(setting => setting.Category ?? "", StringComparer.OrdinalIgnoreCase)
            .ThenBy(setting => setting.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static FeatureSettingDescriptor ToSetting(FeatureSettingManifest setting)
    {
        var (options, optionsProvider) = GetSettingOptions(setting.UI, setting.Validation);
        var name = setting.Name;

        return new FeatureSettingDescriptor(
            name,
            string.IsNullOrWhiteSpace(setting.DisplayName) ? name : setting.DisplayName,
            setting.Description,
            setting.Category,
            GetString(setting.UI, "group"),
            setting.ClrType,
            setting.JsonType,
            setting.Required,
            setting.DefaultValue is JsonElement defaultValue ? defaultValue.Clone() : null,
            setting.Secret,
            GetBool(setting.Extensions, "sensitive"),
            setting.RestartRequired,
            GetBool(setting.UI, "advanced"),
            GetBool(setting.UI, "experimental"),
            GetString(setting.UI, "hint"),
            optionsProvider,
            options);
    }

    /// <summary>
    /// The generator nests static options under <c>ui.options.items</c> and provider-backed options under
    /// <c>ui.options.provider</c> (with <c>ui.options.source</c> discriminating the two), then falls back to
    /// <c>validation.enum</c>.
    /// </summary>
    private static (IReadOnlyList<FeatureSettingOptionDescriptor> Options, string? OptionsProvider) GetSettingOptions(
        Dictionary<string, object?> ui,
        Dictionary<string, object?> validation)
    {
        if (GetElement(ui, "options") is { ValueKind: JsonValueKind.Object } optionsElement)
        {
            if (string.Equals(GetJsonString(optionsElement, "source"), "provider", StringComparison.OrdinalIgnoreCase))
                return ([], GetJsonString(optionsElement, "provider"));

            if (optionsElement.TryGetProperty("items", out var items) && items.ValueKind is JsonValueKind.Array)
                return (MapOptions(items), null);
        }

        if (GetElement(validation, "enum") is { ValueKind: JsonValueKind.Array } enumValues)
            return (MapOptions(enumValues), null);

        return ([], null);
    }

    private static IReadOnlyList<FeatureSettingOptionDescriptor> MapOptions(JsonElement options) =>
        options.EnumerateArray()
            .Select(option => option.ValueKind is JsonValueKind.Object
                ? new FeatureSettingOptionDescriptor(
                    GetJsonString(option, "label") ?? (option.TryGetProperty("value", out var v) ? JsonValueToDisplayText(v) : ""),
                    option.TryGetProperty("value", out var value) ? value.Clone() : default,
                    GetJsonString(option, "description"))
                : new FeatureSettingOptionDescriptor(JsonValueToDisplayText(option), option.Clone(), null))
            .ToArray();

    private static string JsonValueToDisplayText(JsonElement value) =>
        value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? "",
            JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => value.ToString(),
            JsonValueKind.Null or JsonValueKind.Undefined => "",
            _ => value.GetRawText()
        };

    private static IReadOnlyList<string> MergeCategories(string? category, IReadOnlyList<string> categories)
    {
        var result = new List<string>();
        if (!string.IsNullOrWhiteSpace(category))
            result.Add(category);

        result.AddRange(categories.Where(x => !string.IsNullOrWhiteSpace(x)));

        return result.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static JsonElement? GetElement(IReadOnlyDictionary<string, object?> values, string key) =>
        values.TryGetValue(key, out var value) && value is JsonElement element ? element : null;

    private static string? GetString(IReadOnlyDictionary<string, object?> values, string key) =>
        GetElement(values, key) is { ValueKind: JsonValueKind.String } element ? element.GetString() : null;

    private static bool GetBool(IReadOnlyDictionary<string, object?> values, string key) =>
        GetElement(values, key) is { ValueKind: JsonValueKind.True };

    private static string? GetJsonString(JsonElement element, string property) =>
        element.ValueKind is JsonValueKind.Object && element.TryGetProperty(property, out var value) && value.ValueKind is JsonValueKind.String
            ? value.GetString()
            : null;
}

internal sealed record PackageManifestReadResult(string? Path, string? Hash, ElsaPackageManifest? Manifest, string? ReadError)
{
    public static PackageManifestReadResult Found(string path, string hash, ElsaPackageManifest manifest) =>
        new(path, hash, manifest, null);

    public static PackageManifestReadResult Missing(string readError) =>
        new(null, null, null, readError);
}
