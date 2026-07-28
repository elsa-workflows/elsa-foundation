using System.Globalization;
using System.Net.Http;
using Elsa.Activities.Http.Constants;
using Elsa.Activities.Http.Options;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Microsoft.Extensions.Options;

namespace Elsa.Activities.Http.Activities;

/// <summary>
/// Sends an outbound HTTP request and captures the response. Ported in spirit from elsa-core's
/// <c>SendHttpRequest</c> and adapted to this repo's transient typed activity model. A pooled
/// <see cref="HttpClient"/> is supplied through constructor-injected infrastructure from
/// <see cref="IHttpClientFactory"/> under the well-known <see cref="HttpActivityConstants.HttpClientName"/>
/// client, so connection pooling, redirect policy and timeouts are configured once by
/// <c>ActivitiesHttpFeature</c> rather than per activity.
/// </summary>
/// <remarks>
/// <para>
/// <b>Error model.</b> The activity does not throw for a completed-but-unsuccessful exchange: a received
/// response always returns one complete <see cref="SendHttpRequestResult"/>, and the completion
/// <em>outcome</em> encodes the branch so a workflow can react without faulting.
/// A transport failure emits <see cref="HttpActivityOutcomes.Failed"/> and a timeout emits
/// <see cref="HttpActivityOutcomes.Timeout"/>; only a genuine misconfiguration (missing URL) or an external
/// cancellation of the workflow faults the activity.
/// </para>
/// <para>
/// <b>Outcome mapping.</b> When <see cref="ExpectedStatusCodes"/> is supplied, each configured code becomes an
/// outcome port named after the numeric status (e.g. <c>"200"</c>, <c>"404"</c>): a matching response branches on
/// that port and a non-matching response branches on <see cref="HttpActivityOutcomes.UnmatchedStatusCode"/>. The
/// per-status ports are derived from the authored value via <see cref="ActivityValueOutcomesAttribute"/> and pinned
/// per node at publish time. When no expected codes are supplied, a 2xx response branches on the default <c>Done</c>
/// outcome and any other status branches on <see cref="HttpActivityOutcomes.Failed"/> (outputs are still captured in
/// every case).
/// </para>
/// <para>
/// <b>Timeout.</b> The optional <see cref="Timeout"/> input (else <c>HttpActivityOptions.DefaultTimeout</c>)
/// is enforced with a linked <see cref="CancellationTokenSource"/> around the send so it composes with the
/// workflow's own cancellation: a timeout cancels only this request, whereas a workflow cancellation
/// propagates as a normal cancellation.
/// </para>
/// </remarks>
// Static base outcomes. When ExpectedStatusCodes is authored, the compiler adds one outcome per code plus
// "Unmatched status code" (derived from the input via ActivityValueOutcomes); Done is the no-codes success branch.
[ActivityOutcome(ActivityOutcomes.Done)]
[ActivityOutcome(HttpActivityOutcomes.Failed)]
[ActivityOutcome(HttpActivityOutcomes.Timeout)]
[ActivityValueOutcomes(nameof(ExpectedStatusCodes), UnmatchedOutcome = HttpActivityOutcomes.UnmatchedStatusCode)]
public sealed class SendHttpRequest(
    IHttpClientFactory httpClientFactory,
    IOptions<HttpActivityOptions> httpActivityOptions) : Activity<SendHttpRequestResult>
{
    /// <summary>The absolute request URL. Required.</summary>
    [ActivityInput(Key = nameof(Url))]
    [Required]
    public Uri Url { get; set; } = null!;

    /// <summary>The HTTP method (verb). Defaults to <c>GET</c> when not set.</summary>
    [ActivityInput(Key = nameof(Method), DefaultValue = "GET", DefaultSyntax = "Literal")]
    public string Method { get; set; } = "GET";

    /// <summary>Optional request body content, serialized as a UTF-8 string.</summary>
    [ActivityInput(Key = nameof(Content))]
    public string? Content { get; set; }

    /// <summary>Content type (media type) for the request body. Defaults to <c>text/plain</c> when content is present.</summary>
    [ActivityInput(Key = nameof(ContentType), DefaultValue = "text/plain", DefaultSyntax = "Literal")]
    public string? ContentType { get; set; }

    /// <summary>Optional request headers to add to the outbound request.</summary>
    [ActivityInput(Key = nameof(RequestHeaders))]
    public IDictionary<string, string>? RequestHeaders { get; set; }

    /// <summary>Optional set of status codes that should branch on the matching numeric outcome; others branch on <c>Unmatched</c>.</summary>
    [ActivityInput(Key = nameof(ExpectedStatusCodes))]
    public ICollection<int>? ExpectedStatusCodes { get; set; }

    /// <summary>Optional per-request timeout overriding <c>HttpActivityOptions.DefaultTimeout</c>.</summary>
    [ActivityInput(Key = nameof(Timeout))]
    public TimeSpan? Timeout { get; set; }

    protected override async ValueTask<ActivityTransition<SendHttpRequestResult>> ExecuteAsync(ActivityExecutionContext context)
    {
        var url = Url ?? throw new InvalidOperationException("SendHttpRequest requires a non-null Url.");
        var method = Method;
        method = string.IsNullOrWhiteSpace(method) ? "GET" : method.Trim();

        var client = httpClientFactory.CreateClient(HttpActivityConstants.HttpClientName);

        using var request = new HttpRequestMessage(new HttpMethod(method), url);
        AddHeaders(request);
        AddContent(request);

        var effectiveTimeout = Timeout is { } t && t > TimeSpan.Zero ? t : httpActivityOptions.Value.DefaultTimeout;

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
        timeoutCts.CancelAfter(effectiveTimeout);

        try
        {
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseContentRead, timeoutCts.Token);
            var body = await response.Content.ReadAsStringAsync(timeoutCts.Token);
            var status = (int)response.StatusCode;

            return ActivityTransition.Complete(
                new SendHttpRequestResult(status, body, CollectHeaders(response)),
                DetermineResponseOutcome(status, response.IsSuccessStatusCode));
        }
        catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
        {
            // The workflow itself was cancelled — surface it as a normal cancellation, not a timeout outcome.
            throw;
        }
        catch (OperationCanceledException)
        {
            // Only our linked timeout fired.
            return ActivityTransition.Complete(SendHttpRequestResult.NoResponse, HttpActivityOutcomes.Timeout);
        }
        catch (HttpRequestException)
        {
            return ActivityTransition.Complete(SendHttpRequestResult.NoResponse, HttpActivityOutcomes.Failed);
        }
    }

    private void AddHeaders(HttpRequestMessage request)
    {
        if (RequestHeaders is null)
            return;

        foreach (var (name, value) in RequestHeaders)
            request.Headers.TryAddWithoutValidation(name, value);
    }

    private void AddContent(HttpRequestMessage request)
    {
        if (string.IsNullOrEmpty(Content))
            return;

        var contentType = ContentType;
        contentType = string.IsNullOrWhiteSpace(contentType) ? "text/plain" : contentType.Trim();
        request.Content = new StringContent(Content, System.Text.Encoding.UTF8, contentType);
    }

    private string DetermineResponseOutcome(int status, bool isSuccess)
    {
        // With expected codes authored, branch on the matched code's own outcome port (named after the numeric
        // status) or the catch-all Unmatched-status-code port. These names match the per-node outcomes the compiler
        // pins from ExpectedStatusCodes, so the emitted outcome is always a declared branch (#926).
        if (ExpectedStatusCodes is { Count: > 0 })
            return ExpectedStatusCodes.Contains(status)
                ? status.ToString(CultureInfo.InvariantCulture)
                : HttpActivityOutcomes.UnmatchedStatusCode;

        return isSuccess ? ActivityOutcomes.Done : HttpActivityOutcomes.Failed;
    }

    private static IDictionary<string, string[]> CollectHeaders(HttpResponseMessage response)
    {
        var result = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

        foreach (var (name, values) in response.Headers)
            result[name] = values.ToArray();

        foreach (var (name, values) in response.Content.Headers)
            result[name] = values.ToArray();

        return result;
    }
}

/// <summary>The atomic response document returned by <see cref="SendHttpRequest"/>.</summary>
public sealed record SendHttpRequestResult(
    [property: Output(Key = "StatusCode", Path = "statusCode")] int? StatusCode,
    [property: Output(
        Key = "ResponseBody",
        Path = "responseBody",
        HasSourceRepresentation = true,
        SourceRepresentation = ValueRepresentation.FormattedContent)]
    string? ResponseBody,
    [property: Output(Key = "ResponseHeaders", Path = "responseHeaders")] IDictionary<string, string[]>? ResponseHeaders)
{
    public static SendHttpRequestResult NoResponse { get; } = new(null, null, null);
}
