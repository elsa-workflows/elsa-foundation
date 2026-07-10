namespace Elsa.Activities.Http.Models;

/// <summary>
/// A description of an inbound HTTP request delivered to an <see cref="Activities.HttpEndpoint"/> trigger. It is
/// the forward-compatible payload the endpoint's request middleware serializes as the start stimulus input.
/// </summary>
/// <remarks>
/// <para>
/// The middleware serializes this full model as the stimulus input, and the router's start path carries it on
/// the dedicated stimulus-input channel (spec 089 sub-unit A) — separate from workflow inputs by construction —
/// so a workflow started by an <see cref="Activities.HttpEndpoint"/> trigger observes the live request
/// body/headers/query through its trigger activity's Result. Resumed instances receive the same model as the
/// resume input.
/// </para>
/// </remarks>
/// <param name="Path">The endpoint-relative request path (below the configured base path), without leading/trailing slashes.</param>
/// <param name="Method">The HTTP method (verb) of the request.</param>
/// <param name="Headers">The request headers, keyed by header name.</param>
/// <param name="Query">The request query-string values, keyed by parameter name.</param>
/// <param name="Body">The raw request body as a string, if any.</param>
/// <param name="RouteData">Route parameters extracted from the matched route template (spec 089 B), e.g. <c>{ id = "42" }</c> for <c>orders/{id}</c>; empty when the template has none. Optional on the wire (older payloads deserialize with null; consumers coalesce to empty).</param>
/// <remarks>
/// <para>
/// <b>Parsed content is not on the wire (spec 089 efficiency #9).</b> The parsed body is <em>derived</em> at the
/// <see cref="Activities.HttpEndpoint"/> activity from <see cref="Body"/> + the request <c>Content-Type</c> header
/// through the deterministic <c>IHttpRequestBodyParser</c> seam, and surfaced only on the activity's
/// <c>ParsedContent</c> output. It is deliberately NOT persisted here: carrying both the raw body and a
/// re-encoded copy of it doubled the durable stimulus payload per run. The parser is deterministic, so the
/// derived value is identical to what the middleware would have persisted.
/// </para>
/// </remarks>
public sealed record HttpRequestModel(
    string Path,
    string Method,
    IDictionary<string, string[]> Headers,
    IDictionary<string, string[]> Query,
    string? Body,
    IDictionary<string, string>? RouteData = null);
