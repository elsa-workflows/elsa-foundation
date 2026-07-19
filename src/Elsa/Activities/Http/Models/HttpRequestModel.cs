using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

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
public sealed class HttpRequestModel
{
    /// <summary>Creates a request model from the mutable collection shape exposed by ASP.NET Core.</summary>
    public HttpRequestModel(
        string Path,
        string Method,
        IDictionary<string, string[]> Headers,
        IDictionary<string, string[]> Query,
        string? Body,
        IDictionary<string, string>? RouteData = null)
        : this(
            Path,
            Method,
            Headers.ToDictionary(item => item.Key, item => (IReadOnlyList<string>)item.Value, StringComparer.OrdinalIgnoreCase),
            Query.ToDictionary(item => item.Key, item => (IReadOnlyList<string>)item.Value, StringComparer.OrdinalIgnoreCase),
            Body,
            RouteData is null ? null : new Dictionary<string, string>(RouteData, StringComparer.OrdinalIgnoreCase))
    {
    }

    /// <summary>Creates the persistable wire document and snapshots every collection.</summary>
    [JsonConstructor]
    public HttpRequestModel(
        string Path,
        string Method,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? Headers,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? Query,
        string? Body,
        IReadOnlyDictionary<string, string>? RouteData = null)
    {
        this.Path = Path;
        this.Method = Method;
        this.Headers = SnapshotValues(Headers);
        this.Query = SnapshotValues(Query);
        this.Body = Body;
        this.RouteData = Snapshot(RouteData);
    }

    public string Path { get; }
    public string Method { get; }
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Headers { get; }
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Query { get; }
    public string? Body { get; }
    public IReadOnlyDictionary<string, string> RouteData { get; }

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> SnapshotValues(
        IReadOnlyDictionary<string, IReadOnlyList<string>>? source)
    {
        var snapshot = (source ?? new Dictionary<string, IReadOnlyList<string>>())
            .ToDictionary(
                item => item.Key,
                item => (IReadOnlyList<string>)Array.AsReadOnly(item.Value?.ToArray() ?? []),
                StringComparer.OrdinalIgnoreCase);
        return new ReadOnlyDictionary<string, IReadOnlyList<string>>(snapshot);
    }

    private static IReadOnlyDictionary<string, string> Snapshot(IReadOnlyDictionary<string, string>? source) =>
        new ReadOnlyDictionary<string, string>(
            new Dictionary<string, string>(source ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase));
}
