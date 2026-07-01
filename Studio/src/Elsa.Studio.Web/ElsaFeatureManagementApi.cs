using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CShells.Features;
using CShells.Lifecycle;
using Elsa.Platform.PackageManifests;
using Microsoft.AspNetCore.Mvc;
using Nuplane.Abstractions;
using Nuplane.Admin;

namespace Elsa.Studio.Web;

internal static class ElsaFeatureManagementApi
{
    public static IEndpointRouteBuilder MapElsaFeatureManagementApi(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/modularity/features", GetFeaturesAsync);
        endpoints.MapPost("/modularity/features/apply", ApplyFeaturesAsync);
        return endpoints;
    }

    private static async Task<IResult> GetFeaturesAsync(
        [FromServices] StudioShellFeatureConfigurationStore shellStore,
        [FromServices] StudioRuntimeFeatureCatalogRefresher runtimeFeatureCatalog,
        [FromServices] INuplaneAdminOperations nuplaneAdmin,
        CancellationToken cancellationToken)
    {
        var shell = await shellStore.LoadAsync(cancellationToken);
        var descriptors = await runtimeFeatureCatalog.GetFeatureDescriptorsAsync(cancellationToken);
        var packages = await nuplaneAdmin.GetPackagesAsync(cancellationToken);
        return Results.Ok(BuildCatalog(shell, descriptors, packages.Packages));
    }

    private static async Task<IResult> ApplyFeaturesAsync(
        [FromBody] FeatureManagementApplyRequest request,
        [FromServices] StudioShellFeatureConfigurationStore shellStore,
        [FromServices] StudioRuntimeFeatureCatalogRefresher runtimeFeatureCatalog,
        [FromServices] INuplaneAdminOperations nuplaneAdmin,
        [FromServices] IShellRegistry shellRegistry,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Revision))
            return Results.BadRequest(new FeatureManagementErrorResponse("Revision is required."));

        var currentShell = await shellStore.LoadAsync(cancellationToken);
        var currentDescriptors = await runtimeFeatureCatalog.GetFeatureDescriptorsAsync(cancellationToken);
        var currentPackages = await nuplaneAdmin.GetPackagesAsync(cancellationToken);
        var currentCatalog = BuildCatalog(currentShell, currentDescriptors, currentPackages.Packages);
        var knownFeatureIds = currentCatalog.Features.Select(feature => feature.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var feature in request.Features)
        {
            if (string.IsNullOrWhiteSpace(feature.Id))
                return Results.BadRequest(new FeatureManagementErrorResponse("Feature ID is required."));

            if (feature.Enabled && feature.Configuration.ValueKind is not JsonValueKind.Object)
                return Results.BadRequest(new FeatureManagementErrorResponse($"Feature '{feature.Id}' configuration must be a JSON object."));
        }

        StudioShellFeatureConfigurationSnapshot savedShell;
        try
        {
            savedShell = await shellStore.SaveAsync(
                request.Revision,
                request.Features.Where(feature => knownFeatureIds.Contains(feature.Id)).ToArray(),
                cancellationToken);
        }
        catch (StudioFeatureCatalogRevisionConflictException e)
        {
            return Results.Conflict(new FeatureManagementErrorResponse($"Expected revision {e.ExpectedRevision} but found {e.ActualRevision}."));
        }

        var reloadResults = await shellRegistry.ReloadActiveAsync(null, cancellationToken);
        var reloadedShellCount = reloadResults.Count(result => result.Error is null);
        var savedPackages = await nuplaneAdmin.GetPackagesAsync(cancellationToken);
        var descriptors = await runtimeFeatureCatalog.GetFeatureDescriptorsAsync(cancellationToken);
        var catalog = BuildCatalog(savedShell, descriptors, savedPackages.Packages);

        return Results.Ok(new FeatureManagementApplyResultResponse(
            catalog,
            descriptors.Count,
            reloadedShellCount));
    }

    private static FeatureManagementCatalogResponse BuildCatalog(
        StudioShellFeatureConfigurationSnapshot shell,
        IReadOnlyCollection<ShellFeatureDescriptor> descriptors,
        IEnumerable<ActivePackage> packages)
    {
        var descriptorById = descriptors.ToDictionary(descriptor => descriptor.Id, StringComparer.OrdinalIgnoreCase);
        var manifestById = GetPackageManifestFeatures(packages)
            .GroupBy(feature => feature.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var featureIds = descriptorById.Keys
            .Concat(manifestById.Keys)
            .Concat(shell.Features.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        var features = featureIds
            .Select(id => ToCatalogItem(id, shell, descriptorById, manifestById))
            .OrderByDescending(feature => feature.Enabled)
            .ThenBy(feature => feature.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(feature => feature.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new FeatureManagementCatalogResponse(shell.Revision, features);
    }

    private static FeatureManagementCatalogItemResponse ToCatalogItem(
        string id,
        StudioShellFeatureConfigurationSnapshot shell,
        IReadOnlyDictionary<string, ShellFeatureDescriptor> descriptors,
        IReadOnlyDictionary<string, FeatureManagementManifestFeature> manifests)
    {
        var hasDescriptor = descriptors.TryGetValue(id, out var descriptor);
        var hasManifest = manifests.TryGetValue(id, out var manifest);
        var enabled = shell.Features.TryGetValue(id, out var configuration);

        return new FeatureManagementCatalogItemResponse(
            id,
            manifest?.DisplayName ?? ReadMetadataString(descriptor, "DisplayName") ?? id,
            manifest?.Description ?? ReadMetadataString(descriptor, "Description"),
            manifest?.Categories ?? ["Studio"],
            hasManifest ? "manifest" : "shell",
            manifest?.PackageId,
            manifest?.PackageVersion,
            enabled,
            enabled ? configuration : EmptyJsonObject(),
            manifest?.Advanced ?? false,
            manifest?.Experimental ?? false,
            manifest?.ManifestPath,
            manifest?.ManifestHash,
            hasDescriptor || hasManifest ? null : "Feature is enabled in shells.json but no runtime descriptor is currently available.",
            manifest?.Settings ?? [],
            descriptor?.Dependencies is { Count: > 0 } dependencies ? dependencies : manifest?.Dependencies ?? []);
    }

    /// <summary>
    /// Reads <c>elsa-package.json</c> manifests using the shared <c>Elsa.Platform.PackageManifests</c> wire contract
    /// (the same models the generator that produces these files is built against), instead of hand-parsing raw JSON.
    /// </summary>
    private static IEnumerable<FeatureManagementManifestFeature> GetPackageManifestFeatures(IEnumerable<ActivePackage> packages)
    {
        foreach (var package in packages)
        {
            var manifestFile = ModuleManagementPackageManifest.Read(package.InstallPath);
            if (manifestFile?.Content is null)
                continue;

            ElsaPackageManifest? manifest;
            try
            {
                manifest = manifestFile.Content.Deserialize<ElsaPackageManifest>(ManifestJsonSerializerOptions.Default);
            }
            catch (JsonException)
            {
                continue;
            }

            if (manifest is null)
                continue;

            var packageId = string.IsNullOrWhiteSpace(manifest.Package.Id) ? package.PackageId : manifest.Package.Id;
            var packageVersion = string.IsNullOrWhiteSpace(manifest.Package.Version) ? package.Version : manifest.Package.Version;
            var manifestHash = ComputeHash(manifestFile.Content.ToJsonString());

            foreach (var feature in manifest.Features)
            {
                var featureId = GetString(feature.Extensions, "cshellsFeatureName");
                if (string.IsNullOrWhiteSpace(featureId))
                    featureId = feature.Id;
                if (string.IsNullOrWhiteSpace(featureId))
                    continue;

                yield return new FeatureManagementManifestFeature(
                    featureId,
                    string.IsNullOrWhiteSpace(feature.DisplayName) ? featureId : feature.DisplayName,
                    feature.Description,
                    GetCategories(feature),
                    packageId,
                    packageVersion,
                    feature.Advanced,
                    feature.Experimental,
                    manifestFile.Path,
                    manifestHash,
                    GetSettings(feature.Settings),
                    GetDependencies(feature, packageId));
            }
        }
    }

    private static IReadOnlyList<string> GetCategories(FeatureManifest feature)
    {
        var categories = new List<string>();
        if (!string.IsNullOrWhiteSpace(feature.Category))
            categories.Add(feature.Category);

        categories.AddRange(feature.Categories.Where(value => !string.IsNullOrWhiteSpace(value)));

        return categories
            .DefaultIfEmpty("Studio")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// The generator qualifies a feature's own <c>[ShellFeature(DependsOn = ...)]</c> references with its declaring
    /// package ID (e.g. <c>"Elsa.JavaScript.JintEngine"</c>), but the runtime feature catalog keys features by their
    /// short CShells ID (e.g. <c>"JintEngine"</c>). Strip that same-package prefix so manifest-declared dependencies
    /// line up with the IDs the cascade (and the runtime-resolved path) actually uses.
    /// </summary>
    private static IReadOnlyList<string> GetDependencies(FeatureManifest feature, string packageId)
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

    private static IReadOnlyList<FeatureManagementSettingResponse> GetSettings(IReadOnlyList<FeatureSettingManifest> settings) =>
        settings
            .Where(setting => !string.IsNullOrWhiteSpace(setting.Name))
            .Select(ToSetting)
            .OrderBy(setting => setting.Category ?? "", StringComparer.OrdinalIgnoreCase)
            .ThenBy(setting => setting.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static FeatureManagementSettingResponse ToSetting(FeatureSettingManifest setting)
    {
        var (options, optionsProvider) = GetSettingOptions(setting.UI, setting.Validation);

        return new(
            setting.Name,
            string.IsNullOrWhiteSpace(setting.DisplayName) ? setting.Name : setting.DisplayName,
            setting.Description,
            setting.Category,
            GetString(setting.UI, "group"),
            setting.ClrType,
            setting.JsonType,
            setting.Required,
            ToJsonNode(setting.DefaultValue),
            setting.Secret,
            GetBool(setting.Extensions, "sensitive"),
            setting.RestartRequired,
            GetBool(setting.UI, "advanced"),
            GetBool(setting.UI, "experimental"),
            GetString(setting.UI, "hint"),
            optionsProvider ?? GetString(setting.UI, "optionsProvider"),
            options);
    }

    /// <summary>
    /// The generator nests static options under <c>ui.options.items</c> and provider-backed options under
    /// <c>ui.options.provider</c> (with <c>ui.options.source</c> discriminating the two); this also accepts a flat
    /// <c>ui.options</c> array for forward/backward compatibility, then falls back to <c>validation.enum</c>.
    /// </summary>
    private static (IReadOnlyList<FeatureManagementSettingOptionResponse> Options, string? OptionsProvider) GetSettingOptions(
        Dictionary<string, object?> ui,
        Dictionary<string, object?> validation)
    {
        if (GetElement(ui, "options") is { } optionsElement)
        {
            if (optionsElement.ValueKind is JsonValueKind.Object)
            {
                if (string.Equals(GetJsonString(optionsElement, "source"), "provider", StringComparison.OrdinalIgnoreCase))
                    return ([], GetJsonString(optionsElement, "provider"));

                if (optionsElement.TryGetProperty("items", out var items) && items.ValueKind is JsonValueKind.Array)
                    return (MapOptions(items), null);
            }
            else if (optionsElement.ValueKind is JsonValueKind.Array)
            {
                return (MapOptions(optionsElement), null);
            }
        }

        if (GetElement(validation, "enum") is { ValueKind: JsonValueKind.Array } enumValues)
            return (MapOptions(enumValues), null);

        return ([], null);
    }

    private static IReadOnlyList<FeatureManagementSettingOptionResponse> MapOptions(JsonElement options) =>
        options.EnumerateArray()
            .Select(option => option.ValueKind is JsonValueKind.Object
                ? new FeatureManagementSettingOptionResponse(
                    GetJsonString(option, "label") ?? (option.TryGetProperty("value", out var v) ? JsonValueToDisplayText(v) : ""),
                    option.TryGetProperty("value", out var value) ? ToJsonNode(value) : null,
                    GetJsonString(option, "description"))
                : new FeatureManagementSettingOptionResponse(JsonValueToDisplayText(option), ToJsonNode(option), null))
            .ToArray();

    private static string JsonValueToDisplayText(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString() ?? "",
        JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => value.ToString(),
        JsonValueKind.Null or JsonValueKind.Undefined => "",
        _ => value.GetRawText()
    };

    private static string? ReadMetadataString(ShellFeatureDescriptor? descriptor, string key) =>
        descriptor?.Metadata.TryGetValue(key, out var value) == true
            ? value?.ToString()
            : null;

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

    private static JsonNode? ToJsonNode(object? value) => value is JsonElement element ? ToJsonNode(element) : null;

    private static JsonNode? ToJsonNode(JsonElement element) =>
        element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined ? null : JsonNode.Parse(element.GetRawText());

    private static JsonElement EmptyJsonObject()
    {
        using var document = JsonDocument.Parse("{}");
        return document.RootElement.Clone();
    }

    private static string ComputeHash(string text)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}

internal sealed record FeatureManagementApplyRequest(
    string Revision,
    IReadOnlyList<FeatureManagementApplyItemRequest> Features);

internal sealed record FeatureManagementApplyItemRequest(
    string Id,
    bool Enabled,
    JsonElement Configuration);

internal sealed record FeatureManagementApplyResultResponse(
    FeatureManagementCatalogResponse Catalog,
    int FeatureDescriptorCount,
    int ReloadedShellCount);

internal sealed record FeatureManagementManifestFeature(
    string Id,
    string DisplayName,
    string? Description,
    IReadOnlyList<string> Categories,
    string? PackageId,
    string? PackageVersion,
    bool Advanced,
    bool Experimental,
    string? ManifestPath,
    string? ManifestHash,
    IReadOnlyList<FeatureManagementSettingResponse> Settings,
    IReadOnlyList<string> Dependencies);

internal sealed record FeatureManagementCatalogResponse(
    string Revision,
    IReadOnlyList<FeatureManagementCatalogItemResponse> Features);

internal sealed record FeatureManagementCatalogItemResponse(
    string Id,
    string DisplayName,
    string? Description,
    IReadOnlyList<string> Categories,
    string SourceKind,
    string? PackageId,
    string? PackageVersion,
    bool Enabled,
    JsonElement Configuration,
    bool Advanced,
    bool Experimental,
    string? ManifestPath,
    string? ManifestHash,
    string? ReadError,
    IReadOnlyList<FeatureManagementSettingResponse> Settings,
    IReadOnlyList<string> Dependencies);

internal sealed record FeatureManagementSettingResponse(
    string Name,
    string DisplayName,
    string? Description,
    string? Category,
    string? Group,
    string? ClrType,
    string? JsonType,
    bool Required,
    JsonNode? DefaultValue,
    bool Secret,
    bool Sensitive,
    bool RestartRequired,
    bool Advanced,
    bool Experimental,
    string? UiHint,
    string? OptionsProvider,
    IReadOnlyList<FeatureManagementSettingOptionResponse> Options);

internal sealed record FeatureManagementSettingOptionResponse(
    string Label,
    JsonNode? Value,
    string? Description);

internal sealed record FeatureManagementErrorResponse(string Error);
