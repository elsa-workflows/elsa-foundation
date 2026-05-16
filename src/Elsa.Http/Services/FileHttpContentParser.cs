using Elsa.Http.Core.Contracts;
using Elsa.Http.Core.Models;

namespace Elsa.Http.Services
{
    internal sealed class FileHttpContentParser : IHttpContentParser
    {
        /// <inheritdoc />
        // Lower priority than other parsers, so that they can be tried first.
        // If none of them can parse the content, this parser will be tried and interpret the content as a file.
        public int Priority => -100;

        /// <inheritdoc />
        public bool GetSupportsContentType(HttpResponseParserContext context)
        {
            return true;
        }

        /// <inheritdoc />
        public Task<object> ReadAsync(HttpResponseParserContext context)
        {
            var stream = context.Content;
            var filename = GetFilename(context.Headers) ?? "file.dat";
            var contentType = context.ContentType;
            context.Headers.TryGetValue("ETag", out var eTag);
            var file = new HttpFile(stream, filename, contentType, eTag?.FirstOrDefault());
            return Task.FromResult<object>(file);
        }

        static string? GetFilename(IDictionary<string, string[]> headers)
        {
            if (!headers.TryGetValue("Content-Disposition", out var values))
                return null;

            var contentDispositionString = string.Join("", values);
            var contentDisposition = new System.Net.Mime.ContentDisposition(contentDispositionString);
            return contentDisposition.FileName;
        }
    }
}
