using Elsa.Http.Core.Contracts;
using Elsa.Http.Core.Models;
using System.Net.Mime;
using System.Text;

namespace Elsa.Http.Services
{
    /// <summary>
    /// Creates a <see cref="StringContent"/> object for text/plain, text/richtext and text/html content types.
    /// </summary>
    internal sealed class TextContentFactory : IHttpContentFactory
    {
        /// <inheritdoc />
        public IEnumerable<string> SupportedContentTypes => new[]
        {
        MediaTypeNames.Text.Plain,
        MediaTypeNames.Text.RichText,
        MediaTypeNames.Text.Html,
    };

        /// <inheritdoc />
        public HttpContent CreateHttpContent(object content, string contentType)
        {
            var text = content as string ?? content.ToString();

            if (string.IsNullOrWhiteSpace(contentType))
                contentType = MediaTypeNames.Text.Plain;

            return new RawStringContent(text!, Encoding.UTF8, contentType);
        }
    }
}
