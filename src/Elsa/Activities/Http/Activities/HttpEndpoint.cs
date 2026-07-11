using System.Text.Json;
using Elsa.Activities.Http.Models;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Http.Core;
using Elsa.Http.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts;

namespace Elsa.Activities.Http.Activities;

/// <summary>
/// An HTTP endpoint activity (W16, on the W7 <c>IActivityTriggerStimulusProvider</c> seam). It plays two roles
/// depending on where it sits in a workflow and how it is authored:
/// <list type="bullet">
/// <item><description><b>Start trigger.</b> With <see cref="CanStartWorkflow"/> explicitly set to true the
/// publish-time trigger extractor reads its <see cref="Path"/> and <see cref="SupportedMethods"/> and records a
/// durable trigger binding per <c>(template, method)</c> (via <see cref="HttpEndpointTriggerStimulusProvider"/>);
/// the request middleware starts a new instance when a matching request arrives.</description></item>
/// <item><description><b>Mid-flow resume point.</b> When the activity runs as a step inside an already-running
/// workflow (not the node that triggered the run), it suspends and waits for a matching request to resume it
/// (spec 089 D). Leaving <see cref="CanStartWorkflow"/> false or unauthored keeps the node out of the start role,
/// so it produces no trigger bindings at publish time.</description></item>
/// </list>
/// </summary>
/// <remarks>
/// <para>
/// <b>Start vs mid-flow decision (D-D1).</b> The run-time branch is chosen from the run's trigger-node identity
/// (<see cref="IExecutionExpressionState.TriggerNodeId"/>, spec 089 D) and <see cref="CanStartWorkflow"/>:
/// <list type="table">
/// <item><term><c>TriggerNodeId</c> == this node's id</term><description><b>Start path</b> — completes with the
/// live request from the stimulus channel (falls back to the authored route on a foreign/malformed payload).</description></item>
/// <item><term><c>TriggerNodeId</c> == null (direct run) and <see cref="CanStartWorkflow"/> == true</term>
/// <description><b>Completes</b> via the authored-route fallback — preserves sub-unit A's direct-run behavior for a
/// start-capable endpoint executed with no HTTP stimulus.</description></item>
/// <item><term>every other case (another node triggered the run; a mid-flow node; or
/// <see cref="CanStartWorkflow"/> is false or unauthored)</term><description><b>Suspends</b> — creates one bookmark
/// per supported method and waits for a matching request to resume it.</description></item>
/// </list>
/// A <see cref="CanStartWorkflow"/> = false or unauthored node therefore always suspends, even on a direct run (the
/// spec 089 D independent test starts a workflow directly and must suspend at the mid-flow endpoint). Requiring an
/// explicit true keeps the authored toggle and the runtime behavior aligned with elsa-core's default-false contract.
/// </para>
/// <para>
/// <b>Response model.</b> This is the <c>async/202</c> baseline: the endpoint replies <c>202 Accepted</c> the
/// moment it starts (or resumes) the workflow, so no request is waiting when the run executes. A workflow that
/// wants to record an intended response uses <see cref="WriteHttpResponse"/>, which writes a durable, observable
/// artifact. Synchronous request/response correlation is spec 089 sub-unit E (request-affine execution).
/// </para>
/// <para>
/// <b>Result resolution.</b> On the start path the middleware serializes the live request as the stimulus input,
/// and the router's start path carries it on the dedicated stimulus-input channel (spec 089 FR-001/FR-002) —
/// surfaced via <see cref="IExecutionExpressionState.StimulusInput"/>. On resume it rides
/// <see cref="IExecutionExpressionState.ResumeInput"/> (spec 089 D). Both channels are separate from workflow
/// inputs by construction, so an author input cannot collide with them and the execute API's inputs bag cannot
/// forge them. Without a valid payload the Result falls back to a model projected from the authored route.
/// </para>
/// <para>
/// <b>ParsedContent (spec 089 efficiency #9/#16).</b> The parsed body is <em>derived</em> here from
/// <see cref="HttpRequestModel.Body"/> and the request <c>Content-Type</c> header via the deterministic
/// <c>IHttpRequestBodyParser</c> seam — it is not persisted on the stimulus/resume payload. CLR <c>null</c> means
/// "no content" (empty body, unrecognized/absent content type, malformed JSON, or a non-HTTP run); an explicit
/// JSON <c>null</c> body surfaces as a present <see cref="JsonElement"/> of <see cref="JsonValueKind.Null"/>.
/// </para>
/// </remarks>
[TriggerActivity]
public sealed class HttpEndpoint : CodeActivity<HttpRequestModel>
{
    /// <summary>The stable activity type key the trigger extractor's provider matches on.</summary>
    public const string ActivityType = "Elsa.HttpEndpoint";

    /// <summary>The stable runtime resume-target id for the mid-flow resume callback (spec 089 D).</summary>
    public const string ResumeTargetId = "resume-target:http-endpoint";

    /// <summary>The bookmark id prefix for a mid-flow suspension (one bookmark per supported method).</summary>
    private const string BookmarkIdPrefix = "http-endpoint:";

    /// <summary>The non-routing method placeholder stamped on the authored-route fallback model (see <see cref="BuildAuthoredRouteModel"/>).</summary>
    private const string NonRoutingMethodPlaceholder = "*";

    private static readonly string[] DefaultMethods = ["GET"];

    public HttpEndpoint() : base(ActivityType)
    {
    }

    /// <summary>
    /// The endpoint-relative route template that starts (or resumes) the workflow (e.g. <c>orders/{id}</c>). Drives
    /// the stimulus hash together with each supported method. Required, authored literal.
    /// </summary>
    [ActivityInput(Category = "Simple", Order = 10)]
    public InputArgument<string> Path { get; set; } = null!;

    /// <summary>
    /// The HTTP methods this endpoint accepts (spec 089 B: routing-significant — one trigger binding / bookmark per
    /// (template, method)). Authored literal; unauthored defaults to <c>GET</c> (elsa-core parity).
    /// </summary>
    [ActivityInput(Category = "Simple", Order = 20)]
    public InputArgument<ICollection<string>>? SupportedMethods { get; set; }

    /// <summary>
    /// Whether this endpoint may start a new workflow. Defaults to false: only an explicitly authored true causes
    /// the publish-time extractor to record trigger bindings and lets the request middleware start a fresh instance.
    /// False or unauthored nodes produce NO trigger bindings and always run as mid-flow suspension points (spec 089 D).
    /// Authored literal, resolved at publish time like <see cref="Path"/>; a non-literal fails the publish.
    /// <b>Non-identity</b>: it never enters the endpoint stimulus hash.
    /// </summary>
    [ActivityInput(Category = "Simple", Order = 0)]
    public InputArgument<bool>? CanStartWorkflow { get; set; }

    /// <summary>
    /// When true, the endpoint requires an authorized caller (spec 089 C: the middleware resolves an
    /// <c>IHttpEndpointAuthorizationHandler</c> and 401s an unauthorized request before dispatch). Authored
    /// literal, resolved at publish time like <see cref="Path"/>; a non-literal fails the publish. Defaults to
    /// false (unauthored → omitted from binding/bookmark metadata).
    /// </summary>
    [ActivityInput(Category = "Advanced", Order = 100)]
    public InputArgument<bool>? Authorize { get; set; }

    /// <summary>
    /// The authorization policy name evaluated for this endpoint when <see cref="Authorize"/> is true. Authored
    /// literal, resolved at publish time; a non-literal fails the publish. Null/absent applies no named policy.
    /// </summary>
    [ActivityInput(Category = "Advanced", Order = 110)]
    public InputArgument<string>? Policy { get; set; }

    /// <summary>
    /// The per-request timeout applied around the dispatch (spec 089 C: a linked CTS whose elapse maps to 408).
    /// Authored literal, resolved at publish time; a non-literal fails the publish. Null/absent applies no
    /// per-endpoint timeout.
    /// </summary>
    [ActivityInput(Category = "Advanced", Order = 120)]
    public InputArgument<TimeSpan>? RequestTimeout { get; set; }

    /// <summary>
    /// The per-request body size limit in bytes, overriding the global bound for this endpoint (spec 089 C:
    /// an oversized body 413s). Authored literal, resolved at publish time; a non-literal fails the publish.
    /// Null/absent applies the global limit.
    /// </summary>
    [ActivityInput(Category = "Advanced", Order = 130)]
    public InputArgument<long>? RequestSizeLimit { get; set; }

    /// <summary>
    /// How the endpoint delivers the workflow-authored response (spec 089 sub-unit E). <see cref="ResponseMode.Sync"/>
    /// holds the request open on the caller's async flow so a <see cref="WriteHttpResponse"/> the run reaches writes
    /// the live response in the same exchange (degrading to <c>202</c> on suspend/no-write); <see cref="ResponseMode.Async"/>
    /// (the default) is today's async/202 baseline. Authored literal, resolved at publish time like <see cref="Path"/>;
    /// a non-literal or invalid mode string fails the publish. <b>Non-identity</b>: it never enters the endpoint
    /// stimulus hash. Null/absent applies <see cref="ResponseMode.Async"/> (omitted from binding/bookmark metadata).
    /// </summary>
    [ActivityInput(Category = "Simple", Order = 30)]
    public InputArgument<ResponseMode>? ResponseMode { get; set; }

    /// <summary>
    /// Route parameters extracted from the matched template (e.g. <c>id = "42"</c> for <c>orders/{id}</c>);
    /// empty for direct runs or templates without parameters.
    /// </summary>
    public OutputArgument<IDictionary<string, string>>? RouteData { get; set; }

    /// <summary>
    /// The request body parsed by content type into a wire-safe JSON value (spec 089 C, FR-011), <em>derived</em>
    /// here from <see cref="HttpRequestModel.Body"/> and the request <c>Content-Type</c> header via the
    /// deterministic <c>IHttpRequestBodyParser</c> seam — not persisted on the stimulus/resume payload (spec 089
    /// efficiency #9). Null when there is no content to parse; an explicit JSON <c>null</c> body surfaces as a
    /// present <see cref="JsonElement"/> of <see cref="JsonValueKind.Null"/> (spec 089 #16).
    /// </summary>
    public OutputArgument<object?>? ParsedContent { get; set; }

    /// <summary>
    /// Runs the start-vs-mid-flow decision (D-D1). On the start path (or a start-capable direct run) it completes
    /// with the resolved request; otherwise it suspends by creating one bookmark per supported method (D-D2).
    /// </summary>
    protected override ValueTask ExecuteAsync(IActivityExecutionContext context)
    {
        if (ShouldComplete(context))
        {
            CompleteWithRequest(context, ResolveStimulusRequest(context) ?? BuildAuthoredRouteModel(context));
            return ValueTask.CompletedTask;
        }

        Suspend(context);
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Resume callback (D-D3): a matching request resumed a mid-flow bookmark. The live request rides
    /// <see cref="IExecutionExpressionState.ResumeInput"/>; validation follows the start path exactly (shared
    /// helper), and a missing/malformed input falls back to the authored-route model (D-D7). Completing the
    /// activity is handled by the resume work handler after this returns.
    /// </summary>
    [ResumeTarget(ResumeTargetId)]
    private void OnRequestReceived(IActivityExecutionContext context) =>
        CompleteWithRequest(context, ResolveResumeRequest(context) ?? BuildAuthoredRouteModel(context));

    /// <summary>
    /// True when this execution should complete now rather than suspend (D-D1): it is the node that triggered the
    /// run, or a start-capable node on a direct run (no trigger identity on the run).
    /// </summary>
    private bool ShouldComplete(IActivityExecutionContext context)
    {
        var triggerNodeId = (context.ExpressionExecutionContext as IExecutionExpressionState)?.TriggerNodeId;

        // This node triggered the run → start path.
        if (triggerNodeId is not null && context is IRuntimeActivityExecutionContext runtimeContext &&
            StringComparer.Ordinal.Equals(triggerNodeId, runtimeContext.ExecutableNode.ExecutableNodeId))
            return true;

        // Direct run (no trigger identity) of a start-capable endpoint → complete via the authored-route fallback.
        // A CanStartWorkflow = false node always suspends, even on a direct run.
        return triggerNodeId is null && CanStart(context);
    }

    private void CompleteWithRequest(IActivityExecutionContext context, HttpRequestModel model)
    {
        context.Set(Result, model);
        context.Set(RouteData, model.RouteData ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        context.Set(ParsedContent, DeriveParsedContent(context, model));
    }

    /// <summary>
    /// Derives the parsed body from the raw <see cref="HttpRequestModel.Body"/> and the request
    /// <c>Content-Type</c> header (spec 089 #9). Returns CLR <c>null</c> for no content — an empty body, an
    /// absent/unrecognized content type, or malformed JSON — and boxes the parser's <see cref="JsonElement"/>
    /// otherwise, so an explicit JSON <c>null</c> stays distinguishable as a <see cref="JsonValueKind.Null"/>
    /// element rather than collapsing to CLR null (spec 089 #16).
    /// </summary>
    private static object? DeriveParsedContent(IActivityExecutionContext context, HttpRequestModel model)
    {
        if (string.IsNullOrEmpty(model.Body))
            return null;

        // A body only reaches here on a genuine HTTP-stimulus start or resume (the authored/direct-run fallback
        // carries a null Body), and that path composes the Http feature — which registers the parser — by
        // construction (ActivitiesHttp → WorkflowsRuntimeHttp → Http). So the seam is guaranteed present whenever
        // there is content to parse.
        var parser = context.GetRequiredService<IHttpRequestBodyParser>();
        var parsed = parser.Parse(GetContentType(model.Headers), model.Body);
        return parsed is { } element ? element : null;
    }

    /// <summary>Reads the (case-insensitive) <c>Content-Type</c> request header from the model, if present.</summary>
    private static string? GetContentType(IDictionary<string, string[]>? headers)
    {
        if (headers is null)
            return null;

        foreach (var (key, values) in headers)
        {
            if (string.Equals(key, "Content-Type", StringComparison.OrdinalIgnoreCase))
                return values is { Length: > 0 } ? values[0] : null;
        }

        return null;
    }

    /// <summary>
    /// Suspends the workflow by creating one bookmark per supported method (D-D2): <c>bookmarkId =
    /// "http-endpoint:{activityExecutionId}:{method}"</c>, the shared endpoint stimulus type, a per-method
    /// <see cref="HttpEndpointStimulus.Hash"/>, no expiry, and the same metadata payload the trigger provider
    /// stamps on bindings (template + method + endpoint options) — reusing <see cref="HttpEndpointStimulus.Describe"/>
    /// so the describe/suspend sides never drift.
    /// </summary>
    private void Suspend(IActivityExecutionContext context)
    {
        var runtimeContext = RequireRuntimeContext(context);
        var activityExecutionId = runtimeContext.ActivityExecutionState.Execution.ActivityExecutionId;

        var path = context.Get(Path) ?? string.Empty;
        var methods = context.Get(SupportedMethods) is { Count: > 0 } authored ? authored : DefaultMethods;

        // One descriptor per method carries the per-method hash and the full binding-parity metadata payload.
        foreach (var descriptor in HttpEndpointStimulus.Describe(path, methods.ToArray(), ReadOptions(context)))
        {
            var method = descriptor.Metadata[HttpEndpointRouting.MethodMetadataKey];
            context.CreateBookmark(new ActivityBookmarkRequest(
                bookmarkId: $"{BookmarkIdPrefix}{activityExecutionId}:{method}",
                resumeTargetId: ResumeTargetId,
                stimulusType: descriptor.StimulusType,
                stimulusHash: descriptor.StimulusHash,
                expiresAt: null,
                metadata: descriptor.Metadata));
        }
    }

    /// <summary>Reads the non-identity endpoint options from the activity's inputs for the bookmark metadata.</summary>
    private HttpEndpointStimulusOptions ReadOptions(IActivityExecutionContext context) =>
        new(
            Authorize: Authorize is not null && context.Get(Authorize),
            Policy: Policy is not null ? context.Get(Policy) : null,
            RequestTimeout: RequestTimeout is not null ? context.Get(RequestTimeout) : null,
            RequestSizeLimit: RequestSizeLimit is not null ? context.Get(RequestSizeLimit) : null,
            ResponseMode: ResponseMode is not null ? context.Get(ResponseMode) : Elsa.Http.Core.ResponseMode.Async);

    private bool CanStart(IActivityExecutionContext context) =>
        CanStartWorkflow is not null && context.Get(CanStartWorkflow);

    /// <summary>
    /// Resolves the live request model from the dedicated stimulus-input channel (start path). Returns null when
    /// the input is absent (non-HTTP start) or is not a valid request-model payload — the caller then uses the
    /// authored-route fallback.
    /// </summary>
    private static HttpRequestModel? ResolveStimulusRequest(IActivityExecutionContext context) =>
        context.ExpressionExecutionContext is IExecutionExpressionState { StimulusInput: JsonElement { ValueKind: JsonValueKind.Object } json }
            ? DeserializeRequest(json)
            : null;

    /// <summary>
    /// Resolves the live request model from the resume-input channel (mid-flow resume path, D-D3/D-D7). Applies the
    /// same validation discipline as the start path; a missing/malformed input yields null (authored-route fallback).
    /// </summary>
    private static HttpRequestModel? ResolveResumeRequest(IActivityExecutionContext context) =>
        context.ExpressionExecutionContext is IExecutionExpressionState { ResumeInput: JsonElement { ValueKind: JsonValueKind.Object } json }
            ? DeserializeRequest(json)
            : null;

    /// <summary>
    /// Deserializes and validates a request-model payload shared by the start and resume paths (DRY). Validation is
    /// strict on the identifying members: a JSON object that deserializes without <c>Path</c> and <c>Method</c> is
    /// not a request model, never a half-populated Result. A malformed payload falls back (returns null) rather than
    /// faulting — the durable value remains inspectable for diagnosis.
    /// </summary>
    private static HttpRequestModel? DeserializeRequest(JsonElement json)
    {
        try
        {
            var model = json.Deserialize<HttpRequestModel>();
            if (model is not { Path: not null, Method: not null })
                return null;

            // Tolerate payloads missing the collection members rather than surfacing null dictionaries.
            return model with
            {
                Headers = model.Headers ?? new Dictionary<string, string[]>(),
                Query = model.Query ?? new Dictionary<string, string[]>(),
                RouteData = model.RouteData ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private HttpRequestModel BuildAuthoredRouteModel(IActivityExecutionContext context)
    {
        var path = context.Get(Path) ?? string.Empty;

        // Direct-run / non-HTTP-start fallback: no inbound request drove this execution, so there is no real
        // request method. Post-B, the authored SupportedMethods are a routing-time concern (one binding per
        // method) — projecting the first of them here would misleadingly imply this run was a GET/POST request.
        // Emit a stable, non-routing placeholder instead so consumers can tell the model is authored, not live
        // (#592 item 20).
        return new HttpRequestModel(
            Path: HttpEndpointStimulus.NormalizeTemplate(path),
            Method: NonRoutingMethodPlaceholder,
            Headers: new Dictionary<string, string[]>(),
            Query: new Dictionary<string, string[]>(),
            Body: null,
            RouteData: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
    }

    private static IRuntimeActivityExecutionContext RequireRuntimeContext(IActivityExecutionContext context)
    {
        if (context is IRuntimeActivityExecutionContext runtimeContext)
            return runtimeContext;

        throw new InvalidOperationException("HttpEndpoint requires an Elsa runtime activity execution context to suspend.");
    }
}
