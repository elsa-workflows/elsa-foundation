using Elsa.Http.Core.Contracts;
using Elsa.Http.Core.Models;

namespace Elsa.Http.Services;

/// <summary>
/// Base class for parsers that read a textual content-type stream verbatim into a string. Concrete
/// parsers only decide which content type they support via <see cref="GetSupportsContentType"/>.
/// </summary>
internal abstract class TextHttpContentParserBase : IHttpContentParser
{
    /// <inheritdoc />
    public virtual int Priority => 0;

    /// <inheritdoc />
    public abstract bool GetSupportsContentType(HttpResponseParserContext context);

    /// <inheritdoc />
    public async Task<object> ReadAsync(HttpResponseParserContext context)
    {
        var content = context.Content;
        using var reader = new StreamReader(content, leaveOpen: true);
        var stringContent = await reader.ReadToEndAsync();
        return stringContent;
    }
}
