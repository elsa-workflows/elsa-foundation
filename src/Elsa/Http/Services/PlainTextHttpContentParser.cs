using Elsa.Http.Core.Models;

namespace Elsa.Http.Services;

/// <summary>
/// Reads text/plain content type streams.
/// </summary>
internal sealed class PlainTextHttpContentParser : TextHttpContentParserBase
{
    /// <inheritdoc />
    public override bool GetSupportsContentType(HttpResponseParserContext context) => context.ContentType.Contains("text/plain", StringComparison.InvariantCultureIgnoreCase);
}
