using Microsoft.AspNetCore.Http;

namespace Elsa.Api.FastEndpoints.Extensions;

/// <summary>
/// Response helpers for endpoints that stream Server-Sent Events.
/// </summary>
public static class ServerSentEventResponseExtensions
{
    /// <summary>
    /// Writes the SSE response preamble: a 200 status, the <c>text/event-stream</c> content type, and the
    /// cache/connection/proxy-buffering headers a long-lived event stream needs.
    /// </summary>
    /// <remarks>
    /// <c>X-Accel-Buffering: no</c> is the one header that is easy to omit and hard to notice missing: without it
    /// a buffering reverse proxy (nginx and friends) holds events until its buffer fills, so the stream appears
    /// to hang rather than fail. Callers still control when to flush.
    /// </remarks>
    public static void StartServerSentEventStream(this HttpResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        response.StatusCode = StatusCodes.Status200OK;
        response.ContentType = "text/event-stream";
        response.Headers.CacheControl = "no-cache";
        response.Headers.Connection = "keep-alive";
        response.Headers["X-Accel-Buffering"] = "no";
    }
}
