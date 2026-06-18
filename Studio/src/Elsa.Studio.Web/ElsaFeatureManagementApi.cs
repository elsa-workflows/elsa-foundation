using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CShells.Features;
using CShells.Lifecycle;
using Elsa.Studio.Api.Features;
using Elsa.Studio.ConsoleStream;
using Elsa.Studio.FeatureManagement;
using Elsa.Studio.Samples.Dashboard;
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
        [FromServices] StudioNuplaneAssemblyProvider assemblyProvider,
        [FromServices] IServiceProvider serviceProvider,
        [FromServices] INuplaneAdminOperations nuplaneAdmin,
        CancellationToken cancellationToken)
    {
        var shell = await shellStore.LoadAsync(cancellationToken);
        var descriptors = await GetFeatureDescriptorsAsync(assemblyProvider, serviceProvider, cancellationToken);
        var packages = await nuplaneAdmin.GetPackagesAsync(cancellationToken);
        return Results.Ok(BuildCatalog(shell, descriptors, packages.Packages));
    }

    private static async Task<IResult> ApplyFeaturesAsync(
        [FromBody] FeatureManagementApplyRequest request,
        [FromServices] StudioShellFeatureConfigurationStore shellStore,
        [FromServices] StudioNuplaneAssemblyProvider assemblyProvider,
        [FromServices] IServiceProvider serviceProvider,
        [FromServices] INuplaneAdminOperations nuplaneAdmin,
        [FromServices] IShellRegistry shellRegistry,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Revision))
            return Results.BadRequest(new FeatureManagementErrorResponse("Revision is required."));

        var currentShell = await shellStore.LoadAsync(cancellationToken);
        var currentDescriptors = await GetFeatureDescriptorsAsync(assemblyProvider, serviceProvider, cancellationToken);
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
        var descriptors = await GetFeatureDescriptorsAsync(assemblyProvider, serviceProvider, cancellationToken);
        var catalog = BuildCatalog(savedShell, descriptors, savedPackages.Packages);

        return Results.Ok(new FeatureManagementApplyResultResponse(
            catalog,
            descriptors.Count,
            reloadedShellCount));
    }

    private static async Task<IReadOnlyCollection<ShellFeatureDescriptor>> GetFeatureDescriptorsAsync(
        StudioNuplaneAssemblyProvider assemblyProvider,
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken)
    {
        var packageAssemblies = await assemblyProvider.GetAssembliesAsync(serviceProvider, cancellationToken);
        var assemblies = new[]
            {
                typeof(StudioApiFeature).Assembly,
                typeof(ConsoleStreamStudioFeature).Assembly,
                typeof(FeatureManagementStudioFeature).Assembly,
                typeof(DashboardStudioFeature).Assembly
            }
            .Concat(packageAssemblies)
            .Distinct()
            .ToArray();

        return FeatureDiscovery.DiscoverFeatures(assemblies).ToArray();
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
            manifest?.Settings ?? []);
    }

    private static IEnumerable<FeatureManagementManifestFeature> GetPackageManifestFeatures(IEnumerable<ActivePackage> packages)
    {
        foreach (var package in packages)
        {
            var manifest = ModuleManagementPackageManifest.Read(package.InstallPath);
            if (manifest?.Content is not JsonObject manifestObject)
                continue;

            var manifestPackage = manifestObject["package"] as JsonObject;
            var packageId = ReadString(manifestPackage, "id") ?? package.PackageId;
            var packageVersion = ReadString(manifestPackage, "version") ?? package.Version;

            if (manifestObject["features"] is not JsonArray features)
                continue;

            foreach (var featureNode in features.OfType<JsonObject>())
            {
                var featureId = ReadString(featureNode["extensions"] as JsonObject, "cshellsFeatureName")
                    ?? ReadString(featureNode, "id");

                if (string.IsNullOrWhiteSpace(featureId))
                    continue;

                yield return new FeatureManagementManifestFeature(
                    featureId,
                    ReadString(featureNode, "displayName") ?? featureId,
                    ReadString(featureNode, "description"),
                    GetCategories(featureNode),
                    packageId,
                    packageVersion,
                    ReadBool(featureNode, "advanced"),
                    ReadBool(featureNode, "experimental"),
                    manifest.Path,
                    ComputeHash(manifestObject.ToJsonString()),
                    GetSettings(featureNode));
            }
        }
    }

    private static IReadOnlyList<string> GetCategories(JsonObject feature)
    {
        var categories = new List<string>();
        var category = ReadString(feature, "category");
        if (!string.IsNullOrWhiteSpace(category))
            categories.Add(category);

        if (feature["categories"] is JsonArray values)
        {
            categories.AddRange(values
                .Select(ReadString)
                .OfType<string>()
                .Where(value => !string.IsNullOrWhiteSpace(value)));
        }

        return categories
            .DefaultIfEmpty("Studio")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<FeatureManagementSettingResponse> GetSettings(JsonObject feature)
    {
        if (feature["settings"] is not JsonArray settings)
            return [];

        return settings
            .OfType<JsonObject>()
            .Select(ToSetting)
            .Where(setting => !string.IsNullOrWhiteSpace(setting.Name))
            .OrderBy(setting => setting.Category ?? "", StringComparer.OrdinalIgnoreCase)
            .ThenBy(setting => setting.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static FeatureManagementSettingResponse ToSetting(JsonObject setting)
    {
        var ui = setting["ui"] as JsonObject;
        var validation = setting["validation"] as JsonObject;
        var name = ReadString(setting, "name") ?? "";

        return new(
            name,
            ReadString(setting, "displayName") ?? name,
            ReadString(setting, "description"),
            ReadString(setting, "category"),
            ReadString(setting, "group"),
            ReadString(setting, "clrType"),
            ReadString(setting, "jsonType"),
            ReadBool(setting, "required"),
            setting["defaultValue"]?.DeepClone(),
            ReadBool(setting, "secret"),
            ReadBool(setting, "sensitive") || ReadBool(setting["extensions"] as JsonObject, "sensitive"),
            ReadBool(setting, "restartRequired"),
            ReadBool(ui, "advanced"),
            ReadBool(ui, "experimental"),
            ReadString(ui, "hint", "uiHint"),
            ReadString(ui, "optionsProvider"),
            GetSettingOptions(ui, validation));
    }

    private static IReadOnlyList<FeatureManagementSettingOptionResponse> GetSettingOptions(JsonObject? ui, JsonObject? validation)
    {
        if (ui?["options"] is JsonArray options)
        {
            return options
                .OfType<JsonObject>()
                .Select(option => new FeatureManagementSettingOptionResponse(
                    ReadString(option, "label") ?? ReadString(option["value"]) ?? "",
                    option["value"]?.DeepClone(),
                    ReadString(option, "description")))
                .ToArray();
        }

        if (validation?["enum"] is not JsonArray enumValues)
            return [];

        return enumValues
            .Select(value => new FeatureManagementSettingOptionResponse(
                ReadString(value) ?? value?.ToJsonString() ?? "",
                value?.DeepClone(),
                null))
            .ToArray();
    }

    private static string? ReadMetadataString(ShellFeatureDescriptor? descriptor, string key) =>
        descriptor?.Metadata.TryGetValue(key, out var value) == true
            ? value?.ToString()
            : null;

    private static string? ReadString(JsonObject? obj, params string[] names)
    {
        if (obj is null)
            return null;

        foreach (var name in names)
        {
            var value = ReadString(obj[name]);
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        return null;
    }

    private static string? ReadString(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text)
            ? text
            : null;

    private static bool ReadBool(JsonObject? obj, string name) =>
        obj?[name] is JsonValue value && value.TryGetValue<bool>(out var result) && result;

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
    IReadOnlyList<FeatureManagementSettingResponse> Settings);

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
    IReadOnlyList<FeatureManagementSettingResponse> Settings);

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
