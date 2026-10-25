using System.Net;

namespace Elsa.Integrations.Sdk.Harness;

/// <summary>Captures outbound requests so tests can assert auth headers without a network.</summary>
public sealed class RecordingHttpMessageHandler : HttpMessageHandler
{
    private readonly List<HttpRequestMessage> _requests = [];
    private readonly List<RecordedHttpRequest> _recorded = [];
    private readonly Queue<CannedHttpResponse> _queued = new();

    public HttpStatusCode ResponseStatus { get; set; } = HttpStatusCode.OK;
    public string ResponseContent { get; set; } = "{}";
    public IDictionary<string, string> ResponseHeaders { get; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Optional per-request factory. When set, it wins over the queue and the default
    /// <see cref="ResponseStatus"/>/<see cref="ResponseContent"/>.
    /// </summary>
    public Func<HttpRequestMessage, string?, CannedHttpResponse>? Respond { get; set; }

    public IReadOnlyList<HttpRequestMessage> Requests
    {
        get
        {
            lock (_requests)
                return [.._requests];
        }
    }

    public HttpRequestMessage? LastRequest
    {
        get
        {
            lock (_requests)
                return _requests.Count == 0 ? null : _requests[^1];
        }
    }

    public IReadOnlyList<RecordedHttpRequest> Recorded
    {
        get
        {
            lock (_recorded)
                return [.._recorded];
        }
    }

    public RecordedHttpRequest? LastRecorded
    {
        get
        {
            lock (_recorded)
                return _recorded.Count == 0 ? null : _recorded[^1];
        }
    }

    public void Enqueue(CannedHttpResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        lock (_queued)
            _queued.Enqueue(response);
    }

    public void EnqueueJson(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        Enqueue(CannedHttpResponse.Json(json, status));

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null
            ? null
            : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        var clone = Clone(request);
        var recorded = ToRecorded(request, body);
        lock (_requests)
            _requests.Add(clone);
        lock (_recorded)
            _recorded.Add(recorded);

        var canned = Resolve(request, body);
        var response = new HttpResponseMessage(canned.Status)
        {
            RequestMessage = request,
            Content = new StringContent(canned.Content)
        };
        foreach (var header in canned.Headers)
            response.Headers.TryAddWithoutValidation(header.Key, header.Value);
        return response;
    }

    private CannedHttpResponse Resolve(HttpRequestMessage request, string? body)
    {
        if (Respond is not null)
            return Respond(request, body);

        lock (_queued)
        {
            if (_queued.Count > 0)
                return _queued.Dequeue();
        }

        return new CannedHttpResponse
        {
            Status = ResponseStatus,
            Content = ResponseContent,
            Headers = new Dictionary<string, string>(ResponseHeaders, StringComparer.OrdinalIgnoreCase)
        };
    }

    private static RecordedHttpRequest ToRecorded(HttpRequestMessage request, string? body)
    {
        var headers = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in request.Headers)
            headers[header.Key] = [..header.Value];

        return new RecordedHttpRequest
        {
            Method = request.Method,
            RequestUri = request.RequestUri,
            Authorization = request.Headers.Authorization,
            Body = body,
            Headers = headers
        };
    }

    private static HttpRequestMessage Clone(HttpRequestMessage request)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri);
        foreach (var header in request.Headers)
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        return clone;
    }
}
