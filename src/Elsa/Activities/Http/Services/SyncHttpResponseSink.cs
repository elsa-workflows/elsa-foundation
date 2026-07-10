using Microsoft.AspNetCore.Http;

namespace Elsa.Activities.Http.Services;

/// <summary>
/// The request-scoped seam that lets a <see cref="Activities.WriteHttpResponse"/> running inside a synchronous
/// (spec 089 sub-unit E) endpoint dispatch write the live HTTP response in the same exchange (E-D2). The
/// middleware populates the request scope's instance with the live <see cref="HttpContext"/> ONLY for a sync-mode
/// dispatch, and passes <c>HttpContext.RequestServices</c> as the dispatch's ambient services so the inline drain
/// resolves this same instance. <see cref="WriteHttpResponse"/> resolves it null-safely: a populated sink ⇒ live
/// write; an empty sink (async mode, durable resume, non-HTTP start, or absent registration) ⇒ artifact-only.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a scoped sink and not <c>IHttpContextAccessor</c> (the load-bearing design guard).</b> The inline drain
/// runs on the <em>caller's</em> async flow, so the <c>AsyncLocal</c>-backed <c>IHttpContextAccessor</c> would
/// resolve the live <see cref="HttpContext"/> even for an ASYNC-mode dispatch — and even from a fresh internal
/// scope — leaking the live context into async-mode runs and turning every async endpoint into an accidental live
/// write. The discriminator must therefore be an <em>ambient-scope-only</em> holder that the middleware populates
/// deliberately for sync mode alone, never an AsyncLocal accessor. A <see cref="WriteHttpResponse"/> resolving
/// this sink from a fresh internal scope (async mode) gets a distinct, unpopulated instance — so it stays
/// artifact-only even when an <see cref="HttpContext"/> technically exists on the flow.
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
