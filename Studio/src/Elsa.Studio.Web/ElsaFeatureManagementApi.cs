using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elsa.Studio.Api.Contracts;
using Elsa.Studio.Api.Models;
using Microsoft.AspNetCore.Mvc;

namespace Elsa.Studio.Web;

internal static class ElsaFeatureManagementApi
{
    public static IEndpointRouteBuilder MapElsaFeatureManagementApi(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/modularity/features", GetFeaturesAsync);
        endpoints.MapPost("/modularity/features/apply", ApplyFeatures);
        return endpoints;
    }

    private static async Task<IResult> GetFeaturesAsync([FromServices] IStudioModuleManifestProvider moduleManifestProvider, CancellationToken cancellationToken)
    {
        var registry = await moduleManifestProvider.GetModuleRegistry(cancellationToken);
        var features = registry.Modules
            .Select(ToFeature)
            .OrderBy(feature => feature.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(feature => feature.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return Results.Ok(new FeatureManagementCatalogResponse(
            ComputeRevision(features),
            features));
    }

    private static IResult ApplyFeatures() =>
        Results.BadRequest(new FeatureManagementErrorResponse("Studio feature toggling is not supported by this host yet."));

    private static FeatureManagementCatalogItemResponse ToFeature(StudioModuleRegistryItem module) =>
        new(
            module.Id,
            module.DisplayName,
            null,
            GetCategories(module),
            module.SourceKind,
            null,
            module.Version,
            string.Equals(module.Status, "available", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(module.Status, "loaded", StringComparison.OrdinalIgnoreCase),
            EmptyJsonObject(),
            false,
            false,
            null,
            null,
            null,
            []);

    private static IReadOnlyList<string> GetCategories(StudioModuleRegistryItem module) =>
        new[] { "Studio", module.Scope }
            .Concat(module.Contributions.Select(contribution => contribution.Type))
            .Where(category => !string.IsNullOrWhiteSpace(category))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static JsonElement EmptyJsonObject()
    {
        using var document = JsonDocument.Parse("{}");
        return document.RootElement.Clone();
    }

    private static string ComputeRevision(IReadOnlyList<FeatureManagementCatalogItemResponse> features)
    {
        var seed = string.Join('\n', features.Select(feature => $"{feature.Id}:{feature.DisplayName}:{feature.SourceKind}:{feature.Enabled}"));
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(seed));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}

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
    IReadOnlyList<object> Settings);

internal sealed record FeatureManagementErrorResponse(string Error);
