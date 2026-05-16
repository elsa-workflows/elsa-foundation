using Elsa.Http.Core.Contracts;
using Elsa.Http.Core.Models;
using Microsoft.AspNetCore.StaticFiles;

namespace Elsa.Http.Services
{
    /// <summary>
    /// Handles content that represents a downloadable URL.
    /// </summary>
    internal sealed class UrlDownloadableContentHandler(IFileDownloader fileDownloader, IContentTypeProvider contentTypeProvider) 
        : IDownloadableContentHandler
    {
        public float Priority => 0;

        public IEnumerable<Func<ValueTask<Downloadable>>> GetDownloadablesAsync(object content, CancellationToken cancellationToken)
        {
            yield return () => DownloadAsync(content, cancellationToken);
        }

        /// <inheritdoc />
        public bool SupportsContent(object content) => (content is string url && url.StartsWith("http", StringComparison.OrdinalIgnoreCase)) || content is Uri;

        private async ValueTask<Downloadable> DownloadAsync(object content, CancellationToken cancellationToken)
        {
            var url = content is string s ? new Uri(s) : (Uri)content;            
            var response = await fileDownloader.DownloadAsync(url, new(), cancellationToken);
            var eTag = response.Headers.ETag?.Tag;
            var filename = GetFilename(response) ?? url.Segments.Last();
            var contentType = response.Content.Headers.ContentType?.MediaType ?? GetContentType(filename);
            var stream = await response.Content.ReadAsStreamAsync(cancellationToken);

            return new Downloadable(stream, filename, contentType, eTag);
        }

        private static string? GetFilename(HttpResponseMessage response)
        {
            if (!response.Content.Headers.TryGetValues("Content-Disposition", out var values))
                return null;

            var contentDispositionString = string.Join("", values);
            var contentDisposition = new System.Net.Mime.ContentDisposition(contentDispositionString);
            return contentDisposition.FileName;
        }

        private string GetContentType(string filename)
        {
            return contentTypeProvider.TryGetContentType(filename, out var contentType) ? contentType : System.Net.Mime.MediaTypeNames.Application.Octet;
        }
    }
}
