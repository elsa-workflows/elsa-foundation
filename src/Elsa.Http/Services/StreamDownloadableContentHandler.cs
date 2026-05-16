using Elsa.Http.Core.Contracts;
using Elsa.Http.Core.Models;

namespace Elsa.Http.Services
{
    /// <summary>
    /// Handles content that represents a downloadable stream.
    /// </summary>
    internal sealed class StreamDownloadableContentHandler : IDownloadableContentHandler
    {
        public float Priority => 0;

        /// <inheritdoc />
        public bool SupportsContent(object content) => content is Stream;

        public IEnumerable<Func<ValueTask<Downloadable>>> GetDownloadablesAsync(object content, CancellationToken cancellationToken)
        {
            yield return () => new(GetDownloadable(content));
        }

        /// <inheritdoc />
        private static Downloadable GetDownloadable(object content)
        {
            var stream = (Stream)content;
            var fileName = "file.bin";
            var contentType = "application/octet-stream";
            return new Downloadable(stream, fileName, contentType);
        }
    }
}
