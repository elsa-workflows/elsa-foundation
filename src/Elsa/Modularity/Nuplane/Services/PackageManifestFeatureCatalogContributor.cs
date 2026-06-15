using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Modularity.Core.Contracts;
using Elsa.Modularity.Core.Models;
using Elsa.Modularity.Nuplane.Manifest;
using Nuplane.Admin;

namespace Elsa.Modularity.Nuplane.Services;

public sealed class PackageManifestFeatureCatalogContributor(INuplaneAdminOperations nuplaneAdmin) : IFeatureCatalogContributor
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task ContributeAsync(FeatureCatalogContributionContext context, CancellationToken cancellationToken = default)
    {
        var packages = await nuplaneAdmin.GetPackagesAsync(cancellationToken);
        foreach (var package in packages.Packages)
        {
            var manifest = ReadManifest(package.InstallPath);
            if (manifest.Manifest is null)
            {
                var error = context.GetOrAdd($"{package.PackageId}:{package.Version}");
                error.SourceKind = FeatureSourceKinds.ManifestError;
                error.DisplayName = package.PackageId;
                error.PackageId = package.PackageId;
                error.PackageVersion = package.Version;
                error.ReadError = manifest.ReadError ?? "Package manifest not found.";
                continue;
            }

            foreach (var feature in manifest.Manifest.Features ?? [])
            {
                var featureName = GetStringExtension(feature.Extensions, "cshellsFeatureName") ?? feature.Id;
                if (string.IsNullOrWhiteSpace(featureName))
                    continue;

                var builder = context.GetOrAdd(featureName);
                builder.SourceKind = FeatureSourceKinds.Manifest;
                builder.DisplayName = string.IsNullOrWhiteSpace(feature.DisplayName) ? featureName : feature.DisplayName;
                builder.Description = feature.Description ?? builder.Description;
                builder.Categories = MergeCategories(feature.Category, feature.Categories);
                builder.PackageId = manifest.Manifest.Package?.Id ?? package.PackageId;
                builder.PackageVersion = manifest.Manifest.Package?.Version ?? package.Version;
                builder.Advanced = feature.Advanced;
                builder.Experimental = feature.Experimental;
                builder.Settings = MapSettings(feature.Settings);
                builder.ManifestPath = manifest.Path;
                builder.ManifestHash = manifest.Hash;
            }
        }
    }

    internal static PackageManifestReadResult ReadManifest(string installPath)
    {
        try
        {
            if (Directory.Exists(installPath))
            {
                var directPath = Path.Combine(installPath, "elsa-package.json");
                if (File.Exists(directPath))
                    return ReadManifestBytes(File.ReadAllBytes(directPath), "elsa-package.json");

                var buildPath = Path.Combine(installPath, "build", "elsa-package.json");
                if (File.Exists(buildPath))
                    return ReadManifestBytes(File.ReadAllBytes(buildPath), "build/elsa-package.json");

                var packageArchive = Directory.EnumerateFiles(installPath, "*.nupkg", SearchOption.TopDirectoryOnly).FirstOrDefault();
                if (packageArchive is not null)
                    return ReadPackageArchive(packageArchive);
            }

            if (File.Exists(installPath) && string.Equals(Path.GetExtension(installPath), ".nupkg", StringComparison.OrdinalIgnoreCase))
                return ReadPackageArchive(installPath);

            return PackageManifestReadResult.Missing("Package manifest not found.");
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or UnauthorizedAccessException)
        {
            return PackageManifestReadResult.Missing(ex.Message);
        }
    }

    private static PackageManifestReadResult ReadPackageArchive(string archivePath)
    {
        using var stream = File.OpenRead(archivePath);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var selected = archive.Entries.FirstOrDefault(x => IsRootManifestPath(x.FullName))
            ?? archive.Entries.FirstOrDefault(x => IsFallbackManifestPath(x.FullName));

        if (selected is null)
            return PackageManifestReadResult.Missing("Package manifest not found.");

        using var manifestStream = selected.Open();
        using var memory = new MemoryStream();
        manifestStream.CopyTo(memory);
        return ReadManifestBytes(memory.ToArray(), NormalizePath(selected.FullName));
    }

    private static PackageManifestReadResult ReadManifestBytes(byte[] bytes, string path)
    {
        var json = System.Text.Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF');
        var manifest = JsonSerializer.Deserialize<PackageManifest>(json, JsonOptions);
        if (manifest is null)
            return PackageManifestReadResult.Missing("Package manifest was empty.");

        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        return PackageManifestReadResult.Found(path, hash, manifest);
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

    private static bool IsRootManifestPath(string path) =>
        string.Equals(NormalizePath(path), "elsa-package.json", StringComparison.OrdinalIgnoreCase);

    private static bool IsFallbackManifestPath(string path) =>
        string.Equals(NormalizePath(path), "build/elsa-package.json", StringComparison.OrdinalIgnoreCase);

    private static string NormalizePath(string path) =>
        path.Replace('\\', '/').TrimStart('/');
}

internal sealed record PackageManifestReadResult(string? Path, string? Hash, PackageManifest? Manifest, string? ReadError)
{
    public static PackageManifestReadResult Found(string path, string hash, PackageManifest manifest) =>
        new(path, hash, manifest, null);

    public static PackageManifestReadResult Missing(string readError) =>
        new(null, null, null, readError);
}
