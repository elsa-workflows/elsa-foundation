using Elsa.Http.Core.Contracts;
using Elsa.Http.Core.Models;
using System.Diagnostics.CodeAnalysis;
using System.Net.Mime;
using System.Text;
using System.Text.Json;

namespace Elsa.Http.Services;

/// <summary>
/// Creates a <see cref="HttpContent"/> object for application/json.
/// </summary>
internal sealed class JsonContentFactory : IHttpContentFactory
{
    /// <inheritdoc />
    public IEnumerable<string> SupportedContentTypes => [MediaTypeNames.Application.Json, "text/json"];

    private static readonly UTF8Encoding _utf8Encoding = new(encoderShouldEmitUTF8Identifier: false);

    /// <inheritdoc />
    [RequiresUnreferencedCode("The JsonSerializer type is not trim-compatible.")]
    public HttpContent CreateHttpContent(object content, string contentType)
    {
        var text = content as string ?? JsonSerializer.Serialize(content);

        if (string.IsNullOrWhiteSpace(contentType))
            contentType = MediaTypeNames.Application.Json;

        return new RawStringContent(text, _utf8Encoding, contentType);
    }
}