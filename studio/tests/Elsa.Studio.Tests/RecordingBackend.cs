using System.Net;
using System.Text.Json;
using Elsa.Studio.Api.Options;

namespace Elsa.Studio.Tests;

/// <summary>
/// A recording <see cref="HttpMessageHandler"/> standing in for the backend Elsa host. Captures every request
/// (method, path+query, body, content type, management-key header, Authorization presence) so tests can assert
/// forwarding fidelity, header hygiene, and the fail-closed no-call guarantees.
/// </summary>
internal sealed class RecordingBackend : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? _responder;
    private readonly Exception? _throw;

    private RecordingBackend(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? responder, Exception? toThrow)
    {
        _responder = responder;
        _throw = toThrow;
    }

    public List<RecordedRequest> Requests { get; } = [];

    /// <summary>Recorded requests that carried the management key (i.e. Studio→backend management calls).</summary>
    public IReadOnlyList<RecordedRequest> ManagementRequests => Requests.Where(x => x.ManagementKey is not null).ToArray();

    public static RecordingBackend RespondingWith(Func<HttpRequestMessage, HttpResponseMessage> responder) =>
        new((request, _) => Task.FromResult(responder(request)), null);

    public static RecordingBackend RespondingWithAsync(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder) =>
        new(responder, null);

    public static RecordingBackend Throwing(Exception toThrow) => new(null, toThrow);

    /// <summary>
    /// A backend that maps a Studio->backend management call to a fixed outcome: a bare status code, or (when null) a
    /// transport failure.
    /// </summary>
    public static RecordingBackend For(HttpStatusCode? backendStatus) =>
        backendStatus is { } status
            ? RespondingWith(_ => new HttpResponseMessage(status))
            : Throwing(new HttpRequestException("connection refused"));

    public static HttpResponseMessage JsonOk(string json) => Json(HttpStatusCode.OK, json);

    public static HttpResponseMessage Json(HttpStatusCode statusCode, string json) =>
        new(statusCode) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };

    public static HttpResponseMessage TextOk(string text) =>
        new(HttpStatusCode.OK) { Content = new StringContent(text, System.Text.Encoding.UTF8, "text/plain") };

    /// <summary>
    /// Wraps a backend responder with the backend's anonymous <c>/_elsa/identity/session</c> endpoint, recognizing the
    /// given bearers and reflecting their permission sets (the flat camelCase array the auth gate projects onto the
    /// ticket). Unknown bearers yield an <c>anonymous</c> session.
    /// </summary>
    public static Func<HttpRequestMessage, HttpResponseMessage> WithSessionEndpoint(
        IReadOnlyDictionary<string, string[]> bearerPermissions,
        Func<HttpRequestMessage, HttpResponseMessage> backendResponder) =>
        request =>
        {
            if (!request.RequestUri!.AbsolutePath.EndsWith("/identity/session"))
                return backendResponder(request);

            var bearer = request.Headers.Authorization?.Parameter;
            var json = bearer is not null && bearerPermissions.TryGetValue(bearer, out var permissions)
                ? JsonSerializer.Serialize(new { status = "authenticated", subject = "alice", permissions })
                : """{ "status": "anonymous" }""";
            return JsonOk(json);
        };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.Headers.TryGetValues(StudioBackendManagementOptions.ManagementApiKeyHeaderName, out var keyValues);
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add(new(
            request.Method.Method,
            request.RequestUri!.PathAndQuery,
            body,
            request.Content?.Headers.ContentType?.ToString(),
            keyValues?.FirstOrDefault(),
            request.Headers.Authorization is not null));

        if (_throw is not null)
            throw _throw;

        return await _responder!(request, cancellationToken);
    }
}

internal sealed record RecordedRequest(
    string Method,
    string PathAndQuery,
    string? Body,
    string? ContentType,
    string? ManagementKey,
    bool HasAuthorization);
