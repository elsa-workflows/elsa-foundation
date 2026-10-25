using System.Net;

namespace Elsa.Integrations.Sdk.Harness;

/// <summary>One canned HTTP response for <see cref="RecordingHttpMessageHandler"/>.</summary>
public sealed class CannedHttpResponse
{
    public HttpStatusCode Status { get; init; } = HttpStatusCode.OK;

    public string Content { get; init; } = "{}";

    public IReadOnlyDictionary<string, string> Headers { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public static CannedHttpResponse Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new() { Status = status, Content = json };

    public static CannedHttpResponse Json(
        string json,
        HttpStatusCode status,
        IReadOnlyDictionary<string, string> headers) =>
        new() { Status = status, Content = json, Headers = headers };

    public static CannedHttpResponse WithStatus(
        HttpStatusCode status,
        string content = "",
        IReadOnlyDictionary<string, string>? headers = null) =>
        new()
        {
            Status = status,
            Content = content,
            Headers = headers ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        };
}

/// <summary>A captured outbound request, including the body the handler read.</summary>
public sealed class RecordedHttpRequest
{
    public required HttpMethod Method { get; init; }

    public required Uri? RequestUri { get; init; }

    public System.Net.Http.Headers.AuthenticationHeaderValue? Authorization { get; init; }

    public string? Body { get; init; }

    public IReadOnlyDictionary<string, IReadOnlyList<string>> Headers { get; init; } =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
}
