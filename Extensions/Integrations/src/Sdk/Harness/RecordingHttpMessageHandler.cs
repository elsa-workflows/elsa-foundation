using System.Net;

namespace Elsa.Integrations.Sdk.Harness;

/// <summary>Captures outbound requests so tests can assert auth headers without a network.</summary>
public sealed class RecordingHttpMessageHandler : HttpMessageHandler
{
    private readonly List<HttpRequestMessage> _requests = [];
    public HttpStatusCode ResponseStatus { get; set; } = HttpStatusCode.OK;
    public string ResponseContent { get; set; } = "{}";

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

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var clone = Clone(request);
        lock (_requests)
            _requests.Add(clone);
        return Task.FromResult(new HttpResponseMessage(ResponseStatus)
        {
            RequestMessage = request,
            Content = new StringContent(ResponseContent)
        });
    }

    private static HttpRequestMessage Clone(HttpRequestMessage request)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri);
        foreach (var header in request.Headers)
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        return clone;
    }
}
