using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
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
/// arguments are stored, so an authored <c>Authorization</c>, <c>Proxy-Authorization</c>, <c>Cookie</c> or <c>X-Api-Key</c>
/// header is refused, and the result leaves out the <c>Set-Cookie</c> and credential headers a partner sends back.</item>
/// <item>The host's client must not follow redirects or keep cookies (<see cref="NativeHttpOptions"/>): a 3xx is a status
/// like any other, so a Connection's credentials never go to a host the partner names.</item>
/// </list>
/// </summary>
[Background(TimeoutInput = nameof(Timeout), DefaultTimeoutMilliseconds = DefaultTimeoutMilliseconds, TimeoutOutcome = TimeoutOutcome)]
public sealed class SendHttpRequest : Activity<HttpResponseResult?>
{
    public const string IdempotencyKeyHeader = "Idempotency-Key";
    public const string DoneOutcome = "Done";
    public const string FailedOutcome = "Failed";
    public const string TimeoutOutcome = "Timeout";
    public const string UnmatchedOutcome = "Unmatched status code";
    public const string TooLargeOutcome = "ResponseTooLarge";

    /// <summary>The timeout when <see cref="Timeout"/> is unbound, as the old core's default: 60 seconds.</summary>
    public const long DefaultTimeoutMilliseconds = 60_000;

    /// <summary>The longest <see cref="Timeout"/>: one day.</summary>
    public static readonly TimeSpan MaxTimeout = ActivityTimeouts.MaxTimeout;

    private static readonly HashSet<string> Credentials =
        new(["authorization", "proxy-authorization", "cookie", "x-api-key"], StringComparer.OrdinalIgnoreCase);

    // Response headers the result leaves out: what a partner hands back as credentials.
    private static readonly HashSet<string> ReturnedCredentials =
        new(["set-cookie", "set-cookie2", "authorization", "proxy-authorization", "x-api-key"], StringComparer.OrdinalIgnoreCase);

    [Input] public string Url { get; set; } = "";
    [Input] public string Method { get; set; } = "POST";
    /// <summary>The body: a string is sent as its text (text/plain unless <see cref="ContentType"/> says otherwise), any other JSON value as JSON (application/json unless it says otherwise).</summary>
    [Input] public JsonNode? Body { get; set; }
    /// <summary>The body's media type, parameters allowed (<c>text/plain; charset=utf-8</c>).</summary>
    [Input] public string? ContentType { get; set; }
    /// <summary>Request headers: an object of strings, numbers or booleans, without credentials or a content type.</summary>
    [Input] public JsonObject? Headers { get; set; }
    /// <summary>The status codes that each have an outcome of their own: numbers or numeric strings, one or a list.</summary>
    [Input] public JsonNode? ExpectedStatusCodes { get; set; }
    /// <summary>
    /// The HTTP client's timeout and the job's deadline: milliseconds as a number, or a time span (<c>00:00:30</c>,
    /// <c>PT30S</c>); at most <see cref="MaxTimeout"/>, 60 seconds when unbound.
    /// </summary>
    [Input] public JsonNode? Timeout { get; set; }
    /// <summary>The name of the Connection whose credentials the host adds when the job runs.</summary>
    [Input] public string? Connection { get; set; }

    protected override async ValueTask<Transition> ExecuteAsync(ActivityContext context)
    {
        if (context.Http?.Client is not { } client) return Fault("http.sender.unavailable", "The host composes no HTTP client for SendHttpRequest.");
        if (!ActivityTimeouts.TryParse(Timeout, MaxTimeout, out var timeout))
            return Fault("http.request.invalid", $"The timeout '{Timeout?.ToJsonString()}' is not milliseconds or a time span of at most a day.");
        if (!TryStatusCodes(ExpectedStatusCodes, out var expected))
            return Fault("http.request.invalid", $"The expected status codes '{ExpectedStatusCodes?.ToJsonString()}' are not status codes.");
        using var request = Request(context, out var refused);
        if (request is null) return Fault("http.request.invalid", refused!);
        if (Connection is { Length: > 0 } connection)
        {
            if (context.Http.Connection is not { } resolve) return Fault("http.connection.unavailable", $"The host resolves no Connection, so '{connection}' cannot be used.");
            foreach (var (name, value) in await resolve(connection, context.CancellationToken))
                if (!request.Headers.TryAddWithoutValidation(name, value)) request.Content?.Headers.TryAddWithoutValidation(name, value);
        }
        var sent = request.RequestUri;     // a client that follows a redirect rewrites the request it sends
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
        deadline.CancelAfter(timeout ?? TimeSpan.FromMilliseconds(DefaultTimeoutMilliseconds));
        try
        {
            // Headers first, so that the body is read only as far as the cap: an HttpClient would buffer all of it.
            using var response = client is HttpClient http
                ? await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token)
                : await client.SendAsync(request, deadline.Token);
            // The response answers the URI sent, unless the client followed a redirect, which carried this request's headers
            // to the URI it landed on. This only reports the leak; NativeHttpOptions.CreateClient's client prevents it.
            if (response.RequestMessage?.RequestUri is { } answered && answered != sent)
                return Fault("http.client.redirects", $"The host's HTTP client followed a redirect to {answered.GetLeftPart(UriPartial.Authority)}; it must not (NativeHttpOptions.CreateClient).");
            var headers = ResponseHeaders(response);
            var status = (int)response.StatusCode;
            var (body, tooLarge) = await ReadAsync(response, context.Http.MaxResponseBytes, deadline.Token);
            return tooLarge
                ? Complete(new HttpResponseResult(status, headers, null), TooLargeOutcome)
                : Complete(new HttpResponseResult(status, headers, body), OutcomeOf(status, expected));
        }
        catch (OperationCanceledException) when (!context.CancellationToken.IsCancellationRequested)
        {
            return Complete(null, TimeoutOutcome);
        }
        catch (Exception transport) when (transport is HttpRequestException or IOException or InvalidDataException && !context.CancellationToken.IsCancellationRequested)
        {
            return Complete(null, FailedOutcome);     // a refused connection, a reset mid-body, a broken encoding
        }
    }

    private static bool TryStatusCodes(JsonNode? value, out int[] codes)
    {
        codes = [];
        var list = value switch { null => [], JsonArray array => array.ToArray(), _ => [value] };
        var parsed = new List<int>(list.Length);
        foreach (var item in list)
        {
            if (item is not JsonValue scalar) return false;
            var text = scalar.GetValueKind() switch
            {
                JsonValueKind.Number => scalar.ToJsonString(),
                JsonValueKind.String => scalar.TryGetValue<string>(out var s) ? s : null,
                _ => null,
            };
            if (text is not null && int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var number))
            {
                if (number is < 100 or > 599) return false;
                parsed.Add(number);
            }
            else return false;
        }
        codes = [.. parsed];
        return true;
    }

    private static string OutcomeOf(int status, int[] expected) => expected.Length > 0
        ? expected.Contains(status) ? status.ToString(CultureInfo.InvariantCulture) : UnmatchedOutcome
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
        MediaTypeHeaderValue? mediaType = null;
        if (ContentType is { } type && !MediaTypeHeaderValue.TryParse(type, out mediaType))
        {
            refused = $"'{type}' is not a media type.";
            request.Dispose();
            return null;
        }
        if (Body is not null)
        {
            // A string is sent as its text, whatever the media type says; any other value as JSON. Without a content type a
            // string goes as text/plain and anything else as application/json.
            var text = Body is JsonValue value && value.TryGetValue<string>(out var plain) ? plain : null;
            mediaType ??= new MediaTypeHeaderValue(text is null ? "application/json" : "text/plain");
            var encoding = Encoding.UTF8;
            if (mediaType.CharSet is { Length: > 0 } charset)
                try { encoding = Encoding.GetEncoding(charset.Trim('"')); }
                catch (Exception unknown) when (unknown is ArgumentException or NotSupportedException) { refused = $"'{charset}' is not a charset this host encodes."; request.Dispose(); return null; }
            else mediaType.CharSet = "utf-8";
            request.Content = new StringContent(text ?? Body.ToJsonString(), encoding);
            request.Content.Headers.ContentType = mediaType;
        }
        var keyed = false;
        foreach (var (name, value) in Headers ?? [])
        {
            if (string.Equals(name, "Content-Type", StringComparison.OrdinalIgnoreCase))
            {
                refused = "The body's media type is the ContentType input, not a header.";
                request.Dispose();
                return null;
            }
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
            if (!ReturnedCredentials.Contains(name))
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
        var encoding = Encoding.UTF8;
        if (response.Content.Headers.ContentType?.CharSet is { Length: > 0 } charset)
            try { encoding = Encoding.GetEncoding(charset.Trim('"')); }
            catch (Exception unknown) when (unknown is ArgumentException or NotSupportedException) { }     // a charset this host lacks reads as UTF-8
        var text = encoding.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
        if (response.Content.Headers.ContentType?.MediaType?.Contains("json", StringComparison.OrdinalIgnoreCase) == true)
            try { return (JsonNode.Parse(text), false); }
            catch (JsonException) { }
        return (JsonValue.Create(text), false);
    }
}
