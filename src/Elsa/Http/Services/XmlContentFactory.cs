using Elsa.Http.Core.Contracts;
using Elsa.Http.Core.Models;
using System.Net.Mime;
using System.Text;
using System.Xml.Serialization;

namespace Elsa.Http.Services;

/// <summary>
/// Creates a <see cref="HttpContent"/> object for XML types.
/// </summary>
internal sealed class XmlContentFactory : IHttpContentFactory
{
    /// <inheritdoc />
    public IEnumerable<string> SupportedContentTypes =>
    [
        MediaTypeNames.Application.Xml,
        MediaTypeNames.Text.Xml,
        MediaTypeNames.Application.Soap,
    ];

    /// <inheritdoc />
    public HttpContent CreateHttpContent(object content, string contentType)
    {
        var text = content as string ?? Serialize(content);
        return new RawStringContent(text, Encoding.UTF8, contentType);
    }

    private static string Serialize(object value)
    {
        using var writer = new StringWriter();
        new XmlSerializer(value.GetType()).Serialize(writer, value);
        return writer.ToString();
    }
}