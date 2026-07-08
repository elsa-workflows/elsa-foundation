namespace Elsa.Activities.Http.Models;

/// <summary>
/// A description of an inbound HTTP request delivered to an <see cref="Activities.HttpEndpoint"/> trigger. It is
/// the forward-compatible payload the endpoint's request middleware serializes as the start stimulus input.
/// </summary>
/// <remarks>
/// <para>
/// The middleware serializes this full model as the stimulus input, and the router's start path seeds it as the
/// <c>WellKnownStimulusInputs.StimulusInput</c> workflow input (spec 089 sub-unit A), so a workflow started by an
/// <see cref="Activities.HttpEndpoint"/> trigger observes the live request body/headers/query through its
/// trigger activity's Result. Resumed instances receive the same model as the resume input.
/// </para>
/// </remarks>
/// <param name="Path">The endpoint-relative request path (below the configured base path), without leading/trailing slashes.</param>
/// <param name="Method">The HTTP method (verb) of the request.</param>
/// <param name="Headers">The request headers, keyed by header name.</param>
/// <param name="Query">The request query-string values, keyed by parameter name.</param>
/// <param name="Body">The raw request body as a string, if any.</param>
public sealed record HttpRequestModel(
    string Path,
    string Method,
    IDictionary<string, string[]> Headers,
    IDictionary<string, string[]> Query,
    string? Body);
