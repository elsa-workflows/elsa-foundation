using System.Text.Json;
using Elsa.Http.Core.Contracts;

namespace Elsa.Http.Services;

/// <summary>
/// Default <see cref="IHttpRequestBodyParser"/>: a stateless content-type dispatch that turns an
/// already-read inbound body string into a wire-safe <see cref="JsonElement"/>.
/// </summary>
/// <remarks>
/// <para>
/// This is the request-side counterpart to the response-side <see cref="IHttpContentParser"/> set
/// (json/xml/text/html/file) that <c>SendHttpRequest</c> uses. It deliberately does NOT reuse those
/// implementations verbatim: they are <see cref="HttpResponseMessage"/>/<see cref="Stream"/>-shaped
/// (they read from <c>HttpResponseParserContext.Content</c>), they return <see cref="object"/> driven
/// by a caller-supplied <c>ReturnType</c>, and they depend on <c>IObjectConverter</c>/<c>IPayloadSerializer</c>.
/// The inbound path has a different shape entirely — the body is already a string and the only
/// legal output is wire-safe <see cref="JsonElement"/> (ADR 0035/0036: no <see cref="object"/> graph,
/// no <c>ExpandoObject</c>). Adapting the response set to this shape would mean synthesising a stream,
/// a fake return type, and discarding the converter/serializer pipeline — more coupling, not less.
/// So we share the response set's <em>selection intent</em> (content-type dispatch, json/text priority)
/// while keeping the dispatch here small and self-contained. The response-side path is untouched:
/// <see cref="IHttpContentParser"/> and its implementations keep their signatures.
/// </para>
/// <para>
/// Semantics: <c>application/json</c> / <c>text/json</c> / any <c>+json</c> suffix → parsed element
/// (malformed → <c>null</c>, never throws); <c>text/*</c> → string element; unknown/absent content
/// type or empty body → <c>null</c>. A <c>charset</c> (or other) parameter is tolerated.
/// </para>
/// </remarks>
public sealed class HttpRequestBodyParser : IHttpRequestBodyParser
{
    /// <inheritdoc />
    public JsonElement? Parse(string? contentType, string body)
    {
        if (string.IsNullOrEmpty(body))
            return null;

        var mediaType = GetMediaType(contentType);

        if (mediaType.Length == 0)
            return null;

        if (IsJson(mediaType))
        {
            try
            {
                using var document = JsonDocument.Parse(body);
                return document.RootElement.Clone();
            }
            catch (JsonException)
            {
                // Wire-safe contract: malformed JSON yields null, never throws.
                return null;
            }
        }

        if (mediaType.StartsWith("text/", StringComparison.OrdinalIgnoreCase))
            return JsonSerializer.SerializeToElement(body);

        return null;
    }

    /// <summary>Strips any parameters (e.g. <c>; charset=utf-8</c>) and returns the trimmed media type.</summary>
    private static string GetMediaType(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
            return string.Empty;

        var separatorIndex = contentType.IndexOf(';');
        var mediaType = separatorIndex >= 0 ? contentType[..separatorIndex] : contentType;
        return mediaType.Trim();
    }

    /// <summary>True for <c>application/json</c>, <c>text/json</c>, and any <c>+json</c>-structured-suffix type.</summary>
    private static bool IsJson(string mediaType) =>
        mediaType.Equals("application/json", StringComparison.OrdinalIgnoreCase)
        || mediaType.Equals("text/json", StringComparison.OrdinalIgnoreCase)
        || mediaType.EndsWith("+json", StringComparison.OrdinalIgnoreCase);
}
