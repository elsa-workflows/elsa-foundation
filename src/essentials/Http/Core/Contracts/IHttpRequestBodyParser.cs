using System.Text.Json;

namespace Elsa.Http.Core.Contracts;

/// <summary>
/// Parses an already-read inbound request body string into a wire-safe <see cref="JsonElement"/>,
/// selecting behaviour by content type. This is the request-side counterpart to the response-side
/// <see cref="IHttpContentParser"/> strategy set (used by SendHttpRequest): it shares the same
/// content-type dispatch intent but operates on an in-memory body string rather than an
/// <see cref="HttpResponseMessage"/>/stream, and it always yields wire-safe output (<see cref="JsonElement"/>)
/// per ADR 0035/0036 — never a CLR object graph or <c>ExpandoObject</c>.
/// </summary>
/// <remarks>
/// Semantics:
/// <list type="bullet">
/// <item><description><c>application/json</c>, <c>text/json</c>, or any <c>+json</c> suffix (e.g. <c>application/problem+json</c>) → the parsed <see cref="JsonElement"/>; malformed JSON yields <c>null</c> (never throws).</description></item>
/// <item><description><c>text/*</c> → a JSON string <see cref="JsonElement"/> wrapping the raw body.</description></item>
/// <item><description>Unknown or absent content type, or an empty body → <c>null</c>.</description></item>
/// </list>
/// A <c>charset</c> (or any other) parameter on the content type is tolerated — only the media type is inspected.
/// </remarks>
public interface IHttpRequestBodyParser
{
    /// <summary>
    /// Parses <paramref name="body"/> according to <paramref name="contentType"/>. Returns <c>null</c>
    /// for an absent/unknown content type, an empty body, or malformed JSON; never throws for bad input.
    /// </summary>
    JsonElement? Parse(string? contentType, string body);
}
