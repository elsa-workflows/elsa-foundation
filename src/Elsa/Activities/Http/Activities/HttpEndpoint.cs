using System.Text.Json;
using Elsa.Activities.Http.Models;
using Elsa.Activities.Runtime.Core;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Http.Core;
using Elsa.Primitives.Models;
using Elsa.Http.Core.Contracts;

namespace Elsa.Activities.Http.Activities;

/// <summary>
/// Receives an HTTP start trigger or suspends with typed HTTP trigger registrations for a mid-flow request.
/// Workflow data is hydrated into plain properties and each attempt returns one closed transition.
/// </summary>
[TriggerActivity]
public sealed class HttpEndpoint :
    StatefulTriggerActivity<HttpEndpointResult, HttpEndpointState, HttpRequestModel>
{
    public const string ActivityType = "Elsa.HttpEndpoint";
    public const string ResumeTargetId = "resume-target:http-endpoint";
    private const string NonRoutingMethodPlaceholder = "*";
    private static readonly string[] DefaultMethods = ["GET"];
    private readonly IHttpRequestBodyParser _bodyParser;

    public HttpEndpoint(IHttpRequestBodyParser bodyParser)
    {
        _bodyParser = bodyParser;
    }

    /// <summary>The endpoint-relative route template.</summary>
    [ActivityInput(Key = nameof(Path), Category = "Simple", Order = 10)]
    [Required]
    [ConsumerNote(ConsumerNoteKind.Constraint,
        "Endpoint-relative and normalised before matching: surrounding slashes are stripped and literal text "
        + "and parameter names are lower-cased, so 'Orders/{OrderId}' and '/orders/{orderid}' are the same "
        + "route. Inline constraint bodies and default values are preserved verbatim because the matcher "
        + "treats them as case-significant. The route this answers on is this path under the ActivitiesHttp "
        + "feature's BasePath, not the path alone.")]
    public string Path { get; set; } = string.Empty;

    /// <summary>The accepted HTTP methods; defaults to GET.</summary>
    [ActivityInput(
        Key = nameof(SupportedMethods),
        Category = "Simple",
        Order = 20,
        UIHint = ActivityInputUIHints.CheckList,
        Options = ["GET", "POST", "PUT", "HEAD", "DELETE"])]
    public ICollection<string>? SupportedMethods { get; set; }

    /// <summary>Whether this node may start a workflow.</summary>
    [ActivityInput(Key = nameof(CanStartWorkflow), Category = "Simple", Order = 0)]
    [ConsumerNote(ConsumerNoteKind.Requirement,
        "Defaults to FALSE. An endpoint meant to start a workflow from an incoming request must set this to "
        + "true, or publication produces zero trigger bindings and the route never fires — the definition "
        + "publishes cleanly and the request 404s, with nothing to indicate why. Leave it false only for a "
        + "mid-flow endpoint that resumes an already-running workflow.")]
    public bool CanStartWorkflow { get; set; }

    [ActivityInput(Key = nameof(Authorize), Category = "Advanced", Order = 100)]
    public bool Authorize { get; set; }

    [ActivityInput(Key = nameof(Policy), Category = "Advanced", Order = 110)]
    public string? Policy { get; set; }

    [ActivityInput(Key = nameof(RequestTimeout), Category = "Advanced", Order = 120)]
    public TimeSpan? RequestTimeout { get; set; }

    [ActivityInput(Key = nameof(RequestSizeLimit), Category = "Advanced", Order = 130)]
    public long? RequestSizeLimit { get; set; }

    [ActivityInput(Key = nameof(ResponseMode), Category = "Simple", Order = 30)]
    [ConsumerNote(ConsumerNoteKind.Default,
        "The default. The triggering request is answered 202 immediately with a started-workflow body, and a "
        + "WriteHttpResponse later in the workflow never reaches that caller - its output goes nowhere "
        + "observable and no incident is recorded. Choose this for fire-and-forget triggers only.",
        Member = "async")]
    [ConsumerNote(ConsumerNoteKind.Default,
        "Holds the request open so a later WriteHttpResponse answers the original caller. This is what a "
        + "request/response endpoint needs. It degrades back to a 202 - silently, by design - when the "
        + "workflow suspends, finishes without writing a response, or dispatches non-locally.",
        Member = "sync")]
    public ResponseMode ResponseMode { get; set; } = ResponseMode.Async;

    protected override ValueTask<ActivityTransition<HttpEndpointResult, HttpEndpointState>> ExecuteAsync(
        ActivityStartContext<HttpRequestModel> context)
    {
        if (ShouldComplete(context))
        {
            var request = context.TryGetTrigger(out var trigger)
                ? NormalizeRequest(trigger) ?? BuildAuthoredRouteModel(Path)
                : BuildAuthoredRouteModel(Path);
            return ValueTask.FromResult(Complete(BuildResult(request)));
        }

        var registrations = HttpEndpointStimulus
            .Describe(Path, EffectiveMethods(), ReadOptions())
            .Select(descriptor => new ActivityTriggerRegistration<HttpRequestModel>(
                ResumeTargetId,
                descriptor.StimulusType,
                descriptor.StimulusHash,
                metadata: descriptor.Metadata))
            .ToArray();
        return ValueTask.FromResult(Suspend(new HttpEndpointState(Path), registrations));
    }

    [ResumeTarget(ResumeTargetId)]
    protected override ValueTask<ActivityTransition<HttpEndpointResult, HttpEndpointState>> ResumeAsync(
        ActivityResumeContext<HttpEndpointState, HttpRequestModel> context)
    {
        var request = NormalizeRequest(context.Trigger) ?? BuildAuthoredRouteModel(context.State.Path);
        return ValueTask.FromResult(Complete(BuildResult(request)));
    }

    private bool ShouldComplete(ActivityStartContext<HttpRequestModel> context)
    {
        if (!context.IsDirectInvocation)
            return context.IsTriggerDelivery;

        return CanStartWorkflow;
    }

    private HttpEndpointResult BuildResult(HttpRequestModel request)
    {
        return new HttpEndpointResult(request, request.RouteData, DeriveParsedContent(request));
    }

    private JsonElement? DeriveParsedContent(HttpRequestModel request)
    {
        if (string.IsNullOrEmpty(request.Body))
            return null;

        return _bodyParser.Parse(GetContentType(request.Headers), request.Body);
    }

    private string[] EffectiveMethods() =>
        SupportedMethods is { Count: > 0 } authored ? authored.ToArray() : DefaultMethods;

    private HttpEndpointStimulusOptions ReadOptions() =>
        new(Authorize, Policy, RequestTimeout, RequestSizeLimit, ResponseMode);

    private static HttpRequestModel? NormalizeRequest(HttpRequestModel? request)
    {
        if (request is not { Path: not null, Method: not null })
            return null;

        return request;
    }

    private static string? GetContentType(IReadOnlyDictionary<string, IReadOnlyList<string>> headers)
    {
        if (headers is null)
            return null;

        foreach (var (key, values) in headers)
        {
            if (string.Equals(key, "Content-Type", StringComparison.OrdinalIgnoreCase))
                return values is { Count: > 0 } ? values[0] : null;
        }

        return null;
    }

    private static HttpRequestModel BuildAuthoredRouteModel(string path) =>
        new(
            HttpEndpointStimulus.NormalizeTemplate(path),
            NonRoutingMethodPlaceholder,
            new Dictionary<string, string[]>(),
            new Dictionary<string, string[]>(),
            Body: null,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
}
