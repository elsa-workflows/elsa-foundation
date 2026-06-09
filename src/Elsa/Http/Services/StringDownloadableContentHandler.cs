using Elsa.Http.Core.Contracts;
using Elsa.Http.Core.Models;
using System.Text;

namespace Elsa.Http.Services
{
    /// <summary>
    /// Handles content that represents a downloadable string file.
    /// </summary>
    internal sealed class StringDownloadableContentHandler : IDownloadableContentHandler
    {
        public float Priority => 0;

        public IEnumerable<Func<ValueTask<Downloadable>>> GetDownloadablesAsync(object content, CancellationToken cancellationToken)
        {
            yield return () => new(GetDownloadable(content));
        }

        /// <inheritdoc />
        public bool SupportsContent(object content) => content is string;

        /// <inheritdoc />
        private static Downloadable GetDownloadable(object content)
        {
            var stream = new MemoryStream(Encoding.UTF8.GetBytes((string)content));
            return new(stream, "file.txt", "text/plain");
        }
    }
}
