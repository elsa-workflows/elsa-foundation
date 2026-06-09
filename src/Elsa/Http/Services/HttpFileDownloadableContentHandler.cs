using Elsa.Http.Core.Contracts;
using Elsa.Http.Core.Models;

namespace Elsa.Http.Services
{
    /// <summary>
    /// Handles content that represents a downloadable stream.
    /// </summary>
    internal sealed class HttpFileDownloadableContentHandler : IDownloadableContentHandler
    {
        public float Priority => 0;

        public IEnumerable<Func<ValueTask<Downloadable>>> GetDownloadablesAsync(object content, CancellationToken cancellationToken)
        {
            yield return () => new(GetDownloadable(content));
        }

        /// <inheritdoc />
        public bool SupportsContent(object content) => content is HttpFile;

        /// <inheritdoc />
        private static Downloadable GetDownloadable(object content)
        {
            var file = (HttpFile)content;
            var stream = file.Stream;
            var fileName = file.Filename;
            var contentType = file.ContentType;
            return new Downloadable(stream, fileName, contentType);
        }
    }
}
