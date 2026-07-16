using System.Text;
using System.Text.Json;
using Elsa.Activities.Http.Activities;
using Elsa.Activities.Http.Models;
using Elsa.Activities.Http.Options;
using Elsa.Activities.Http.Services;
using Elsa.Http.Core;
using Elsa.Http.Core.Contracts;
using Elsa.Http.Core.Exceptions;
using Elsa.Http.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Elsa.Activities.Http.Middleware;

/// <summary>
/// The request middleware that turns an inbound HTTP request into an <see cref="HttpEndpoint"/> stimulus
/// (async/202 baseline; spec 089 B routing, extended for mid-flow resume in D). A request whose path is under
/// <see cref="HttpEndpointOptions.BasePath"/> (matched on a whole path segment, never a bare prefix) has its
/// endpoint-relative path resolved against the per-shell <see cref="IRouteTable"/> via <see cref="IRouteMatcher"/>
/// (ASP.NET route templates, e.g. <c>orders/{id}</c>); the matched template plus the request method form the
/// stimulus identity (<see cref="HttpEndpointStimulus.Hash(string,string)"/>) dispatched through
/// <see cref="IStimulusRouter"/> in <see cref="StimulusRoutingMode.StartAndResume"/> mode — it both starts new
/// instances from matching triggers and resumes waiting instances suspended on a mid-flow endpoint. Unmatched
/// templates or methods yield 404; a (template, method) claimed by more than one workflow <em>definition</em>
/// yields 409 and starts nothing. Any other request passes through to the next middleware.
/// </summary>
/// <remarks>
/// <para>
/// <b>Async/202.</b> The router's start and resume paths are asynchronous (they enqueue work through the actor
/// mailbox); the middleware does not wait for the workflow to run. When at least one instance was started OR
/// resumed it replies <c>202 Accepted</c> with <c>{ started: [...], resumed: [...] }</c> (started/resumed
/// execution ids); when neither happened it replies <c>404 Not Found</c>.
/// </para>
/// <para>
/// <b>Sync (spec 089 sub-unit E, E-D5).</b> When the resolved <see cref="MergedHttpEndpointOptions.ResponseMode"/>
/// is <see cref="ResponseMode.Sync"/> the middleware lets the workflow author the live response in the same
/// exchange. The in-process drain runs to quiescence on the caller's async flow. A <see cref="WriteHttpResponse"/>
/// reached during that drain commits one typed <see cref="HttpResponseInstruction"/> result; after
/// <c>RouteAsync</c> returns, <see cref="HttpResponseInstructionDelivery"/> reads and delivers that committed
/// result through the request-owned <see cref="HttpResponse"/>. The activity therefore never receives a live
/// request service or <see cref="HttpContext"/>, preserving its isolated per-attempt activation scope. When no
/// instruction was committed, the middleware degrades to the same <c>202</c> writer as async mode — ONE degrade path covering suspend-before-response, a run
/// that authored no <see cref="WriteHttpResponse"/>, and a non-local (<see cref="BookmarkResumeDispatchStatus"/>
/// deferred) dispatch whose ambient services never crossed the transport — with no locality inspection. The 404
/// branch (zero starts AND zero dispatched resumes) still runs FIRST, before the sink is consulted. Sink resolution
/// is null-safe: an absent registration degrades to async behaviour rather than throwing. The per-endpoint timeout
/// (408) and fault mapping (400/500) already enclose the dispatch and now bound the synchronous wait. Async mode is
/// bit-identical to the pre-E behaviour — no sink populate, no ambient services.
/// </para>
/// <para>
/// <b>Live input.</b> The full <see cref="HttpRequestModel"/> (path, method, headers, query, body) is serialized
/// as the stimulus input; the router carries it on the dedicated stimulus-input channel for starts and as the
/// resume input for resumes (spec 089 sub-units A/D), where <see cref="HttpEndpoint"/> surfaces it as its Result.
/// Bodies larger than <see cref="HttpEndpointOptions.MaxRequestBodyBytes"/> (or the per-endpoint override) are
/// rejected with <c>413</c> before dispatch — the payload becomes durable state on the instance, so the transport
/// guards its size.
/// </para>
/// <para>
/// <b>Endpoint options for resume-only matches (D-D5).</b> The per-endpoint options (authorize/policy/timeout/size
/// limit) normally ride the trigger claimant's binding metadata. When NO trigger claimant exists for the matched
/// (template, method) — a purely mid-flow endpoint — they are read instead from a waiting bookmark's metadata via
/// the expiry-aware <see cref="IGlobalBookmarkStimulusLookup"/>, filtered to the matched stimulus hash. When both a
/// claimant and waiting bookmarks exist, the claimant wins (single deterministic source). The enforcement order
/// (authorize → size limit → parse → timeout+faults) is unchanged and now applies to resume-only matches too.
/// </para>
/// <para>
/// Registered as an <see cref="IMiddleware"/> (resolved from DI per request) so it can take the scoped
/// <see cref="IStimulusRouter"/>; <see cref="ActivitiesHttpFeature"/> mounts it into the shell pipeline through
/// the CShells middleware seam.
/// </para>
/// </remarks>
public sealed class HttpEndpointMiddleware(
    IStimulusRouter router,
    IRouteTable routeTable,
    IRouteMatcher routeMatcher,
    IWorkflowTriggerBindingStore triggerBindingStore,
    IGlobalBookmarkStimulusLookup bookmarkStimulusLookup,
    IOptions<HttpEndpointOptions> options) : IMiddleware
{
    private const string RequestedBy = "http-endpoint";
    private readonly HttpEndpointOptions _options = options.Value;

    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        var requestPath = context.Request.Path.Value ?? string.Empty;
        var basePath = _options.BasePath.TrimEnd('/');

        // An empty/root base path would make every request an endpoint candidate and turn unmatched routes into
        // 404s host-wide; workflow endpoints require a dedicated base path (see HttpEndpointOptions.BasePath).
        if (basePath.Length == 0)
        {
            await next(context);
            return;
        }

        // Segment-boundary match: '/workflows/http/orders' is ours; '/workflows/httpstatus' is a sibling route.
        if (!requestPath.StartsWith(basePath, StringComparison.OrdinalIgnoreCase)
            || (requestPath.Length > basePath.Length && requestPath[basePath.Length] != '/'))
        {
            await next(context);
            return;
        }

        var endpointPath = requestPath[basePath.Length..].Trim('/');
        if (string.IsNullOrWhiteSpace(endpointPath))
        {
            await next(context);
            return;
        }

        // From here on the request is ours to handle. A client disconnect cancels RequestAborted; any operation
        // observing it (the claimant lookup, the dispatch, a response write) throws OperationCanceledException.
        // There is no live connection to write a response to, so swallow it and return rather than letting it
        // escape as an unhandled pipeline exception (#592 item 12). A cancellation the request did NOT ask for
        // still propagates.
        try
        {
            await HandleEndpointAsync(context, endpointPath);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
        }
    }

    private async Task HandleEndpointAsync(HttpContext context, string endpointPath)
    {
        // Resolve the concrete endpoint-relative path to a published route template (spec 089 B). The route
        // table holds endpoint-relative templates; TemplateMatcher wants rooted paths, so both sides get a
        // leading slash for the match. The route table enumerates most-specific-first (issue #592 item 1), so
        // "first match wins" is deterministic: a literal template (orders/list) beats a parameter template
        // (orders/{id}) regardless of publish/insertion order.
        var (template, routeValues) = ResolveTemplate(endpointPath);
        if (template is null)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        var stimulusHash = HttpEndpointStimulus.Hash(template, context.Request.Method);

        // Fetch the trigger claimants once (also the source of the endpoint options when present). The ambiguity
        // guard below shares this fetch rather than re-querying, and the router reuses it on the start path.
        var claimants = await triggerBindingStore.ListByStimulusAsync(HttpEndpointStimulus.StimulusType, stimulusHash, context.RequestAborted);

        // Endpoint options ride the claimant binding's non-identity metadata (spec 089 C, FR-012..FR-014). Sibling
        // claimants of one definition share options; on an ambiguous route (rejected below) any claimant's
        // Authorize flag is enough to know the endpoint is protected, so read the strongest: if ANY claimant
        // authorizes, authorization must pass before we disclose anything else. When there is NO trigger claimant
        // (a purely mid-flow endpoint) the options come instead from a FAIL-CLOSED merge across ALL waiting bookmarks
        // matching this stimulus hash via the expiry-aware lookup (spec 089 D, D-D5) — options are excluded from the
        // hash, so divergent bookmarks on one (template, method) must not let a lax one defeat a strict one. The
        // claimant wins when both exist, so the bookmark merge only runs when no claimant was found.
        var endpointOptions = claimants.Count > 0
            ? ResolveEndpointOptions(claimants)
            : HttpEndpointStimulusOptions.MergeForResume(await ResolveWaitingBookmarkMetadataAsync(stimulusHash, context.RequestAborted));

        // Authorization runs before the body is read, before ambiguity/existence is disclosed, and before any
        // dispatch (FR-012, #592 item 10 — auth before disclosure). Fail closed: an Authorize endpoint with no
        // handler composed (WorkflowsRuntimeHttp feature absent) denies. Every distinct merged policy must pass —
        // a resume-only match can span waiting bookmarks with divergent policies (D-D5). A configuration fault
        // (missing scheme / unregistered policy) surfaces as 500, distinct from the 401 an anonymous caller gets
        // (#592 item 11).
        if (endpointOptions.Authorize)
        {
            bool authorized;
            try
            {
                var authorizationHandler = context.RequestServices?.GetService(typeof(IHttpEndpointAuthorizationHandler)) as IHttpEndpointAuthorizationHandler;
                authorized = authorizationHandler is not null
                    && await AuthorizeAllPoliciesAsync(authorizationHandler, context, endpointOptions.Policies);
            }
            catch (HttpEndpointAuthorizationConfigurationException)
            {
                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                return;
            }

            if (!authorized)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }
        }

        // Ambiguity guard (spec FR-009): a (template, method) claimed by more than one workflow definition is an
        // authoring error, not fan-out — reject before any dispatch so neither workflow starts. Evaluated only
        // after authorization, so an anonymous caller cannot distinguish an ambiguous route from a valid one. The
        // body is slimmed to a stable code — it must not echo the method/template to an (authorized) caller
        // (#592 item 10). The guard stays trigger-claimant-only: bookmark resumes are instance-scoped and cannot
        // be ambiguous across definitions (D-D5).
        if (claimants.Select(binding => binding.DefinitionId).Distinct(StringComparer.Ordinal).Count() > 1)
        {
            context.Response.StatusCode = StatusCodes.Status409Conflict;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(
                JsonSerializer.Serialize(new { error = "ambiguous-endpoint" }),
                context.RequestAborted);
            return;
        }

        var maxBodyBytes = endpointOptions.RequestSizeLimit ?? _options.MaxRequestBodyBytes;
        var requestModel = await BuildRequestModelAsync(context, endpointPath, routeValues, maxBodyBytes);
        if (requestModel is null)
        {
            context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
            return;
        }

        var input = JsonSerializer.SerializeToElement(requestModel);

        // Sync mode (E-D5): drain inline, then deliver any committed typed response instruction in this request.
        // The sink remains a compatibility marker for an already-started custom response; canonical
        // WriteHttpResponse does not consume it or any other request-affine service.
        var sync = endpointOptions.ResponseMode == ResponseMode.Sync;
        var responseSink = sync ? context.RequestServices?.GetService<SyncHttpResponseSink>() : null;
        responseSink?.Populate(context);

        // Reuse the claimant set already fetched for the ambiguity guard + options: the router's start path would
        // otherwise issue an identical ListByStimulusAsync(type, hash) for the same request (spec 089 efficiency #7).
        // StartAndResume (spec 089 D): start new instances from matching triggers AND resume waiting instances
        // suspended on this mid-flow endpoint. The router's snapshot-before-start guard prevents an instance
        // started by THIS request from also resuming itself.
        var dispatch = new StimulusDispatchRequest(
            stimulusType: HttpEndpointStimulus.StimulusType,
            stimulusHash: stimulusHash,
            input: input,
            mode: StimulusRoutingMode.StartAndResume,
            requestedBy: RequestedBy,
            matchedTriggerBindings: claimants,
            dispatchOptions: sync ? new WorkflowExecutionCommandDispatchOptions(context.RequestServices) : null,
            payloadType: new Elsa.Primitives.Models.ValueTypeDescriptor(
                Elsa.Primitives.Models.TypeAliasConvention.CanonicalAlias(typeof(HttpRequestModel)),
                schemaVersion: 1),
            providerId: HttpEndpoint.ActivityType);

        // Per-endpoint RequestTimeout bounds dispatch (which drains inline on the in-process actor, so it can
        // genuinely take time); faults map to statuses via the endpoint fault handler seam (FR-013/FR-014).
        // Non-positive values are rejected at publish (review C2); the > Zero guard here is defense in depth
        // against hand-seeded binding metadata — CancelAfter would throw on a negative TimeSpan.
        StimulusRoutingResult result;
        using var timeoutSource = endpointOptions.RequestTimeout is { } timeout && timeout > TimeSpan.Zero
            ? CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted)
            : null;
        timeoutSource?.CancelAfter(endpointOptions.RequestTimeout!.Value);
        try
        {
            result = await router.RouteAsync(dispatch, timeoutSource?.Token ?? context.RequestAborted);
        }
        catch (Exception exception) when (!context.RequestAborted.IsCancellationRequested)
        {
            // A genuine dispatch fault (not a client abort — that path is swallowed by the top-level guard in
            // InvokeAsync): map it to a status. A per-endpoint timeout trip is an OperationCanceledException on
            // the linked token while RequestAborted stays live, so it lands here (mapped to 408), not the guard.
            await HandleDispatchFaultAsync(context, exception, timedOut: timeoutSource?.IsCancellationRequested == true);
            return;
        }

        // Nothing matched: no trigger started AND no waiting instance was resumed (spec 089 D, D-D6). A resolved
        // route whose only claim was a since-consumed bookmark deterministically lands here → 404. This branch stays
        // FIRST — a sync-mode request that started/resumed nothing is still 404, never a 202 degrade (E-D5).
        if (result.StartedCount == 0 && result.ResumedCount == 0)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        // A legacy/custom live writer may already have started the response; preserve it before looking for the
        // canonical committed instruction.
        if (responseSink?.ResponseWritten == true || context.Response.HasStarted)
            return;

        var startedIds = result.Starts
            .Where(start => start.WorkflowExecutionId is not null)
            .Select(start => start.WorkflowExecutionId!)
            .ToArray();

        // Resumed = the execution ids of the DISPATCHED resumes only (a skipped/lost resume is not reported).
        var resumedIds = result.Resumes
            .Where(resume => resume.Status == BookmarkResumeDispatchStatus.Dispatched)
            .Select(resume => resume.WorkflowExecutionId)
            .ToArray();

        // Canonical sync delivery happens after the inline drain: WriteHttpResponse returned one typed instruction,
        // the activity checkpoint committed it, and this request-owned adapter now delivers that durable result.
        // The HttpContext never enters the activity activation or its isolated per-attempt DI scope.
        if (sync && context.RequestServices?.GetService<HttpResponseInstructionDelivery>() is { } delivery)
        {
            var dispatchedIds = startedIds.Concat(resumedIds).ToArray();
            if (await delivery.TryDeliverAsync(context.Response, dispatchedIds, context.RequestAborted))
            {
                responseSink?.MarkResponseWritten();
                return;
            }
        }

        context.Response.StatusCode = StatusCodes.Status202Accepted;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(
            JsonSerializer.Serialize(new { started = startedIds, resumed = resumedIds }),
            context.RequestAborted);
    }

    /// <summary>
    /// Resolves the per-endpoint options from the claimant bindings' non-identity metadata. Sibling claimants of
    /// one definition share options, so the first claimant's metadata drives timeout/size/policy. Authorization is
    /// treated as the strongest claim across claimants: if ANY claimant requires authorization the endpoint is
    /// protected, so authorization runs (and an anonymous caller is denied) before route ambiguity is disclosed on
    /// an ambiguous route (#592 item 10). Empty claimants (a purely mid-flow endpoint) are handled by the caller
    /// via <see cref="ResolveWaitingBookmarkMetadataAsync"/> + <see cref="HttpEndpointStimulusOptions.MergeForResume"/>
    /// instead.
    /// </summary>
    private static MergedHttpEndpointOptions ResolveEndpointOptions(IEnumerable<WorkflowTriggerBinding> claimants)
    {
        HttpEndpointStimulusOptions? first = null;
        var authorizeAny = false;

        foreach (var claimant in claimants)
        {
            var options = HttpEndpointStimulusOptions.FromMetadata(claimant.Metadata);
            first ??= options;
            authorizeAny |= options.Authorize;
        }

        return MergedHttpEndpointOptions.FromClaimant(first ?? HttpEndpointStimulusOptions.None, authorizeAny);
    }

    /// <summary>
    /// Runs the authorization handler once per distinct <paramref name="policies"/> and requires ALL to pass
    /// (fail closed, D-D5): a resume-only match can span waiting bookmarks with divergent policies, so an
    /// endpoint is authorized only when every declared policy is satisfied. When the set is empty the endpoint is
    /// protected by an authorize-only check (a single call with a null policy), preserving the single-policy
    /// (claimant) behavior. Short-circuits on the first denial.
    /// </summary>
    private static async ValueTask<bool> AuthorizeAllPoliciesAsync(
        IHttpEndpointAuthorizationHandler handler,
        HttpContext context,
        IReadOnlyCollection<string> policies)
    {
        if (policies.Count == 0)
            return await handler.AuthorizeAsync(new AuthorizeHttpEndpointContext(context, Policy: null));

        foreach (var policy in policies)
        {
            if (!await handler.AuthorizeAsync(new AuthorizeHttpEndpointContext(context, policy)))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Resolves the endpoint options metadata for a resume-only match (no trigger claimant) from EVERY waiting
    /// bookmark matching this stimulus hash (spec 089 D, D-D5). Uses the expiry-aware lookup and filters to the
    /// matched stimulus hash, so an expired or differently-hashed bookmark never supplies options. The caller merges
    /// the returned metadata fail-closed via <see cref="HttpEndpointStimulusOptions.MergeForResume"/> — an empty
    /// sequence (no waiting bookmark matched) merges to defaults (no authorize, global size limit, no timeout).
    /// </summary>
    private async ValueTask<IReadOnlyList<IReadOnlyDictionary<string, string>?>> ResolveWaitingBookmarkMetadataAsync(string stimulusHash, CancellationToken cancellationToken)
    {
        var waiting = await bookmarkStimulusLookup.FindWaitingByTypeAsync(
            new GlobalBookmarkStimulusTypeLookupRequest(HttpEndpointStimulus.StimulusType, DateTimeOffset.UtcNow),
            cancellationToken);

        return waiting.Matches
            .Where(bookmark => StringComparer.Ordinal.Equals(bookmark.StimulusHash, stimulusHash))
            .Select(bookmark => (IReadOnlyDictionary<string, string>?)bookmark.Metadata)
            .ToArray();
    }

    /// <summary>Maps a dispatch fault to a response status via the endpoint fault handler seam; inline fallback (the shared <see cref="Elsa.Http.Core.HttpEndpointFaultMapping"/>) when the policy feature is absent.</summary>
    private static async Task HandleDispatchFaultAsync(HttpContext context, Exception exception, bool timedOut)
    {
        // Sync mode can fault AFTER WriteHttpResponse already wrote the live response (e.g. endpoint -> write ->
        // stalling activity tripping the request timeout). Once the response has started, a status code can no
        // longer be written — attempting it throws and escapes as an unhandled pipeline exception on a connection
        // that already carries the workflow-authored response. The caller got that response; the fault remains
        // observable in durable state per normal runtime semantics, so the response is left untouched.
        if (context.Response.HasStarted)
            return;

        var faultException = timedOut && exception is OperationCanceledException
            ? new TimeoutException("The endpoint's request timeout elapsed before dispatch completed.", exception)
            : exception;

        if (context.RequestServices?.GetService(typeof(IHttpEndpointFaultHandler)) is IHttpEndpointFaultHandler faultHandler)
        {
            await faultHandler.HandleAsync(new HttpEndpointFaultContext(context, [faultException], context.RequestAborted));
            return;
        }

        // No handler composed: apply the same default mapping the policy module's handler uses (shared owner).
        context.Response.StatusCode = Elsa.Http.Core.HttpEndpointFaultMapping.ToStatusCode(faultException);
    }

    /// <summary>
    /// Resolves the endpoint-relative path against the per-shell route table. Returns the matched template
    /// (endpoint-relative, as stored) plus its extracted route values, or (null, empty) when nothing matches.
    /// </summary>
    private (string? Template, IReadOnlyDictionary<string, string> RouteValues) ResolveTemplate(string endpointPath)
    {
        var rootedPath = "/" + endpointPath;

        foreach (var routeData in routeTable)
        {
            var template = routeData.Route;
            if (string.IsNullOrWhiteSpace(template))
                continue;

            // Reuse the route table's precompiled matcher (issue #592 item 6) — no per-request template parse.
            var values = routeMatcher.Match(routeData, rootedPath);
            if (values is null)
                continue;

            var routeValues = values.ToDictionary(
                item => item.Key,
                item => item.Value?.ToString() ?? string.Empty,
                StringComparer.OrdinalIgnoreCase);
            return (template.Trim('/'), routeValues);
        }

        return (null, EmptyRouteValues);
    }

    private static readonly IReadOnlyDictionary<string, string> EmptyRouteValues =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Builds the request model; null when the body exceeds <paramref name="maxBodyBytes"/>.</summary>
    private static async Task<HttpRequestModel?> BuildRequestModelAsync(
        HttpContext context,
        string endpointPath,
        IReadOnlyDictionary<string, string> routeValues,
        long maxBodyBytes)
    {
        if (context.Request.ContentLength is { } declaredLength && declaredLength > maxBodyBytes)
            return null;

        string? body = null;
        if (context.Request.Body.CanRead)
        {
            body = await ReadBodyBoundedAsync(context.Request.Body, maxBodyBytes, context.RequestAborted);
            if (body is null)
                return null; // Exceeded the cap during streaming (Content-Length absent or lied).
            if (body.Length == 0)
                body = null;
        }

        var headers = context.Request.Headers
            .ToDictionary(header => header.Key, header => header.Value.Select(v => v ?? string.Empty).ToArray(), StringComparer.OrdinalIgnoreCase);
        var query = context.Request.Query
            .ToDictionary(item => item.Key, item => item.Value.Select(v => v ?? string.Empty).ToArray(), StringComparer.OrdinalIgnoreCase);

        // Parsed content is NOT persisted here (spec 089 efficiency #9): the HttpEndpoint activity derives it from
        // Body + the Content-Type header via the deterministic IHttpRequestBodyParser seam, so the stimulus payload
        // carries the raw body exactly once instead of the body plus a re-encoded copy of it.
        return new HttpRequestModel(
            Path: HttpEndpointStimulus.NormalizeTemplate(endpointPath),
            Method: context.Request.Method.ToUpperInvariant(),
            Headers: headers,
            Query: query,
            Body: body,
            RouteData: new Dictionary<string, string>(routeValues, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>Reads the body up to the byte cap (enforced during streaming, not only via Content-Length); null when exceeded.</summary>
    private static async Task<string?> ReadBodyBoundedAsync(Stream body, long maxBodyBytes, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int read;
        while ((read = await body.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > maxBodyBytes)
                return null;
            buffer.Write(chunk, 0, read);
        }

        return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }
}
