using Elsa.Http.Core.Models;

namespace Elsa.Http.Services;

/// <summary>
/// Reads text/html content type streams.
/// </summary>
internal sealed class TextHtmlHttpContentParser : TextHttpContentParserBase
{
    /// <inheritdoc />
    public override bool GetSupportsContentType(HttpResponseParserContext context) => context.ContentType.Contains("text/html", StringComparison.InvariantCultureIgnoreCase);
}
