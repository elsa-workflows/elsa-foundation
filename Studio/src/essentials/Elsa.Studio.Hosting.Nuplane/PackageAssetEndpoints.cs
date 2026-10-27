using System.Security.Cryptography;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using global::Nuplane.Abstractions.PackageStoreProtection;
using global::Nuplane.Feeds.Configuration;

namespace Elsa.Studio.Hosting.Nuplane;

internal static class PackageAssetEndpoints
{
    private static readonly FileExtensionContentTypeProvider ContentTypes = new();

    public static void Map(IEndpointRouteBuilder endpoints) => endpoints.MapGet("/_content/{packageId}/{**assetPath}", ReadAsync);

    private static async Task<IResult> ReadAsync(string packageId, string? assetPath, HttpContext context,
        IWebHostEnvironment environment, IOptions<FeedResolutionOptions> options, PackageContentReader reader)
    {
        context.Response.Headers.CacheControl = "no-cache";
        var path = assetPath is null ? null : PackageReadScope.NormalizeRelativePath(assetPath);
        if (path is null || string.IsNullOrWhiteSpace(packageId) || packageId is "." or ".."
            || packageId.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '.' and not '-' and not '_'))
            return Results.NotFound();
        var deployed = environment.WebRootFileProvider.GetFileInfo($"_content/{packageId}/{path}");
        // SDK/deployed assets are immutable host content. A provider pointing into the configured mutable install
        // root must go through admitted package access instead; it cannot turn that path into an unprotected result.
        if (deployed.Exists && !IsInstalledPath(deployed.PhysicalPath, options.Value.PackageInstallRoot))
            return DeployedFile(deployed);
        var lease = await reader.TryAcquireResponseAsync(context.RequestAborted);
        if (lease is null) return Busy(context);
        try
        {
            await using var scope = await reader.OpenAsync(packageId, context.RequestAborted);
            var content = scope?.Read("staticwebassets/" + path, PackageContentReader.MaximumAssetBytes, context.RequestAborted);
            if (content is null)
            {
                await lease.DisposeAsync();
                return Results.NotFound();
            }
            var tag = new EntityTagHeaderValue('"' + Convert.ToHexString(SHA256.HashData(content.Bytes)).ToLowerInvariant() + '"');
            var result = Results.File(content.Bytes, ContentType(path), lastModified: content.LastModified, entityTag: tag, enableRangeProcessing: true);
            return new PayloadResult(result, lease);
        }
        catch (PackageContentSizeException error)
        {
            await lease.DisposeAsync();
            return Results.Json(new { Error = error.Message }, statusCode: StatusCodes.Status413PayloadTooLarge);
        }
        catch (PackageStoreAdmissionException error)
        {
            await lease.DisposeAsync();
            context.Response.Headers.RetryAfter = "1";
            return Results.Json(new { Error = "Protected package content is unavailable.", Reason = error.Reason.ToString() }, statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (PackageContentProfileException error)
        {
            await lease.DisposeAsync();
            return Results.Json(new { Error = error.Message }, statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (IOException)
        {
            await lease.DisposeAsync();
            return Busy(context);
        }
        catch (UnauthorizedAccessException)
        {
            await lease.DisposeAsync();
            return Results.NotFound();
        }
        catch
        {
            await lease.DisposeAsync();
            throw;
        }
    }

    private static IResult Busy(HttpContext context)
    {
        context.Response.Headers.RetryAfter = "1";
        return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    }

    private static bool IsInstalledPath(string? path, string? root)
    {
        if (path is null || root is null) return false;
        try
        {
            var actual = ResolveLinks(path, 0);
            var installed = ResolveLinks(root, 0);
            // Case folding on macOS is conservative on its optional case-sensitive volumes: an
            // ambiguous alias takes the protected path rather than becoming deployed-file authority.
            var comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
                ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            var prefix = Path.EndsInDirectorySeparator(installed) ? installed : installed + Path.DirectorySeparatorChar;
            return string.Equals(actual, installed, comparison)
                || actual.StartsWith(prefix, comparison);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            // An unresolvable physical identity cannot establish the immutable deployed-file profile.
            return true;
        }
    }

    private static string ResolveLinks(string path, int depth)
    {
        if (depth > 32) throw new IOException("The physical path has too many linked ancestors.");
        var full = Path.GetFullPath(path);
        var current = Path.GetPathRoot(full)!;
        foreach (var component in full[current.Length..].Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries))
        {
            var next = Path.Combine(current, component);
            FileSystemInfo info = Directory.Exists(next) ? new DirectoryInfo(next) : new FileInfo(next);
            current = info.LinkTarget is null ? next
                : ResolveLinks(info.ResolveLinkTarget(returnFinalTarget: true)?.FullName
                    ?? throw new IOException("A physical path link could not be resolved."), depth + 1);
        }
        // A not-yet-created install root retains its resolved existing ancestors and missing suffix.
        return Path.TrimEndingDirectorySeparator(current);
    }

    private static IResult DeployedFile(IFileInfo file) => file.PhysicalPath is { Length: > 0 } physical
        ? Results.File(physical, ContentType(file.Name), lastModified: file.LastModified, enableRangeProcessing: true)
        : Results.Stream(file.CreateReadStream(), ContentType(file.Name), lastModified: file.LastModified, enableRangeProcessing: true);

    private static string ContentType(string path) => ContentTypes.TryGetContentType(path, out var type) ? type : "application/octet-stream";

    private sealed class PayloadResult(IResult inner, IAsyncDisposable lease) : IResult
    {
        public async Task ExecuteAsync(HttpContext context)
        {
            try { await inner.ExecuteAsync(context); }
            finally { await lease.DisposeAsync(); }
        }
    }
}
