namespace Elsa.Activities.Http.Models;

/// <summary>
/// The intended HTTP response an <see cref="Activities.WriteHttpResponse"/> activity records into workflow
/// state. In the async/202 endpoint model no HTTP request is waiting at execution time (the endpoint replied
/// <c>202 Accepted</c> the moment it started the workflow), so this is a durable, observable <em>artifact</em>
/// rather than a live write to a response stream.
/// </summary>
/// <remarks>
/// <para>
/// <b>Contract.</b> <see cref="Activities.WriteHttpResponse"/> records exactly one of these under the well-known
/// workflow-output name <see cref="OutputName"/>, using the same durable output channel as any other workflow
/// output. It is therefore (a) observable and assertable immediately after the run through the durable-value
/// store, and (b) the exact payload the future "HTTP synchronous response correlation" subsystem will deliver
/// to a caller that is waiting on a keyed channel. The activity never silently no-ops: recording the artifact
/// is its whole effect.
/// </para>
/// </remarks>
/// <param name="StatusCode">The HTTP status code to return. Defaults to 200 when not set by the author.</param>
/// <param name="Headers">Response headers to return, keyed by header name.</param>
/// <param name="Body">The response body, if any.</param>
/// <param name="ContentType">The response content type (media type), if any.</param>
public sealed record HttpResponseInstruction(
    int StatusCode,
    IDictionary<string, string[]> Headers,
    string? Body,
    string? ContentType)
{
    /// <summary>The well-known workflow-output name the recorded response artifact is tagged with.</summary>
    public const string OutputName = "HttpResponse";
}
