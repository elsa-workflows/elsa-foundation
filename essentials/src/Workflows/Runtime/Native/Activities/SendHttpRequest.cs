using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Kernel.Native;

/// <summary>What <see cref="SendHttpRequest"/> received: the status, the headers by lower-case name, and the body.</summary>
public sealed record HttpResponseResult(int StatusCode, IReadOnlyDictionary<string, string> Headers, JsonNode? Body);

/// <summary>
/// An outbound HTTP request whose response completes the activity (design card 2, option A). It runs as a background job,
/// so the instance still takes a cancel or a timer meanwhile and parallel calls run side by side; the turn commits the
/// job ticket with the evaluated inputs and the effect key, and the job sends the request with the host's client
/// (<see cref="NativeHttpOptions"/>). The effect key goes out as the <c>Idempotency-Key</c> header, unless the author sets
/// that header: the job runs at least once, and a receiver that keeps the keys it has seen drops a repeat.
/// <list type="bullet">
/// <item>Outcomes, as on the old core: one per <see cref="ExpectedStatusCodes"/> entry and <c>Unmatched status code</c>;
/// without expected codes, <c>Done</c> for a 2xx and <c>Failed</c> otherwise. A transport error is <c>Failed</c>, the
/// <see cref="Timeout"/> passing (the HTTP client's or the job's own deadline) is <c>Timeout</c>, and a body over the host's
/// <see cref="NativeHttpOptions.MaxResponseBytes"/> is <c>ResponseTooLarge</c>, with the status and headers and no body.</item>
/// <item>A bad URL or method, a header the rules refuse, or no client faults the activity.</item>
/// <item>Credentials come only through a <see cref="Connection"/> the host resolves when the job runs: the job's
/// arguments are stored, so an authored <c>Authorization</c>, <c>Proxy-Authorization</c> or <c>Cookie</c> header is refused.</item>
/// </list>
/// </summary>
[Background(TimeoutInput = nameof(Timeout), DefaultTimeoutMilliseconds = 60_000, TimeoutOutcome = TimeoutOutcome)]
public sealed class SendHttpRequest : Activity<HttpResponseResult?>
{
    public const string IdempotencyKeyHeader = "Idempotency-Key";
    public const string DoneOutcome = "Done";
    public const string FailedOutcome = "Failed";
    public const string TimeoutOutcome = "Timeout";
    public const string UnmatchedOutcome = "Unmatched status code";
    public const string TooLargeOutcome = "ResponseTooLarge";

    /// <summary>The timeout when <see cref="Timeout"/> is unbound or not positive, as the old core's default.</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(60);

    private static readonly HashSet<string> Credentials = new(["authorization", "proxy-authorization", "cookie"], StringComparer.OrdinalIgnoreCase);

    [Input] public string Url { get; set; } = "";
    [Input] public string Method { get; set; } = "POST";
    /// <summary>The body: a JSON value sent as application/json, or a string sent as <see cref="ContentType"/> text.</summary>
    [Input] public JsonNode? Body { get; set; }
    [Input] public string? ContentType { get; set; }
    /// <summary>Request headers: an object of strings, numbers or booleans, without credentials.</summary>
    [Input] public JsonObject? Headers { get; set; }
    /// <summary>The status codes that each have an outcome of their own.</summary>
    [Input] public int[]? ExpectedStatusCodes { get; set; }
    /// <summary>The HTTP client's timeout and the job's deadline; <see cref="DefaultTimeout"/> when unbound.</summary>
    [Input] public TimeSpan? Timeout { get; set; }
    /// <summary>The name of the Connection whose credentials the host adds when the job runs.</summary>
    [Input] public string? Connection { get; set; }

    protected override async ValueTask<Transition> ExecuteAsync(ActivityContext context)
    {
        if (context.Http?.Client is not { } client) return Fault("http.sender.unavailable", "The host composes no HTTP client for SendHttpRequest.");
        using var request = Request(context, out var refused);
        if (request is null) return Fault("http.request.invalid", refused!);
        if (Connection is { Length: > 0 } connection)
        {
            if (context.Http.Connection is not { } resolve) return Fault("http.connection.unavailable", $"The host resolves no Connection, so '{connection}' cannot be used.");
            foreach (var (name, value) in await resolve(connection, context.CancellationToken))
                if (!request.Headers.TryAddWithoutValidation(name, value)) request.Content?.Headers.TryAddWithoutValidation(name, value);
        }
        var timeout = Timeout is { } t && t > TimeSpan.Zero ? t : DefaultTimeout;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
        deadline.CancelAfter(timeout);
        try
        {
            using var response = await client.SendAsync(request, deadline.Token);
            var headers = ResponseHeaders(response);
            var status = (int)response.StatusCode;
            var (body, tooLarge) = await ReadAsync(response, context.Http.MaxResponseBytes, deadline.Token);
            return tooLarge
                ? Complete(new HttpResponseResult(status, headers, null), TooLargeOutcome)
                : Complete(new HttpResponseResult(status, headers, body), OutcomeOf(status));
        }
        catch (OperationCanceledException) when (!context.CancellationToken.IsCancellationRequested)
        {
            return Complete(null, TimeoutOutcome);
        }
        catch (HttpRequestException)
        {
            return Complete(null, FailedOutcome);
        }
    }

    private string OutcomeOf(int status) => ExpectedStatusCodes is { Length: > 0 } expected
        ? expected.Contains(status) ? status.ToString(System.Globalization.CultureInfo.InvariantCulture) : UnmatchedOutcome
        : status is >= 200 and < 300 ? DoneOutcome : FailedOutcome;

    private HttpRequestMessage? Request(ActivityContext context, out string? refused)
    {
        refused = null;
        if (!Uri.TryCreate(Url, UriKind.Absolute, out var url) || url.Scheme is not ("http" or "https"))
        {
            refused = $"'{Url}' is not an absolute http or https URL.";
            return null;
        }
        HttpRequestMessage request;
        try { request = new HttpRequestMessage(new HttpMethod(Method), url); }
        catch (FormatException) { refused = $"'{Method}' is not an HTTP method."; return null; }
        if (Body is not null)
            request.Content = Body is JsonValue text && text.GetValueKind() == JsonValueKind.String && ContentType is { } type && !type.Contains("json", StringComparison.OrdinalIgnoreCase)
                ? new StringContent(text.GetValue<string>(), Encoding.UTF8, type)
                : new StringContent(Body.ToJsonString(), Encoding.UTF8, ContentType ?? "application/json");
        var keyed = false;
        foreach (var (name, value) in Headers ?? [])
        {
            if (Credentials.Contains(name))
            {
                refused = $"The header '{name}' carries credentials; they come through a Connection, which the host resolves when the job runs.";
                request.Dispose();
                return null;
            }
            var header = value is JsonValue scalar ? scalar.GetValueKind() switch
            {
                JsonValueKind.String => scalar.TryGetValue<string>(out var str) ? str : JsonSerializer.Deserialize<string>(scalar.ToJsonString()),
                JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => scalar.ToJsonString(),
                _ => null,
            } : null;
            if (header is null || header.AsSpan().IndexOfAny('\r', '\n', '\0') >= 0 ||
                !request.Headers.TryAddWithoutValidation(name, header) && request.Content?.Headers.TryAddWithoutValidation(name, header) != true)
            {
                refused = $"The header '{name}' is not a valid request header with a string, number or boolean value.";
                request.Dispose();
                return null;
            }
            keyed |= string.Equals(name, IdempotencyKeyHeader, StringComparison.OrdinalIgnoreCase);
        }
        if (!keyed) request.Headers.TryAddWithoutValidation(IdempotencyKeyHeader, context.EffectKey);
        return request;
    }

    private static Dictionary<string, string> ResponseHeaders(HttpResponseMessage response)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, values) in response.Headers.Concat(response.Content.Headers))
            headers[name.ToLowerInvariant()] = headers.TryGetValue(name, out var earlier) ? earlier + ", " + string.Join(", ", values) : string.Join(", ", values);
        return headers;
    }

    // The body up to the host's cap: past it, nothing is kept. JSON is parsed when the content type says so; else text.
    private static async Task<(JsonNode? Body, bool TooLarge)> ReadAsync(HttpResponseMessage response, int maxBytes, CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength is { } length && length > maxBytes) return (null, true);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var buffer = new MemoryStream();
        var chunk = new byte[8_192];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > maxBytes) return (null, true);
            buffer.Write(chunk, 0, read);
        }
        if (buffer.Length == 0) return (null, false);
        var text = Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
        if (response.Content.Headers.ContentType?.MediaType?.Contains("json", StringComparison.OrdinalIgnoreCase) == true)
            try { return (JsonNode.Parse(text), false); }
            catch (JsonException) { }
        return (JsonValue.Create(text), false);
    }
}
