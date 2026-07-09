using System.Text.Json;
using Elsa.Activities.Http.Models;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;

namespace Elsa.Activities.Http.Activities;

/// <summary>
/// An HTTP endpoint start trigger (W16, on the W7 <c>IActivityTriggerStimulusProvider</c> seam). Authored with
/// <see cref="Elsa.Activities.Runtime.Core.Models.ActivityExecutionType.Trigger"/>, it lets an inbound HTTP
/// request start a workflow with no explicit execution id: the publish-time trigger extractor reads its
/// <see cref="Path"/> and records a durable trigger binding keyed by the endpoint's stimulus (via
/// <see cref="HttpEndpointTriggerStimulusProvider"/>), and the request middleware starts a new instance when a
/// matching request arrives.
/// </summary>
/// <remarks>
/// <para>
/// <b>Response model.</b> This is the <c>async/202</c> baseline: the endpoint replies <c>202 Accepted</c> the
/// moment it starts the workflow, so no request is waiting when the run executes. A workflow that wants to
/// record an intended response uses <see cref="WriteHttpResponse"/>, which writes a durable, observable
/// artifact. Synchronous request/response correlation is spec 089 sub-unit E (request-affine execution).
/// </para>
/// <para>
/// <b>Result resolution.</b> The middleware serializes the live request as the stimulus input, and the router's
/// start path carries it on the dedicated stimulus-input channel (spec 089 FR-001/FR-002) — surfaced here via
/// <see cref="IExecutionExpressionState.StimulusInput"/>. That channel is separate from workflow inputs by
/// construction, so an author input cannot collide with it and the execute API's inputs bag cannot forge it.
/// When the execution was not started by an HTTP stimulus (direct run, or a foreign/malformed stimulus
/// payload), the Result falls back to a model projected from the authored route.
/// </para>
/// </remarks>
public sealed class HttpEndpoint : CodeActivity<HttpRequestModel>
{
    /// <summary>The stable activity type key the trigger extractor's provider matches on.</summary>
    public const string ActivityType = "Elsa.HttpEndpoint";

    public HttpEndpoint() : base(ActivityType)
    {
    }

    /// <summary>
    /// The endpoint-relative route template that starts the workflow (e.g. <c>orders/{id}</c>). Drives the
    /// stimulus hash together with each supported method. Required, authored literal.
    /// </summary>
    public InputArgument<string> Path { get; set; } = null!;

    /// <summary>
    /// The HTTP methods this endpoint accepts (spec 089 B: routing-significant — one trigger binding per
    /// (template, method)). Authored literal; unauthored defaults to <c>GET</c> (elsa-core parity).
    /// </summary>
    public InputArgument<ICollection<string>>? SupportedMethods { get; set; }

    /// <summary>
    /// When true, the endpoint requires an authorized caller (spec 089 C: the middleware resolves an
    /// <c>IHttpEndpointAuthorizationHandler</c> and 401s an unauthorized request before dispatch). Authored
    /// literal, resolved at publish time like <see cref="Path"/>; a non-literal fails the publish. Defaults to
    /// false (unauthored → omitted from binding metadata).
    /// </summary>
    public InputArgument<bool>? Authorize { get; set; }

    /// <summary>
    /// The authorization policy name evaluated for this endpoint when <see cref="Authorize"/> is true. Authored
    /// literal, resolved at publish time; a non-literal fails the publish. Null/absent applies no named policy.
    /// </summary>
    public InputArgument<string>? Policy { get; set; }

    /// <summary>
    /// The per-request timeout applied around the dispatch (spec 089 C: a linked CTS whose elapse maps to 408).
    /// Authored literal, resolved at publish time; a non-literal fails the publish. Null/absent applies no
    /// per-endpoint timeout.
    /// </summary>
    public InputArgument<TimeSpan>? RequestTimeout { get; set; }

    /// <summary>
    /// The per-request body size limit in bytes, overriding the global bound for this endpoint (spec 089 C:
    /// an oversized body 413s). Authored literal, resolved at publish time; a non-literal fails the publish.
    /// Null/absent applies the global limit.
    /// </summary>
    public InputArgument<long>? RequestSizeLimit { get; set; }

    /// <summary>
    /// Route parameters extracted from the matched template (e.g. <c>id = "42"</c> for <c>orders/{id}</c>);
    /// empty for direct runs or templates without parameters.
    /// </summary>
    public OutputArgument<IDictionary<string, string>>? RouteData { get; set; }

    /// <summary>
    /// The request body parsed by content type into a wire-safe JSON value (spec 089 C, FR-011); null when the
    /// body was empty, the content type unrecognized, or the run was not started by an HTTP stimulus.
    /// </summary>
    public OutputArgument<object?>? ParsedContent { get; set; }

    protected override void Execute(IActivityExecutionContext context)
    {
        var model = ResolveStimulusRequest(context) ?? BuildAuthoredRouteModel(context);
        context.Set(Result, model);
        context.Set(RouteData, model.RouteData ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        context.Set(ParsedContent, (object?)model.ParsedContent);
    }

    /// <summary>
    /// Resolves the live request model from the dedicated stimulus-input channel. Returns null when the input
    /// is absent (non-HTTP start) or is not a valid request-model payload (a foreign stimulus started this
    /// artifact, or the payload is malformed) — the caller then uses the authored-route fallback. Validation is
    /// strict on the identifying members: a JSON object that deserializes without <c>Path</c> and <c>Method</c>
    /// is not a request model, never a half-populated Result.
    /// </summary>
    private static HttpRequestModel? ResolveStimulusRequest(IActivityExecutionContext context)
    {
        if (context.ExpressionExecutionContext is not IExecutionExpressionState { StimulusInput: JsonElement { ValueKind: JsonValueKind.Object } json })
            return null;

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
            // Malformed payload on the stimulus channel: fall back rather than fault. The durable stimulus
            // value remains inspectable on the instance for diagnosis.
            return null;
        }
    }

    private HttpRequestModel BuildAuthoredRouteModel(IActivityExecutionContext context)
    {
        var path = context.Get(Path) ?? string.Empty;
        var methods = context.Get(SupportedMethods);
        var method = methods is { Count: > 0 } ? methods.First() : "*";

        return new HttpRequestModel(
            Path: HttpEndpointStimulus.NormalizeTemplate(path),
            Method: method,
            Headers: new Dictionary<string, string[]>(),
            Query: new Dictionary<string, string[]>(),
            Body: null,
            RouteData: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
    }
}
