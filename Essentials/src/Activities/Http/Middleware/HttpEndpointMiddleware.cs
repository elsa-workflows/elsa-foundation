using System.Globalization;
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
/// (async/202 baseline routing, extended for mid-flow resume). A request whose path is under
/// <see cref="HttpEndpointOptions.BasePath"/> (matched on a whole path segment, never a bare prefix) has its
/// endpoint-relative path and method resolved against the per-shell <see cref="IRouteTable"/> via <see cref="IRouteMatcher"/>
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
/// execution ids) plus response-mode diagnostics; when neither happened it replies <c>404 Not Found</c>.
/// </para>
/// <para>
/// <b>Sync.</b> When the resolved <see cref="MergedHttpEndpointOptions.ResponseMode"/>
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
/// is null-safe: an absent registration degrades to async behaviour rather than throwing. A finite host timeout
/// bounds synchronous request work and response delivery; an endpoint may choose a shorter limit. Timeout expiry
/// maps to 408 before response start and aborts an already-started response; client disconnects remain silent.
/// Async mode keeps authored timeout behavior, with no sink
/// population or ambient services.
/// </para>
/// <para>
/// <b>Live input.</b> The full <see cref="HttpRequestModel"/> (path, method, headers, query, body) is serialized
/// as the stimulus input; the router carries it on the dedicated stimulus-input channel for starts and as the
/// resume input for resumes, where <see cref="HttpEndpoint"/> surfaces it as its Result.
/// Bodies larger than <see cref="HttpEndpointOptions.MaxRequestBodyBytes"/> (or the per-endpoint override) are
/// rejected with <c>413</c> before dispatch — the payload becomes durable state on the instance, so the transport
/// guards its size.
/// </para>
/// <para>
/// <b>Endpoint options for resume-only matches.</b> The per-endpoint options (authorize/policy/timeout/size
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
        // The production route table exposes an additive snapshot lease. Holding it from route matching through
        // dispatch binds this request to the exact generation it selected; older route-table implementations fall
        // back to their existing enumerable behavior for compatibility.
        using var snapshotLease = (routeTable as IRouteTableSnapshotProvider)?.AcquireSnapshot();
        // Production leases resolve against their private authoritative route generation, so mutation of the
        // public compatibility snapshot cannot affect request routing. Custom providers built against the additive
        // lease API keep the enumerable fallback.
        var resolveThroughLease = snapshotLease?.SupportsRouteResolution == true;
        var resolved = resolveThroughLease
            ? snapshotLease!.ResolveRoute(endpointPath, context.Request.Method, routeMatcher)
            : HttpRouteResolution.Resolve(
                snapshotLease?.Snapshot.Routes ?? routeTable.ToArray(),
                endpointPath,
                context.Request.Method,
                routeMatcher);
        var template = resolved?.Template;
        var routeValues = resolved?.RouteValues ?? EmptyRouteValues;
        if (template is null)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        var stimulusHash = HttpEndpointStimulus.Hash(template, context.Request.Method);

        // Fetch the trigger claimants once (also the source of the endpoint options when present). The ambiguity
        // guard below shares this fetch rather than re-querying, and the router reuses it on the start path.
        var claimants = await triggerBindingStore.ListAllByStimulusAsync(
            HttpEndpointStimulus.StimulusType,
            stimulusHash,
            context.RequestAborted);

        // Endpoint options ride the claimant binding's non-identity metadata. Sibling
        // claimants of one definition share options; on an ambiguous route (rejected below) any claimant's
        // Authorize flag is enough to know the endpoint is protected, so read the strongest: if ANY claimant
        // authorizes, authorization must pass before we disclose anything else. When there is NO trigger claimant
        // (a purely mid-flow endpoint) the options come instead from a FAIL-CLOSED merge across ALL waiting bookmarks
        // matching this stimulus hash via the expiry-aware lookup — options are excluded from the
        // hash, so divergent bookmarks on one (template, method) must not let a lax one defeat a strict one. The
        // claimant wins when both exist, so the bookmark merge only runs when no claimant was found.
        var endpointOptions = claimants.Count > 0
            ? ResolveEndpointOptions(claimants)
            : HttpEndpointStimulusOptions.MergeForResume(await ResolveWaitingBookmarkMetadataAsync(stimulusHash, context.RequestAborted));

        var clientAborted = context.RequestAborted;
        var requestTimeout = endpointOptions.RequestTimeout is { } authoredTimeout && authoredTimeout > TimeSpan.Zero
            ? authoredTimeout
            : (TimeSpan?)null;
        if (endpointOptions.ResponseMode == ResponseMode.Sync)
            requestTimeout = requestTimeout is { } shorter && shorter < _options.SyncRequestTimeout
                ? shorter
                : _options.SyncRequestTimeout;

        using var timeoutSource = requestTimeout is not null
            ? CancellationTokenSource.CreateLinkedTokenSource(clientAborted)
            : null;
        if (timeoutSource is not null && endpointOptions.ResponseMode == ResponseMode.Sync)
        {
            timeoutSource.CancelAfter(requestTimeout!.Value);
            context.RequestAborted = timeoutSource.Token;
        }

        try
        {
            await HandleMatchedEndpointAsync(context, endpointPath, routeValues, stimulusHash, claimants, endpointOptions, timeoutSource, clientAborted);
        }
        catch (OperationCanceledException exception) when (!clientAborted.IsCancellationRequested && timeoutSource?.IsCancellationRequested == true)
        {
            // Restore the client token before writing a timeout response. Expiry cancels request work, not the
            // live connection, and must produce 408 rather than being mistaken for a disconnected caller.
            context.RequestAborted = clientAborted;
            await HandleDispatchFaultAsync(context, exception, timedOut: true);
        }
        finally
        {
            context.RequestAborted = clientAborted;
        }
    }

    private async Task HandleMatchedEndpointAsync(
        HttpContext context,
        string endpointPath,
        IReadOnlyDictionary<string, string> routeValues,
        string stimulusHash,
        IReadOnlyCollection<WorkflowTriggerBinding> claimants,
        MergedHttpEndpointOptions endpointOptions,
        CancellationTokenSource? timeoutSource,
        CancellationToken clientAborted)
    {
        // Authorization runs before the body is read, before ambiguity/existence is disclosed, and before any
        // dispatch (#592 item 10 — auth before disclosure). Fail closed: an Authorize endpoint with no
        // handler composed (WorkflowsRuntimeHttp feature absent) denies. Every distinct merged policy must pass —
        // a resume-only match can span waiting bookmarks with divergent policies. A configuration fault
        // (missing scheme / unregistered policy) surfaces as 500, distinct from the 401 an anonymous caller gets
        // (#592 item 11).
        if (endpointOptions.Authorize)
        {
            bool authorized;
            try
            {
                var authorizationHandler = context.RequestServices?.GetService(typeof(IHttpEndpointAuthorizationHandler)) as IHttpEndpointAuthorizationHandler;
                authorized = authorizationHandler is not null
                    && await HttpEndpointRequestAuthorization.AuthorizeAsync(authorizationHandler, context, endpointOptions.Policies);
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

        // Ambiguity guard: a (template, method) claimed by more than one workflow definition is an
        // authoring error, not fan-out — reject before any dispatch so neither workflow starts. Evaluated only
        // after authorization, so an anonymous caller cannot distinguish an ambiguous route from a valid one. The
        // body is slimmed to a stable code — it must not echo the method/template to an (authorized) caller
        // (#592 item 10). The guard stays trigger-claimant-only: bookmark resumes are instance-scoped and cannot
        // be ambiguous across definitions.
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
        var requestModel = await HttpEndpointRequestModelReader.ReadAsync(context, endpointPath, routeValues, maxBodyBytes);
        if (requestModel is null)
        {
            context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
            return;
        }

        var input = JsonSerializer.SerializeToElement(requestModel);

        // Sync mode: drain inline, then deliver any committed typed response instruction in this request.
        // The sink remains a compatibility marker for an already-started custom response; canonical
        // WriteHttpResponse does not consume it or any other request-affine service.
        var sync = endpointOptions.ResponseMode == ResponseMode.Sync;
        var responseSink = sync ? context.RequestServices?.GetService<SyncHttpResponseSink>() : null;
        responseSink?.Populate(context);

        // Reuse the claimant set already fetched for the ambiguity guard + options: the router's start path would
        // otherwise issue an identical bounded ListAllByStimulusAsync traversal for the same request.
        // StartAndResume: start new instances from matching triggers AND resume waiting instances
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

        // Async keeps its existing authored dispatch timeout; only Sync has a host deadline covering body
        // reading and response delivery as well. Do not start an Async endpoint's timer while reading its body.
        if (!sync)
            timeoutSource?.CancelAfter(endpointOptions.RequestTimeout!.Value);
        StimulusRoutingResult result;
        try
        {
            result = await router.RouteAsync(dispatch, timeoutSource?.Token ?? context.RequestAborted);
        }
        catch (Exception exception) when (!clientAborted.IsCancellationRequested)
        {
            // Preserve the established dispatch fault policy, including an authored live response. Other
            // request faults must reach the host for logging and connection abort; only deadline cancellation
            // is translated by the outer request boundary.
            context.RequestAborted = clientAborted;
            await HandleDispatchFaultAsync(context, exception, timedOut: timeoutSource?.IsCancellationRequested == true);
            return;
        }

        // Admission shed every start and nothing was resumed (#2548): nothing was written, so this is backpressure, not
        // acceptance. 429 with Retry-After tells the caller to retry, as the Execute endpoint does for a shed start; a 202
        // here would hand the caller a started execution id that never exists, and a 404 would claim the route is dead.
        if (result.StartedCount == 0 && result.ResumedCount == 0 && result.ShedStartCount > 0)
        {
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            context.Response.Headers.RetryAfter = Math.Max(1, (int)Math.Ceiling((result.ShedRetryAfter ?? TimeSpan.FromSeconds(1)).TotalSeconds))
                .ToString(CultureInfo.InvariantCulture);
            return;
        }

        // Nothing matched: no trigger started AND no waiting instance was resumed. A resolved
        // route whose only claim was a since-consumed bookmark deterministically lands here → 404. This branch stays
        // FIRST — a sync-mode request that started/resumed nothing is still 404, never a 202 degrade.
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

        var response = sync
            ? new
            {
                mode = "sync",
                reason = "workflow-response-not-produced",
                message = "The workflow may have suspended, reached no response activity, or continued on another worker. Inspect the run."
            }
            : new
            {
                mode = "async",
                reason = "background-accepted",
                message = "The endpoint accepts work in the background and returns 202 before the workflow responds."
            };

        context.Response.StatusCode = StatusCodes.Status202Accepted;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(
            JsonSerializer.Serialize(new { started = startedIds, resumed = resumedIds, response }),
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
    /// Resolves the endpoint options metadata for a resume-only match (no trigger claimant) from EVERY waiting
    /// bookmark matching this stimulus hash. Uses the expiry-aware lookup and filters to the
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
        // Headers cannot be replaced once the response starts. A deadline can interrupt body delivery, so
        // abort that connection rather than completing a truncated success response. Ordinary dispatch-phase
        // post-write faults keep the existing authored response intact. Other failures propagate to the host.
        if (context.Response.HasStarted)
        {
            if (timedOut && exception is OperationCanceledException)
                context.Abort();
            return;
        }

        var faultException = timedOut && exception is OperationCanceledException
            ? new TimeoutException("The HTTP endpoint request timeout elapsed before the response completed.", exception)
            : exception;

        if (context.RequestServices?.GetService(typeof(IHttpEndpointFaultHandler)) is IHttpEndpointFaultHandler faultHandler)
        {
            await faultHandler.HandleAsync(new HttpEndpointFaultContext(context, [faultException], context.RequestAborted));
            return;
        }

        // No handler composed: apply the same default mapping the policy module's handler uses (shared owner).
        context.Response.StatusCode = Elsa.Http.Core.HttpEndpointFaultMapping.ToStatusCode(faultException);
    }

    private static readonly IReadOnlyDictionary<string, string> EmptyRouteValues =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

}
