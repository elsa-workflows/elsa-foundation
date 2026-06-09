using Elsa.Http.Core.Contracts;
using Elsa.Http.Core.Options;

namespace Elsa.Http.Services;

/// <summary>
/// A general-purpose downloader of files from a given URL that uses <see cref="HttpClient"/>.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="HttpClientFileDownloader"/> class.
/// </remarks>
internal sealed class HttpClientFileDownloader(HttpClient httpClient) : IFileDownloader
{
    internal const string HttpClientName = nameof(HttpClientFileDownloader);

    /// <inheritdoc />
    public async Task<HttpResponseMessage> DownloadAsync(Uri url, FileDownloadOptions? options = default, CancellationToken cancellationToken = default)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);

        if (options?.ETag != null)
            request.Headers.IfNoneMatch.Add(options.ETag);

        if (options?.Range != null)
            request.Headers.Range = options.Range;

        return await httpClient.SendAsync(request, cancellationToken);
    }
}