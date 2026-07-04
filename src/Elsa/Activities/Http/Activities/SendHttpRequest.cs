using System.Net.Http;
using Elsa.Activities.Http.Constants;
using Elsa.Activities.Http.Options;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Microsoft.Extensions.Options;

namespace Elsa.Activities.Http.Activities;

/// <summary>
/// Sends an outbound HTTP request and captures the response. Ported in spirit from elsa-core's
/// <c>SendHttpRequest</c> and adapted to this repo's runtime activity model: a CLR leaf (resolved by the
/// existing <c>ClrActivityConstructor</c>) that resolves a pooled <see cref="HttpClient"/> from
/// <see cref="IHttpClientFactory"/> under the well-known <see cref="HttpActivityConstants.HttpClientName"/>
/// client, so connection pooling, redirect policy and timeouts are configured once by
/// <c>ActivitiesHttpFeature</c> rather than per activity.
/// </summary>
/// <remarks>
/// <para>
/// <b>Error model.</b> The activity does not throw for a completed-but-unsuccessful exchange: a received
/// response always sets the <see cref="StatusCode"/>/<see cref="ResponseBody"/>/<see cref="ResponseHeaders"/>
/// outputs, and the completion <em>outcome</em> encodes the result so a workflow can branch without faulting.
/// A transport failure emits <see cref="HttpActivityOutcomes.Failed"/> and a timeout emits
/// <see cref="HttpActivityOutcomes.Timeout"/>; only a genuine misconfiguration (missing URL) or an external
/// cancellation of the workflow faults the activity.
/// </para>
/// <para>
/// <b>Outcome mapping.</b> When <see cref="ExpectedStatusCodes"/> is supplied, a matching response branches on
/// the numeric status code string (e.g. <c>"200"</c>) and a non-matching response branches on
/// <see cref="HttpActivityOutcomes.Unmatched"/>. When it is not supplied, a 2xx response branches on the
/// default <c>Done</c> outcome and any other status branches on <see cref="HttpActivityOutcomes.Failed"/>
/// (outputs are still captured in both cases).
/// </para>
/// <para>
/// <b>Timeout.</b> The optional <see cref="Timeout"/> input (else <c>HttpActivityOptions.DefaultTimeout</c>)
/// is enforced with a linked <see cref="CancellationTokenSource"/> around the send so it composes with the
/// workflow's own cancellation: a timeout cancels only this request, whereas a workflow cancellation
/// propagates as a normal cancellation.
/// </para>
/// </remarks>
public sealed class SendHttpRequest : CodeActivity
{
    /// <summary>The absolute request URL. Required.</summary>
    public InputArgument<Uri>? Url { get; set; }

    /// <summary>The HTTP method (verb). Defaults to <c>GET</c> when not set.</summary>
    public InputArgument<string>? Method { get; set; }

    /// <summary>Optional request body content, serialized as a UTF-8 string.</summary>
    public InputArgument<string>? Content { get; set; }

    /// <summary>Content type (media type) for the request body. Defaults to <c>text/plain</c> when content is present.</summary>
    public InputArgument<string>? ContentType { get; set; }

    /// <summary>Optional request headers to add to the outbound request.</summary>
    public InputArgument<IDictionary<string, string>>? RequestHeaders { get; set; }

    /// <summary>Optional set of status codes that should branch on the matching numeric outcome; others branch on <c>Unmatched</c>.</summary>
    public InputArgument<ICollection<int>>? ExpectedStatusCodes { get; set; }

    /// <summary>Optional per-request timeout overriding <c>HttpActivityOptions.DefaultTimeout</c>.</summary>
    public InputArgument<TimeSpan?>? Timeout { get; set; }

    /// <summary>The received response status code (set for any completed exchange).</summary>
    public OutputArgument<int>? StatusCode { get; set; }

    /// <summary>The received response body as a string (set for any completed exchange).</summary>
    public OutputArgument<string>? ResponseBody { get; set; }

    /// <summary>The received response headers, keyed by header name (set for any completed exchange).</summary>
    public OutputArgument<IDictionary<string, string[]>>? ResponseHeaders { get; set; }

    protected override async ValueTask ExecuteAsync(IActivityExecutionContext context)
    {
        var url = context.Get(Url) ?? throw new InvalidOperationException("SendHttpRequest requires a non-null Url.");
        var method = context.Get(Method);
        method = string.IsNullOrWhiteSpace(method) ? "GET" : method.Trim();

        var factory = context.GetRequiredService<IHttpClientFactory>();
        var options = context.GetRequiredService<IOptions<HttpActivityOptions>>().Value;
        var client = factory.CreateClient(HttpActivityConstants.HttpClientName);

        using var request = new HttpRequestMessage(new HttpMethod(method), url);
        AddHeaders(context, request);
        AddContent(context, request);

        var requestedTimeout = context.Get(Timeout);
        var effectiveTimeout = requestedTimeout is { } t && t > TimeSpan.Zero ? t : options.DefaultTimeout;

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
        timeoutCts.CancelAfter(effectiveTimeout);

        try
        {
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseContentRead, timeoutCts.Token);
            var body = await response.Content.ReadAsStringAsync(timeoutCts.Token);
            var status = (int)response.StatusCode;

            context.Set(StatusCode, status);
            context.Set(ResponseBody, body);
            context.Set(ResponseHeaders, CollectHeaders(response));

            context.SetOutcomes(DetermineResponseOutcomes(context, status, response.IsSuccessStatusCode));
        }
        catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
        {
            // The workflow itself was cancelled — surface it as a normal cancellation, not a timeout outcome.
            throw;
        }
        catch (OperationCanceledException)
        {
            // Only our linked timeout fired.
            context.SetOutcomes([HttpActivityOutcomes.Timeout]);
        }
        catch (HttpRequestException)
        {
            context.SetOutcomes([HttpActivityOutcomes.Failed]);
        }
    }

    private void AddHeaders(IActivityExecutionContext context, HttpRequestMessage request)
    {
        var headers = context.Get(RequestHeaders);
        if (headers is null)
            return;

        foreach (var (name, value) in headers)
            request.Headers.TryAddWithoutValidation(name, value);
    }

    private void AddContent(IActivityExecutionContext context, HttpRequestMessage request)
    {
        var content = context.Get(Content);
        if (string.IsNullOrEmpty(content))
            return;

        var contentType = context.Get(ContentType);
        contentType = string.IsNullOrWhiteSpace(contentType) ? "text/plain" : contentType.Trim();
        request.Content = new StringContent(content, System.Text.Encoding.UTF8, contentType);
    }

    private string[] DetermineResponseOutcomes(IActivityExecutionContext context, int status, bool isSuccess)
    {
        var expected = context.Get(ExpectedStatusCodes);
        if (expected is { Count: > 0 })
            return expected.Contains(status)
                ? [status.ToString()]
                : [HttpActivityOutcomes.Unmatched];

        return isSuccess ? [ActivityOutcomes.Done] : [HttpActivityOutcomes.Failed];
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
