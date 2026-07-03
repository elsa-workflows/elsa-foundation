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
public static class PackageManifestCatalogMapper
{
    /// <summary>Parses manifest bytes and computes a content hash. Returns a <see cref="PackageManifestReadResult"/>.</summary>
    public static PackageManifestReadResult ReadManifestBytes(byte[] bytes, string path)
    {
        // System.Text.Json's byte/span overloads reject a leading UTF-8 BOM ("'0xEF' is an invalid start of a value"),
        // so strip it before deserializing — manifests re-saved by BOM-emitting editors/tooling must still parse.
        var manifest = JsonSerializer.Deserialize<ElsaPackageManifest>(StripByteOrderMark(bytes), ManifestJsonSerializerOptions.Default);
        if (manifest is null)
            return PackageManifestReadResult.Missing("Package manifest was empty.");

        // Hash the original file bytes (BOM included) so the content identity matches the file on disk.
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        return PackageManifestReadResult.Found(path, hash, manifest);
    }

    private static ReadOnlySpan<byte> StripByteOrderMark(ReadOnlySpan<byte> bytes) =>
        bytes is [0xEF, 0xBB, 0xBF, ..] ? bytes[3..] : bytes;

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
        // System.Text.Json overrides an empty-collection initializer with null when the JSON key is *explicitly* null
        // (initializers only apply when the key is absent), so a manifest with `"features": null` deserializes to a null
        // list. Coalesce here — and guard the per-feature collections/dictionaries below — so such a manifest is treated
        // as empty rather than throwing and aborting catalog enumeration for the whole package.
        foreach (var feature in manifest.Features ?? [])
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
            // Gate on the runtime's "was resolved" signal so a loaded feature that genuinely has zero dependencies is
            // not backfilled from a stale manifest, and keep the emptiness check so a second manifest declaring the
            // same feature does not clobber a dependency list an earlier manifest already populated.
            if (!builder.DependenciesResolved && builder.Dependencies.Count == 0 && feature.Dependencies is { Count: > 0 })
                builder.Dependencies = GetDependencies(feature, packageId);
        }
    }

    /// <summary>
    /// The generator qualifies a feature's own <c>[ShellFeature(DependsOn = ...)]</c> references with its declaring
    /// package ID (e.g. <c>"Elsa.JavaScript.JintEngine"</c>), but the runtime feature catalog keys features by their
    /// short CShells ID (e.g. <c>"JintEngine"</c>). Strip that same-package prefix so manifest-declared dependencies
    /// line up with the IDs the cascade (and the runtime-resolved path) actually use.
    /// </summary>
    private static IReadOnlyList<FeatureDependency> GetDependencies(FeatureManifest feature, string? packageId)
    {
        var prefix = string.IsNullOrWhiteSpace(packageId) ? null : packageId + ".";

        return (feature.Dependencies ?? [])
            .Where(dependency => !string.IsNullOrWhiteSpace(dependency.FeatureId))
            .Select(dependency =>
            {
                var featureId = dependency.FeatureId!;
                if (prefix is not null && featureId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    featureId = featureId[prefix.Length..];
                return new FeatureDependency(featureId, dependency.Optional);
            })
            .DistinctBy(dependency => dependency.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<FeatureSettingDescriptor> MapSettings(IReadOnlyList<FeatureSettingManifest>? settings) =>
        (settings ?? [])
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
        Dictionary<string, object?>? ui,
        Dictionary<string, object?>? validation)
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
            .Select(MapOption)
            .OfType<FeatureSettingOptionDescriptor>()
            .ToArray();

    private static FeatureSettingOptionDescriptor? MapOption(JsonElement option)
    {
        // A scalar enum value is itself the option's value.
        if (option.ValueKind is not JsonValueKind.Object)
            return new FeatureSettingOptionDescriptor(JsonValueToDisplayText(option), option.Clone(), null);

        // An option object without a "value" would otherwise map to a default(JsonElement) whose ValueKind is Undefined;
        // that throws when later serialized/GetRawText'd, so skip it — mirroring the pre-refactor `.Where(o => o.Value is not null)`.
        if (!option.TryGetProperty("value", out var value))
            return null;

        return new FeatureSettingOptionDescriptor(
            GetJsonString(option, "label") ?? JsonValueToDisplayText(value),
            value.Clone(),
            GetJsonString(option, "description"));
    }

    private static string JsonValueToDisplayText(JsonElement value) =>
        value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? "",
            JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => value.ToString(),
            JsonValueKind.Null or JsonValueKind.Undefined => "",
            _ => value.GetRawText()
        };

    private static IReadOnlyList<string> MergeCategories(string? category, IReadOnlyList<string>? categories)
    {
        var result = new List<string>();
        if (!string.IsNullOrWhiteSpace(category))
            result.Add(category);

        result.AddRange((categories ?? []).Where(x => !string.IsNullOrWhiteSpace(x)));

        return result.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    // `values` is nullable because System.Text.Json nulls out an initialized dictionary property when its JSON key is
    // explicitly null (e.g. `"ui": null`); guard here so every caller tolerates that without a NullReferenceException.
    private static JsonElement? GetElement(IReadOnlyDictionary<string, object?>? values, string key) =>
        values is not null && values.TryGetValue(key, out var value) && value is JsonElement element ? element : null;

    private static string? GetString(IReadOnlyDictionary<string, object?>? values, string key) =>
        GetElement(values, key) is { ValueKind: JsonValueKind.String } element ? element.GetString() : null;

    private static bool GetBool(IReadOnlyDictionary<string, object?>? values, string key) =>
        GetElement(values, key) is { ValueKind: JsonValueKind.True };

    private static string? GetJsonString(JsonElement element, string property) =>
        element.ValueKind is JsonValueKind.Object && element.TryGetProperty(property, out var value) && value.ValueKind is JsonValueKind.String
            ? value.GetString()
            : null;
}

public sealed record PackageManifestReadResult(string? Path, string? Hash, ElsaPackageManifest? Manifest, string? ReadError)
{
    public static PackageManifestReadResult Found(string path, string hash, ElsaPackageManifest manifest) =>
        new(path, hash, manifest, null);

    public static PackageManifestReadResult Missing(string readError) =>
        new(null, null, null, readError);
}
