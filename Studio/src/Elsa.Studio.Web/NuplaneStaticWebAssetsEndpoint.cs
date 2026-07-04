using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using Nuplane.Admin;

namespace Elsa.Studio.Web;

internal static class NuplaneStaticWebAssetsEndpoint
{
    private static readonly FileExtensionContentTypeProvider ContentTypeProvider = new();

    public static IEndpointRouteBuilder MapNuplaneStaticWebAssets(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/_content/{packageId}/{**assetPath}", ServePackageAssetAsync);
        return endpoints;
    }

    private static async Task<IResult> ServePackageAssetAsync(
        [FromRoute] string packageId,
        [FromRoute] string assetPath,
        [FromServices] INuplaneAdminOperations nuplaneAdmin,
        [FromServices] IWebHostEnvironment environment,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(packageId) || string.IsNullOrWhiteSpace(assetPath))
            return Results.NotFound();

        var staticWebAsset = environment.WebRootFileProvider.GetFileInfo($"_content/{packageId}/{assetPath}");
        if (staticWebAsset.Exists)
            return ServeFile(staticWebAsset);

        var packages = await nuplaneAdmin.GetPackagesAsync(cancellationToken);
        var package = packages.Packages.FirstOrDefault(package =>
            StringComparer.OrdinalIgnoreCase.Equals(package.PackageId, packageId));

        if (package is null)
            return Results.NotFound();

        var staticWebAssetsPath = Path.Combine(package.InstallPath, "staticwebassets");
        var resolvedStaticWebAssetsPath = Path.GetFullPath(staticWebAssetsPath);
        var resolvedAssetPath = Path.GetFullPath(Path.Combine(resolvedStaticWebAssetsPath, assetPath.Replace('/', Path.DirectorySeparatorChar)));

        if (!IsChildPath(resolvedStaticWebAssetsPath, resolvedAssetPath) || !File.Exists(resolvedAssetPath))
            return Results.NotFound();

        return Results.File(resolvedAssetPath, GetContentType(resolvedAssetPath), enableRangeProcessing: true);
    }

    private static IResult ServeFile(IFileInfo file) =>
        file.PhysicalPath is { Length: > 0 } physicalPath
            ? Results.File(physicalPath, GetContentType(physicalPath), enableRangeProcessing: true)
            : Results.Stream(file.CreateReadStream(), GetContentType(file.Name));

    internal static bool IsChildPath(string parentPath, string childPath)
    {
        var normalizedParent = Path.TrimEndingDirectorySeparator(parentPath) + Path.DirectorySeparatorChar;
        return childPath.StartsWith(normalizedParent, StringComparison.OrdinalIgnoreCase);
    }

    private static string GetContentType(string path) =>
        ContentTypeProvider.TryGetContentType(path, out var contentType)
            ? contentType
            : "application/octet-stream";
}
