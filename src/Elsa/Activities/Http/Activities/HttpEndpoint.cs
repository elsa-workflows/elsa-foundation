using System.Text.Json;
using Elsa.Activities.Http.Models;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Constants;
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
/// start path seeds it as the workflow input named
/// <see cref="WellKnownStimulusInputs.StimulusInput"/> (spec 089 FR-001/FR-002); this activity surfaces that
/// live <see cref="HttpRequestModel"/> as its <see cref="CodeActivity{T}.Result"/>. When the workflow was not
/// started by an HTTP stimulus (e.g. a direct run through the execute API), the Result falls back to a model
/// projected from the authored route.
/// </para>
/// </remarks>
public sealed class HttpEndpoint : CodeActivity<HttpRequestModel>
{
    /// <summary>The stable activity type key the trigger extractor's provider matches on.</summary>
    public const string ActivityType = "Elsa.HttpEndpoint";

    public HttpEndpoint() : base(ActivityType)
    {
    }

    /// <summary>The endpoint-relative path that starts the workflow. Drives the stimulus hash. Required, authored literal.</summary>
    public InputArgument<string> Path { get; set; } = null!;

    /// <summary>The HTTP methods this endpoint accepts. Informational on the trigger; routing keys on the path only.</summary>
    public InputArgument<ICollection<string>>? SupportedMethods { get; set; }

    protected override void Execute(IActivityExecutionContext context)
    {
        var model = ResolveStimulusRequest(context) ?? BuildAuthoredRouteModel(context);
        context.Set(Result, model);
    }

    /// <summary>
    /// Resolves the live request model the router seeded as the <see cref="WellKnownStimulusInputs.StimulusInput"/>
    /// workflow input. Returns null when the input is absent (non-HTTP start) or not a request-model payload.
    /// </summary>
    private static HttpRequestModel? ResolveStimulusRequest(IActivityExecutionContext context)
    {
        if (context.ExpressionExecutionContext is not IExecutionExpressionState state)
            return null;

        if (!state.WorkflowInputs.TryGetValue(WellKnownStimulusInputs.StimulusInput, out var value))
            return null;

        try
        {
            return value switch
            {
                HttpRequestModel model => model,
                JsonElement { ValueKind: JsonValueKind.Object } json => json.Deserialize<HttpRequestModel>(),
                _ => null
            };
        }
        catch (JsonException)
        {
            // A foreign stimulus payload under the well-known key is not ours to interpret.
            return null;
        }
    }

    private HttpRequestModel BuildAuthoredRouteModel(IActivityExecutionContext context)
    {
        var path = context.Get(Path) ?? string.Empty;
        var methods = context.Get(SupportedMethods);
        var method = methods is { Count: > 0 } ? methods.First() : "*";

        return new HttpRequestModel(
            Path: HttpEndpointStimulus.NormalizePath(path),
            Method: method,
            Headers: new Dictionary<string, string[]>(),
            Query: new Dictionary<string, string[]>(),
            Body: null);
    }
}
