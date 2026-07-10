using Elsa.Activities.Http.Models;
using Elsa.Activities.Http.Services;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Http.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts;
using Microsoft.AspNetCore.Http;

namespace Elsa.Activities.Http.Activities;

/// <summary>
/// Records the HTTP response a workflow intends to return, as a durable, observable artifact — and, for a
/// synchronous (spec 089 sub-unit E) endpoint dispatch, additionally writes that response to the live HTTP
/// response stream in the same exchange.
/// </summary>
/// <remarks>
/// <para>
/// <b>Durable artifact (always).</b> Recording exactly one <see cref="HttpResponseInstruction"/> into the durable
/// workflow-output channel under <see cref="HttpResponseInstruction.OutputName"/> is the activity's baseline effect
/// and is unchanged by E: it is durably persisted and assertable immediately after the run through the same output
/// channel any workflow output uses. In the async/202 model (see <see cref="HttpEndpoint"/>) no HTTP request is
/// waiting when the workflow executes — the endpoint already replied <c>202 Accepted</c> — so the artifact is the
/// whole effect. The activity never silently no-ops.
/// </para>
/// <para>
/// <b>Live write (sync mode, E-D3).</b> When the run is a synchronous-endpoint dispatch, the middleware populated
/// this request scope's <see cref="SyncHttpResponseSink"/> with the live <see cref="HttpContext"/> (E-D2). If that
/// sink exposes a context AND the response has not started, this activity also writes the authored status code and
/// headers, and the body through the <see cref="IHttpContentFactory"/> set matched on the authored content type
/// (<see cref="IHttpContentFactory.CreateHttpContent"/> → <see cref="HttpContent.CopyToAsync(System.IO.Stream)"/>);
/// an absent/unmatched content type with a non-empty body falls back to a plain
/// <see cref="HttpResponseWritingExtensions.WriteAsync(HttpResponse, string, System.Threading.CancellationToken)"/>
/// with the raw body and the authored content type (or <c>text/plain</c> when absent). It then marks
/// <see cref="SyncHttpResponseSink.MarkResponseWritten"/> so the middleware returns rather than degrading to 202.
/// A second <see cref="WriteHttpResponse"/> in one run finds <c>Response.HasStarted == true</c> and skips the live
/// write (the artifact is still recorded). A missing HTTP context (async mode, durable resume, non-HTTP start, or
/// an absent sink registration) yields artifact-only with NO fault (spec 089 assumption, softer than elsa-core).
/// </para>
/// <para>
/// <b>Design guard — the sink, never <c>IHttpContextAccessor</c>.</b> The live-write discriminator is the
/// request-scoped <see cref="SyncHttpResponseSink"/>, not <see cref="IHttpContextAccessor"/>. The synchronous
/// dispatch drains INLINE on the caller's async flow, so the <c>AsyncLocal</c>-backed accessor would resolve the
/// live <see cref="HttpContext"/> even for an ASYNC-mode dispatch — and even from a fresh internal scope — leaking
/// the live context into async-mode runs and turning every async endpoint into an accidental live write. Only the
/// ambient scope the middleware deliberately populates for sync mode carries a non-null
/// <see cref="SyncHttpResponseSink.HttpContext"/>; a fresh internal scope gets a distinct, unpopulated sink and
/// stays artifact-only. See <see cref="SyncHttpResponseSink"/>.
/// </para>
/// <para>
/// Like the other control leaves, <see cref="WriteHttpResponse"/> requires the Elsa runtime activity execution
/// context (it records a workflow output).
/// </para>
/// </remarks>
public sealed class WriteHttpResponse : CodeActivity
{
    /// <summary>The HTTP status code to record. Defaults to 200 when not set.</summary>
    public InputArgument<int>? StatusCode { get; set; }

    /// <summary>The response body to record.</summary>
    public InputArgument<string>? Body { get; set; }

    /// <summary>The response content type (media type) to record.</summary>
    public InputArgument<string>? ContentType { get; set; }

    /// <summary>Response headers to record, keyed by header name.</summary>
    public InputArgument<IDictionary<string, string[]>>? Headers { get; set; }

    protected override async ValueTask ExecuteAsync(IActivityExecutionContext context)
    {
        var runtimeContext = RequireRuntimeContext(context);

        var statusCode = context.Get(StatusCode);
        var effectiveStatus = statusCode > 0 ? statusCode : 200;
        var headers = context.Get(Headers) ?? new Dictionary<string, string[]>();
        var body = context.Get(Body);
        var contentType = context.Get(ContentType);

        var instruction = new HttpResponseInstruction(
            StatusCode: effectiveStatus,
            Headers: headers,
            Body: body,
            ContentType: contentType);

        // The durable artifact is recorded unconditionally — unchanged by E.
        runtimeContext.SetWorkflowOutput(HttpResponseInstruction.OutputName, instruction);

        // Sync-mode live write (E-D3): only when the middleware populated this scope's sink with a live context and
        // the response has not started. Resolved null-safely — an absent sink or empty sink stays artifact-only.
        var sink = ResolveSink(context);
        if (sink?.HttpContext is not { } httpContext || httpContext.Response.HasStarted)
            return;

        await WriteLiveResponseAsync(context, httpContext.Response, instruction);
        sink.MarkResponseWritten();
    }

    /// <summary>
    /// Writes the recorded response to the live stream (E-D3): status code first, then the authored headers, then
    /// the body. A non-empty body is encoded through the <see cref="IHttpContentFactory"/> whose
    /// <see cref="IHttpContentFactory.SupportedContentTypes"/> include the authored content type; an absent or
    /// unmatched content type falls back to a plain string write with the raw body and the authored content type
    /// (or <c>text/plain</c> when absent), so a body is never silently dropped.
    /// </summary>
    private static async ValueTask WriteLiveResponseAsync(IActivityExecutionContext context, HttpResponse response, HttpResponseInstruction instruction)
    {
        response.StatusCode = instruction.StatusCode;

        foreach (var (name, values) in instruction.Headers)
            response.Headers[name] = values;

        if (string.IsNullOrEmpty(instruction.Body))
        {
            // No body to write; still stamp the authored content type so the response advertises it.
            if (!string.IsNullOrWhiteSpace(instruction.ContentType))
                response.ContentType = instruction.ContentType;
            return;
        }

        var factory = MatchContentFactory(context, instruction.ContentType);
        if (factory is not null)
        {
            using var content = factory.CreateHttpContent(instruction.Body, instruction.ContentType!);
            if (content.Headers.ContentType is { } factoryContentType)
                response.ContentType = factoryContentType.ToString();
            await content.CopyToAsync(response.Body, context.CancellationToken);
            return;
        }

        // Raw fallback: absent or unmatched content type — write the body verbatim with the authored content type
        // (or text/plain when absent) rather than dropping it.
        response.ContentType = string.IsNullOrWhiteSpace(instruction.ContentType) ? "text/plain" : instruction.ContentType;
        await response.WriteAsync(instruction.Body, context.CancellationToken);
    }

    /// <summary>
    /// Finds the <see cref="IHttpContentFactory"/> that supports <paramref name="contentType"/> (case-insensitive
    /// on the media type, ignoring any charset parameter). Returns null for an absent content type or when no
    /// factory matches — the caller then uses the raw fallback.
    /// </summary>
    private static IHttpContentFactory? MatchContentFactory(IActivityExecutionContext context, string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
            return null;

        // Match on the bare media type so "application/json; charset=utf-8" still resolves the JSON factory.
        var mediaType = contentType.Split(';', 2)[0].Trim();
        var factories = TryResolve<IEnumerable<IHttpContentFactory>>(context) ?? [];
        return factories.FirstOrDefault(factory => factory.SupportedContentTypes
            .Any(supported => string.Equals(supported, mediaType, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// Resolves the request-scoped <see cref="SyncHttpResponseSink"/> null-safely: an absent registration (a
    /// composition without the HTTP feature) yields null so the activity stays artifact-only and never faults on
    /// the sync seam. See the design guard in the type remarks for why this is the sink and never
    /// <see cref="IHttpContextAccessor"/>.
    /// </summary>
    private static SyncHttpResponseSink? ResolveSink(IActivityExecutionContext context) =>
        TryResolve<SyncHttpResponseSink>(context);

    /// <summary>
    /// Null-safe service resolution over the activity context's <c>GetRequiredService</c> (the only resolver the
    /// context exposes): a missing registration surfaces as an <see cref="InvalidOperationException"/>, which is
    /// swallowed to null so the optional sync-response seam degrades to artifact-only rather than faulting.
    /// </summary>
    private static TService? TryResolve<TService>(IActivityExecutionContext context) where TService : class
    {
        try
        {
            return context.GetRequiredService<TService>();
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static IRuntimeActivityExecutionContext RequireRuntimeContext(IActivityExecutionContext context)
    {
        if (context is IRuntimeActivityExecutionContext runtimeContext)
            return runtimeContext;

        throw new InvalidOperationException("WriteHttpResponse requires an Elsa runtime activity execution context.");
    }
}
