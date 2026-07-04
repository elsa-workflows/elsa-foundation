using Elsa.Activities.Http.Models;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;

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
/// artifact. Synchronous request/response correlation (a caller that blocks for the workflow's response) is a
/// deliberately separate subsystem — the named "HTTP synchronous response correlation" follow-up.
/// </para>
/// <para>
/// <b>Start-input delivery is pending.</b> The runtime start path does not yet thread a stimulus's input into
/// the started instance (only the resume path does), so the surfaced <see cref="Result"/> currently reflects
/// the <em>authored</em> route rather than the live request body/headers/query. This is the named "HTTP
/// endpoint start-input delivery" follow-up; when it lands, the live <see cref="HttpRequestModel"/> the
/// middleware already serializes as the stimulus input becomes available with no wire change.
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
        var path = context.Get(Path) ?? string.Empty;
        var methods = context.Get(SupportedMethods);
        var method = methods is { Count: > 0 } ? methods.First() : "*";

        // Authored-route projection until start-input delivery lands (see remarks): the live request body/
        // headers/query are not threaded through the runtime start path yet.
        var model = new HttpRequestModel(
            Path: HttpEndpointStimulus.NormalizePath(path),
            Method: method,
            Headers: new Dictionary<string, string[]>(),
            Query: new Dictionary<string, string[]>(),
            Body: null);

        context.Set(Result, model);
    }
}
