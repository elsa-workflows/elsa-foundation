using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Modularity.Core.Models;
using Elsa.Modularity.Nuplane.Manifest;

namespace Elsa.Modularity.Nuplane.Services;

/// <summary>
/// Shared mapping from a parsed <c>elsa-package.json</c> manifest onto the feature catalog. Used both by the
/// package contributor (manifests inside installed Nuplane packages) and the bundled contributor (manifests
/// emitted by feature projects the host references directly).
/// </summary>
internal static class PackageManifestCatalogMapper
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>Parses manifest bytes and computes a content hash. Returns a <see cref="PackageManifestReadResult"/>.</summary>
    public static PackageManifestReadResult ReadManifestBytes(byte[] bytes, string path)
    {
        var json = System.Text.Encoding.UTF8.GetString(bytes).TrimStart('﻿');
        var manifest = JsonSerializer.Deserialize<PackageManifest>(json, JsonOptions);
        if (manifest is null)
            return PackageManifestReadResult.Missing("Package manifest was empty.");

        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        return PackageManifestReadResult.Found(path, hash, manifest);
    }

    /// <summary>Adds every feature declared in <paramref name="manifest"/> to the catalog. Existing builders
    /// (e.g. shell-enabled features seeded earlier) are enriched in place; their <c>Enabled</c> state is left untouched.</summary>
    public static void Apply(
        FeatureCatalogContributionContext context,
        PackageManifest manifest,
        string? manifestPath,
        string? manifestHash,
        string? packageId,
        string? packageVersion)
    {
        foreach (var feature in manifest.Features ?? [])
        {
            var featureName = GetStringExtension(feature.Extensions, "cshellsFeatureName") ?? feature.Id;
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
        }
    }

    private static IReadOnlyList<FeatureSettingDescriptor> MapSettings(IReadOnlyList<PackageFeatureSettingManifest>? settings) =>
        settings?
            .Where(setting => !string.IsNullOrWhiteSpace(setting.Name))
            .Select(setting =>
            {
                var advanced = setting.Ui?.Advanced ?? false;
                var experimental = setting.Ui?.Experimental ?? false;
                var sensitive = setting.Sensitive || GetBoolExtension(setting.Extensions, "sensitive");
                var options = MapOptions(setting.Ui?.Options, setting.Validation?.Enum);
                var name = setting.Name!;

                return new FeatureSettingDescriptor(
                    name,
                    string.IsNullOrWhiteSpace(setting.DisplayName) ? name : setting.DisplayName!,
                    setting.Description,
                    setting.Category,
                    setting.Group,
                    setting.ClrType,
                    setting.JsonType,
                    setting.Required,
                    setting.DefaultValue?.Clone(),
                    setting.Secret,
                    sensitive,
                    setting.RestartRequired,
                    advanced,
                    experimental,
                    setting.Ui?.Hint,
                    setting.Ui?.OptionsProvider,
                    options);
            })
            .OrderBy(setting => setting.Category ?? "", StringComparer.OrdinalIgnoreCase)
            .ThenBy(setting => setting.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray()
        ?? [];

    private static IReadOnlyList<FeatureSettingOptionDescriptor> MapOptions(
        IReadOnlyList<PackageFeatureSettingOptionManifest>? uiOptions,
        IReadOnlyList<JsonElement>? validationOptions)
    {
        if (uiOptions is { Count: > 0 })
        {
            return uiOptions
                .Where(option => option.Value is not null)
                .Select(option => new FeatureSettingOptionDescriptor(
                    option.Label ?? JsonElementToDisplayText(option.Value!.Value),
                    option.Value!.Value.Clone(),
                    option.Description))
                .ToArray();
        }

        return validationOptions?
            .Select(option => new FeatureSettingOptionDescriptor(JsonElementToDisplayText(option), option.Clone(), null))
            .ToArray()
        ?? [];
    }

    private static string JsonElementToDisplayText(JsonElement value) =>
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

        if (categories is not null)
            result.AddRange(categories.Where(x => !string.IsNullOrWhiteSpace(x)));

        return result.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string? GetStringExtension(JsonObject? extensions, string name)
    {
        if (extensions is null ||
            !extensions.TryGetPropertyValue(name, out var node) ||
            node is not JsonValue value ||
            !value.TryGetValue<string>(out var text) ||
            string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return text;
    }

    private static bool GetBoolExtension(JsonObject? extensions, string name)
    {
        return extensions is not null &&
            extensions.TryGetPropertyValue(name, out var node) &&
            node is JsonValue value &&
            value.TryGetValue<bool>(out var result) &&
            result;
    }
}

internal sealed record PackageManifestReadResult(string? Path, string? Hash, PackageManifest? Manifest, string? ReadError)
{
    public static PackageManifestReadResult Found(string path, string hash, PackageManifest manifest) =>
        new(path, hash, manifest, null);

    public static PackageManifestReadResult Missing(string readError) =>
        new(null, null, null, readError);
}
