using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Elsa.Versioning.Publisher;

/// <summary>How a feed answered a push.</summary>
public enum PushStatus
{
    /// <summary>The feed took the package.</summary>
    Pushed,

    /// <summary>The feed already holds this id and version, and kept its copy (409).</summary>
    AlreadyExists,

    /// <summary>Anything else: the package may or may not have landed.</summary>
    Failed
}

public sealed record PushResult(PushStatus Status, string Detail);

/// <summary>The feed could not answer a read, so nothing may be concluded from it.</summary>
public sealed class FeedException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// The package feed: the three things the publisher asks of it. It never decides what to publish (spec 150 FR-006,
/// FR-013): a push, the fingerprint of the one version a rejected push targeted (FR-018), and, for the bootstrap
/// alone, the versions a package id holds.
/// </summary>
public interface IPackageFeed
{
    /// <summary>Pushes one package, never skipping a duplicate (FR-011).</summary>
    Task<PushResult> PushAsync(string packagePath, CancellationToken cancellationToken);

    /// <summary>Every version the feed holds of a package id; empty when it holds none.</summary>
    /// <exception cref="FeedException">The feed did not answer.</exception>
    Task<IReadOnlyList<string>> ListVersionsAsync(string packageId, CancellationToken cancellationToken);

    /// <summary>The input fingerprint the feed's copy of a version carries.</summary>
    /// <exception cref="FeedException">The copy could not be downloaded, or carries no readable fingerprint.</exception>
    Task<string> ReadFingerprintAsync(string packageId, string version, CancellationToken cancellationToken);
}

/// <summary>
/// <see cref="IPackageFeed"/> over the NuGet V3 protocol: the service index names the push endpoint
/// (<c>PackagePublish/2.0.0</c>) and the package content endpoint (<c>PackageBaseAddress/3.0.0</c>).
/// </summary>
/// <remarks>
/// A push is the protocol's multipart <c>PUT</c> with the key in <c>X-NuGet-ApiKey</c>; 200, 201 and 202 mean the feed
/// took the package, 409 that the version exists. Every other answer, and a request that never got one, is a failure:
/// a push that timed out may still have landed, and the next run settles that through FR-018. Reads are anonymous.
/// </remarks>
public sealed class NuGetFeed(HttpClient http, Uri serviceIndex, string apiKey) : IPackageFeed
{
    private const string PublishResource = "PackagePublish/2.0.0";
    private const string ContentResource = "PackageBaseAddress/3.0.0";

    private (Uri Publish, Uri Content)? endpoints;

    public async Task<PushResult> PushAsync(string packagePath, CancellationToken cancellationToken)
    {
        try
        {
            var (publish, _) = await EndpointsAsync(cancellationToken);
            await using var file = File.OpenRead(packagePath);
            using var package = new StreamContent(file);
            package.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            using var content = new MultipartFormDataContent { { package, "package", "package.nupkg" } };
            using var request = new HttpRequestMessage(HttpMethod.Put, publish) { Content = content };
            request.Headers.Add("X-NuGet-ApiKey", apiKey);
            request.Headers.Add("X-NuGet-Protocol-Version", "4.1.0");

            using var response = await http.SendAsync(request, cancellationToken);
            var detail = $"{(int)response.StatusCode} {response.ReasonPhrase}";
            return response.StatusCode switch
            {
                HttpStatusCode.OK or HttpStatusCode.Created or HttpStatusCode.Accepted => new PushResult(PushStatus.Pushed, detail),
                HttpStatusCode.Conflict => new PushResult(PushStatus.AlreadyExists, detail),
                _ => new PushResult(PushStatus.Failed, $"{detail}: {Excerpt(await response.Content.ReadAsStringAsync(cancellationToken))}")
            };
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or IOException or FeedException)
        {
            return new PushResult(PushStatus.Failed, $"no answer: {exception.Message}");
        }
    }

    public async Task<IReadOnlyList<string>> ListVersionsAsync(string packageId, CancellationToken cancellationToken)
    {
        var (_, content) = await EndpointsAsync(cancellationToken);
        var url = new Uri(content, $"{packageId.ToLowerInvariant()}/index.json");
        using var response = await GetAsync(url, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return [];

        using var document = await ReadJsonAsync(response, url, cancellationToken);
        try
        {
            return document.RootElement.GetProperty("versions").EnumerateArray().Select(version => version.GetString()!).ToArray();
        }
        catch (Exception exception) when (exception is KeyNotFoundException or InvalidOperationException)
        {
            throw new FeedException($"{url} lists no versions: {exception.Message}", exception);
        }
    }

    public async Task<string> ReadFingerprintAsync(string packageId, string version, CancellationToken cancellationToken)
    {
        var (_, content) = await EndpointsAsync(cancellationToken);
        var (id, lowerVersion) = (packageId.ToLowerInvariant(), version.ToLowerInvariant());
        var url = new Uri(content, $"{id}/{lowerVersion}/{id}.{lowerVersion}.nupkg");
        using var response = await GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new FeedException($"{url} answered {(int)response.StatusCode} {response.ReasonPhrase}.");

        try
        {
            using var package = new MemoryStream(await response.Content.ReadAsByteArrayAsync(cancellationToken));
            return PackedPackage.ReadFingerprint(package, url.ToString());
        }
        catch (Exception exception) when (exception is InvalidOperationException or InvalidDataException or HttpRequestException or IOException or TaskCanceledException)
        {
            throw new FeedException($"{url} could not be read: {exception.Message}", exception);
        }
    }

    private async Task<(Uri Publish, Uri Content)> EndpointsAsync(CancellationToken cancellationToken)
    {
        if (endpoints is { } known)
            return known;

        using var response = await GetAsync(serviceIndex, cancellationToken);
        using var document = await ReadJsonAsync(response, serviceIndex, cancellationToken);
        Uri Resource(string type) =>
            document.RootElement.TryGetProperty("resources", out var resources) &&
            resources.EnumerateArray().FirstOrDefault(resource => resource.TryGetProperty("@type", out var name) && name.GetString() == type) is
                { ValueKind: JsonValueKind.Object } found &&
            found.TryGetProperty("@id", out var id) && id.GetString() is { } address
                ? new Uri(type == ContentResource ? address.TrimEnd('/') + "/" : address)
                : throw new FeedException($"The service index {serviceIndex} names no {type} resource.");

        return (endpoints = (Resource(PublishResource), Resource(ContentResource))).Value;
    }

    private async Task<HttpResponseMessage> GetAsync(Uri url, CancellationToken cancellationToken)
    {
        try
        {
            return await http.GetAsync(url, cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            throw new FeedException($"{url} did not answer: {exception.Message}", exception);
        }
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response, Uri url, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
            throw new FeedException($"{url} answered {(int)response.StatusCode} {response.ReasonPhrase}.");

        try
        {
            return await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        }
        catch (Exception exception) when (exception is JsonException or HttpRequestException or IOException or TaskCanceledException)
        {
            throw new FeedException($"{url} could not be read as JSON: {exception.Message}", exception);
        }
    }

    private static string Excerpt(string body) => body.Length <= 300 ? body.Trim() : body[..300].Trim() + "...";
}
