namespace Elsa.Activities.Http.Models;

/// <summary>
/// A description of an inbound HTTP request delivered to an <see cref="Activities.HttpEndpoint"/> trigger. It is
/// the forward-compatible payload the endpoint's request middleware serializes as the start stimulus input.
/// </summary>
/// <remarks>
/// <para>
/// <b>Start-input delivery is pending.</b> The current runtime start path
/// (<c>WorkflowExecutionStartDispatchRequest</c>) does not thread a stimulus's <c>Input</c> into the started
/// instance — only the resume path does. So a workflow started by an <see cref="Activities.HttpEndpoint"/>
/// trigger observes its <em>authored</em> route (path/method), not the live request body/headers/query yet. The
/// middleware still serializes this full model as the stimulus input so that when start-input delivery lands
/// (the named "HTTP endpoint start-input delivery" follow-up), the live request becomes available with no wire
/// change. Until then the live fields are populated only on the middleware side for observability/logging.
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
