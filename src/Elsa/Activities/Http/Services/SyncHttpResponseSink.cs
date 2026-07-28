using Microsoft.AspNetCore.Http;

namespace Elsa.Activities.Http.Services;

/// <summary>
/// Request-scoped compatibility marker for a synchronous endpoint response started directly during dispatch.
/// Canonical <see cref="Activities.WriteHttpResponse"/> does not consume this service; it returns a typed result
/// that <see cref="HttpResponseInstructionDelivery"/> delivers after the isolated activity attempt commits.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why not <c>IHttpContextAccessor</c>.</b> Activities must never observe the live request. The middleware owns
/// both this marker and the post-drain delivery adapter, so request-affine state cannot cross the activity scope.
/// </para>
/// <para>
/// Scoped (per request/scope): registered <c>AddScoped</c> in <c>ActivitiesHttpFeature</c>. It holds only live,
/// request-affine state and is never persisted.
/// </para>
/// </remarks>
public sealed class SyncHttpResponseSink
{
    /// <summary>
    /// The live <see cref="HttpContext"/> for a sync-mode dispatch, or null when this scope is not serving a
    /// sync-mode HTTP request (async mode, durable resume, non-HTTP start, or an un-populated internal scope).
    /// </summary>
    public HttpContext? HttpContext { get; private set; }

    /// <summary>
    /// Whether a <see cref="WriteHttpResponse"/> has written the live response in this scope. The middleware reads
    /// this after the inline drain returns to decide between returning (the workflow authored the response) and
    /// degrading to <c>202 Accepted</c> (E-D5).
    /// </summary>
    public bool ResponseWritten { get; private set; }

    /// <summary>Populates the sink with the live <see cref="HttpContext"/> — called by the middleware for a sync-mode dispatch only.</summary>
    public void Populate(HttpContext httpContext) =>
        HttpContext = httpContext ?? throw new ArgumentNullException(nameof(httpContext));

    /// <summary>Marks that the live response was written in this scope (called by <see cref="WriteHttpResponse"/> after a successful live write).</summary>
    public void MarkResponseWritten() =>
        ResponseWritten = true;
}
